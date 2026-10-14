using System;
using System.Collections.Generic;
using NUnit.Framework;
using V = Ezg.Package.CheatGate.Tests.CheatGateTestVectors;

namespace Ezg.Package.CheatGate.Tests
{
    public class CheatGateCoreTests
    {
        private const string Endpoint = "https://cheat.test";
        private const string GrantKey = "ezg.cheatgate.grant.SM007";
        private const string CheckedKey = "ezg.cheatgate.checked.SM007";
        private const string GrantedBody = "{\"granted\":true,\"grant\":\"" + V.Fresh + "\",\"expiresAt\":" + "1760604800" + "}";
        private const string CheckUrl = "https://cheat.test/v1/check?project=SM007&device=7F3A9C21B04E&n=" + V.Nonce;
        private const string NotListedBody = "{\"granted\":false}";

        private MemoryStore _store;
        private FakeTransport _http;
        private long _now;
        private List<string> _log;
        private List<bool> _changes;
        private Func<string> _nonce;

        [SetUp]
        public void SetUp()
        {
            _store = new MemoryStore();
            _http = new FakeTransport();
            _now = V.IssuedAt + 60;
            _log = new List<string>();
            _changes = new List<bool>();
            _nonce = () => V.Nonce;
        }

        private CheatGateCore Core(string project = V.Project, string device = V.DeviceId, string endpoint = Endpoint)
        {
            var core = new CheatGateCore(endpoint, project, device, new[] { V.TestKey }, _store, _http, () => _now, _log.Add,
                CheatGateCore.DefaultRevalidateAfterSeconds, () => _nonce());
            core.Changed += _changes.Add;
            return core;
        }

        private static CheatGateHttpResponse Ok(string body) => new CheatGateHttpResponse { Status = 200, Body = body };

        private CheatGateResult Unlock(CheatGateCore core)
        {
            CheatGateResult? result = null;
            core.TryUnlock(r => result = r);
            Assert.IsTrue(result.HasValue, "callback did not run");
            return result.Value;
        }

        [Test]
        public void APlayerWhoNeverTapsSendsNothing()
        {
            var core = Core();
            core.Start();
            Assert.IsFalse(core.IsUnlocked);
            Assert.AreEqual(0, _http.Urls.Count);
        }

        [Test]
        public void NotListed()
        {
            _http.Reply = Ok(NotListedBody);
            var core = Core();
            Assert.AreEqual(CheatGateResult.NotListed, Unlock(core));
            Assert.IsFalse(core.IsUnlocked);
            Assert.AreEqual(new[] { CheckUrl }, _http.Urls.ToArray());
            Assert.IsFalse(_store.Data.ContainsKey(GrantKey));
            Assert.IsEmpty(_changes);
        }

        [Test]
        public void GrantedUnlocksAndPersists()
        {
            _http.Reply = Ok(GrantedBody);
            var core = Core();
            Assert.AreEqual(CheatGateResult.Unlocked, Unlock(core));
            Assert.IsTrue(core.IsUnlocked);
            Assert.AreEqual(V.Fresh, _store.Data[GrantKey]);
            Assert.AreEqual(_now.ToString(), _store.Data[CheckedKey]);
            Assert.AreEqual(V.ExpiresAt, core.GrantExpiresAt);
            Assert.AreEqual(new[] { true }, _changes.ToArray());

            Assert.AreEqual(CheatGateResult.Unlocked, Unlock(core), "second tap");
            Assert.AreEqual(1, _http.Urls.Count, "already unlocked: no second request");
        }

        [Test]
        public void NextLaunchWithinThirtyMinutesSendsNothing()
        {
            _http.Reply = Ok(GrantedBody);
            Unlock(Core());
            _now += 29 * 60;
            var next = Core();
            next.Start();
            Assert.IsTrue(next.IsUnlocked);
            Assert.AreEqual(1, _http.Urls.Count);
        }

        [Test]
        public void LaterLaunchRechecksAndARemovedPhoneIsRevoked()
        {
            _http.Reply = Ok(GrantedBody);
            Unlock(Core());
            _now += 31 * 60;
            _http.Reply = Ok(NotListedBody);
            _changes.Clear();
            var next = Core();
            next.Start();
            Assert.AreEqual(2, _http.Urls.Count);
            Assert.IsFalse(next.IsUnlocked);
            Assert.AreEqual(new[] { false }, _changes.ToArray());
            Assert.IsFalse(_store.Data.ContainsKey(GrantKey));
            Assert.IsFalse(_store.Data.ContainsKey(CheckedKey));

            var third = Core();
            third.Start();
            Assert.AreEqual(2, _http.Urls.Count, "revoked phone is back to sending nothing");
        }

