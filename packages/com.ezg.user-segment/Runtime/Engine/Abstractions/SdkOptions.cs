using System;
using System.Collections.Generic;

namespace Ezg.UserSegment
{
    /// <summary>Kiểu của custom state khai trong manifest — §8.5.</summary>
    public enum CustomType
    {
        Number,
        Bool,
        String
    }

    /// <summary>Dữ liệu seed cho user hiện hữu — §4.6. Field không truyền giữ 0 (= default).</summary>
    public sealed class SeedData
    {
        public long InstallAt;
        public long LastActiveAt;
        public long PurchaseCount;
        public double TotalSpendUsd;
        public long FirstPurchaseAt;
        public long LastPurchaseAt;
        public long ProgressMax;
        public long SessionCount;
    }

    /// <summary>
    ///     Giới hạn tham số action + manifest do GAME khai (§C.1.5, §C.6.7). Default = giá trị spec v0.4. Được export trong
    ///     manifest (<c>limits</c>) để CLI / Worker validator dùng đúng range của game đó thay vì hằng số chung.
    /// </summary>
    public sealed class SdkLimits
    {
        public const int DEFAULT_REWARD_AMOUNT_MAX = 1000;
        public const int DEFAULT_DIFFICULTY_DELTA_MAX = 2;
        public const int DEFAULT_MAX_CUSTOM_EVENTS = 10;

        /// <summary>GIVE_REWARD.params.amount ∈ [1, RewardAmountMax].</summary>
        public int RewardAmountMax = DEFAULT_REWARD_AMOUNT_MAX;

        /// <summary>CHANGE_DIFFICULTY.params.delta ∈ [−DifficultyDeltaMax, DifficultyDeltaMax], ≠ 0.</summary>
        public int DifficultyDeltaMax = DEFAULT_DIFFICULTY_DELTA_MAX;

        /// <summary>Số tên CUSTOM_EVENT tối đa trong manifest whitelist.</summary>
        public int MaxCustomEvents = DEFAULT_MAX_CUSTOM_EVENTS;

        public static SdkLimits Default => new SdkLimits();

        /// <summary>Chuẩn hoá: giá trị &lt; 1 quay về default để config không bao giờ bị từ chối vì limits vô nghĩa.</summary>
        public SdkLimits Sanitized()
        {
            return new SdkLimits
            {
                RewardAmountMax = RewardAmountMax < 1 ? DEFAULT_REWARD_AMOUNT_MAX : RewardAmountMax,
                DifficultyDeltaMax = DifficultyDeltaMax < 1 ? DEFAULT_DIFFICULTY_DELTA_MAX : DifficultyDeltaMax,
                MaxCustomEvents = MaxCustomEvents < 1 ? DEFAULT_MAX_CUSTOM_EVENTS : MaxCustomEvents
            };
        }
    }

    /// <summary>Tuỳ chọn khởi tạo SDK — §C.6.3.</summary>
    public sealed class SdkOptions
    {
        public string GameId;
        public string Env;
        public string ConfigBaseUrl;

        /// <summary>Header X-Config-Token khi Env ≠ prod. KHÔNG đưa vào release build.</summary>
        public string ConfigToken;

        public ITrackingSink Tracking;
        public IActionHistoryStore ActionHistoryStore;

        /// <summary>Gọi trước SESSION_START đầu khi NeedsSeed; trả null = chưa có dữ liệu.</summary>
        public Func<SeedData> SeedProvider;

        public string[] Rewards = Array.Empty<string>();
        public string[] Screens = Array.Empty<string>();
        public string[] CustomEvents = Array.Empty<string>();
        public Dictionary<string, CustomType> CustomState = new Dictionary<string, CustomType>();

        /// <summary>Range amount / delta / số custom event — null = default spec. Export vào manifest.limits.</summary>
        public SdkLimits Limits = new SdkLimits();

        public int FetchTimeoutMs = 3000;
        public int RefetchAfterResumeS = 300;

        public IStateStorage Storage;
        public IConfigFetcher Fetcher;
        public ITimeSource Clock;
        public ISegLogger Logger;
        public bool DebugBuild;

        /// <summary>
        ///     JSON envelope dùng khi không có URL / fetch fail mà chưa có cache (dev only). Null = không fallback.
        /// </summary>
        public Func<string> DevFallbackEnvelope;

        /// <summary>Giữ để tương thích; giá trị hiệu lực là <see cref="Limits" />.MaxCustomEvents.</summary>
        public const int MAX_CUSTOM_EVENTS = SdkLimits.DEFAULT_MAX_CUSTOM_EVENTS;
    }
}
