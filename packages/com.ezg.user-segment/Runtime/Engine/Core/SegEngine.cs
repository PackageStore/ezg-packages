using System;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json.Linq;

namespace Ezg.UserSegment.Engine
{
    public enum ConfigSource
    {
        None,
        Fresh,
        Cache
    }

    /// <summary>Bản ghi một lần evaluate trigger (debug overlay + test).</summary>
    public sealed class DecisionRecord
    {
        public long At;
        public string Trigger;
        public List<string> RulesMatched = new List<string>();
        public List<string> Selected = new List<string>();
        public List<string> Dropped = new List<string>();
        public string Segments = string.Empty;
        public string Features = string.Empty;
        public long EvalMs;
    }

    /// <summary>
    ///     Engine core — C# thuần, không Unity API. Xử lý tuần tự một queue trên main thread: rollover → reducer →
    ///     (trigger) formula → segment → rule → resolver → executor → tracking → persist. Toàn bộ trong sandbox — §4.7.
    /// </summary>
    public sealed class SegEngine
    {
        public const string SDK_VERSION = "1.0.0";
        public const string STATE_KEY = "state";
        public const string CACHE_KEY = "config_cache";
        public const long STALE_S = 604800;
        public const long LAST_TOUCH_WINDOW_S = 604800;
        public const double SAMPLE_RATE = 0.05;
        public const int DEFAULT_SESSION_TIMEOUT_S = 1800;
        public const int MAX_RECENT_DECISIONS = 50;

        #region Fields

        private readonly SdkOptions _o;
        private readonly IStateStorage _storage;
        private readonly ISegLogger _log;
        private readonly TrackingEmitter _track;
        private readonly LatestWinsWriter _stateWriter;
        private readonly LatestWinsWriter _cacheWriter;
        private readonly Dictionary<ActionType, IActionExecutor> _executors = new Dictionary<ActionType, IActionExecutor>();
        private readonly Queue<SegEvent> _queue = new Queue<SegEvent>();
        private readonly EvalContext _ctx = new EvalContext();
        private readonly Dictionary<string, SelectedAction> _pending = new Dictionary<string, SelectedAction>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _unsupported = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Random _rng = new Random();
        private readonly Stopwatch _sw = new Stopwatch();

        private ExperimentRuntime _exp = new ExperimentRuntime();
        private ActionHistory _history = new ActionHistory();
        private bool _queueOpen;
        private bool _processing;
        private bool _evalDisabled;
        private bool _engineErrorLogged;
        private bool _inForeground;
        private bool _threadErrorLogged;

        public EngineState State { get; private set; }
        public Clock Clock { get; private set; }
        public SegConfig Config { get; private set; }
        public ConfigSource ConfigSource { get; private set; } = ConfigSource.None;
        public Manifest Manifest { get; private set; }
        public bool Initialized { get; private set; }
        public bool QueueOpen => _queueOpen;
        public bool NeedsSeed => State != null && !State.Seeded;
        public bool HasHistoryStore => _o.ActionHistoryStore != null;
        public bool EvalDisabled => _evalDisabled;
        public long LastEvalMs { get; private set; }
        public string LastEnvelopeRaw { get; private set; }
        public ActionHistory History => _history;
        public ExperimentRuntime Experiments => _exp;
        public IReadOnlyDictionary<string, string> UnsupportedRules => _unsupported;
        public EvalContext LastEval => _ctx;
        public SdkOptions Options => _o;
        public TrackingEmitter Tracking => _track;
        public readonly List<DecisionRecord> RecentDecisions = new List<DecisionRecord>();
        public readonly Dictionary<string, int> DroppedEvents = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Kiểm thread: Unity SDK gán để dev build ném khi gọi sai thread — §C.6.6.</summary>
        public Func<bool> IsMainThread;

        #endregion

        #region Init

        public SegEngine(SdkOptions options)
        {
            _o = options ?? throw new ArgumentNullException(nameof(options));
            _storage = options.Storage ?? throw new ArgumentException("SdkOptions.Storage required");
            if (options.Clock == null) throw new ArgumentException("SdkOptions.Clock required");
            _log = options.Logger;
            _track = new TrackingEmitter(options.Tracking, _log, options.DebugBuild);
            _stateWriter = new LatestWinsWriter(_storage, _log);
            _cacheWriter = new LatestWinsWriter(_storage, _log);
            Manifest = Manifest.FromOptions(_o, _executors.Keys, SDK_VERSION);
        }

        /// <summary>Đăng ký executor — trước Initialize để manifest.actions đúng ngay lần load config đầu.</summary>
        public void RegisterExecutor(IActionExecutor executor)
        {
            if (executor == null) return;
            _executors[executor.ActionType] = executor;
            Manifest = Manifest.FromOptions(_o, _executors.Keys, SDK_VERSION);
            if (Initialized && _o.DebugBuild)
                _log?.Log(LogLevel.Warn, $"[UserSegment] executor {executor.ActionType} registered after Initialize — effective from next config load");
        }

