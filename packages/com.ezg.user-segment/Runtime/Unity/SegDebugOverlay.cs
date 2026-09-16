using System;
using System.Collections.Generic;
using System.Globalization;
using Ezg.UserSegment.Engine;
using UnityEngine;

namespace Ezg.UserSegment
{
    /// <summary>
    ///     Debug overlay IMGUI — §11.3: xem state / feature / segment / decision / history / config; ép state, tag,
    ///     assignment, now; chạy trigger; copy manifest JSON. Chỉ dev build.
    /// </summary>
    internal sealed class SegDebugOverlay : MonoBehaviour, ISdkDebug
    {
        private SegEngine _e;
        private int _tab;
        private Vector2 _scroll;
        private string _unitId = "1";
        private string _scalarName = "fail_streak";
        private string _scalarValue = "3";
        private string _customKey = "";
        private string _customValue = "";
        private string _nowShift = "0";
        private static readonly string[] Tabs = { "Overview", "State", "Segments", "Decisions", "Actions", "Override", "Config" };
        private const float SCALE_REF_DPI = 160f;

        public bool Visible { get; set; }
        public void Toggle() => Visible = !Visible;

        public SegDebugOverlay Bind(SegEngine engine)
        {
            _e = engine;
            return this;
        }

        private void OnGUI()
        {
            // Phím tắt F9 đọc qua Event của IMGUI thay vì UnityEngine.Input: không phụ thuộc Active Input Handling,
            // project chỉ bật Input System Package không bị InvalidOperationException mỗi frame.
            var ev = Event.current;
            if (ev != null && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.F9)
            {
                Toggle();
                ev.Use();
            }

            if (_e == null) return;
            var scale = Mathf.Max(1f, Screen.dpi > 0 ? Screen.dpi / SCALE_REF_DPI : 1f);
            if (!Visible)
            {
                // Nút nổi nhỏ góc dưới-trái (dev build) để bật overlay mà không cần nút cheat riêng của game
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
                if (GUI.Button(new Rect(4, Screen.height / scale - 28, 44, 24), "SEG")) Visible = true;
                GUI.matrix = Matrix4x4.identity;
                return;
            }

            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            var w = Screen.width / scale;
            var h = Screen.height / scale;
            GUILayout.BeginArea(new Rect(8, 8, w - 16, h - 16), GUI.skin.box);
            GUILayout.BeginHorizontal();
            _tab = GUILayout.Toolbar(_tab, Tabs);
            if (GUILayout.Button("X", GUILayout.Width(30))) Visible = false;
            GUILayout.EndHorizontal();
            _scroll = GUILayout.BeginScrollView(_scroll);
            try
            {
                switch (_tab)
                {
                    case 0: DrawOverview(); break;
                    case 1: DrawState(); break;
                    case 2: DrawSegments(); break;
                    case 3: DrawDecisions(); break;
                    case 4: DrawActions(); break;
                    case 5: DrawOverride(); break;
                    default: DrawConfig(); break;
                }
            }
            catch (Exception ex)
            {
                GUILayout.Label("overlay error: " + ex.Message);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.matrix = Matrix4x4.identity;
        }

        private void DrawOverview()
        {
            var s = _e.State;
            L($"sdk {SegEngine.SDK_VERSION}  user_id {s.UserId}");
            L($"config: version {_e.Config?.Version ?? 0}  source {_e.ConfigSource}  enabled {_e.Config?.Enabled}  stale {s.Context.ConfigStale}");
            L($"queue open {_e.QueueOpen}  eval disabled {_e.EvalDisabled}  last eval_ms {_e.LastEvalMs}  tracking errors {_e.Tracking.SinkErrors}");
            L($"now {_e.Clock.Now()}  offset {s.Clock.OffsetS}s has_offset {s.Clock.HasOffset}  clock_suspect {s.Context.ClockSuspect}");
            L($"seeded {s.Seeded}  needs_seed {_e.NeedsSeed}  holdout {s.Holdout.Value}  session_open {s.Session.Open}");
            L($"history store: {(_e.HasHistoryStore ? "ok" : "none")}   exps: {_e.Experiments.ExpsString()}");
            if (_e.UnsupportedRules.Count > 0)
            {
                L("rule_unsupported:");
                foreach (var kv in _e.UnsupportedRules) L($"  {kv.Key}: {kv.Value}");
            }

            if (_e.DroppedEvents.Count > 0)
            {
                L("dropped_events:");
                foreach (var kv in _e.DroppedEvents) L($"  {kv.Key}: {kv.Value}");
            }

            GUILayout.Space(8);
            L("Run trigger:");
            GUILayout.BeginHorizontal();
            L("unit_id", 60);
            _unitId = GUILayout.TextField(_unitId, GUILayout.Width(80));
            if (GUILayout.Button("PROGRESS_START")) Fire(SegEvent.Progress(SegEventType.PROGRESS_START, Unit()));
            if (GUILayout.Button("FAIL")) Fire(SegEvent.Progress(SegEventType.PROGRESS_FAIL, Unit(), 10));
            if (GUILayout.Button("COMPLETE")) Fire(SegEvent.Progress(SegEventType.PROGRESS_COMPLETE, Unit(), 10));
            if (GUILayout.Button("QUIT")) Fire(SegEvent.Progress(SegEventType.PROGRESS_QUIT, Unit(), 10));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("AD_REWARDED")) Fire(SegEvent.Ad(SegEventType.AD_REWARDED, "debug"));
            if (GUILayout.Button("AD_INTERSTITIAL")) Fire(SegEvent.Ad(SegEventType.AD_INTERSTITIAL, "debug"));
            if (GUILayout.Button("PURCHASE $0.99")) Fire(SegEvent.Purchase("debug_pack", 0.99, "dbg_" + Guid.NewGuid().ToString("N").Substring(0, 8)));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            foreach (var sc in _e.Manifest.Screens)
                if (GUILayout.Button("SCREEN " + sc))
                    Fire(SegEvent.Screen(sc));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            foreach (var ce in _e.Manifest.CustomEvents)
                if (GUILayout.Button("CUSTOM " + ce))
                    Fire(SegEvent.Custom(ce));
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy manifest JSON")) GUIUtility.systemCopyBuffer = _e.ExportManifestJson();
            if (GUILayout.Button("Copy envelope raw")) GUIUtility.systemCopyBuffer = _e.LastEnvelopeRaw ?? string.Empty;
            if (GUILayout.Button("Copy state JSON")) GUIUtility.systemCopyBuffer = _e.State.ToJson();
            if (_e.EvalDisabled && GUILayout.Button("Re-enable eval")) _e.DebugClearEvalDisabled();
            GUILayout.EndHorizontal();
        }

