using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Ezg.CheatGate
{
    /// <summary>Outcome of <see cref="CheatGateCore.TryUnlock" /> / <see cref="CheatGate.TryUnlock" />.</summary>
    public enum CheatGateResult
    {
        /// <summary>This phone may open cheats.</summary>
        Unlocked,

        /// <summary>The server says this phone is not on the project's list. Show <see cref="CheatGate.DeviceIdDisplay" />.</summary>
        NotListed,

        /// <summary>The platform gives no device identifier: this phone can never be listed.</summary>
        NoDeviceId,

        /// <summary><see cref="CheatGate.Initialize" /> was not called, or with an invalid project code.</summary>
        NotConfigured,

        /// <summary>No connection (or timeout). Try again with network.</summary>
        Offline,

        /// <summary>Too many checks from this network for a minute.</summary>
        RateLimited,

        /// <summary>The server answered something unexpected (5xx, malformed JSON, 400), or the request could not be built.</summary>
        ServerError,

        /// <summary>
        ///     The server granted, but the grant does not verify (the package's keys do not match the server's key) or was
        ///     issued for another request (a replayed answer).
        /// </summary>
        InvalidGrant,
    }

    /// <summary>Persistent key-value storage (PlayerPrefs in a game).</summary>
    public interface ICheatGateStore
    {
        string Get(string key);
        void Set(string key, string value);
        void Delete(string key);
    }

    public struct CheatGateHttpResponse
    {
        /// <summary>True when no HTTP answer arrived at all (no network, DNS, timeout, TLS).</summary>
        public bool NetworkError;

        public long Status;
        public string Body;
    }

    /// <summary>One HTTP GET. <paramref name="done" /> must be called exactly once, on the main thread.</summary>
    public interface ICheatGateTransport
    {
        void Get(string url, Action<CheatGateHttpResponse> done);
    }

    /// <summary>
    ///     The cheat-gate state machine, free of UnityEngine (the <see cref="CheatGate" /> facade plugs in PlayerPrefs,
    ///     UnityWebRequest, the clock and the focus events).
    ///     <list type="bullet">
    ///         <item>A phone without a grant never sends anything until <see cref="TryUnlock" /> (the 10th tap).</item>
    ///         <item>
    ///             A phone holding a grant re-checks at <see cref="Start" /> and at <see cref="Recheck" /> (the game coming
    ///             back to the foreground) when its last check is older than <c>revalidateAfterSeconds</c>: "not listed"
    ///             revokes, a new grant extends, no answer changes nothing (the grant runs until it expires).
    ///         </item>
    ///         <item>
    ///             Every check carries a fresh random nonce; an answer is believed only if its grant echoes it, so a recorded
    ///             answer replayed by a proxy is refused.
    ///         </item>
    ///         <item>
    ///             Unlocked = a grant received in this run, for its own duration measured on this phone's clock from the
    ///             moment it arrived (a tester's skewed clock does not matter), or a stored grant current by the clock.
    ///         </item>
    ///     </list>
    /// </summary>
    public sealed class CheatGateCore
    {
        public const long DefaultRevalidateAfterSeconds = 30 * 60;
        private const string GrantKeyPrefix = "ezg.cheatgate.grant.";
        private const string CheckedKeyPrefix = "ezg.cheatgate.checked.";

        private readonly string _endpoint;
        private readonly IReadOnlyList<CheatGateKeys.Key> _keys;
        private readonly ICheatGateStore _store;
        private readonly ICheatGateTransport _transport;
        private readonly Func<long> _now;
        private readonly Func<string> _newNonce;
        private readonly Action<string> _log;
        private readonly long _revalidateAfter;
        private readonly List<Action<CheatGateResult>> _waiting = new List<Action<CheatGateResult>>();

        private CheatGateClaims _stored;
        private long _sessionUntil;
        private long _checkedAt = -1;
        private bool _announced;
        private bool _inFlight;
        private bool _started;

        public CheatGateCore(string endpoint, string project, string deviceId, IReadOnlyList<CheatGateKeys.Key> keys, ICheatGateStore store,
            ICheatGateTransport transport, Func<long> nowSeconds, Action<string> log = null, long revalidateAfterSeconds = DefaultRevalidateAfterSeconds,
            Func<string> newNonce = null)
        {
            _endpoint = (endpoint ?? string.Empty).Trim().TrimEnd('/');
            Project = NormalizeProject(project);
            DeviceId = CheatGateDeviceId.Normalize(deviceId);
            _keys = keys ?? Array.Empty<CheatGateKeys.Key>();
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _now = nowSeconds ?? throw new ArgumentNullException(nameof(nowSeconds));
            _newNonce = newNonce ?? RandomNonce;
            _log = log ?? (_ => { });
            _revalidateAfter = Math.Max(0, revalidateAfterSeconds);
        }

        /// <summary>Upper-case project code, "" when the one given is not valid (then nothing is ever unlocked).</summary>
        public string Project { get; }

        /// <summary>12 hex digits, "" when the platform has no identifier.</summary>
        public string DeviceId { get; }

        public bool IsConfigured => Project.Length > 0 && _endpoint.Length > 0;

        /// <summary>A check is on the way.</summary>
        public bool IsChecking => _inFlight;

        public bool IsUnlocked
        {
            get
            {
                var now = _now();
                return now < _sessionUntil || (_stored != null && CheatGateGrant.IsCurrent(_stored, now));
            }
        }

        /// <summary>When the stored grant ends (epoch seconds), 0 without one.</summary>
        public long GrantExpiresAt => _stored?.ExpiresAt ?? 0;

        /// <summary>
        ///     Raised with the new value whenever <see cref="IsUnlocked" /> differs from the value last reported, looked at when
        ///     the server answers and at <see cref="Recheck" /> (false = revoked or expired: turn cheats off).
        /// </summary>
        public event Action<bool> Changed;

        private string GrantKey => GrantKeyPrefix + Project;
        private string CheckedKey => CheckedKeyPrefix + Project;
        private bool HoldsGrant => _stored != null || _sessionUntil > 0;

        /// <summary>
        ///     Project code as BugHub / the server spell it: trimmed, upper-case, letters / digits / _ / -, first one a letter or
        ///     digit, at most 32. "" otherwise. Same rule as devices.js normalizeProject.
        /// </summary>
        public static string NormalizeProject(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var code = value.Trim().ToUpperInvariant();
            if (code.Length == 0 || code.Length > 32) return string.Empty;
            for (var i = 0; i < code.Length; i++)
            {
                var c = code[i];
                var alnum = (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
                if (alnum) continue;
                if (i > 0 && (c == '_' || c == '-')) continue;
                return string.Empty;
            }

            return code;
        }

        /// <summary>Loads the stored grant and, if there is one and its last check is old, re-checks it in the background.</summary>
        public void Start()
        {
            if (_started) return;
            _started = true;
            if (!IsConfigured || DeviceId.Length == 0) return;

            var raw = SafeGet(GrantKey);
            if (!string.IsNullOrEmpty(raw))
            {
                if (CheatGateGrant.TryVerify(raw, _keys, Project, DeviceId, out var claims))
                {
                    _stored = claims;
                    var checkedRaw = SafeGet(CheckedKey);
                    _checkedAt = long.TryParse(checkedRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var at) ? at : -1;
                }
                else
                {
                    // Tampered, another device / project, or signed by a key this build no longer trusts.
                    Forget();
                }
            }

            _announced = IsUnlocked;
            if (HoldsGrant && NeedsRecheck()) Check(null);
        }

        /// <summary>
        ///     The game came back to the foreground: report an expiry that happened meanwhile, and re-check a held grant whose
        ///     last check is old. A phone holding no grant sends nothing.
        /// </summary>
        public void Recheck()
        {
            if (!_started || !IsConfigured || DeviceId.Length == 0) return;
            Announce();
            if (HoldsGrant && !_inFlight && NeedsRecheck()) Check(null);
        }

        /// <summary>
        ///     The 10th tap: already unlocked -> <see cref="CheatGateResult.Unlocked" /> at once, no request; otherwise asks the
        ///     server once (concurrent calls share that request). <paramref name="done" /> may run synchronously.
        /// </summary>
        public void TryUnlock(Action<CheatGateResult> done)
        {
            if (!IsConfigured)
            {
                Invoke(done, CheatGateResult.NotConfigured);
                return;
            }

            if (DeviceId.Length == 0)
            {
                Invoke(done, CheatGateResult.NoDeviceId);
                return;
            }

            if (!_started) Start();
            if (IsUnlocked)
            {
                Invoke(done, CheatGateResult.Unlocked);
                return;
            }

            Check(done);
        }

        private bool NeedsRecheck()
        {
            var now = _now();
            var last = _checkedAt;
            if (last < 0) return true;
            // The clock moved back before the last check: trust nothing about the interval.
            if (last > now + CheatGateGrant.ClockSkewSeconds) return true;
            return now - last >= _revalidateAfter;
        }

        private string CheckUrl(string nonce)
        {
            var sb = new StringBuilder(_endpoint.Length + 96);
            sb.Append(_endpoint).Append("/v1/check?project=").Append(Uri.EscapeDataString(Project)).Append("&device=").Append(DeviceId)
                .Append("&n=").Append(nonce);
            return sb.ToString();
        }

        private void Check(Action<CheatGateResult> done)
        {
            if (done != null) _waiting.Add(done);
            if (_inFlight) return;
            _inFlight = true;
            var answered = false;
            string nonce;
            try
            {
                nonce = _newNonce();
            }
            catch (Exception e)
            {
                _log("[CheatGate] no random nonce: " + e.Message);
                nonce = null;
            }

            if (!IsNonce(nonce))
            {
                answered = true;
                Complete(CheatGateResult.ServerError);
                return;
            }

            try
            {
                _transport.Get(CheckUrl(nonce), response =>
                {
                    if (answered) return;
                    answered = true;
                    Complete(Handle(response, nonce));
                });
            }
            catch (Exception e)
            {
                _log("[CheatGate] check could not be sent: " + e.Message);
                if (answered) return;
                answered = true;
                Complete(CheatGateResult.ServerError);
            }
        }

        private CheatGateResult Handle(CheatGateHttpResponse response, string nonce)
        {
            if (response.NetworkError) return CheatGateResult.Offline;
            if (response.Status == 429) return CheatGateResult.RateLimited;
            if (response.Status != 200)
            {
                _log("[CheatGate] server answered HTTP " + response.Status);
                return CheatGateResult.ServerError;
            }

            var body = CheatGateJson.ParseFlatObject(response.Body);
            if (!CheatGateJson.TryGetBool(body, "granted", out var granted))
            {
                _log("[CheatGate] unexpected answer from the server");
                return CheatGateResult.ServerError;
            }

            if (!granted)
            {
                Forget();
                _checkedAt = _now();
                return CheatGateResult.NotListed;
            }

            if (!CheatGateJson.TryGetString(body, "grant", out var grant) || !CheatGateGrant.TryVerify(grant, _keys, Project, DeviceId, out var claims))
            {
                _log("[CheatGate] the server granted, but its grant does not verify: are CheatGateKeys.Trusted and the Worker's GRANT_PRIVATE_KEY the same key?");
                return CheatGateResult.InvalidGrant;
            }

            if (claims.Nonce != nonce)
            {
                _log("[CheatGate] the grant was issued for another request (a replayed answer?): ignored");
                return CheatGateResult.InvalidGrant;
            }

            var now = _now();
            _stored = claims;
            _sessionUntil = now + Math.Max(0, claims.ExpiresAt - claims.IssuedAt);
            _checkedAt = now;
            SafeSet(GrantKey, grant);
            SafeSet(CheckedKey, now.ToString(CultureInfo.InvariantCulture));
            return CheatGateResult.Unlocked;
        }

        private void Complete(CheatGateResult result)
        {
            _inFlight = false;
            var waiting = _waiting.ToArray();
            _waiting.Clear();
            Announce();
            foreach (var done in waiting) Invoke(done, result);
        }

        /// <summary>Raises <see cref="Changed" /> when <see cref="IsUnlocked" /> is not what was reported last.</summary>
        private void Announce()
        {
            var now = IsUnlocked;
            if (now == _announced) return;
            _announced = now;
            var handlers = Changed;
            if (handlers == null) return;
            foreach (var handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<bool>)handler)(now);
                }
                catch (Exception e)
                {
                    _log("[CheatGate] a Changed handler threw: " + e);
                }
            }
        }

        private void Forget()
        {
            _stored = null;
            _sessionUntil = 0;
            _checkedAt = -1;
            SafeDelete(GrantKey);
            SafeDelete(CheckedKey);
        }

        /// <summary>16 random bytes as 32 hex digits (the server accepts 16-64 of [A-Za-z0-9_-]).</summary>
        public static string RandomNonce()
        {
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            var sb = new StringBuilder(32);
            foreach (var b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static bool IsNonce(string value)
        {
            if (value == null || value.Length < 16 || value.Length > 64) return false;
            foreach (var c in value)
            {
                var ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!ok) return false;
            }

            return true;
        }

        private void Invoke(Action<CheatGateResult> done, CheatGateResult result)
        {
            if (done == null) return;
            try
            {
                done(result);
            }
            catch (Exception e)
            {
                _log("[CheatGate] a TryUnlock callback threw: " + e);
            }
        }

        private string SafeGet(string key)
        {
            try
            {
                return _store.Get(key);
            }
            catch (Exception e)
            {
                _log("[CheatGate] storage read failed: " + e.Message);
                return null;
            }
        }

        private void SafeSet(string key, string value)
        {
            try
            {
                _store.Set(key, value);
            }
            catch (Exception e)
            {
                _log("[CheatGate] storage write failed: " + e.Message);
            }
        }

        private void SafeDelete(string key)
        {
            try
            {
                _store.Delete(key);
            }
            catch (Exception e)
            {
                _log("[CheatGate] storage delete failed: " + e.Message);
            }
        }
    }
}