        /// <summary>
        ///     Đọc state + history, đóng session cũ nếu crash, seed nếu có provider, enqueue SESSION_START. Queue vẫn đóng
        ///     cho tới <see cref="OpenQueue" /> (sau khi fetch config xong hoặc timeout) — §4.4.
        /// </summary>
        public void Initialize()
        {
            if (Initialized) return;
            var storeHistory = LoadStoreHistory(out var historyBroken);
            LoadState(storeHistory);
            Clock = new Clock(_o.Clock, State);
            var now = Clock.Now();
            if (State.CreatedAt == 0) State.CreatedAt = now;
            if (historyBroken) _track.Log(TrackEvent.StateReset, P(("reason", "history")));

            _history = ActionHistory.Merge(storeHistory, State.HistoryMirror);
            if (string.IsNullOrEmpty(_history.Uid)) _history.Uid = State.UserId;
            SaveHistory();
            Info($"Initialize: user_id={State.UserId} seeded={State.Seeded} session_open(prev)={State.Session.Open} " +
                 $"history: store={(storeHistory == null ? "none" : storeHistory.Actions.Count + " action")} mirror={State.HistoryMirror.Actions.Count} merged={_history.Actions.Count} " +
                 $"store_attached={HasHistoryStore} executors=[{string.Join(",", Manifest.Actions)}] now={now}");

            _inForeground = true;
            Clock.ResetForegroundAnchor();
            Initialized = true;

            if (State.Session.Open)
            {
                Info("Initialize: session cũ chưa đóng (crash / kill) → enqueue SESSION_END");
                _queue.Enqueue(SegEvent.Simple(SegEventType.SESSION_END, Math.Max(State.Clock.LastEventAt, State.Clock.LastPauseAt)));
            }

            if (NeedsSeed && _o.SeedProvider != null)
            {
                SeedData seed = null;
                try
                {
                    seed = _o.SeedProvider();
                }
                catch (Exception e)
                {
                    _log?.Log(LogLevel.Error, "[UserSegment] SeedProvider threw: " + e.Message);
                }

                if (seed != null) Seed(seed);
                else Info("Initialize: SeedProvider trả null → chờ Seed() gọi sau");
            }

            _queue.Enqueue(SegEvent.Simple(SegEventType.SESSION_START));
            Info("Initialize xong: SESSION_START đã vào queue, queue ĐÓNG chờ fetch config / timeout");
        }

        private ActionHistory LoadStoreHistory(out bool broken)
        {
            broken = false;
            if (_o.ActionHistoryStore == null) return null;
            string blob = null;
            try
            {
                blob = _o.ActionHistoryStore.Load();
            }
            catch (Exception e)
            {
                _log?.Log(LogLevel.Error, "[UserSegment] history store Load threw: " + e.Message);
            }

            if (string.IsNullOrWhiteSpace(blob)) return null;
            var h = ActionHistory.Parse(blob);
            if (h == null) broken = true;
            return h;
        }

        private void LoadState(ActionHistory storeHistory)
        {
            string json = null;
            try
            {
                json = _storage.Read(STATE_KEY);
            }
            catch (Exception e)
            {
                _log?.Log(LogLevel.Error, "[UserSegment] storage Read threw: " + e.Message);
            }

            if (json != null)
            {
                var loaded = EngineState.FromJson(json, out var reason);
                if (loaded != null)
                {
                    State = loaded;
                    if (_o.DebugBuild) _log?.Log(LogLevel.Info, $"[UserSegment] LoadState: đọc file state OK (schema {loaded.SchemaVersion}, {json.Length} ký tự)");
                    return;
                }

                try
                {
                    _storage.Delete(STATE_KEY);
                }
                catch
                {
                    // ignore
                }

                State = NewState(storeHistory);
                _track.Log(TrackEvent.StateReset, P(("reason", reason)));
                _log?.Log(LogLevel.Warn, "[UserSegment] state_reset: " + reason);
                return;
            }

            State = NewState(storeHistory);
            if (_o.DebugBuild) _log?.Log(LogLevel.Info, "[UserSegment] LoadState: chưa có file state → tạo mới" +
                                                  (!string.IsNullOrEmpty(storeHistory?.Uid) ? $" (dùng lại uid từ save: {storeHistory.Uid})" : string.Empty));
        }

        private static EngineState NewState(ActionHistory storeHistory)
        {
            var s = new EngineState
            {
                UserId = !string.IsNullOrEmpty(storeHistory?.Uid) ? storeHistory.Uid : HashUtil.NewUserId()
            };
            s.Normalize();
            return s;
        }

        #endregion

        #region Config

        /// <summary>Envelope tươi từ Worker: validate → cache → học offset → cài. false = bị từ chối (đã log config_rejected).</summary>
        public bool ApplyFreshEnvelope(string body)
        {
            LastEnvelopeRaw = body;
            Dbg($"ApplyFreshEnvelope: body {body?.Length ?? 0} ký tự");
            if (!TryBuild(body, out var serverTime, out var cfgObj, out var cfg)) return false;
            Clock.LearnOffset(serverTime);
            var now = Clock.Now();
            Info($"Config FRESH chấp nhận: version={cfg.Version} enabled={cfg.Enabled} rules={cfg.Rules.Count} actions={cfg.Actions.Count} " +
                 $"experiments={cfg.Experiments.Count} server_time={serverTime} offset={State.Clock.OffsetS}s clock_suspect={State.Context.ClockSuspect}");
            State.LastFetchAt = now;
            State.Context.ConfigStale = false;
            var cache = new ConfigCache { FetchedAt = now, ServerTime = serverTime, Config = cfgObj };
            _cacheWriter.Schedule(CACHE_KEY, cache.ToJson());
            Install(cfg, ConfigSource.Fresh);
            return true;
        }