        private void DrawState()
        {
            var s = _e.State;
            var sc = s.Scalars;
            L($"install_at {sc.InstallAt}  last_active_at {sc.LastActiveAt}  days_since_last_active {s.Session.DaysSinceLastActive}");
            L($"progress current {sc.ProgressCurrent} max {sc.ProgressMax}  attempt_cur_unit {sc.AttemptCountCurrentUnit}");
            L($"fail_streak {sc.FailStreak}  win_streak {sc.WinStreak}  quit_after_fail {sc.QuitAfterFailCount}  last_fail_at {sc.LastFailAt}");
            L($"spend {sc.TotalSpendUsd}  first_purchase {sc.FirstPurchaseAt}  last_purchase {sc.LastPurchaseAt}");
            L($"context: screen '{s.Context.Screen}' last_event '{s.Context.LastEvent}' shown_today {s.ShownToday.Count} (day {s.ShownToday.Day})");
            L("counters (life / session / 7d / 30d):");
            foreach (var kv in s.Counters)
                L($"  {kv.Key}: {kv.Value.Life} / {kv.Value.Session} / {kv.Value.Get(RefWindow.Days7)} / {kv.Value.Get(RefWindow.Days30)}");
            L("custom:");
            foreach (var kv in s.Custom) L($"  {kv.Key} = {kv.Value}");
        }

