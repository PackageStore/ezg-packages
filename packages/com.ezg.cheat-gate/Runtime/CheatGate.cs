using System;
using UnityEngine;
using UnityEngine.Networking;

namespace Ezg.CheatGate
{
    /// <summary>
    ///     Who may open cheats in this build. Usage:
    ///     <code>
    ///     // once, at startup (splash / bootstrap):
    ///     CheatGate.Initialize("SM007");
    ///     CheatGate.Changed += unlocked => { if (!unlocked) GameSystems.DisableCheat(); }; // revoked / expired
    ///
    ///     // anywhere a cheat is shown or used: if (!CheatGate.IsUnlocked) ...
    /// 
    ///     // the 10th tap on the version label:
    ///     CheatGate.TryUnlock(result =>
    ///     {
    ///         if (result == CheatGateResult.Unlocked) GameSystems.isCheat = true; // the game's own switch
    ///         else if (result == CheatGateResult.NotListed) ShowMessage("Device ID: " + CheatGate.DeviceIdDisplay);
    ///         else if (result == CheatGateResult.Offline) ShowMessage("Cần mạng để kiểm tra máy test.");
    ///     });
    ///     </code>
    ///     The project code is the game's code in BugHub (the one test phones are listed under with the ezg MCP tool
    ///     test_device_add). Players who never tap 10 times never send a request.
    /// </summary>
    public static class CheatGate
    {
        public const string DefaultEndpoint = "https://ezg-cheat-gate.developer-a1f.workers.dev";

        /// <summary>In the Unity Editor cheats are always allowed (default true).</summary>
        public static bool UnlockInEditor = true;

        /// <summary>Development Builds (Debug.isDebugBuild) allow cheats without a check (default false: they go to QA too).</summary>
        public static bool UnlockInDevelopmentBuild = false;

        private static CheatGateCore _core;
        private static string _deviceId;
        private static bool _focusHooked;

        /// <summary>Whenever an answer from the server turns cheats on or off for this phone (false = revoked: turn cheats off).</summary>
        public static event Action<bool> Changed;

        public static bool IsInitialized => _core != null;

        /// <summary>The project code in use ("" before <see cref="Initialize" /> or when it was invalid).</summary>
        public static string Project => _core?.Project ?? string.Empty;

        /// <summary>12 hex digits ("" when the platform has no device identifier).</summary>
        public static string DeviceId => _deviceId ?? (_deviceId = CheatGateDeviceId.FromIdentifier(SystemInfo.deviceUniqueIdentifier));

        /// <summary>XXXX-XXXX-XXXX, what QA sends to whoever runs test_device_add; "N/A" without an identifier.</summary>
        public static string DeviceIdDisplay => CheatGateDeviceId.Display(DeviceId);

        /// <summary>A check is on the way.</summary>
        public static bool IsChecking => _core != null && _core.IsChecking;

        /// <summary>Cheats may be opened on this phone now.</summary>
        public static bool IsUnlocked => IsOverridden || (_core != null && _core.IsUnlocked);

        private static bool IsOverridden
        {
            get
            {
#if UNITY_EDITOR
                return UnlockInEditor;
#else
                return UnlockInDevelopmentBuild && Debug.isDebugBuild;
#endif
            }
        }

        /// <summary>
        ///     Call once at startup, on the main thread. A phone that already holds a grant re-checks it in the background
        ///     (at launch and when the game comes back to the foreground, at most every 30 minutes); any other phone sends
        ///     nothing. A second call with the same project does nothing; with another project it is refused (one game, one
        ///     code) unless the first code was invalid.
        /// </summary>
        /// <param name="projectCode">The game's BugHub project code, e.g. "SM007".</param>
        /// <param name="endpoint">Override of <see cref="DefaultEndpoint" /> (tests, a staging Worker).</param>
        public static void Initialize(string projectCode, string endpoint = null)
        {
            var project = CheatGateCore.NormalizeProject(projectCode);
            var url = string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint;
            if (_core != null)
            {
                if (_core.Project == project) return;
                if (_core.IsConfigured)
                {
                    Debug.LogError($"[CheatGate] already initialized with {_core.Project}; Initialize(\"{projectCode}\") ignored.");
                    return;
                }

                _core.Changed -= RaiseChanged;
            }

            if (project.Length == 0) Debug.LogError($"[CheatGate] invalid project code \"{projectCode}\": cheats stay locked.");
            _core = new CheatGateCore(url, project, DeviceId, CheatGateKeys.Trusted, new PlayerPrefsStore(), new UnityWebRequestTransport(),
                () => DateTimeOffset.UtcNow.ToUnixTimeSeconds(), message => Debug.LogWarning(message));
            _core.Changed += RaiseChanged;
            if (!_focusHooked)
            {
                Application.focusChanged += OnFocusChanged;
                _focusHooked = true;
            }

            _core.Start();
        }