        /// <summary>
        ///     Fetch fail / bị từ chối: giữ config đang chạy nếu đã có; không thì đọc cache (stale ok), rồi tới
        ///     DevFallbackEnvelope, cuối cùng là none.
        /// </summary>
        public void UseCacheOrFallback()
        {
            if (Config != null)
            {
                Info($"UseCacheOrFallback: giữ config đang chạy (version {Config.Version}, source {ConfigSource})");
                return;
            }

            var now = Clock.Now();
            Info("UseCacheOrFallback: không có config tươi → thử cache");
            string cacheJson = null;
            try
            {
                cacheJson = _storage.Read(CACHE_KEY);
            }
            catch
            {
                // ignore
            }

            var cache = ConfigCache.FromJson(cacheJson);
            if (cache?.Config != null)
            {
                try
                {
                    var cfg = ConfigParser.Parse(cache.Config);
                    ConfigValidator.Validate(cfg, SDK_VERSION, _o.GameId, _o.Env, Manifest);
                    State.Context.ConfigStale = now - cache.FetchedAt > STALE_S;
                    Info($"Config CACHE chấp nhận: version={cfg.Version} fetched {now - cache.FetchedAt}s trước, stale={State.Context.ConfigStale}");
                    Install(cfg, ConfigSource.Cache);
                    return;
                }
                catch (ConfigException e)
                {
                    _log?.Log(LogLevel.Warn, "[UserSegment] cached config rejected: " + e.Message);
                }
            }

            if (_o.DevFallbackEnvelope != null)
            {
                string dev = null;
                try
                {
                    dev = _o.DevFallbackEnvelope();
                }
                catch (Exception e)
                {
                    _log?.Log(LogLevel.Error, "[UserSegment] DevFallbackEnvelope threw: " + e.Message);
                }

                if (!string.IsNullOrEmpty(dev) && TryBuild(dev, out _, out _, out var devCfg))
                {
                    LastEnvelopeRaw = dev;
                    _log?.Log(LogLevel.Warn, "[UserSegment] using DEV fallback config");
                    Install(devCfg, ConfigSource.Cache);
                    return;
                }
            }

            Info("UseCacheOrFallback: KHÔNG có config (source none) — reducer/persist vẫn chạy, không rule nào evaluate");
        }

        private bool TryBuild(string body, out long serverTime, out JObject cfgObj, out SegConfig cfg)
        {
            serverTime = 0;
            cfgObj = null;
            cfg = null;
            try
            {
                ConfigParser.ParseEnvelope(body, out serverTime, out cfgObj);
                cfg = ConfigParser.Parse(cfgObj);
                ConfigValidator.Validate(cfg, SDK_VERSION, _o.GameId, _o.Env, Manifest);
                return true;
            }
            catch (ConfigException e)
            {
                long version = 0;
                if (cfgObj?["version"]?.Type == JTokenType.Integer) version = cfgObj["version"].Value<long>();
                _track.Log(TrackEvent.ConfigRejected, P(("version", version), ("reason", e.Reason), ("sdk_version", SDK_VERSION)));
                _log?.Log(LogLevel.Error, "[UserSegment] config_rejected: " + e.Message);
                return false;
            }
        }

        private void Install(SegConfig cfg, ConfigSource source)
        {
            Config = cfg;
            ConfigSource = source;
            PostLoad();
            Persist(false);
        }