        private void DrawSegments()
        {
            var ctx = _e.LastEval;
            L("features (last pass):");
            foreach (var kv in ctx.Features) L($"  {kv.Key} = {kv.Value}");
            L("segments (last pass):");
            foreach (var kv in ctx.Segments) L($"  {kv.Key} = {kv.Value}");
            L("tags persisted:");
            foreach (var kv in _e.State.Tags) L($"  {kv.Key} = {kv.Value.Value} ({kv.Value.DefHash})");
            L("edges:");
            foreach (var kv in _e.State.Edges) L($"  {kv.Key} prev={kv.Value.Prev}");
        }

        private void DrawDecisions()
        {
            for (var i = _e.RecentDecisions.Count - 1; i >= 0; i--)
            {
                var d = _e.RecentDecisions[i];
                L($"[{d.At}] {d.Trigger}  eval_ms {d.EvalMs}");
                L($"   matched: {string.Join(",", d.RulesMatched)}");
                L($"   selected: {string.Join(",", d.Selected)}   dropped: {string.Join(",", d.Dropped)}");
                L($"   segments: {d.Segments}");
                L($"   features: {d.Features}");
            }
        }

        private void DrawActions()
        {
            L($"history store: {(_e.HasHistoryStore ? "ok" : "none")}");
            L("action_history (n / first / last):");
            var keys = new List<string>(_e.History.Actions.Keys);
            foreach (var k in keys)
            {
                var h = _e.History.Actions[k];
                GUILayout.BeginHorizontal();
                L($"  {k}: {h.N} / {h.F} / {h.L}");
                if (GUILayout.Button("remove", GUILayout.Width(70))) _e.RemoveHistoryEntry(k);
                GUILayout.EndHorizontal();
            }

            L("action_ts (cooldown / cap):");
            foreach (var kv in _e.State.ActionTs) L($"  {kv.Key}: {string.Join(",", kv.Value)}");
            L("assignments:");
            foreach (var kv in _e.State.Assignments) L($"  {kv.Key}: {kv.Value.ExpId}:{kv.Value.Variant}");
            L($"exposed: {string.Join(",", _e.State.Exposed)}");
        }

        private void DrawOverride()
        {
            L("now override (shift seconds from real now; 0 = clear):");
            GUILayout.BeginHorizontal();
            _nowShift = GUILayout.TextField(_nowShift, GUILayout.Width(100));
            if (GUILayout.Button("+1h")) _nowShift = (ParseLong(_nowShift) + 3600).ToString();
            if (GUILayout.Button("+1d")) _nowShift = (ParseLong(_nowShift) + 86400).ToString();
            if (GUILayout.Button("Apply"))
            {
                var shift = ParseLong(_nowShift);
                _e.DebugSetNowOverride(shift == 0 ? (long?)null : _e.Options.Clock.DeviceUtcNowSeconds() + _e.State.Clock.OffsetS + shift);
            }

            GUILayout.EndHorizontal();

            L("scalar (fail_streak, win_streak, progress_current, progress_max, attempt_count_current_unit, total_spend_usd, install_at, last_active_at):");
            GUILayout.BeginHorizontal();
            _scalarName = GUILayout.TextField(_scalarName, GUILayout.Width(200));
            _scalarValue = GUILayout.TextField(_scalarValue, GUILayout.Width(100));
            if (GUILayout.Button("Set")) SetScalar(_scalarName, _scalarValue);
            GUILayout.EndHorizontal();

            L("custom state (key value — kiểu theo manifest):");
            GUILayout.BeginHorizontal();
            _customKey = GUILayout.TextField(_customKey, GUILayout.Width(150));
            _customValue = GUILayout.TextField(_customValue, GUILayout.Width(150));
            if (GUILayout.Button("Set") && _e.Manifest.CustomState.TryGetValue(_customKey, out var ct))
            {
                switch (ct)
                {
                    case CustomType.Bool: Fire(SegEvent.CustomState(_customKey, SegValue.Bool(_customValue == "true" || _customValue == "1"))); break;
                    case CustomType.String: Fire(SegEvent.CustomState(_customKey, SegValue.String(_customValue))); break;
                    default: Fire(SegEvent.CustomState(_customKey, SegValue.Number(ParseDouble(_customValue)))); break;
                }
            }

            GUILayout.EndHorizontal();

            if (_e.Config != null)
            {
                L("tags:");
                foreach (var s in _e.Config.Segments)
                {
                    if (!s.IsTag) continue;
                    GUILayout.BeginHorizontal();
                    var cur = _e.State.Tags.TryGetValue(s.Id, out var t) && t.Value;
                    L($"  {s.Id} = {cur}", 220);
                    if (GUILayout.Button("true")) _e.DebugSetTag(s.Id, true);
                    if (GUILayout.Button("false")) _e.DebugSetTag(s.Id, false);
                    GUILayout.EndHorizontal();
                }

                L("experiments:");
                foreach (var ex in _e.Config.Experiments)
                {
                    GUILayout.BeginHorizontal();
                    L($"  {ex.Layer}/{ex.Id}", 260);
                    foreach (var v in ex.Variants)
                        if (GUILayout.Button(v.Id))
                            _e.DebugSetAssignment(ex.Layer, ex.Id, v.Id);
                    GUILayout.EndHorizontal();
                }
            }

            if (GUILayout.Button("Persist now")) _e.Persist(true);
        }

