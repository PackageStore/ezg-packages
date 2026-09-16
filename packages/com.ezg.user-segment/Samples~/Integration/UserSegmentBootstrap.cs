using System.Collections.Generic;
using Ezg.Feature.Shared.Config;
using Ezg.UserSegment;
using TigerForge;
using UnityEngine;

namespace Ezg.Feature.System.UserSegment
{
    /// <summary>
    ///     Điểm wiring duy nhất giữa game và SDK User Segmentation. Module TỰ khởi động: đăng ký lắng nghe hook generic
    ///     của core (<c>EventName.PlayerDataLoaded</c>, <c>OnShowFeature</c>, <c>PurchaseOnlineRequested</c>,
    ///     <c>IapTransactionGranted</c>, <c>AdRewardedCompleted</c>, <c>AdInterstitialShown</c>) ngay khi assembly load.
    ///     Core không gọi gì vào module; gỡ thư mục UserSegment là game vẫn chạy.
    /// </summary>
    public static class UserSegmentBootstrap
    {
        public const string DEV_CONFIG_RESOURCE = "UserSegmentDevConfig";
        private const string DEFAULT_ENV = "prod";

        /// <summary>Project có knob độ khó runtime thì gán trước khi PlayerDataLoaded để đăng ký executor CHANGE_DIFFICULTY.</summary>
        public static IDifficultyKnob DifficultyKnob;

        private static bool _registered;
        private static bool _initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            if (_registered) return;
            _registered = true;
            EventManager.StartListening(EventName.PlayerDataLoaded, Init);
            EventManager.StartListening(nameof(EventName.OnShowFeature), UserSegmentScreens.OnFeatureShown);
            EventManager.StartListening(EventName.PurchaseOnlineRequested, UserSegmentPurchaseBridge.OnPurchaseRequested);
            EventManager.StartListening(EventName.IapTransactionGranted, UserSegmentPurchaseBridge.OnTransactionGranted);
            EventManager.StartListening(EventName.AdRewardedCompleted, OnAdRewarded);
            EventManager.StartListening(EventName.AdInterstitialShown, OnAdInterstitial);
            if (Debug.isDebugBuild || Application.isEditor) Debug.Log("[UserSegment] Bootstrap: đã đăng ký hook, chờ PlayerDataLoaded");
        }

        /// <summary>Khởi tạo SDK. Tự chạy khi core phát PlayerDataLoaded; gọi tay cũng được (idempotent).</summary>
        public static void Init()
        {
            if (_initialized || SegmentationSdk.IsInitialized) return;
            _initialized = true;

            var catalog = UserSegmentCatalog.Current;
            var debugBuild = Debug.isDebugBuild || Application.isEditor;
            var env = string.IsNullOrEmpty(catalog?.Env) ? DEFAULT_ENV : catalog.Env;
            var gameId = string.IsNullOrEmpty(catalog?.GameId) ? UserSegmentCatalog.DEFAULT_GAME_ID : catalog.GameId;
            if (debugBuild)
                Debug.Log($"[UserSegment] Bootstrap.Init: game={gameId} env={env} url={(string.IsNullOrEmpty(catalog?.ConfigBaseUrl) ? "(rỗng)" : catalog.ConfigBaseUrl)} " +
                          $"catalog={(catalog != null ? "có" : "KHÔNG có UserSegmentCatalog")} difficulty_knob={(DifficultyKnob != null ? "có" : "không")}");

            SegmentationSdk.RegisterExecutor(new GiveRewardExecutor());
            SegmentationSdk.RegisterExecutor(new ShowPopupExecutor());
            SegmentationSdk.RegisterExecutor(new ShowOfferExecutor());
            SegmentationSdk.RegisterExecutor(new ScheduleNotificationExecutor());
            if (DifficultyKnob != null) SegmentationSdk.RegisterExecutor(new ChangeDifficultyExecutor(DifficultyKnob));

            SegmentationSdk.Initialize(new SdkOptions
            {
                GameId = gameId,
                Env = env,
                ConfigBaseUrl = catalog?.ConfigBaseUrl,
                // Token chỉ có ý nghĩa với env ≠ prod và không được đi vào release build — §8.8
                ConfigToken = debugBuild ? catalog?.ConfigToken : null,
                Tracking = new UserSegmentTrackingSink(),
                ActionHistoryStore = UserSegmentPlayerData.Module,
                SeedProvider = UserSegmentSeedProvider.Build,
                Rewards = catalog != null ? catalog.RewardIds() : new string[0],
                Screens = UserSegmentScreens.All(),
                CustomEvents = catalog != null ? catalog.CustomEvents : new string[0],
                CustomState = catalog != null ? catalog.CustomStateMap() : new Dictionary<string, CustomType>(),
                DebugBuild = debugBuild,
                DevFallbackEnvelope = debugBuild ? LoadDevConfig : null
            });
        }

        private static void OnAdRewarded()
        {
            if (!SegmentationSdk.IsInitialized) return;
            SegmentationSdk.AdRewarded(EventManager.GetData<string>(EventName.AdRewardedCompleted) ?? string.Empty);
        }

        private static void OnAdInterstitial()
        {
            if (!SegmentationSdk.IsInitialized) return;
            SegmentationSdk.AdInterstitial(EventManager.GetData<string>(EventName.AdInterstitialShown) ?? string.Empty);
        }

        /// <summary>Envelope dev trong Resources (TextAsset) — chỉ dev build / Editor, dùng khi không có URL / cache.</summary>
        private static string LoadDevConfig()
        {
            var ta = Resources.Load<TextAsset>(DEV_CONFIG_RESOURCE);
            return ta != null ? ta.text : null;
        }

        /// <summary>Bật / tắt overlay debug (§11.3). Không làm gì ở release.</summary>
        public static void ToggleDebugOverlay()
        {
            Debug.Log($"[UserSegment] ToggleDebugOverlay (overlay {(SegmentationSdk.Debug != null ? "có" : "KHÔNG — không phải dev build hoặc SDK chưa init")})");
            SegmentationSdk.Debug?.Toggle();
        }
    }
}
