using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Ezg.UserSegment.Engine;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Ezg.UserSegment.Tests
{
    /// <summary>
    ///     Chạy test vector JSON theo §C.10.5: một file = một kịch bản. Fixture = file state rút gọn + now; `variant` ép
    ///     assignment; PAUSE / RESUME / QUIT / SEED là pseudo-event.
    /// </summary>
    public sealed class TestVectorRunner
    {
        public const double EPS = 1e-6;

        public static string TestsDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        public static IEnumerable<TestCaseData> Vectors()
        {
            var dir = Path.Combine(TestsDir(), "Vectors");
            if (!Directory.Exists(dir)) yield break;
            var files = Directory.GetFiles(dir, "*.json");
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var f in files) yield return new TestCaseData(f).SetName("Vector_" + Path.GetFileNameWithoutExtension(f));
        }

        [Test, TestCaseSource(nameof(Vectors))]
        public void Run(string vectorPath)
        {
            var v = ConfigParser.ParseObject(File.ReadAllText(vectorPath));
            var fixture = v["fixture"] as JObject ?? new JObject();
            var now = fixture["now"]?.Value<long>() ?? 1759287600;

            var clock = new FakeClock { Now = now, Mono = 1000 };
            var storage = new FakeStorage();
            var tracking = new FakeTracking();
            var logger = new FakeLogger();
            var store = v["no_history_store"]?.Value<bool>() == true ? null : new FakeHistoryStore();
            if (store != null && fixture["history"] != null) store.Blob = fixture["history"].ToString();

            // Manifest
            var man = v["manifest"] as JObject ?? new JObject();
            var options = new SdkOptions
            {
                GameId = v["game_id"]?.Value<string>() ?? "puzzle_x",
                Env = v["env"]?.Value<string>() ?? "prod",
                Storage = storage, Clock = clock, Tracking = tracking, Logger = logger, ActionHistoryStore = store,
                DebugBuild = true,
                Rewards = ToArray(man["rewards"]), Screens = ToArray(man["screens"]), CustomEvents = ToArray(man["custom_events"])
            };
            if (man["custom_state"] is JObject cs)
                foreach (var p in cs.Properties())
                    options.CustomState[p.Name] = (CustomType)Enum.Parse(typeof(CustomType), Capitalize(p.Value.Value<string>()));

            // State từ fixture
            var stateJson = BuildFixtureState(fixture, now);
            if (stateJson != null) storage.Files[SegEngine.STATE_KEY] = stateJson;

            var engine = new SegEngine(options);
            var executors = new Dictionary<ActionType, FakeExecutor>();
            foreach (var a in ToArray(man["actions"]))
            {
                var t = (ActionType)Enum.Parse(typeof(ActionType), a);
                var ex = new FakeExecutor(t);
                executors[t] = ex;
                engine.RegisterExecutor(ex);
            }

            var expectExec = v["expect"]?["executor"] as JArray;
            if (expectExec != null)
                foreach (var e in expectExec)
                {
                    var report = e["report"]?.Value<string>();
                    if (report == null) continue;
                    var t = (ActionType)Enum.Parse(typeof(ActionType), e["type"].Value<string>());
                    executors[t].Reports.Enqueue(report);
                }

            engine.Initialize();

            // Config
            var config = LoadConfig(v, vectorPath);
            if (v["variant"] is JObject variant)
                foreach (var p in variant.Properties())
                {
                    var layer = FindLayer(config, p.Name);
                    engine.State.Assignments[layer] = new AssignmentState { ExpId = p.Name, Variant = p.Value.Value<string>(), At = now };
                }

            var envelope = new JObject { ["server_time"] = now, ["config"] = config };
            var accepted = engine.ApplyFreshEnvelope(envelope.ToString());
            if (!accepted) engine.UseCacheOrFallback();
            engine.OpenQueue();

            // Events
            foreach (var evTok in v["events"] as JArray ?? new JArray())
            {
                var ev = (JObject)evTok;
                var at = ev["at"]?.Value<long>() ?? clock.Now;
                clock.Set(at);
                var type = ev["type"].Value<string>();
                var pl = ev["payload"] as JObject ?? ev;
                switch (type)
                {
                    case "PAUSE": engine.OnPause(); break;
                    case "RESUME": engine.OnResume(); break;
                    case "QUIT": engine.OnQuit(); break;
                    case "SEED": engine.Seed(pl.ToObject<SeedData>()); break;
                    case "IMPORT_HISTORY": engine.ImportActionHistory(pl["blob"].ToString()); break;
                    case "SESSION_START":
                        if (!engine.State.Session.Open) engine.Enqueue(SegEvent.Simple(SegEventType.SESSION_START));
                        break;
                    case "SESSION_END": engine.Enqueue(SegEvent.Simple(SegEventType.SESSION_END)); break;
                    case "PROGRESS_START":
                    case "PROGRESS_COMPLETE":
                    case "PROGRESS_FAIL":
                    case "PROGRESS_QUIT":
                        engine.Enqueue(SegEvent.Progress((SegEventType)Enum.Parse(typeof(SegEventType), type),
                            pl["unit_id"]?.Value<double>() ?? 0, pl["duration_s"]?.Value<double>() ?? 0));
                        break;
                    case "PURCHASE":
                        engine.Enqueue(SegEvent.Purchase(pl["product_id"]?.Value<string>(), pl["usd"]?.Value<double>() ?? 0,
                            pl["transaction_id"]?.Value<string>(), pl["execution_id"]?.Value<string>()));
                        break;
                    case "AD_REWARDED":
                    case "AD_INTERSTITIAL":
                        engine.Enqueue(SegEvent.Ad((SegEventType)Enum.Parse(typeof(SegEventType), type), pl["placement"]?.Value<string>() ?? ""));
                        break;
                    case "SCREEN_OPEN": engine.Enqueue(SegEvent.Screen(pl["screen_id"]?.Value<string>())); break;
                    case "CUSTOM_EVENT": engine.Enqueue(SegEvent.Custom(pl["name"]?.Value<string>())); break;
                    case "CUSTOM_STATE":
                    {
                        var val = pl["value"];
                        SegValue sv;
                        if (val.Type == JTokenType.Boolean) sv = SegValue.Bool(val.Value<bool>());
                        else if (val.Type == JTokenType.String) sv = SegValue.String(val.Value<string>());
                        else sv = SegValue.Number(val.Value<double>());
                        engine.Enqueue(SegEvent.CustomState(pl["key"]?.Value<string>(), sv));
                        break;
                    }
                    default: Assert.Fail("unknown event type " + type); break;
                }
            }

            // Expect
            var expect = v["expect"] as JObject ?? new JObject();
            var failures = new List<string>();
            var stateObj = JObject.Parse(engine.State.ToJson());
            if (expect["state"] is JObject es)
                foreach (var p in es.Properties())
                {
                    var actual = stateObj.SelectToken(p.Name);
                    if (!TokenEquals(p.Value, actual)) failures.Add($"state.{p.Name}: expected {p.Value} got {actual}");
                }

            if (expect["features"] is JObject ef)
                foreach (var p in ef.Properties())
                {
                    engine.LastEval.Features.TryGetValue(p.Name, out var actual);
                    if (!ValueEquals(p.Value, actual)) failures.Add($"feature.{p.Name}: expected {p.Value} got {actual}");
                }

            if (expect["segments"] is JObject eseg)
                foreach (var p in eseg.Properties())
                {
                    engine.LastEval.Segments.TryGetValue(p.Name, out var actual);
                    if (!ValueEquals(p.Value, actual)) failures.Add($"segment.{p.Name}: expected {p.Value} got {actual}");
                }

            if (expect["decisions"] is JArray ed)
            {
                var cursor = 0;
                foreach (var dTok in ed)
                {
                    var d = (JObject)dTok;
                    var trigger = d["trigger"].Value<string>();
                    DecisionRecord rec = null;
                    while (cursor < engine.RecentDecisions.Count)
                    {
                        var r = engine.RecentDecisions[cursor++];
                        if (r.Trigger == trigger)
                        {
                            rec = r;
                            break;
                        }
                    }

                    if (rec == null)
                    {
                        failures.Add($"decision {trigger}: not found");
                        continue;
                    }

                    CheckList(d["rules_matched"], rec.RulesMatched, $"decision {trigger}.rules_matched", failures);
                    CheckList(d["selected"], StripExec(rec.Selected), $"decision {trigger}.selected", failures);
                    CheckList(d["dropped"], rec.Dropped, $"decision {trigger}.dropped", failures);
                }
            }

            if (expect["tracking"] is JArray et)
            {
                var cursor = 0;
                foreach (var tTok in et)
                {
                    var t = (JObject)tTok;
                    var name = t["name"].Value<string>();
                    var wanted = t["params"] as JObject;
                    var found = false;
                    while (cursor < tracking.Events.Count)
                    {
                        var e = tracking.Events[cursor++];
                        if (e.Name != name || !ParamsMatch(wanted, e.Params)) continue;
                        found = true;
                        break;
                    }

                    if (!found) failures.Add($"tracking {name} {wanted}: not found in order. Events:\n  " + string.Join("\n  ", tracking.Events));
                }
            }

            if (expect["tracking_absent"] is JArray eta)
                foreach (var name in eta)
                    if (tracking.Of(name.Value<string>()).Count > 0)
                        failures.Add($"tracking {name} should be absent");

            if (expectExec != null)
            {
                var all = new List<ActionRequest>();
                foreach (var ex in executors.Values) all.AddRange(ex.Requests);
                all.Sort((a, b) => string.CompareOrdinal(a.ExecutionId, b.ExecutionId));
                if (all.Count != expectExec.Count) failures.Add($"executor calls: expected {expectExec.Count} got {all.Count}");
                for (var i = 0; i < Math.Min(all.Count, expectExec.Count); i++)
                {
                    var e = (JObject)expectExec[i];
                    if (e["action_id"]?.Value<string>() != all[i].ActionId) failures.Add($"executor[{i}].action_id expected {e["action_id"]} got {all[i].ActionId}");
                    if (e["type"] != null && e["type"].Value<string>() != all[i].Type.ToString()) failures.Add($"executor[{i}].type expected {e["type"]} got {all[i].Type}");
                    if (e["params"] is JObject ep)
                        foreach (var p in ep.Properties())
                        {
                            all[i].Params.Raw.TryGetValue(p.Name, out var actual);
                            if (!TokenEquals(p.Value, actual == null ? null : JToken.FromObject(actual)))
                                failures.Add($"executor[{i}].params.{p.Name} expected {p.Value} got {actual}");
                        }
                }
            }

            if (expect["history"] is JObject eh)
                foreach (var p in eh.Properties())
                {
                    var n = engine.History.Count(p.Name);
                    if (n != p.Value.Value<int>()) failures.Add($"history.{p.Name}.n expected {p.Value} got {n}");
                }

            if (expect["properties"] is JObject epr)
                foreach (var p in epr.Properties())
                {
                    tracking.Properties.TryGetValue(p.Name, out var actual);
                    var want = p.Value.Type == JTokenType.Null ? null : p.Value.Value<string>();
                    if (want != actual) failures.Add($"property {p.Name}: expected {want ?? "null"} got {actual ?? "null"}");
                }

            if (expect["config_accepted"] != null && expect["config_accepted"].Value<bool>() != accepted)
                failures.Add($"config_accepted expected {expect["config_accepted"]} got {accepted}");

            if (failures.Count > 0)
                Assert.Fail($"{v["name"]}:\n - " + string.Join("\n - ", failures) + "\n\nLog:\n" + string.Join("\n", logger.Lines));
        }

        #region Helpers

        private static JObject LoadConfig(JObject v, string vectorPath)
        {
            JObject config;
            var c = v["config"];
            if (c is JObject inline) config = (JObject)inline.DeepClone();
            else
            {
                var rel = c?.Value<string>() ?? "fixtures/config_min.json";
                var path = Path.Combine(Path.GetDirectoryName(vectorPath), "..", rel);
                if (!File.Exists(path)) path = Path.Combine(TestsDir(), rel);
                config = ConfigParser.ParseObject(File.ReadAllText(path));
            }

            if (v["config_patch"] is JObject patch)
                foreach (var p in patch.Properties())
                    config[p.Name] = p.Value.DeepClone();
            return config;
        }

        private static string FindLayer(JObject config, string expId)
        {
            foreach (var e in config["experiments"] as JArray ?? new JArray())
                if (e["id"].Value<string>() == expId)
                    return e["layer"].Value<string>();
            throw new Exception("experiment not found: " + expId);
        }

        /// <summary>Fixture rút gọn → file state đầy đủ: default + merge fixture; thiếu user_id → "fixture".</summary>
        private static string BuildFixtureState(JObject fixture, long now)
        {
            var keys = new List<string>();
            foreach (var p in fixture.Properties())
                if (p.Name != "now" && p.Name != "history")
                    keys.Add(p.Name);
            if (keys.Count == 0) return null;

            var s = new EngineState { UserId = "fixture", CreatedAt = now };
            s.Normalize();
            var baseObj = JObject.Parse(s.ToJson());
            foreach (var k in keys) baseObj[k] = fixture[k].DeepClone();
            if (baseObj["counters"] is JObject counters)
                foreach (var p in counters.Properties())
                {
                    var c = (JObject)p.Value;
                    if (c["head_day"] == null) c["head_day"] = now / 86400;
                    var days = c["days"] as JArray ?? new JArray();
                    while (days.Count < WindowCounter.DAYS) days.Add(0);
                    c["days"] = days;
                    c["life"] ??= 0;
                    c["session"] ??= 0;
                }

            return baseObj.ToString();
        }

        private static string[] ToArray(JToken t)
        {
            var list = new List<string>();
            if (t is JArray arr)
                foreach (var x in arr)
                    list.Add(x.Value<string>());
            return list.ToArray();
        }

        private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();

        private static List<string> StripExec(List<string> selected)
        {
            var r = new List<string>();
            foreach (var s in selected)
            {
                var i = s.IndexOf(':');
                r.Add(i < 0 ? s : s.Substring(0, i));
            }

            return r;
        }

        private static void CheckList(JToken expected, List<string> actual, string what, List<string> failures)
        {
            if (!(expected is JArray arr)) return;
            var exp = ToArray(arr);
            if (exp.Length != actual.Count || string.Join(",", exp) != string.Join(",", actual))
                failures.Add($"{what}: expected [{string.Join(",", exp)}] got [{string.Join(",", actual)}]");
        }

        private static bool ParamsMatch(JObject wanted, Dictionary<string, object> actual)
        {
            if (wanted == null) return true;
            foreach (var p in wanted.Properties())
            {
                if (!actual.TryGetValue(p.Name, out var a)) return false;
                if (!TokenEquals(p.Value, JToken.FromObject(a))) return false;
            }

            return true;
        }

        private static bool TokenEquals(JToken expected, JToken actual)
        {
            if (actual == null) return expected.Type == JTokenType.Null;
            switch (expected.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float:
                    if (actual.Type == JTokenType.Boolean) return Math.Abs(expected.Value<double>() - (actual.Value<bool>() ? 1 : 0)) < EPS;
                    if (actual.Type != JTokenType.Integer && actual.Type != JTokenType.Float) return false;
                    return Math.Abs(expected.Value<double>() - actual.Value<double>()) < EPS;
                case JTokenType.Boolean:
                    if (actual.Type == JTokenType.Boolean) return expected.Value<bool>() == actual.Value<bool>();
                    return actual.Type == JTokenType.Integer && (actual.Value<long>() != 0) == expected.Value<bool>();
                case JTokenType.String:
                    return actual.Type == JTokenType.String && expected.Value<string>() == actual.Value<string>();
                default:
                    return JToken.DeepEquals(expected, actual);
            }
        }

        private static bool ValueEquals(JToken expected, SegValue actual)
        {
            switch (expected.Type)
            {
                case JTokenType.Boolean: return actual.AsBool == expected.Value<bool>();
                case JTokenType.String: return actual.Type == SegType.String && actual.Str == expected.Value<string>();
                default: return Math.Abs(actual.AsNumber - expected.Value<double>()) < EPS;
            }
        }

        #endregion
    }
}
