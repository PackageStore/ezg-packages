using System;
using System.Collections.Generic;
using System.Threading;
using Ezg.UserSegment.Engine;
using UnityEngine;

namespace Ezg.UserSegment
{
    /// <summary>Public API cho game — §C.6.2. Mọi API chạy trên main thread; SESSION_START / END do SDK tự emit.</summary>
    public static class SegmentationSdk
    {
        private static SegEngine _engine;
        private static SegSdkBehaviour _behaviour;
        private static readonly List<IActionExecutor> PendingExecutors = new List<IActionExecutor>();
        private static int _mainThreadId;

        public static bool IsInitialized => _engine != null && _engine.Initialized;
        public static string SdkVersion => SegEngine.SDK_VERSION;

        /// <summary>Engine core — dùng cho debug overlay, test và integration (last-touch attribution).</summary>
        public static SegEngine Engine => _engine;

        /// <summary>Debug overlay — chỉ dev build; release trả null — §11.3.</summary>
        public static ISdkDebug Debug { get; private set; }

        #region Init

        /// <summary>Khởi tạo — gọi một lần, main thread, càng sớm càng tốt sau khi game biết GameId / Env.</summary>
        public static void Initialize(SdkOptions options)
        {
            if (_engine != null)
            {
                UnityEngine.Debug.LogWarning("[UserSegment] Initialize called twice — ignored");
                return;
            }

            if (options == null) throw new ArgumentNullException(nameof(options));
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            options.Storage ??= new FileStateStorage();
            options.Fetcher ??= new UnityConfigFetcher();
            options.Clock ??= new UnityTimeSource();
            options.Logger ??= new UnityLogger();
            options.Limits = (options.Limits ?? SdkLimits.Default).Sanitized();
            if (options.CustomEvents != null && options.CustomEvents.Length > options.Limits.MaxCustomEvents)
                UnityEngine.Debug.LogError($"[UserSegment] CustomEvents ({options.CustomEvents.Length}) > Limits.MaxCustomEvents ({options.Limits.MaxCustomEvents}) — manifest whitelist quá lớn");

            if (options.DebugBuild)
                UnityEngine.Debug.Log($"[UserSegment] Initialize: game={options.GameId} env={options.Env} url={(string.IsNullOrEmpty(options.ConfigBaseUrl) ? "(rỗng)" : options.ConfigBaseUrl)} " +
                                      $"history_store={(options.ActionHistoryStore != null ? "có" : "KHÔNG")} seed_provider={(options.SeedProvider != null ? "có" : "không")} " +
                                      $"rewards={options.Rewards?.Length ?? 0} screens={options.Screens?.Length ?? 0} custom_events={options.CustomEvents?.Length ?? 0} custom_state={options.CustomState?.Count ?? 0} " +
                                      $"pending_executors={PendingExecutors.Count} dev_fallback={(options.DevFallbackEnvelope != null ? "có" : "không")}");
            _engine = new SegEngine(options) { IsMainThread = () => Thread.CurrentThread.ManagedThreadId == _mainThreadId };
            foreach (var ex in PendingExecutors) _engine.RegisterExecutor(ex);
            PendingExecutors.Clear();
            _engine.Initialize();

            var go = new GameObject("[UserSegmentSdk]") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            _behaviour = go.AddComponent<SegSdkBehaviour>();
            _behaviour.Begin(_engine, options);
            if (options.DebugBuild) Debug = go.AddComponent<SegDebugOverlay>().Bind(_engine);
        }

        /// <summary>Đăng ký executor — TRƯỚC Initialize để manifest.actions đúng ngay lần load config đầu.</summary>
        public static void RegisterExecutor(IActionExecutor executor)
        {
            if (executor == null) return;
            if (_engine == null) PendingExecutors.Add(executor);
            else _engine.RegisterExecutor(executor);
            if (Debug != null || UnityEngine.Debug.isDebugBuild) UnityEngine.Debug.Log($"[UserSegment] RegisterExecutor {executor.ActionType} ({executor.GetType().Name}){(_engine == null ? " (chờ Initialize)" : "")}");
        }

        #endregion

        #region Seed + history

        public static bool NeedsSeed => _engine != null && _engine.NeedsSeed;
        public static void Seed(SeedData data) => Guard()?.Seed(data);
        public static void ImportActionHistory(string blob) => Guard()?.ImportActionHistory(blob);
        public static void MarkActionUsed(string actionId, long atEpochSeconds) => Guard()?.MarkActionUsed(actionId, atEpochSeconds);

        #endregion

        #region Events — §4.1

        public static void ProgressStart(long unitId) => Guard()?.Enqueue(SegEvent.Progress(SegEventType.PROGRESS_START, unitId));
        public static void ProgressComplete(long unitId, double durationS) => Guard()?.Enqueue(SegEvent.Progress(SegEventType.PROGRESS_COMPLETE, unitId, durationS));
        public static void ProgressFail(long unitId, double durationS) => Guard()?.Enqueue(SegEvent.Progress(SegEventType.PROGRESS_FAIL, unitId, durationS));
        public static void ProgressQuit(long unitId, double durationS) => Guard()?.Enqueue(SegEvent.Progress(SegEventType.PROGRESS_QUIT, unitId, durationS));

        /// <summary>Chỉ từ luồng mua mới thành công — KHÔNG gọi từ restore purchase — §4.1.</summary>
        public static void Purchase(string productId, double usd, string transactionId, string executionId = null) =>
            Guard()?.Enqueue(SegEvent.Purchase(productId, usd, transactionId, executionId));

        public static void AdRewarded(string placement) => Guard()?.Enqueue(SegEvent.Ad(SegEventType.AD_REWARDED, placement ?? string.Empty));
        public static void AdInterstitial(string placement) => Guard()?.Enqueue(SegEvent.Ad(SegEventType.AD_INTERSTITIAL, placement ?? string.Empty));
        public static void ScreenOpen(string screenId) => Guard()?.Enqueue(SegEvent.Screen(screenId));
        public static void CustomEvent(string name) => Guard()?.Enqueue(SegEvent.Custom(name));
        public static void SetCustomState(string key, double value) => Guard()?.Enqueue(SegEvent.CustomState(key, SegValue.Number(value)));
        public static void SetCustomState(string key, bool value) => Guard()?.Enqueue(SegEvent.CustomState(key, SegValue.Bool(value)));
        public static void SetCustomState(string key, string value) => Guard()?.Enqueue(SegEvent.CustomState(key, SegValue.String(value ?? string.Empty)));

        #endregion

        /// <summary>Manifest JSON để paste vào repo config — §8.5.</summary>
        public static string ExportManifestJson() => _engine?.ExportManifestJson();

        /// <summary>Last-touch SHOW_OFFER (execution_id, offer_id, at) trong 7 ngày, để game gắn vào tracking purchase — §8.7.</summary>
        public static LastOffer LastOfferAttribution() => _engine?.LastOfferAttribution();

        private static SegEngine Guard()
        {
            if (_engine != null && _engine.Initialized) return _engine;
            UnityEngine.Debug.LogError("[UserSegment] API called before Initialize — dropped");
            return null;
        }
    }

    /// <summary>Điều khiển debug overlay — §11.3.</summary>
    public interface ISdkDebug
    {
        bool Visible { get; set; }
        void Toggle();
    }
}