        /// <summary>
        ///     The 10th tap. Already unlocked: <see cref="CheatGateResult.Unlocked" /> at once. Otherwise one request to the
        ///     server; <paramref name="done" /> runs on the main thread when it answers (10 s timeout).
        /// </summary>
        public static void TryUnlock(Action<CheatGateResult> done)
        {
            if (IsOverridden)
            {
                done?.Invoke(CheatGateResult.Unlocked);
                return;
            }

            if (_core == null)
            {
                Debug.LogError("[CheatGate] TryUnlock before CheatGate.Initialize(projectCode).");
                done?.Invoke(CheatGateResult.NotConfigured);
                return;
            }

            _core.TryUnlock(done);
        }

        /// <summary>Back in the foreground: report an expiry, re-check a held grant whose last check is old.</summary>
        private static void OnFocusChanged(bool focused)
        {
            if (focused) _core?.Recheck();
        }

        private static void RaiseChanged(bool unlocked)
        {
            // Under the Editor / Development Build override cheats stay open whatever the server says.
            if (IsOverridden) return;
            var handlers = Changed;
            if (handlers == null) return;
            foreach (var handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<bool>)handler)(unlocked);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        /// <summary>Statics survive play sessions when the Editor's domain reload is off: start each one clean.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (_core != null) _core.Changed -= RaiseChanged;
            _core = null;
            _deviceId = null;
            Changed = null;
            if (_focusHooked) Application.focusChanged -= OnFocusChanged;
            _focusHooked = false;
        }

        private sealed class PlayerPrefsStore : ICheatGateStore
        {
            public string Get(string key)
            {
                return PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
            }

            public void Set(string key, string value)
            {
                PlayerPrefs.SetString(key, value);
                PlayerPrefs.Save();
            }

            public void Delete(string key)
            {
                if (!PlayerPrefs.HasKey(key)) return;
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        }

        private sealed class UnityWebRequestTransport : ICheatGateTransport
        {
            private const int TimeoutSeconds = 10;

            public void Get(string url, Action<CheatGateHttpResponse> done)
            {
                UnityWebRequest request = null;
                UnityWebRequestAsyncOperation operation;
                try
                {
                    request = UnityWebRequest.Get(url);
                    request.timeout = TimeoutSeconds;
                    operation = request.SendWebRequest();
                }
                catch (Exception e)
                {
                    // A configuration problem (bad URL, insecure http blocked...), not a missing network.
                    Debug.LogWarning("[CheatGate] the check could not be sent: " + e.Message);
                    request?.Dispose();
                    done(new CheatGateHttpResponse { NetworkError = false, Status = -1 });
                    return;
                }

                operation.completed += _ =>
                {
                    var response = new CheatGateHttpResponse { NetworkError = true };
                    try
                    {
                        var noAnswer = request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.DataProcessingError;
                        response = new CheatGateHttpResponse
                        {
                            NetworkError = noAnswer || request.responseCode == 0,
                            Status = request.responseCode,
                            Body = request.downloadHandler != null ? request.downloadHandler.text : null,
                        };
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[CheatGate] reading the answer failed: " + e.Message);
                    }
                    finally
                    {
                        request.Dispose();
                    }

                    done(response);
                };
            }
        }
    }
}
