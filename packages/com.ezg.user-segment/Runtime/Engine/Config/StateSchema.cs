using System.Collections.Generic;

namespace Ezg.UserSegment.Engine
{
    /// <summary>Mô tả một ref trong bảng §C.4.1.</summary>
    public readonly struct RefInfo
    {
        public readonly SegType Type;
        public readonly bool Windowed;
        public readonly bool IsTimestamp;

        public RefInfo(SegType type, bool windowed, bool isTimestamp)
        {
            Type = type;
            Windowed = windowed;
            IsTimestamp = isTimestamp;
        }
    }

    /// <summary>Bảng ref tĩnh của state.* và context.* — §C.4.1. Counter có window (W) lưu life / session / days.</summary>
    public static class StateSchema
    {
        public const string CUSTOM_EVENT_COUNT_PREFIX = "state.custom_event_count.";

        private static RefInfo N(bool w = false) => new RefInfo(SegType.Number, w, false);
        private static RefInfo TS() => new RefInfo(SegType.Number, false, true);
        private static RefInfo B() => new RefInfo(SegType.Bool, false, false);
        private static RefInfo S() => new RefInfo(SegType.String, false, false);

        public static readonly Dictionary<string, RefInfo> Refs = new Dictionary<string, RefInfo>
        {
            { "state.install_at", TS() },
            { "state.days_since_install", N() },
            { "state.session_count", N(true) },
            { "state.days_since_last_active", N() },
            { "state.last_active_at", TS() },
            { "state.seeded", B() },
            { "state.seeded_at", TS() },
            { "state.playtime_s", N(true) },
            { "state.progress.current", N() },
            { "state.progress.max", N() },
            { "state.attempt_count", N(true) },
            { "state.complete_count", N(true) },
            { "state.attempt_count_current_unit", N() },
            { "state.fail_count", N(true) },
            { "state.fail_streak", N() },
            { "state.win_streak", N() },
            { "state.quit_count", N(true) },
            { "state.quit_after_fail_count", N() },
            { "state.last_fail_at", TS() },
            { "state.purchase_count", N(true) },
            { "state.total_spend_usd", N() },
            { "state.first_purchase_at", TS() },
            { "state.last_purchase_at", TS() },
            { "state.ad_rewarded_count", N(true) },
            { "state.ad_interstitial_count", N(true) },
            { "context.screen", S() },
            { "context.last_event", S() },
            { "context.session_time_s", N() },
            { "context.actions_shown_today", N() },
            { "context.clock_suspect", B() },
            { "context.config_stale", B() }
        };

        /// <summary>Tên counter có window (không prefix "state."), dùng để khởi tạo bucket.</summary>
        public static readonly string[] WindowedCounters =
        {
            "session_count", "playtime_s", "attempt_count", "complete_count", "fail_count", "quit_count",
            "purchase_count", "ad_rewarded_count", "ad_interstitial_count"
        };

        /// <summary>Tra ref state.* / context.*; custom_event_count.&lt;name&gt; cần name ∈ manifest.</summary>
        public static bool TryGet(string reference, IReadOnlyCollection<string> customEvents, out RefInfo info)
        {
            if (Refs.TryGetValue(reference, out info)) return true;
            if (reference.StartsWith(CUSTOM_EVENT_COUNT_PREFIX))
            {
                var name = reference.Substring(CUSTOM_EVENT_COUNT_PREFIX.Length);
                if (customEvents != null && name.Length > 0)
                    foreach (var c in customEvents)
                        if (c == name)
                        {
                            info = N(true);
                            return true;
                        }
            }

            info = default;
            return false;
        }
    }
}
