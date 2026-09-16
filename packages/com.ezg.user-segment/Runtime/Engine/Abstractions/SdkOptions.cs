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

        public const int MAX_CUSTOM_EVENTS = 10;
    }
}
