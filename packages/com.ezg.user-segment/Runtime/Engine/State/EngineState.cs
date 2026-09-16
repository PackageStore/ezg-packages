using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ezg.UserSegment.Engine
{
    public sealed class ClockState
    {
        [JsonProperty("offset_s")] public long OffsetS;
        [JsonProperty("has_offset")] public bool HasOffset;
        [JsonProperty("last_event_at")] public long LastEventAt;
        [JsonProperty("last_pause_at")] public long LastPauseAt;
        [JsonProperty("foreground_anchor")] public long ForegroundAnchor;
    }

    public sealed class SessionState
    {
        [JsonProperty("open")] public bool Open;
        [JsonProperty("started_at")] public long StartedAt;
        [JsonProperty("days_since_last_active")] public long DaysSinceLastActive;
    }

    public sealed class Scalars
    {
        [JsonProperty("install_at")] public long InstallAt;
        [JsonProperty("last_active_at")] public long LastActiveAt;
        [JsonProperty("progress_current")] public double ProgressCurrent;
        [JsonProperty("progress_max")] public double ProgressMax;
        [JsonProperty("attempt_count_current_unit")] public double AttemptCountCurrentUnit;
        [JsonProperty("fail_streak")] public double FailStreak;
        [JsonProperty("win_streak")] public double WinStreak;
        [JsonProperty("quit_after_fail_count")] public double QuitAfterFailCount;
        [JsonProperty("last_fail_at")] public long LastFailAt;
        [JsonProperty("total_spend_usd")] public double TotalSpendUsd;
        [JsonProperty("first_purchase_at")] public long FirstPurchaseAt;
        [JsonProperty("last_purchase_at")] public long LastPurchaseAt;
    }

    public sealed class ContextState
    {
        [JsonProperty("screen")] public string Screen = string.Empty;
        [JsonProperty("last_event")] public string LastEvent = string.Empty;
        [JsonProperty("clock_suspect")] public bool ClockSuspect;
        [JsonProperty("config_stale")] public bool ConfigStale;
    }

    public sealed class ShownToday
    {
        [JsonProperty("day")] public long Day;
        [JsonProperty("count")] public int Count;
    }

    public sealed class TagState
    {
        [JsonProperty("value")] public bool Value;
        [JsonProperty("def_hash")] public string DefHash = string.Empty;
    }

    public sealed class EdgeState
    {
        [JsonProperty("prev")] public bool Prev;
        [JsonProperty("def_hash")] public string DefHash = string.Empty;
    }

    public sealed class AssignmentState
    {
        [JsonProperty("exp_id")] public string ExpId;
        [JsonProperty("variant")] public string Variant;
        [JsonProperty("at")] public long At;
    }

    public sealed class HoldoutState
    {
        [JsonProperty("decided")] public bool Decided;
        [JsonProperty("value")] public bool Value;
    }

    public sealed class LastOffer
    {
        [JsonProperty("execution_id")] public string ExecutionId = string.Empty;
        [JsonProperty("offer_id")] public string OfferId = string.Empty;
        [JsonProperty("at")] public long At;
    }

    /// <summary>Tầng vận hành SDK (theo thiết bị) — §4.5, file state §C.10.1.</summary>
    public sealed class EngineState
    {
        public const int SCHEMA_VERSION = 1;
        public const int TX_RING_SIZE = 50;

        [JsonProperty("schema_version")] public int SchemaVersion = SCHEMA_VERSION;
        [JsonProperty("user_id")] public string UserId = string.Empty;
        [JsonProperty("created_at")] public long CreatedAt;
        [JsonProperty("exec_seq")] public uint ExecSeq;
        [JsonProperty("seeded")] public bool Seeded;
        [JsonProperty("seeded_at")] public long SeededAt;
        [JsonProperty("clock")] public ClockState Clock = new ClockState();
        [JsonProperty("session")] public SessionState Session = new SessionState();
        [JsonProperty("scalars")] public Scalars Scalars = new Scalars();

        [JsonProperty("counters")]
        public Dictionary<string, WindowCounter> Counters = new Dictionary<string, WindowCounter>(StringComparer.Ordinal);

        [JsonProperty("custom")] public Dictionary<string, object> Custom = new Dictionary<string, object>(StringComparer.Ordinal);
        [JsonProperty("context")] public ContextState Context = new ContextState();
        [JsonProperty("shown_today")] public ShownToday ShownToday = new ShownToday();
        [JsonProperty("tags")] public Dictionary<string, TagState> Tags = new Dictionary<string, TagState>(StringComparer.Ordinal);
        [JsonProperty("edges")] public Dictionary<string, EdgeState> Edges = new Dictionary<string, EdgeState>(StringComparer.Ordinal);

        [JsonProperty("action_ts")]
        public Dictionary<string, List<long>> ActionTs = new Dictionary<string, List<long>>(StringComparer.Ordinal);

        [JsonProperty("assignments")]
        public Dictionary<string, AssignmentState> Assignments = new Dictionary<string, AssignmentState>(StringComparer.Ordinal);

        [JsonProperty("holdout")] public HoldoutState Holdout = new HoldoutState();
        [JsonProperty("exposed")] public List<string> Exposed = new List<string>();
        [JsonProperty("tx_ring")] public List<string> TxRing = new List<string>();
        [JsonProperty("last_offer")] public LastOffer LastOffer = new LastOffer();
        [JsonProperty("history_mirror")] public ActionHistory HistoryMirror = new ActionHistory();
        [JsonProperty("last_fetch_at")] public long LastFetchAt;

        /// <summary>Giá trị partition lần cuối đã đẩy lên user property (để chỉ set khi đổi) — §7.3.</summary>
        [JsonProperty("partitions")]
        public Dictionary<string, string> Partitions = new Dictionary<string, string>(StringComparer.Ordinal);

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None
        };

        public string ToJson() => JsonConvert.SerializeObject(this, Settings);

        /// <summary>
        ///     Đọc file state. Trả null + reason (parse / schema) khi không đọc được → caller reset, log state_reset.
        /// </summary>
        public static EngineState FromJson(string json, out string failReason)
        {
            failReason = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                failReason = "parse";
                return null;
            }

            EngineState s;
            try
            {
                var o = ConfigParser.ParseObject(json);
                var sv = o["schema_version"]?.Type == JTokenType.Integer ? o["schema_version"].Value<int>() : 0;
                if (sv > SCHEMA_VERSION)
                {
                    failReason = "schema";
                    return null;
                }

                s = o.ToObject<EngineState>(JsonSerializer.Create(Settings));
            }
            catch
            {
                failReason = "parse";
                return null;
            }

            if (s == null || string.IsNullOrEmpty(s.UserId))
            {
                failReason = "parse";
                return null;
            }

            s.Normalize();
            return s;
        }

        /// <summary>Migration đơn giản: thêm field default còn thiếu, chuẩn hoá counter.</summary>
        public void Normalize()
        {
            SchemaVersion = SCHEMA_VERSION;
            Clock ??= new ClockState();
            Session ??= new SessionState();
            Scalars ??= new Scalars();
            Counters ??= new Dictionary<string, WindowCounter>(StringComparer.Ordinal);
            Custom ??= new Dictionary<string, object>(StringComparer.Ordinal);
            Context ??= new ContextState();
            ShownToday ??= new ShownToday();
            Tags ??= new Dictionary<string, TagState>(StringComparer.Ordinal);
            Edges ??= new Dictionary<string, EdgeState>(StringComparer.Ordinal);
            ActionTs ??= new Dictionary<string, List<long>>(StringComparer.Ordinal);
            Assignments ??= new Dictionary<string, AssignmentState>(StringComparer.Ordinal);
            Holdout ??= new HoldoutState();
            Exposed ??= new List<string>();
            TxRing ??= new List<string>();
            LastOffer ??= new LastOffer();
            HistoryMirror ??= new ActionHistory();
            Partitions ??= new Dictionary<string, string>(StringComparer.Ordinal);
            HistoryMirror.Actions ??= new Dictionary<string, HistoryEntry>(StringComparer.Ordinal);
            Context.Screen ??= string.Empty;
            Context.LastEvent ??= string.Empty;
            foreach (var c in Counters.Values) c.Normalize();
        }

        /// <summary>Lấy hoặc tạo counter có window; counter mới có head_day = utcDay(now), toàn 0.</summary>
        public WindowCounter Counter(string name, long today)
        {
            if (!Counters.TryGetValue(name, out var c))
            {
                c = WindowCounter.Create(today);
                Counters[name] = c;
            }

            return c;
        }

        public double CustomNumber(string key) => Custom.TryGetValue(key, out var v) ? ToDouble(v) : 0;

        public static double ToDouble(object v)
        {
            switch (v)
            {
                case double d: return d;
                case long l: return l;
                case int i: return i;
                case float f: return f;
                case decimal m: return (double)m;
                case bool b: return b ? 1 : 0;
                default: return 0;
            }
        }
    }

    /// <summary>Cache config đã chấp nhận — §C.10.2.</summary>
    public sealed class ConfigCache
    {
        [JsonProperty("fetched_at")] public long FetchedAt;
        [JsonProperty("server_time")] public long ServerTime;
        [JsonProperty("config")] public JObject Config;

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None);

        private static readonly JsonSerializerSettings CacheSettings = new JsonSerializerSettings
        {
            DateParseHandling = DateParseHandling.None, // giữ published_at / valid_until là string
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        public static ConfigCache FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonConvert.DeserializeObject<ConfigCache>(json, CacheSettings);
            }
            catch
            {
                return null;
            }
        }
    }
}