        private void DrawConfig()
        {
            var c = _e.Config;
            if (c == null)
            {
                L("no config");
                return;
            }

            L($"game {c.GameId}/{c.Env} v{c.Version} published {c.PublishedAt} min_sdk {c.MinSdkVersion}");
            L($"session_timeout {c.SessionTimeoutS}s max_actions_per_day {c.MaxActionsPerDay} holdout {c.HoldoutAllocation}");
            L("rules (priority, on, fire, then→effective):");
            foreach (var r in c.RulesOrdered)
            {
                var eff = _e.Experiments.EffectiveThen.TryGetValue(r.Id, out var t) ? t : r.Then;
                var unsupported = _e.UnsupportedRules.TryGetValue(r.Id, out var reason) ? $" [unsupported: {reason}]" : string.Empty;
                L($"  {r.Id} p={r.Priority} on={string.Join("|", r.On)} {(r.FireEdge ? "edge" : "level")} then={r.Then}→{eff}{(r.Enabled ? "" : " [disabled]")}{unsupported}");
            }

            L("actions:");
            foreach (var a in c.Actions)
                L($"  {a.Id} {a.Type} group={a.Group} {(a.OneShot ? "one_shot" : "repeatable")} cooldown={a.CooldownS} cap={(a.Cap == null ? "-" : a.Cap.Count + "/" + a.Cap.WindowS)}");
            L($"manifest:\n{_e.ExportManifestJson()}");
        }

        #region Helpers

        private void Fire(SegEvent e) => _e.Enqueue(e);
        private double Unit() => ParseDouble(_unitId);

        private void SetScalar(string name, string value)
        {
            var sc = _e.State.Scalars;
            var d = ParseDouble(value);
            var l = (long)d;
            switch (name)
            {
                case "fail_streak": sc.FailStreak = d; break;
                case "win_streak": sc.WinStreak = d; break;
                case "progress_current": sc.ProgressCurrent = d; break;
                case "progress_max": sc.ProgressMax = d; break;
                case "attempt_count_current_unit": sc.AttemptCountCurrentUnit = d; break;
                case "total_spend_usd": sc.TotalSpendUsd = d; break;
                case "install_at": sc.InstallAt = l; break;
                case "last_active_at": sc.LastActiveAt = l; break;
                case "quit_after_fail_count": sc.QuitAfterFailCount = d; break;
                default:
                    if (_e.State.Counters.TryGetValue(name, out var c)) c.Life = d;
                    break;
            }
        }

        private static double ParseDouble(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        private static long ParseLong(string s) => long.TryParse(s, out var v) ? v : 0;

        private static void L(string text, float width = 0)
        {
            if (width > 0) GUILayout.Label(text, GUILayout.Width(width));
            else GUILayout.Label(text);
        }

        #endregion
    }
}