        [Test]
        public void RecheckWithoutAnAnswerKeepsTheGrant()
        {
            _http.Reply = Ok(GrantedBody);
            Unlock(Core());
            _now += 2 * 86400;
            _http.Reply = new CheatGateHttpResponse { NetworkError = true };
            _changes.Clear();
            var next = Core();
            next.Start();
            Assert.IsTrue(next.IsUnlocked);
            Assert.IsEmpty(_changes);
            Assert.AreEqual(V.Fresh, _store.Data[GrantKey]);
        }

        [TestCase(500)]
        [TestCase(503)]
        [TestCase(429)]
        [TestCase(400)]
        public void RecheckWithAnErrorKeepsTheGrant(int status)
        {
            _http.Reply = Ok(GrantedBody);
            Unlock(Core());
            _now += 2 * 86400;
            _http.Reply = new CheatGateHttpResponse { Status = status, Body = "{\"error\":\"x\"}" };
            var next = Core();
            next.Start();
            Assert.IsTrue(next.IsUnlocked);
            Assert.AreEqual(V.Fresh, _store.Data[GrantKey]);
        }

        [Test]
        public void ExpiredGrantOfflineIsLockedThenAFreshGrantIsTrustedWhateverTheClock()
        {
            _http.Reply = Ok(GrantedBody);
            Unlock(Core());
            _now = V.ExpiresAt + 3 * 86400; // phone clock far ahead, e.g. a tester skipping days
            _http.Reply = new CheatGateHttpResponse { NetworkError = true };
            var next = Core();
            next.Start();
            Assert.IsFalse(next.IsUnlocked, "expired by the phone's clock, no answer");
            Assert.AreEqual(CheatGateResult.Offline, Unlock(next));

            _http.Reply = Ok(GrantedBody); // the server's grant is "expired" by this phone's clock
            _changes.Clear();
            Assert.AreEqual(CheatGateResult.Unlocked, Unlock(next));
            Assert.IsTrue(next.IsUnlocked, "a grant fresh from the server counts for this session");
            Assert.AreEqual(new[] { true }, _changes.ToArray());
        }

        [Test]
        public void StoredGrantOfAnotherDeviceOrGarbageIsDeletedWithoutARequest()
        {
            _store.Data[GrantKey] = V.OtherDevice;
            _store.Data[CheckedKey] = "0";
            var core = Core();
            core.Start();
            Assert.IsFalse(core.IsUnlocked);
            Assert.IsFalse(_store.Data.ContainsKey(GrantKey));
            Assert.IsFalse(_store.Data.ContainsKey(CheckedKey));
            Assert.AreEqual(0, _http.Urls.Count);

            _store.Data[GrantKey] = "garbage";
            var again = Core();
            again.Start();
            Assert.IsFalse(_store.Data.ContainsKey(GrantKey));
            Assert.AreEqual(0, _http.Urls.Count);
        }

        [Test]
        public void HandEditedCheckedTimestampForcesARecheck()
        {
            _http.Reply = Ok(GrantedBody);
            Unlock(Core());
            _store.Data[CheckedKey] = "not a number";
            var a = Core();
            a.Start();
            Assert.AreEqual(2, _http.Urls.Count);

            _store.Data[CheckedKey] = (_now + 86400).ToString(); // clock moved back since the last check
            var b = Core();
            b.Start();
            Assert.AreEqual(3, _http.Urls.Count);
        }

        [Test]
        public void GrantThatDoesNotVerifyIsRefused()
        {
            _http.Reply = Ok("{\"granted\":true,\"grant\":\"" + V.ByOtherKey + "\"}");
            var core = Core();
            Assert.AreEqual(CheatGateResult.InvalidGrant, Unlock(core));
            Assert.IsFalse(core.IsUnlocked);
            Assert.IsFalse(_store.Data.ContainsKey(GrantKey));
            Assert.IsTrue(_log.Exists(line => line.Contains("GRANT_PRIVATE_KEY")));

            _http.Reply = Ok("{\"granted\":true}");
            Assert.AreEqual(CheatGateResult.InvalidGrant, Unlock(core), "granted without a grant");
        }

