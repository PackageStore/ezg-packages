using System;

namespace Ezg.UserSegment.Engine
{
    /// <summary>Event chuẩn — §4.1.</summary>
    public enum SegEventType
    {
        SESSION_START,
        SESSION_END,
        PROGRESS_START,
        PROGRESS_COMPLETE,
        PROGRESS_FAIL,
        PROGRESS_QUIT,
        PURCHASE,
        AD_REWARDED,
        AD_INTERSTITIAL,
        SCREEN_OPEN,
        CUSTOM_STATE,
        CUSTOM_EVENT
    }

    /// <summary>Một event trong queue. <see cref="At" /> = 0 → engine gán now lúc xử lý.</summary>
    public sealed class SegEvent
    {
        public SegEventType Type;
        public long At;
        public double UnitId;
        public double DurationS;
        public string ProductId;
        public double Usd;
        public string TransactionId;
        public string ExecutionId;
        public string Placement;
        public string ScreenId;
        public string Name;
        public string Key;
        public SegValue Value;

        public string Qualifier
        {
            get
            {
                switch (Type)
                {
                    case SegEventType.SCREEN_OPEN: return ScreenId;
                    case SegEventType.CUSTOM_EVENT: return Name;
                    default: return null;
                }
            }
        }

        /// <summary>Chỉ các event này chạy engine — §8.1.</summary>
        public bool IsTrigger
        {
            get
            {
                switch (Type)
                {
                    case SegEventType.SESSION_START:
                    case SegEventType.PROGRESS_COMPLETE:
                    case SegEventType.PROGRESS_FAIL:
                    case SegEventType.PROGRESS_QUIT:
                    case SegEventType.PURCHASE:
                    case SegEventType.SCREEN_OPEN:
                    case SegEventType.CUSTOM_EVENT:
                        return true;
                    default:
                        return false;
                }
            }
        }

        /// <summary>Tên trigger cho tracking: EVENT hoặc EVENT:qualifier.</summary>
        public string TriggerName => Qualifier == null ? Type.ToString() : Type + ":" + Qualifier;

        /// <summary>context.last_event — §C.8.5.</summary>
        public string LastEventName => Type == SegEventType.CUSTOM_EVENT ? "CUSTOM_EVENT:" + Name : Type.ToString();

        /// <summary>Rule `on` khớp event này: EVENT không qualifier khớp mọi qualifier; EVENT:q phải bằng q.</summary>
        public bool MatchesOn(string onEntry)
        {
            Trigger.Split(onEntry, out var ev, out var q);
            if (!string.Equals(ev, Type.ToString(), StringComparison.Ordinal)) return false;
            return q == null || q == Qualifier;
        }

        public static SegEvent Simple(SegEventType t, long at = 0) => new SegEvent { Type = t, At = at };

        public static SegEvent Progress(SegEventType t, double unitId, double durationS = 0, long at = 0) =>
            new SegEvent { Type = t, UnitId = unitId, DurationS = durationS, At = at };

        public static SegEvent Purchase(string productId, double usd, string transactionId, string executionId = null, long at = 0) =>
            new SegEvent
            {
                Type = SegEventType.PURCHASE, ProductId = productId, Usd = usd, TransactionId = transactionId,
                ExecutionId = executionId, At = at
            };

        public static SegEvent Ad(SegEventType t, string placement, long at = 0) =>
            new SegEvent { Type = t, Placement = placement, At = at };

        public static SegEvent Screen(string screenId, long at = 0) =>
            new SegEvent { Type = SegEventType.SCREEN_OPEN, ScreenId = screenId, At = at };

        public static SegEvent Custom(string name, long at = 0) =>
            new SegEvent { Type = SegEventType.CUSTOM_EVENT, Name = name, At = at };

        public static SegEvent CustomState(string key, SegValue value, long at = 0) =>
            new SegEvent { Type = SegEventType.CUSTOM_STATE, Key = key, Value = value, At = at };
    }
}