        /// <summary>Holdout → assignment từng layer → disable rule ngoài manifest — §2.3.</summary>
        private void PostLoad()
        {
            var now = Clock.Now();
            Assignment.DecideHoldout(State, Config, _track);
            _exp = Assignment.Resolve(State, Config, now, _track, out _);
            _unsupported.Clear();
            foreach (var r in Config.Rules)
            {
                if (!r.Enabled) continue;
                var then = _exp.EffectiveThen[r.Id];
                var action = then == RuleDef.NONE ? null : Config.ActionById[then];
                var reason = RuleSupport.Check(r, action, Manifest, HasHistoryStore, now);
                if (reason == null) continue;
                _unsupported[r.Id] = reason;
                _track.Log(TrackEvent.RuleUnsupported, P(("rule_id", r.Id), ("reason", reason), ("config_version", Config.Version)));
            }

            if (_o.DebugBuild)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"PostLoad: holdout={State.Holdout.Value} assignments=[{_exp.ExpsString()}] unsupported={_unsupported.Count}");
                foreach (var kv in _unsupported) sb.Append($" {kv.Key}:{kv.Value}");
                sb.Append(" | then hiệu lực:");
                foreach (var r in Config.RulesOrdered)
                    if (r.Enabled) sb.Append($" {r.Id}→{_exp.EffectiveThen[r.Id]}");
                Info(sb.ToString());
            }
        }

        /// <summary>Debug: đổi assignment rồi resolve lại then hiệu lực.</summary>
        public void ReResolveExperiments()
        {
            if (Config != null) PostLoad();
        }

        public int SessionTimeoutS => Config?.SessionTimeoutS ?? DEFAULT_SESSION_TIMEOUT_S;

        public bool ShouldRefetch(long now) => now - State.LastFetchAt > _o.RefetchAfterResumeS;

        public string ExportManifestJson() => Manifest.ToJson();

        #endregion

        #region Queue + lifecycle

        public void OpenQueue()
        {
            Info($"OpenQueue: mở queue, {_queue.Count} event đang chờ, config={(Config == null ? "none" : "v" + Config.Version + "/" + ConfigSource)}");
            _queueOpen = true;
            ProcessQueue();
        }

        public void Enqueue(SegEvent e)
        {
            if (!CheckThread()) return;
            if (!Initialized)
            {
                _log?.Log(LogLevel.Error, "[UserSegment] event before Initialize dropped: " + e.Type);
                return;
            }

            _queue.Enqueue(e);
            Dbg($"Enqueue {e.TriggerName}{(_queueOpen ? "" : " (queue đóng, chờ)")}{(_processing ? " (đang xử lý → nối đuôi)" : "")}");
            if (_queueOpen && !_processing) ProcessQueue();
        }

        public void Tick()
        {
            if (_queueOpen && !_processing && _queue.Count > 0) ProcessQueue();
        }

        private void ProcessQueue()
        {
            if (_processing) return;
            _processing = true;
            try
            {
                while (_queueOpen && _queue.Count > 0) ProcessEvent(_queue.Dequeue());
            }
            finally
            {
                _processing = false;
            }
        }

        public void OnPause()
        {
            if (!Initialized || !CheckThread()) return;
            var now = Clock.Now();
            AccumulatePlaytime(now);
            State.Clock.LastPauseAt = now;
            _inForeground = false;
            Clock.StopForeground();
            Persist(true);
            Info($"OnPause: now={now} persist đồng bộ, playtime session={SessionTimeS(now):F0}s");
        }

        /// <returns>true nếu nên fetch lại config (lần fetch cuối &gt; RefetchAfterResumeS) — §10.2.</returns>
        public bool OnResume()
        {
            if (!Initialized || !CheckThread()) return false;
            var now = Clock.Now();
            _inForeground = true;
            if (State.Session.Open && State.Clock.LastPauseAt > 0 && now - State.Clock.LastPauseAt >= SessionTimeoutS)
            {
                Info($"OnResume: nền {now - State.Clock.LastPauseAt}s ≥ timeout {SessionTimeoutS}s → SESSION_END + SESSION_START mới");
                _queue.Enqueue(SegEvent.Simple(SegEventType.SESSION_END, State.Clock.LastPauseAt));
                _queue.Enqueue(SegEvent.Simple(SegEventType.SESSION_START));
            }
            else
            {
                Info($"OnResume: nền {now - State.Clock.LastPauseAt}s < timeout → tiếp tục session");
                State.Clock.ForegroundAnchor = now;
            }

            Clock.ResetForegroundAnchor();
            if (_queueOpen && !_processing) ProcessQueue();
            return ShouldRefetch(now);
        }

        public void OnQuit()
        {
            if (!Initialized || !CheckThread()) return;
            var now = Clock.Now();
            AccumulatePlaytime(now);
            State.Clock.LastPauseAt = now;
            _inForeground = false;
            if (State.Session.Open) ProcessEvent(SegEvent.Simple(SegEventType.SESSION_END, now));
            Persist(true);
            Info($"OnQuit: now={now} SESSION_END + persist đồng bộ");
        }

        private bool CheckThread()
        {
            if (IsMainThread == null || IsMainThread()) return true;
            if (_o.DebugBuild) throw new InvalidOperationException("[UserSegment] API must be called on main thread");
            if (!_threadErrorLogged)
            {
                _threadErrorLogged = true;
                _log?.Log(LogLevel.Error, "[UserSegment] call from non-main thread dropped");
            }

            return false;
        }

        #endregion

        #region Event processing

        private void ProcessEvent(SegEvent e)
        {
            var backdated = e.At > 0;
            var now = backdated ? e.At : Clock.Now();
            if (!backdated) Clock.CheckForegroundJump(now);
            var today = Clock.UtcDay(now);
            var stage = "rollover";
            Dbg($"── Event {e.TriggerName} now={now} day={today}{(backdated ? " (backdated)" : "")}{(e.IsTrigger ? " TRIGGER" : "")}");
            try
            {
                Reducer.Rollover(State, today);
                stage = "reducer";
                if (!ValidateEvent(e))
                {
                    DroppedEvents.TryGetValue(e.Type.ToString(), out var n);
                    DroppedEvents[e.Type.ToString()] = n + 1;
                    if (_o.DebugBuild) _log?.Log(LogLevel.Error, "[UserSegment] invalid event dropped: " + e.TriggerName);
                    return;
                }

                if (e.Type == SegEventType.SESSION_START)
                {
                    _evalDisabled = false;
                    _engineErrorLogged = false;
                    _pending.Clear();
                    State.Context.ClockSuspect = !State.Clock.HasOffset;
                }

                if (!Reducer.Apply(e, State, now, today))
                {
                    Info($"Reducer bỏ event {e.TriggerName}: PURCHASE trùng transaction_id {e.TransactionId}");
                    return;
                }

                if (e.Type == SegEventType.SESSION_START)
                    Info($"SESSION_START: install_at={State.Scalars.InstallAt} days_since_last_active={State.Session.DaysSinceLastActive} " +
                         $"session_count={State.Counters["session_count"].Life} clock_suspect={State.Context.ClockSuspect}");
                else if (e.Type == SegEventType.SESSION_END)
                    Info("SESSION_END: đóng session");

                if (e.IsTrigger)
                {
                    stage = "evaluate";
                    EvaluateTrigger(e, now, today, ref stage);
                    stage = "persist";
                    Persist(false);
                }
                else if (e.Type == SegEventType.SESSION_END)
                {
                    Persist(false);
                }
            }
            catch (Exception ex)
            {
                HandleEngineError(stage, ex);
                if (stage != "rollover" && stage != "reducer")
                    try
                    {
                        Persist(false);
                    }
                    catch
                    {
                        // ignore
                    }
            }
        }

        private bool ValidateEvent(SegEvent e)
        {
            switch (e.Type)
            {
                case SegEventType.PURCHASE:
                    return !string.IsNullOrEmpty(e.TransactionId) && !string.IsNullOrEmpty(e.ProductId) && e.Usd >= 0;
                case SegEventType.AD_REWARDED:
                case SegEventType.AD_INTERSTITIAL:
                    return e.Placement != null;
                case SegEventType.SCREEN_OPEN:
                    return ConfigParser.IsIdent(e.ScreenId);
                case SegEventType.CUSTOM_EVENT:
                    return !string.IsNullOrEmpty(e.Name) && Manifest.CustomEvents.Contains(e.Name);
                case SegEventType.CUSTOM_STATE:
                    if (string.IsNullOrEmpty(e.Key) || !Manifest.CustomState.TryGetValue(e.Key, out var t)) return false;
                    switch (t)
                    {
                        case CustomType.Bool: return e.Value.Type == SegType.Bool;
                        case CustomType.String: return e.Value.Type == SegType.String;
                        default: return e.Value.Type == SegType.Number;
                    }
                default:
                    return true;
            }
        }

        private void HandleEngineError(string stage, Exception ex)
        {
            _evalDisabled = true;
            _log?.Log(LogLevel.Error, $"[UserSegment] engine_error stage={stage}: {ex}");
            if (_engineErrorLogged) return;
            _engineErrorLogged = true;
            var msg = ex.Message ?? string.Empty;
            if (msg.Length > 100) msg = msg.Substring(0, 100);
            _track.Log(TrackEvent.EngineError, P(("stage", stage), ("message", msg)));
        }

        private void EvaluateTrigger(SegEvent e, long now, long today, ref string stage)
        {
            _sw.Restart();
            var matched = new List<string>();
            var candidates = new List<Candidate>();
            ResolveResult rr = null;
            var evaluated = Config != null && !_evalDisabled;

            if (evaluated)
            {
                stage = "formula";
                _ctx.State = State;
                _ctx.Config = Config;
                _ctx.Manifest = Manifest;
                _ctx.ResetPass(now, today, SessionTimeS(now));
                EvaluateFeaturesAndSegments();

                if (Config.Enabled && !State.Holdout.Value)
                {
                    stage = "rule";
                    foreach (var r in Config.RulesOrdered)
                    {
                        if (!r.Enabled || _unsupported.ContainsKey(r.Id) || !MatchesAny(r.On, e)) continue;
                        var when = Evaluator.Eval(r.When, _ctx).AsBool;
                        if (!State.Edges.TryGetValue(r.Id, out var es))
                        {
                            es = new EdgeState();
                            State.Edges[r.Id] = es;
                        }

                        if (es.DefHash != r.DefHash)
                        {
                            es.Prev = false;
                            es.DefHash = r.DefHash;
                        }

                        var fired = r.FireEdge ? when && !es.Prev : when;
                        Dbg($"   rule {r.Id}: when={when} prev={es.Prev} fire={(r.FireEdge ? "edge" : "level")} → {(fired ? "FIRE" : "no")}");
                        es.Prev = when;
                        if (!fired) continue;
                        matched.Add(r.Id);
                        LogExposureIfFirst(r, e);
                        var then = _exp.EffectiveThen[r.Id];
                        if (then != RuleDef.NONE) candidates.Add(new Candidate { Rule = r, Action = Config.ActionById[then] });
                    }

                    stage = "resolver";
                    rr = Resolver.Resolve(candidates, State, Config, _history, now, today);
                    if (rr.Selected.Count > 0) SaveHistory();

                    stage = "executor";
                    foreach (var sel in rr.Selected)
                    {
                        _track.Log(TrackEvent.ActionSelected, P(("action_id", sel.Action.Id), ("execution_id", sel.ExecutionId),
                            ("rule_id", sel.Rule.Id), ("group", sel.Action.Group), ("nth", (long)sel.Nth),
                            ("config_version", Config.Version)));
                        Dispatch(sel);
                    }
                }
            }

            _sw.Stop();
            LastEvalMs = _sw.ElapsedMilliseconds;

            if (!evaluated)
                Info($"Trigger {e.TriggerName}: KHÔNG evaluate ({(Config == null ? "chưa có config" : "engine đã tắt vì engine_error")})");
            else if (!Config.Enabled)
                Info($"Trigger {e.TriggerName}: kill-switch enabled=false → chỉ segment, không rule");
            else if (State.Holdout.Value)
                Info($"Trigger {e.TriggerName}: user HOLDOUT → chỉ segment, không rule");
            else
                Info($"Trigger {e.TriggerName}: matched=[{string.Join(",", matched)}] candidates={candidates.Count} " +
                     $"selected=[{string.Join(",", rr.Selected.ConvertAll(x => x.Action.Id + ":" + x.ExecutionId))}] dropped=[{string.Join(",", rr.Dropped)}] eval_ms={LastEvalMs}");
            if (evaluated) Dbg($"   features: {FeaturesString()} | segments: {SegmentsString()}");

            stage = "tracking";
            var selectedStr = rr == null ? new List<string>() : rr.Selected.ConvertAll(x => x.Action.Id + ":" + x.ExecutionId);
            var droppedStr = rr == null ? new List<string>() : rr.Dropped;
            var rec = new DecisionRecord
            {
                At = now, Trigger = e.TriggerName, RulesMatched = matched, Selected = selectedStr, Dropped = droppedStr,
                Segments = evaluated ? SegmentsString() : string.Empty,
                Features = evaluated ? FeaturesString() : string.Empty, EvalMs = LastEvalMs
            };
            RecentDecisions.Add(rec);
            if (RecentDecisions.Count > MAX_RECENT_DECISIONS) RecentDecisions.RemoveAt(0);

            if (e.Type == SegEventType.SESSION_START)
            {
                LogSnapshot(rec);
            }
            else if (evaluated)
            {
                var hasMatch = matched.Count > 0 || selectedStr.Count > 0;
                if (hasMatch || _rng.NextDouble() < SAMPLE_RATE) LogDecision(rec, !hasMatch);
            }
        }

        private void EvaluateFeaturesAndSegments()
        {
            foreach (var node in Config.EvalOrder)
            {
                if (node.StartsWith("feature."))
                {
                    var f = Config.FormulaById[node.Substring(8)];
                    _ctx.Features[f.Id] = Evaluator.Eval(f.Expr, _ctx);
                    continue;
                }

                var def = Config.SegmentById[node.Substring(8)];
                if (def.IsTag)
                {
                    if (!State.Tags.TryGetValue(def.Id, out var ts))
                    {
                        ts = new TagState();
                        State.Tags[def.Id] = ts;
                    }

                    if (ts.DefHash != def.DefHash)
                    {
                        ts.Value = false;
                        ts.DefHash = def.DefHash;
                    }

                    var next = ts.Value ? !Evaluator.Eval(def.Exit, _ctx).AsBool : Evaluator.Eval(def.Enter, _ctx).AsBool;
                    ts.Value = next;
                    _ctx.Segments[def.Id] = SegValue.Bool(next);
                }
                else
                {
                    var value = def.Default;
                    foreach (var c in def.Cases)
                        if (Evaluator.Eval(c.When, _ctx).AsBool)
                        {
                            value = c.Value;
                            break;
                        }

                    _ctx.Segments[def.Id] = SegValue.String(value);
                    if (!State.Partitions.TryGetValue(def.Id, out var prev) || prev != value)
                    {
                        State.Partitions[def.Id] = value;
                        _track.SetUserProperty("seg_" + def.Id, value);
                    }
                }
            }
        }

        private static bool MatchesAny(string[] on, SegEvent e)
        {
            foreach (var entry in on)
                if (e.MatchesOn(entry))
                    return true;
            return false;
        }

        private void LogExposureIfFirst(RuleDef r, SegEvent e)
        {
            if (!_exp.ExperimentByRule.TryGetValue(r.Id, out var exp) || State.Exposed.Contains(exp.Id)) return;
            State.Exposed.Add(exp.Id);
            Info($"exp_exposure lần đầu: {exp.Id} rule={r.Id} trigger={e.TriggerName}");
            var variant = _exp.Assigned.TryGetValue(exp.Layer, out var a) && a.ExpId == exp.Id ? a.Variant : string.Empty;
            var historyUsed = false;
            foreach (var v in exp.Variants)
                if (v.RuleActions.TryGetValue(r.Id, out var aid) && aid != RuleDef.NONE &&
                    Config.ActionById.TryGetValue(aid, out var ad) && ad.OneShot && _history.Count(aid) >= 1)
                    historyUsed = true;
            _track.Log(TrackEvent.ExpExposure, P(("exp_id", exp.Id), ("variant", variant), ("rule_id", r.Id),
                ("history_used", TrackingEmitter.B(historyUsed)), ("trigger", e.TriggerName)));
        }

        #endregion

        #region Executor

        private void Dispatch(SelectedAction sel)
        {
            var a = sel.Action;
            var request = new ActionRequest(a.Id, sel.ExecutionId, a.Type, BuildParams(a), sel.Rule.Id, a.Group, sel.Nth,
                OnPresented, OnExecuted, OnFailed);
            _pending[sel.ExecutionId] = sel;
            if (!_executors.TryGetValue(a.Type, out var ex))
            {
                Info($"Dispatch {a.Id}#{sel.ExecutionId}: không có executor {a.Type} → failed not_available");
                request.ReportFailed(FailReason.NotAvailable);
                return;
            }

            Info($"Dispatch {a.Id}#{sel.ExecutionId} ({a.Type}, group {a.Group}, nth {sel.Nth}, rule {sel.Rule.Id}) → {ex.GetType().Name}");

            try
            {
                ex.Execute(request);
            }
            catch (Exception e)
            {
                _log?.Log(LogLevel.Error, $"[UserSegment] executor {a.Type} threw: {e}");
                request.ReportFailed(FailReason.Exception);
            }
        }

        private static ActionParams BuildParams(ActionDef a)
        {
            var p = new ActionParams(a.Params);
            object v;
            switch (a.Type)
            {
                case ActionType.GIVE_REWARD:
                    p.RewardId = a.Params["reward_id"] as string;
                    p.Amount = (int)EngineState.ToDouble(a.Params["amount"]);
                    break;
                case ActionType.SHOW_POPUP:
                    p.PopupId = a.Params["popup_id"] as string;
                    p.TextKey = a.Params.TryGetValue("text_key", out v) ? v as string : string.Empty;
                    break;
                case ActionType.SHOW_OFFER:
                    p.OfferId = a.Params["offer_id"] as string;
                    break;
                case ActionType.CHANGE_DIFFICULTY:
                    p.Delta = (int)EngineState.ToDouble(a.Params["delta"]);
                    p.Scope = a.Params["scope"] as string;
                    break;
                case ActionType.SCHEDULE_LOCAL_NOTIFICATION:
                    p.TemplateId = a.Params["template_id"] as string;
                    p.DelayH = EngineState.ToDouble(a.Params["delay_h"]);
                    break;
            }

            return p;
        }

        private void OnPresented(ActionRequest r)
        {
            Dbg($"action_presented {r.ActionId}#{r.ExecutionId}");
            _track.Log(TrackEvent.ActionPresented, P(("execution_id", r.ExecutionId), ("action_id", r.ActionId)));
        }

        private void OnExecuted(ActionRequest r, string result)
        {
            if (!ExecResult.IsAllowed(r.Type, result))
            {
                if (_o.DebugBuild) _log?.Log(LogLevel.Warn, $"[UserSegment] result '{result}' not in set for {r.Type} → other");
                result = ExecResult.Other;
            }

            Info($"action_executed {r.ActionId}#{r.ExecutionId} result={result}");
            _track.Log(TrackEvent.ActionExecuted, P(("execution_id", r.ExecutionId), ("action_id", r.ActionId), ("result", result)));
            if (r.Type == ActionType.SHOW_OFFER)
            {
                State.LastOffer.ExecutionId = r.ExecutionId;
                State.LastOffer.OfferId = r.Params.OfferId ?? string.Empty;
                State.LastOffer.At = Clock.Now();
            }

            _pending.Remove(r.ExecutionId);
            Persist(false);
        }

        private void OnFailed(ActionRequest r, string reason)
        {
            var known = FailReason.IsKnown(reason);
            var logged = known ? reason : FailReason.Other;
            Info($"action_failed {r.ActionId}#{r.ExecutionId} reason={logged}{(FailReason.IsRefundable(reason) ? " → hoàn cooldown/cap/one-shot" : "")}");
            _track.Log(TrackEvent.ActionFailed, P(("execution_id", r.ExecutionId), ("action_id", r.ActionId), ("reason", logged)));
            if (known && FailReason.IsRefundable(reason) && _pending.TryGetValue(r.ExecutionId, out var sel))
            {
                Resolver.Refund(sel, State, _history, Clock.UtcDay(Clock.Now()));
                SaveHistory();
            }

            _pending.Remove(r.ExecutionId);
            Persist(false);
        }

        /// <summary>Last-touch SHOW_OFFER trong 7 ngày cho attribution PURCHASE — §8.7. null nếu không có.</summary>
        public LastOffer LastOfferAttribution()
        {
            var lo = State.LastOffer;
            if (string.IsNullOrEmpty(lo.ExecutionId) || Clock.Now() - lo.At > LAST_TOUCH_WINDOW_S) return null;
            return lo;
        }

        #endregion

        #region Seed + history

        /// <summary>Áp khi seeded == false bất kể state trống hay chưa; merge: install_at min khác 0, còn lại max — §4.6.</summary>
        public void Seed(SeedData d)
        {
            if (d == null || !Initialized || State.Seeded) return;
            var sc = State.Scalars;
            var now = Clock.Now();
            var today = Clock.UtcDay(now);
            if (d.InstallAt > 0) sc.InstallAt = sc.InstallAt == 0 ? d.InstallAt : Math.Min(sc.InstallAt, d.InstallAt);
            sc.LastActiveAt = Math.Max(sc.LastActiveAt, d.LastActiveAt);
            sc.TotalSpendUsd = Math.Max(sc.TotalSpendUsd, d.TotalSpendUsd);
            sc.FirstPurchaseAt = Math.Max(sc.FirstPurchaseAt, d.FirstPurchaseAt);
            sc.LastPurchaseAt = Math.Max(sc.LastPurchaseAt, d.LastPurchaseAt);
            sc.ProgressMax = Math.Max(sc.ProgressMax, d.ProgressMax);
            var pc = State.Counter("purchase_count", today);
            pc.Life = Math.Max(pc.Life, d.PurchaseCount);
            var scnt = State.Counter("session_count", today);
            scnt.Life = Math.Max(scnt.Life, d.SessionCount);
            State.Seeded = true;
            State.SeededAt = now;
            Persist(false);
            Info($"Seed áp xong: install_at={sc.InstallAt} last_active_at={sc.LastActiveAt} purchase_count={pc.Life} spend={sc.TotalSpendUsd} progress_max={sc.ProgressMax}");
        }

        /// <summary>Sau restore cloud save về máy đã chơi: merge, không ghi đè — §4.5.</summary>
        public void ImportActionHistory(string blob)
        {
            if (!Initialized) return;
            var h = ActionHistory.Parse(blob);
            if (h == null)
            {
                _log?.Log(LogLevel.Warn, "[UserSegment] ImportActionHistory: blob unparsable, ignored");
                return;
            }

            _history = ActionHistory.Merge(_history, h);
            SaveHistory();
            Info($"ImportActionHistory: merge {h.Actions.Count} action từ blob → tổng {_history.Actions.Count}");
        }

        public void MarkActionUsed(string actionId, long atEpochSeconds)
        {
            if (!Initialized || string.IsNullOrEmpty(actionId)) return;
            _history.MarkUsed(actionId, atEpochSeconds);
            SaveHistory();
            Info($"MarkActionUsed: {actionId} at={atEpochSeconds}");
        }

        /// <summary>Debug: xoá một entry để QA lại one-shot.</summary>
        public void RemoveHistoryEntry(string actionId)
        {
            _history.RemoveEntry(actionId);
            SaveHistory();
        }

        private void SaveHistory()
        {
            if (string.IsNullOrEmpty(_history.Uid)) _history.Uid = State.UserId;
            State.HistoryMirror = _history.Clone();
            if (_o.ActionHistoryStore == null) return;
            try
            {
                _o.ActionHistoryStore.Save(_history.ToJson());
            }
            catch (Exception e)
            {
                _log?.Log(LogLevel.Error, "[UserSegment] history store Save threw: " + e.Message);
            }
        }

        #endregion

        #region Persist + time

        private void AccumulatePlaytime(long now)
        {
            if (!_inForeground || !State.Session.Open) return;
            var anchor = State.Clock.ForegroundAnchor;
            if (anchor > 0 && now > anchor) State.Counter("playtime_s", Clock.UtcDay(now)).Add(now - anchor);
            State.Clock.ForegroundAnchor = now;
        }

        private double SessionTimeS(long now)
        {
            var c = State.Counters.TryGetValue("playtime_s", out var pc) ? pc.Session : 0;
            if (_inForeground && State.Session.Open && State.Clock.ForegroundAnchor > 0 && now > State.Clock.ForegroundAnchor)
                c += now - State.Clock.ForegroundAnchor;
            return c;
        }

        /// <summary>Serialize trên thread gọi; ghi thread nền (latest-wins) hoặc đồng bộ khi pause / quit — §4.5.</summary>
        public void Persist(bool synchronous)
        {
            if (State == null) return;
            AccumulatePlaytime(Clock.Now());
            var json = State.ToJson();
            Dbg($"persist {(synchronous ? "SYNC" : "async")} {json.Length} ký tự");
            if (synchronous) _stateWriter.FlushSync(STATE_KEY, json);
            else _stateWriter.Schedule(STATE_KEY, json);
        }

        #endregion

        #region Tracking builders

        private void LogSnapshot(DecisionRecord rec)
        {
            var p = new Dictionary<string, object>
            {
                ["user_id"] = State.UserId,
                ["config_version"] = Config?.Version ?? 0,
                ["config_source"] = ConfigSource.ToString().ToLowerInvariant(),
                ["sdk_version"] = SDK_VERSION,
                ["seeded"] = TrackingEmitter.B(State.Seeded),
                ["holdout"] = TrackingEmitter.B(State.Holdout.Value),
                ["engine_enabled"] = TrackingEmitter.B(Config?.Enabled ?? false)
            };
            var tags = new List<string>();
            if (Config != null)
                foreach (var s in Config.Segments)
                {
                    if (!_ctx.Segments.TryGetValue(s.Id, out var v)) continue;
                    if (s.IsTag)
                    {
                        if (v.AsBool) tags.Add(s.Id);
                    }
                    else
                    {
                        p["seg_" + s.Id] = v.Str;
                    }
                }

            p["tags"] = TrackingEmitter.Join(tags);
            p["exps"] = _exp.ExpsString();
            p["rules_matched"] = TrackingEmitter.Join(rec.RulesMatched);
            p["selected"] = TrackingEmitter.Join(rec.Selected);
            p["dropped"] = TrackingEmitter.Join(rec.Dropped);
            Dbg($"tracking seg_snapshot: source={p["config_source"]} v={p["config_version"]} tags={p["tags"]} exps={p["exps"]}");
            _track.Log(TrackEvent.SegSnapshot, p);
        }

        private void LogDecision(DecisionRecord rec, bool sampled)
        {
            Dbg($"tracking seg_decision {rec.Trigger}{(sampled ? " (sampled 5%)" : "")}");
            _track.Log(TrackEvent.SegDecision, P(("trigger", rec.Trigger), ("rules_matched", TrackingEmitter.Join(rec.RulesMatched)),
                ("selected", TrackingEmitter.Join(rec.Selected)), ("dropped", TrackingEmitter.Join(rec.Dropped)),
                ("segments", rec.Segments), ("features", rec.Features), ("config_version", Config?.Version ?? 0),
                ("eval_ms", rec.EvalMs), ("sampled", TrackingEmitter.B(sampled))));
        }

        /// <summary>partition theo thứ tự khai (id=value), rồi tag đang true — §C.7.1.</summary>
        public string SegmentsString()
        {
            var parts = new List<string>();
            if (Config == null) return string.Empty;
            foreach (var s in Config.Segments)
                if (!s.IsTag && _ctx.Segments.TryGetValue(s.Id, out var v))
                    parts.Add(s.Id + "=" + v.Str);
            foreach (var s in Config.Segments)
                if (s.IsTag && _ctx.Segments.TryGetValue(s.Id, out var v) && v.AsBool)
                    parts.Add(s.Id);
            return TrackingEmitter.Join(parts);
        }

        public string FeaturesString()
        {
            var parts = new List<string>();
            if (Config == null) return string.Empty;
            foreach (var f in Config.Formulas)
                if (_ctx.Features.TryGetValue(f.Id, out var v))
                    parts.Add(f.Id + "=" + v.ToTrackingString());
            return TrackingEmitter.Join(parts);
        }

        /// <summary>Log luồng — chỉ dev build, tiền tố [UserSegment]. Info = mốc chính, Debug = chi tiết từng event.</summary>
        private void Info(string msg)
        {
            if (_o.DebugBuild) _log?.Log(LogLevel.Info, "[UserSegment] " + msg);
        }

        private void Dbg(string msg)
        {
            if (_o.DebugBuild) _log?.Log(LogLevel.Debug, "[UserSegment] " + msg);
        }

        private static Dictionary<string, object> P(params (string key, object value)[] items)
        {
            var d = new Dictionary<string, object>(items.Length);
            foreach (var (k, v) in items) d[k] = v;
            return d;
        }

        #endregion

        #region Debug overrides

        public void DebugSetNowOverride(long? now) => Clock.NowOverride = now;

        public void DebugSetTag(string id, bool value)
        {
            if (!State.Tags.TryGetValue(id, out var t))
            {
                t = new TagState();
                State.Tags[id] = t;
            }

            t.Value = value;
            if (Config != null && Config.SegmentById.TryGetValue(id, out var def)) t.DefHash = def.DefHash;
        }

        public void DebugSetAssignment(string layer, string expId, string variant)
        {
            State.Assignments[layer] = new AssignmentState { ExpId = expId, Variant = variant, At = Clock.Now() };
            ReResolveExperiments();
        }

        public void DebugClearEvalDisabled()
        {
            _evalDisabled = false;
        }

        #endregion
    }
}