        [TestCase(429, CheatGateResult.RateLimited)]
        [TestCase(500, CheatGateResult.ServerError)]
        [TestCase(503, CheatGateResult.ServerError)]
        [TestCase(400, CheatGateResult.ServerError)]
        [TestCase(404, CheatGateResult.ServerError)]
        public void HttpErrors(int status, CheatGateResult expected)
        {
            _http.Reply = new CheatGateHttpResponse { Status = status, Body = "{\"error\":\"x\"}" };
            Assert.AreEqual(expected, Unlock(Core()));
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("<html>")]
        [TestCase("{\"error\":\"unavailable\"}")]
        [TestCase("{\"granted\":\"yes\"}")]
        public void UnexpectedBodies(string body)
        {
            _http.Reply = Ok(body);
            Assert.AreEqual(CheatGateResult.ServerError, Unlock(Core()));
        }

        [Test]
        public void NoNetwork()
        {
            _http.Reply = new CheatGateHttpResponse { NetworkError = true };
            Assert.AreEqual(CheatGateResult.Offline, Unlock(Core()));
        }

        [Test]
        public void ConcurrentTapsShareOneRequest()
        {
            _http.Deferred = true;
            var core = Core();
            var results = new List<CheatGateResult>();
            core.TryUnlock(results.Add);
            core.TryUnlock(results.Add);
            Assert.IsTrue(core.IsChecking);
            Assert.AreEqual(1, _http.Urls.Count);
            Assert.IsEmpty(results);
            _http.Complete(Ok(GrantedBody));
            Assert.AreEqual(new[] { CheatGateResult.Unlocked, CheatGateResult.Unlocked }, results.ToArray());
            Assert.IsFalse(core.IsChecking);
        }

        [Test]
        public void TapDuringABackgroundRecheckWaitsForIt()
        {
            _http.Reply = Ok(GrantedBody);
            Unlock(Core());
            _now = V.ExpiresAt + 10; // stored grant expired by the clock -> background re-check at Start
            _http.Deferred = true;
            var core = Core();
            core.Start();
            Assert.AreEqual(2, _http.Urls.Count);
            CheatGateResult? result = null;
            core.TryUnlock(r => result = r);
            Assert.AreEqual(2, _http.Urls.Count, "joins the running re-check");
            _http.Complete(Ok(NotListedBody));
            Assert.AreEqual(CheatGateResult.NotListed, result);
        }

        [Test]
        public void TransportThatThrowsIsAServerErrorAndRecovers()
        {
            _http.ThrowOnGet = true;
            var core = Core();
            Assert.AreEqual(CheatGateResult.ServerError, Unlock(core));
            Assert.IsFalse(core.IsChecking);
            _http.ThrowOnGet = false;
            _http.Reply = Ok(GrantedBody);
            Assert.AreEqual(CheatGateResult.Unlocked, Unlock(core));
        }

        [Test]
        public void TransportAnsweringTwiceIsIgnoredTheSecondTime()
        {
            _http.Deferred = true;
            var core = Core();
            var results = new List<CheatGateResult>();
            core.TryUnlock(results.Add);
            var callback = _http.Pending;
            callback(Ok(GrantedBody));
            callback(Ok(NotListedBody));
            Assert.AreEqual(new[] { CheatGateResult.Unlocked }, results.ToArray());
            Assert.IsTrue(core.IsUnlocked);
        }

        [Test]
        public void ThrowingCallbacksAndHandlersDoNotBreakTheGate()
        {
            _http.Deferred = true;
            var core = Core();
            core.Changed += _ => throw new InvalidOperationException("handler");
            var afterThrowingHandler = false;
            core.Changed += _ => afterThrowingHandler = true;
            var second = false;
            core.TryUnlock(_ => throw new InvalidOperationException("callback"));
            core.TryUnlock(_ => second = true);
            _http.Complete(Ok(GrantedBody));
            Assert.IsTrue(second);
            Assert.IsTrue(afterThrowingHandler, "a throwing Changed handler must not stop the next one");
            Assert.IsTrue(core.IsUnlocked);
            Assert.AreEqual(2, _log.FindAll(line => line.Contains("threw")).Count);
        }

        [Test]
        public void StorageFailuresAreLoggedNotThrown()
        {
            _store.Throw = true;
            _http.Reply = Ok(GrantedBody);
            var core = Core();
            Assert.AreEqual(CheatGateResult.Unlocked, Unlock(core));
            Assert.IsTrue(core.IsUnlocked, "unlocked for this session even if it cannot be saved");
            Assert.IsTrue(_log.Exists(line => line.Contains("storage")));
        }

