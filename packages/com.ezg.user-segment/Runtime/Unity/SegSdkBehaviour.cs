using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Ezg.UserSegment.Engine;
using UnityEngine;

namespace Ezg.UserSegment
{
    /// <summary>
    ///     MonoBehaviour ẩn DontDestroyOnLoad: fetch config (queue chỉ mở sau khi fetch xong hoặc timeout), pause / resume /
    ///     quit, tick drain queue — §4.4, §10.2. Game không phải gọi gì.
    /// </summary>
    internal sealed class SegSdkBehaviour : MonoBehaviour
    {
        private SegEngine _engine;
        private SdkOptions _o;
        private bool _fetching;

        public void Begin(SegEngine engine, SdkOptions options)
        {
            _engine = engine;
            _o = options;
            FetchThenOpen().Forget();
        }

        private async UniTaskVoid FetchThenOpen()
        {
            await FetchAsync();
            _engine.OpenQueue();
        }

        /// <summary>Fetch một lần, không retry; fail / reject → cache → dev fallback → none.</summary>
        private async UniTask FetchAsync()
        {
            if (_fetching) return;
            _fetching = true;
            try
            {
                if (string.IsNullOrEmpty(_o.ConfigBaseUrl))
                {
                    if (_o.DebugBuild) Debug.Log("[UserSegment] Fetch: ConfigBaseUrl rỗng → bỏ fetch, dùng cache / dev fallback");
                    _engine.UseCacheOrFallback();
                    return;
                }

                var url = _o.ConfigBaseUrl + (_o.ConfigBaseUrl.Contains("?") ? "&" : "?") +
                          "game=" + Uri.EscapeDataString(_o.GameId) + "&env=" + Uri.EscapeDataString(_o.Env);
                var headers = new Dictionary<string, string>();
                if (_o.Env != "prod" && !string.IsNullOrEmpty(_o.ConfigToken)) headers["X-Config-Token"] = _o.ConfigToken;

                if (_o.DebugBuild) Debug.Log($"[UserSegment] Fetch config: GET {url} timeout {_o.FetchTimeoutMs}ms token={(headers.Count > 0 ? "có" : "không")}");
                FetchResult result;
                using (var cts = new CancellationTokenSource(_o.FetchTimeoutMs))
                {
                    try
                    {
                        var fetchTask = _o.Fetcher.FetchAsync(url, headers, _o.FetchTimeoutMs, cts.Token).AsUniTask();
                        var (timedOut, r) = await UniTask.WhenAny(fetchTask, UniTask.Delay(_o.FetchTimeoutMs + 500, ignoreTimeScale: true))
                            .ContinueWith(x => x.hasResultLeft ? (false, x.result) : (true, default));
                        result = timedOut ? FetchResult.Failed("timeout") : r;
                    }
                    catch (Exception e)
                    {
                        result = FetchResult.Failed(e.Message);
                    }
                }

                await UniTask.SwitchToMainThread();
                if (_o.DebugBuild) Debug.Log($"[UserSegment] Fetch xong: status={result.StatusCode} error={result.Error ?? "none"} body={result.Body?.Length ?? 0} ký tự");
                if (result.Error == null && result.StatusCode == 200 && !string.IsNullOrEmpty(result.Body))
                {
                    if (_engine.ApplyFreshEnvelope(result.Body)) return;
                    if (_o.DebugBuild) Debug.LogWarning("[UserSegment] Envelope tươi bị từ chối → fallback cache");
                }
                else if (_o.DebugBuild)
                {
                    Debug.LogWarning($"[UserSegment] config fetch failed: status={result.StatusCode} error={result.Error}");
                }

                _engine.UseCacheOrFallback();
            }
            finally
            {
                _fetching = false;
            }
        }

        private void Update()
        {
            _engine?.Tick();
        }

        private void OnApplicationPause(bool paused)
        {
            if (_engine == null) return;
            if (paused)
            {
                _engine.OnPause();
            }
            else if (_engine.OnResume())
            {
                if (_o.DebugBuild) Debug.Log($"[UserSegment] Resume: lần fetch cuối > {_o.RefetchAfterResumeS}s → fetch lại config");
                FetchAsync().Forget();
            }
        }

        private void OnApplicationQuit()
        {
            _engine?.OnQuit();
        }
    }
}