        [TestCase("SM:007")]
        [TestCase("")]
        [TestCase(null)]
        [TestCase("-SM")]
        public void InvalidProjectIsNotConfigured(string project)
        {
            Assert.AreEqual(CheatGateResult.NotConfigured, Unlock(Core(project)));
            Assert.AreEqual(0, _http.Urls.Count);
        }

        [Test]
        public void MissingEndpointIsNotConfigured()
        {
            Assert.AreEqual(CheatGateResult.NotConfigured, Unlock(Core(endpoint: " ")));
            Assert.AreEqual(0, _http.Urls.Count);
        }

        [Test]
        public void NoDeviceId()
        {
            Assert.AreEqual(CheatGateResult.NoDeviceId, Unlock(Core(device: "")));
            Assert.AreEqual(0, _http.Urls.Count);
        }

        [Test]
        public void ProjectIsNormalizedAndEndpointSlashTrimmed()
        {
            _http.Reply = Ok(NotListedBody);
            var core = Core(" sm007 ", "7f3a-9c21-b04e", "https://cheat.test/");
            Assert.AreEqual("SM007", core.Project);
            Assert.AreEqual("7F3A9C21B04E", core.DeviceId);
            Unlock(core);
            Assert.AreEqual(CheckUrl, _http.Urls[0]);
        }

        [TestCase("sm007", "SM007")]
        [TestCase("M1", "M1")]
        [TestCase("ezg_m-1", "EZG_M-1")]
        [TestCase("SM 007", "")]
        [TestCase("_SM", "")]
        [TestCase("ĐM1", "")]
        [TestCase("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456", "")]
        public void NormalizeProject(string value, string expected)
        {
            Assert.AreEqual(expected, CheatGateCore.NormalizeProject(value));
        }

        [Test]
        public void RecheckThatExtendsAnUnlockedPhoneDoesNotRaiseChanged()
        {
            _http.Reply = Ok(GrantedBody);
            Unlock(Core());
            _now += 31 * 60;
            _changes.Clear();
            var next = Core();
            next.Start();
            Assert.AreEqual(2, _http.Urls.Count);
            Assert.IsTrue(next.IsUnlocked);
            Assert.IsEmpty(_changes);
        }

        [Test]
        public void AReplayedOrNonceLessAnswerIsRefused()
        {
            _http.Reply = Ok("{\"granted\":true,\"grant\":\"" + V.Replayed + "\"}");
            var core = Core();
            Assert.AreEqual(CheatGateResult.InvalidGrant, Unlock(core));
            Assert.IsFalse(core.IsUnlocked);
            Assert.IsFalse(_store.Data.ContainsKey(GrantKey));
            Assert.IsTrue(_log.Exists(line => line.Contains("replayed")));

            _http.Reply = Ok("{\"granted\":true,\"grant\":\"" + V.Valid + "\"}");
            Assert.AreEqual(CheatGateResult.InvalidGrant, Unlock(core), "a grant without the nonce");
            Assert.IsFalse(core.IsUnlocked);
        }

        [Test]
        public void AReplayedAnswerCannotKeepARevokedPhoneUnlocked()
        {
            _http.Reply = Ok(GrantedBody);
            Unlock(Core());
            _now += 31 * 60;
            // The phone was removed; a proxy replays the answer recorded earlier.
            _http.Reply = Ok(GrantedBody);
            _nonce = () => "ANOTHERNONCE0123456789";
            var next = Core();
            next.Start();
            Assert.AreEqual(2, _http.Urls.Count);
            Assert.IsTrue(next.IsUnlocked, "still the stored grant, by the clock");
            _now = V.ExpiresAt;
            Assert.IsFalse(next.IsUnlocked, "but no longer than that grant's own expiry");
        }

        [Test]
        public void TheSessionLastsTheGrantsDurationFromItsArrival()
        {
            _now = V.ExpiresAt + 30 * 86400; // clock a month ahead of the server
            _http.Reply = Ok(GrantedBody);
            var core = Core();
            Assert.AreEqual(CheatGateResult.Unlocked, Unlock(core));
            _now += 7 * 86400 - 1;
            Assert.IsTrue(core.IsUnlocked);
            _now += 1;
            Assert.IsFalse(core.IsUnlocked, "7 days after it arrived");
        }

        [Test]
        public void RecheckOnResume()
        {
            var silent = Core();
            silent.Start();
            silent.Recheck();
            Assert.AreEqual(0, _http.Urls.Count, "a phone without a grant sends nothing on resume");

            _http.Reply = Ok(GrantedBody);
            var core = Core();
            Unlock(core);
            _changes.Clear();
            _now += 10 * 60;
            core.Recheck();
            Assert.AreEqual(1, _http.Urls.Count, "checked 10 minutes ago: nothing");

            _now += 21 * 60;
            _http.Reply = Ok(NotListedBody);
            core.Recheck();
            Assert.AreEqual(2, _http.Urls.Count);
            Assert.IsFalse(core.IsUnlocked);
            Assert.AreEqual(new[] { false }, _changes.ToArray());

            _now += 3600;
            core.Recheck();
            Assert.AreEqual(2, _http.Urls.Count, "revoked: back to silence");
        }

        [Test]
        public void RecheckReportsAnExpiryThatHappenedInTheBackground()
        {
            _http.Reply = Ok(GrantedBody);
            var core = Core();
            Unlock(core);
            _changes.Clear();
            _now += 8 * 86400;
            _http.Reply = new CheatGateHttpResponse { NetworkError = true };
            core.Recheck();
            Assert.AreEqual(new[] { false }, _changes.ToArray(), "expired while in the background, no network to renew");
            core.Recheck();
            Assert.AreEqual(new[] { false }, _changes.ToArray(), "reported once");
        }

        [Test]
        public void RecheckBeforeStartOrWhileCheckingDoesNothing()
        {
            var core = Core();
            core.Recheck();
            Assert.AreEqual(0, _http.Urls.Count);

            _http.Reply = Ok(GrantedBody);
            Unlock(core);
            _now += 31 * 60;
            _http.Deferred = true;
            core.Recheck();
            core.Recheck();
            Assert.AreEqual(2, _http.Urls.Count, "one re-check at a time");
        }

        [Test]
        public void NegativeCheckedTimestampForcesARecheck()
        {
            _http.Reply = Ok(GrantedBody);
            Unlock(Core());
            _store.Data[CheckedKey] = long.MinValue.ToString();
            var next = Core();
            next.Start();
            Assert.AreEqual(2, _http.Urls.Count);
        }

        [TestCase(null)]
        [TestCase("short")]
        [TestCase("has spaces in it 0123")]
        public void ABrokenNonceGeneratorSendsNothing(string nonce)
        {
            _nonce = () => nonce;
            Assert.AreEqual(CheatGateResult.ServerError, Unlock(Core()));
            Assert.AreEqual(0, _http.Urls.Count);
        }

        [Test]
        public void ANonceGeneratorThatThrowsSendsNothing()
        {
            _nonce = () => throw new InvalidOperationException("no entropy");
            var core = Core();
            Assert.AreEqual(CheatGateResult.ServerError, Unlock(core));
            Assert.AreEqual(0, _http.Urls.Count);
            Assert.IsFalse(core.IsChecking);
        }

        [Test]
        public void RandomNonceIs32HexDigitsAndChanges()
        {
            var a = CheatGateCore.RandomNonce();
            var b = CheatGateCore.RandomNonce();
            StringAssert.IsMatch("^[0-9a-f]{32}$", a);
            Assert.AreNotEqual(a, b);
        }

        private sealed class MemoryStore : ICheatGateStore
        {
            public readonly Dictionary<string, string> Data = new Dictionary<string, string>();
            public bool Throw;

            public string Get(string key)
            {
                if (Throw) throw new InvalidOperationException("storage read");
                return Data.TryGetValue(key, out var value) ? value : null;
            }

            public void Set(string key, string value)
            {
                if (Throw) throw new InvalidOperationException("storage write");
                Data[key] = value;
            }

            public void Delete(string key)
            {
                if (Throw) throw new InvalidOperationException("storage delete");
                Data.Remove(key);
            }
        }

        private sealed class FakeTransport : ICheatGateTransport
        {
            public readonly List<string> Urls = new List<string>();
            public CheatGateHttpResponse Reply = new CheatGateHttpResponse { NetworkError = true };
            public bool Deferred;
            public bool ThrowOnGet;
            public Action<CheatGateHttpResponse> Pending;

            public void Get(string url, Action<CheatGateHttpResponse> done)
            {
                if (ThrowOnGet) throw new InvalidOperationException("no transport");
                Urls.Add(url);
                if (Deferred) Pending = done;
                else done(Reply);
            }

            public void Complete(CheatGateHttpResponse response)
            {
                var pending = Pending;
                Pending = null;
                pending(response);
            }
        }
    }
}
