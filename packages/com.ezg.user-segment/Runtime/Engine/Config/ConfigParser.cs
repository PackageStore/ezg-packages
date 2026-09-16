using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ezg.UserSegment.Engine
{
    /// <summary>
    ///     Parse config đã compile theo schema §C.1 — strict: thiếu field bắt buộc, field lạ, sai kiểu, sai enum
    ///     → <see cref="ConfigException" /> reason schema. Không điền default (default chỉ tồn tại ở file nguồn).
    /// </summary>
    public static class ConfigParser
    {
        private static readonly Regex IdentRegex = new Regex("^[a-z][a-z0-9_]{0,39}$", RegexOptions.Compiled);
        private static readonly Regex SemverRegex = new Regex("^\\d+\\.\\d+\\.\\d+$", RegexOptions.Compiled);
        private static readonly Regex Hex16Regex = new Regex("^[0-9a-f]{16}$", RegexOptions.Compiled);

        public const int MAX_FORMULAS = 50, MAX_SEGMENTS = 30, MAX_PARTITIONS = 8, MAX_ACTIONS = 100, MAX_RULES = 100;

        /// <summary>
        ///     JObject.Parse với DateParseHandling.None: Newtonsoft mặc định đổi chuỗi ISO 8601 thành token Date, làm
        ///     published_at / valid_until / ends_at mất kiểu string. Mọi chỗ đọc config phải đi qua đây.
        /// </summary>
        public static JObject ParseObject(string json)
        {
            using (var sr = new System.IO.StringReader(json))
            using (var reader = new JsonTextReader(sr) { DateParseHandling = DateParseHandling.None })
            {
                var o = JObject.Load(reader);
                if (reader.Read()) throw new JsonReaderException("trailing content after JSON object");
                return o;
            }
        }

        /// <summary>Parse envelope { server_time, config } — §10.1.</summary>
        public static void ParseEnvelope(string json, out long serverTime, out JObject config)
        {
            JObject root;
            try
            {
                root = ParseObject(json);
            }
            catch (Exception e)
            {
                throw new ConfigException(RejectReason.Parse, e.Message);
            }

            serverTime = RequireLong(root, "server_time", "envelope");
            config = root["config"] as JObject ?? throw new ConfigException(RejectReason.Schema, "envelope.config");
        }

        public static SegConfig Parse(string json)
        {
            JObject root;
            try
            {
                root = ParseObject(json);
            }
            catch (Exception e)
            {
                throw new ConfigException(RejectReason.Parse, e.Message);
            }

            return Parse(root);
        }

        public static SegConfig Parse(JObject root)
        {
            // Bước 2: schema_version trước mọi thứ khác — §C.1.9
            var schemaVersion = RequireInt(root, "schema_version", "config");
            if (schemaVersion != SegConfig.SUPPORTED_SCHEMA)
                throw new ConfigException(RejectReason.Schema, $"schema_version {schemaVersion} unsupported");

            CheckKeys(root, "config", "schema_version", "min_sdk_version", "game_id", "env", "version", "published_at",
                "source_commit", "enabled", "session_timeout_s", "max_actions_per_day", "holdout", "const", "formulas",
                "segments", "actions", "rules", "experiments");

            var c = new SegConfig
            {
                SchemaVersion = schemaVersion,
                MinSdkVersion = RequireSemver(root, "min_sdk_version"),
                GameId = RequireIdent(root, "game_id", "config"),
                Env = RequireEnum(root, "env", "config", "prod", "dev", "staging"),
                Version = RequireLong(root, "version", "config"),
                PublishedAt = RequireString(root, "published_at", "config"),
                SourceCommit = OptionalString(root, "source_commit"),
                Enabled = RequireBool(root, "enabled", "config"),
                SessionTimeoutS = RequireIntRange(root, "session_timeout_s", "config", 60, 86400),
                MaxActionsPerDay = RequireIntRange(root, "max_actions_per_day", "config", 0, 20)
            };
            if (c.Version < 1) throw new ConfigException(RejectReason.Schema, "version < 1");
            ParseIsoTime(c.PublishedAt, "published_at");

            var holdout = RequireObject(root, "holdout", "config");
            CheckKeys(holdout, "holdout", "allocation");
            c.HoldoutAllocation = RequireNumberRange(holdout, "allocation", "holdout", 0, 1);

            var consts = RequireObject(root, "const", "config");
            foreach (var p in consts.Properties())
            {
                if (!IdentRegex.IsMatch(p.Name)) throw new ConfigException(RejectReason.Schema, $"const key {p.Name}");
                if (p.Value.Type != JTokenType.Integer && p.Value.Type != JTokenType.Float)
                    throw new ConfigException(RejectReason.Schema, $"const.{p.Name} must be number");
                c.Const[p.Name] = p.Value.Value<double>();
            }

            var formulas = RequireArray(root, "formulas", "config");
            if (formulas.Count > MAX_FORMULAS) throw new ConfigException(RejectReason.Schema, "formulas > 50");
            foreach (var t in formulas) c.Formulas.Add(ParseFormula(AsObject(t, "formula")));

            var segments = RequireArray(root, "segments", "config");
            if (segments.Count > MAX_SEGMENTS) throw new ConfigException(RejectReason.Schema, "segments > 30");
            var partitions = 0;
            foreach (var t in segments)
            {
                var s = ParseSegment(AsObject(t, "segment"));
                if (!s.IsTag) partitions++;
                c.Segments.Add(s);
            }

            if (partitions > MAX_PARTITIONS) throw new ConfigException(RejectReason.Schema, "partitions > 8");

            var actions = RequireArray(root, "actions", "config");
            if (actions.Count > MAX_ACTIONS) throw new ConfigException(RejectReason.Schema, "actions > 100");
            foreach (var t in actions) c.Actions.Add(ParseAction(AsObject(t, "action")));

            var rules = RequireArray(root, "rules", "config");
            if (rules.Count > MAX_RULES) throw new ConfigException(RejectReason.Schema, "rules > 100");
            foreach (var t in rules) c.Rules.Add(ParseRule(AsObject(t, "rule")));

            var exps = RequireArray(root, "experiments", "config");
            foreach (var t in exps) c.Experiments.Add(ParseExperiment(AsObject(t, "experiment")));

            return c;
        }

        #region Entities

        private static FormulaDef ParseFormula(JObject o)
        {
            CheckKeys(o, "formula", "id", "expr");
            return new FormulaDef
            {
                Id = RequireIdent(o, "id", "formula"),
                Expr = ParseAst(Require(o, "expr", "formula"))
            };
        }

        private static SegmentDef ParseSegment(JObject o)
        {
            var kind = RequireEnum(o, "kind", "segment", "tag", "partition");
            var s = new SegmentDef { Id = RequireIdent(o, "id", "segment"), IsTag = kind == "tag" };
            if (s.IsTag)
            {
                CheckKeys(o, "segment", "id", "kind", "enter", "exit", "def_hash");
                s.Enter = ParseAst(Require(o, "enter", "segment"));
                s.Exit = ParseAst(Require(o, "exit", "segment"));
                s.DefHash = OptionalHex16(o, "def_hash");
            }
            else
            {
                CheckKeys(o, "segment", "id", "kind", "default", "cases");
                if (s.Id.Length > 20) throw new ConfigException(RejectReason.Schema, $"partition id {s.Id} > 20");
                s.Default = RequireString(o, "default", "segment");
                if (s.Default.Length > 36) throw new ConfigException(RejectReason.Schema, "partition default > 36");
                var cases = RequireArray(o, "cases", "segment");
                if (cases.Count < 1) throw new ConfigException(RejectReason.Schema, "partition cases empty");
                var seen = new HashSet<string>(StringComparer.Ordinal) { s.Default };
                foreach (var t in cases)
                {
                    var co = AsObject(t, "case");
                    CheckKeys(co, "case", "value", "when");
                    var cd = new CaseDef
                    {
                        Value = RequireString(co, "value", "case"),
                        When = ParseAst(Require(co, "when", "case"))
                    };
                    if (cd.Value.Length > 36) throw new ConfigException(RejectReason.Schema, "case value > 36");
                    if (!seen.Add(cd.Value))
                        throw new ConfigException(RejectReason.Schema, $"partition {s.Id} duplicate value {cd.Value}");
                    s.Cases.Add(cd);
                }
            }

            return s;
        }

        private static ActionDef ParseAction(JObject o)
        {
            CheckKeys(o, "action", "id", "type", "params", "group", "frequency", "cooldown_s", "cap");
            var a = new ActionDef { Id = RequireIdent(o, "id", "action") };
            var typeStr = RequireString(o, "type", "action");
            if (!Enum.TryParse(typeStr, false, out ActionType type))
                throw new ConfigException(RejectReason.Schema, $"action {a.Id} type {typeStr}");
            a.Type = type;
            a.Group = RequireEnum(o, "group", "action", ActionGroup.All);
            var isNotif = a.Type == ActionType.SCHEDULE_LOCAL_NOTIFICATION;
            if (isNotif != (a.Group == ActionGroup.Notification))
                throw new ConfigException(RejectReason.Schema, $"action {a.Id}: group notification ⇔ SCHEDULE_LOCAL_NOTIFICATION");
            a.OneShot = RequireEnum(o, "frequency", "action", "repeatable", "one_shot") == "one_shot";
            if (a.OneShot && isNotif)
                throw new ConfigException(RejectReason.Schema, $"action {a.Id}: one_shot on notification");
            a.CooldownS = RequireIntRange(o, "cooldown_s", "action", 0, int.MaxValue);

            var cap = Require(o, "cap", "action");
            if (cap.Type == JTokenType.Boolean)
            {
                if (cap.Value<bool>()) throw new ConfigException(RejectReason.Schema, $"action {a.Id}: cap true");
            }
            else
            {
                var co = AsObject(cap, "cap");
                CheckKeys(co, "cap", "count", "window_s");
                a.Cap = new CapDef
                {
                    Count = RequireIntRange(co, "count", "cap", 1, int.MaxValue),
                    WindowS = RequireIntRange(co, "window_s", "cap", 1, int.MaxValue)
                };
            }

            var p = RequireObject(o, "params", "action");
            switch (a.Type)
            {
                case ActionType.GIVE_REWARD:
                    CheckKeys(p, "params", "reward_id", "amount");
                    a.Params["reward_id"] = RequireIdent(p, "reward_id", "params");
                    a.Params["amount"] = (long)RequireIntRange(p, "amount", "params", 1, 1000);
                    break;
                case ActionType.SHOW_POPUP:
                    CheckKeys(p, "params", "popup_id", "text_key");
                    a.Params["popup_id"] = RequireIdent(p, "popup_id", "params");
                    a.Params["text_key"] = RequireString(p, "text_key", "params", true);
                    break;
                case ActionType.SHOW_OFFER:
                    CheckKeys(p, "params", "offer_id");
                    a.Params["offer_id"] = RequireIdent(p, "offer_id", "params");
                    break;
                case ActionType.CHANGE_DIFFICULTY:
                    CheckKeys(p, "params", "delta", "scope");
                    var delta = RequireIntRange(p, "delta", "params", -2, 2);
                    if (delta == 0) throw new ConfigException(RejectReason.Schema, $"action {a.Id}: delta 0");
                    a.Params["delta"] = (long)delta;
                    a.Params["scope"] = RequireEnum(p, "scope", "params", "next_unit");
                    break;
                case ActionType.SCHEDULE_LOCAL_NOTIFICATION:
                    CheckKeys(p, "params", "template_id", "delay_h");
                    a.Params["template_id"] = RequireIdent(p, "template_id", "params");
                    a.Params["delay_h"] = RequireNumberRange(p, "delay_h", "params", 0.5, 168);
                    break;
            }

            return a;
        }

        private static RuleDef ParseRule(JObject o)
        {
            CheckKeys(o, "rule", "id", "enabled", "priority", "on", "fire", "when", "then", "valid_until", "def_hash");
            var r = new RuleDef
            {
                Id = RequireIdent(o, "id", "rule"),
                Enabled = RequireBool(o, "enabled", "rule"),
                Priority = RequireIntRange(o, "priority", "rule", -1000, 1000),
                FireEdge = RequireEnum(o, "fire", "rule", "edge", "level") == "edge",
                When = ParseAst(Require(o, "when", "rule")),
                DefHash = OptionalHex16(o, "def_hash")
            };
            var then = RequireString(o, "then", "rule");
            if (then != RuleDef.NONE && !IdentRegex.IsMatch(then))
                throw new ConfigException(RejectReason.Schema, $"rule {r.Id} then {then}");
            r.Then = then;
            r.ValidUntil = ParseIsoTime(RequireString(o, "valid_until", "rule"), "valid_until");

            var on = RequireArray(o, "on", "rule");
            if (on.Count < 1) throw new ConfigException(RejectReason.Schema, $"rule {r.Id} on empty");
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in on)
            {
                if (t.Type != JTokenType.String) throw new ConfigException(RejectReason.Schema, $"rule {r.Id} on item");
                var s = t.Value<string>();
                if (!Trigger.IsValidOnEntry(s)) throw new ConfigException(RejectReason.Schema, $"rule {r.Id} on {s}");
                if (!seen.Add(s)) throw new ConfigException(RejectReason.Schema, $"rule {r.Id} duplicate on {s}");
                list.Add(s);
            }

            r.On = list.ToArray();
            return r;
        }

        private static ExperimentDef ParseExperiment(JObject o)
        {
            CheckKeys(o, "experiment", "id", "layer", "allocation", "ends_at", "variants");
            var e = new ExperimentDef
            {
                Id = RequireIdent(o, "id", "experiment"),
                Layer = RequireIdent(o, "layer", "experiment"),
                Allocation = RequireNumberRange(o, "allocation", "experiment", 0, 1),
                EndsAt = ParseIsoTime(RequireString(o, "ends_at", "experiment"), "ends_at")
            };
            if (e.Allocation <= 0) throw new ConfigException(RejectReason.Schema, $"experiment {e.Id} allocation 0");
            if (e.Layer.Length > 20) throw new ConfigException(RejectReason.Schema, $"experiment {e.Id} layer > 20");

            var variants = RequireArray(o, "variants", "experiment");
            if (variants.Count != 2) throw new ConfigException(RejectReason.Schema, $"experiment {e.Id} must have 2 variants");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            double sum = 0;
            var maxVariantId = 0;
            foreach (var t in variants)
            {
                var vo = AsObject(t, "variant");
                CheckKeys(vo, "variant", "id", "weight", "rule_actions");
                var v = new VariantDef
                {
                    Id = RequireIdent(vo, "id", "variant"),
                    Weight = RequireNumberRange(vo, "weight", "variant", 0, 1)
                };
                if (v.Weight <= 0) throw new ConfigException(RejectReason.Schema, $"variant {v.Id} weight 0");
                if (!ids.Add(v.Id)) throw new ConfigException(RejectReason.Schema, $"experiment {e.Id} duplicate variant");
                maxVariantId = Math.Max(maxVariantId, v.Id.Length);
                sum += v.Weight;
                var ra = RequireObject(vo, "rule_actions", "variant");
                foreach (var p in ra.Properties())
                {
                    if (!IdentRegex.IsMatch(p.Name) || p.Value.Type != JTokenType.String)
                        throw new ConfigException(RejectReason.Schema, $"rule_actions {p.Name}");
                    var val = p.Value.Value<string>();
                    if (val != RuleDef.NONE && !IdentRegex.IsMatch(val))
                        throw new ConfigException(RejectReason.Schema, $"rule_actions {p.Name} -> {val}");
                    v.RuleActions[p.Name] = val;
                }

                e.Variants.Add(v);
            }

            if (Math.Abs(sum - 1.0) > 1e-6) throw new ConfigException(RejectReason.Schema, $"experiment {e.Id} weights ≠ 1");
            if (e.Id.Length + 1 + maxVariantId > 36)
                throw new ConfigException(RejectReason.Schema, $"experiment {e.Id}: id + variant > 36");
            return e;
        }

        #endregion

        #region AST

        public static AstNode ParseAst(JToken t)
        {
            var o = AsObject(t, "ast");
            var hasOp = o.ContainsKey("op");
            var hasRef = o.ContainsKey("ref");
            var hasValue = o.ContainsKey("value");
            var count = (hasOp ? 1 : 0) + (hasRef ? 1 : 0) + (hasValue ? 1 : 0);
            if (count != 1) throw new ConfigException(RejectReason.Schema, "ast node must have exactly one of op/ref/value");

            if (hasOp)
            {
                CheckKeys(o, "ast", "op", "args");
                var op = RequireString(o, "op", "ast");
                if (!AstOps.IsKnown(op)) throw new ConfigException(RejectReason.Schema, $"unknown op {op}");
                var args = RequireArray(o, "args", "ast");
                var list = new AstNode[args.Count];
                for (var i = 0; i < args.Count; i++) list[i] = ParseAst(args[i]);
                return AstNode.MakeOp(op, list);
            }

            if (hasRef)
            {
                CheckKeys(o, "ast", "ref", "window");
                var r = RequireString(o, "ref", "ast");
                var w = RefWindow.None;
                if (o.ContainsKey("window"))
                {
                    var ws = RequireEnum(o, "window", "ast", "session", "7d", "30d");
                    w = ws == "session" ? RefWindow.Session : ws == "7d" ? RefWindow.Days7 : RefWindow.Days30;
                }

                return AstNode.MakeRef(r, w);
            }

            CheckKeys(o, "ast", "value");
            var v = o["value"];
            switch (v.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float:
                    return AstNode.MakeValue(SegValue.Number(v.Value<double>()));
                case JTokenType.Boolean:
                    return AstNode.MakeValue(SegValue.Bool(v.Value<bool>()));
                case JTokenType.String:
                    return AstNode.MakeValue(SegValue.String(v.Value<string>()));
                default:
                    throw new ConfigException(RejectReason.Schema, "ast value must be number/bool/string");
            }
        }

        #endregion

        #region Helpers

        public static long ParseIsoTime(string s, string field)
        {
            if (DateTime.TryParseExact(s, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                return new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeSeconds();
            throw new ConfigException(RejectReason.Schema, $"{field} not ISO 8601 UTC: {s}");
        }

        public static bool IsIdent(string s) => s != null && IdentRegex.IsMatch(s);

        private static void CheckKeys(JObject o, string ctx, params string[] allowed)
        {
            foreach (var p in o.Properties())
                if (Array.IndexOf(allowed, p.Name) < 0)
                    throw new ConfigException(RejectReason.Schema, $"{ctx}: unknown field {p.Name}");
        }

        private static JObject AsObject(JToken t, string ctx)
        {
            return t as JObject ?? throw new ConfigException(RejectReason.Schema, $"{ctx} must be object");
        }

        private static JToken Require(JObject o, string key, string ctx)
        {
            var t = o[key];
            if (t == null || t.Type == JTokenType.Null)
                throw new ConfigException(RejectReason.Schema, $"{ctx}.{key} missing");
            return t;
        }

        private static JObject RequireObject(JObject o, string key, string ctx) => AsObject(Require(o, key, ctx), $"{ctx}.{key}");

        private static JArray RequireArray(JObject o, string key, string ctx)
        {
            return Require(o, key, ctx) as JArray ?? throw new ConfigException(RejectReason.Schema, $"{ctx}.{key} must be array");
        }

        private static string RequireString(JObject o, string key, string ctx, bool allowEmpty = false)
        {
            var t = Require(o, key, ctx);
            if (t.Type != JTokenType.String) throw new ConfigException(RejectReason.Schema, $"{ctx}.{key} must be string");
            var s = t.Value<string>();
            if (!allowEmpty && s.Length == 0) throw new ConfigException(RejectReason.Schema, $"{ctx}.{key} empty");
            return s;
        }

        private static string OptionalString(JObject o, string key)
        {
            var t = o[key];
            if (t == null || t.Type == JTokenType.Null) return null;
            if (t.Type != JTokenType.String) throw new ConfigException(RejectReason.Schema, $"{key} must be string");
            return t.Value<string>();
        }

        /// <summary>def_hash do CLI tính; thiếu → null, engine tự tính bằng <see cref="DefHash" /> (dev / test).</summary>
        private static string OptionalHex16(JObject o, string key)
        {
            var s = OptionalString(o, key);
            if (s == null) return null;
            if (!Hex16Regex.IsMatch(s)) throw new ConfigException(RejectReason.Schema, $"{key} not hex16");
            return s;
        }

        private static string RequireIdent(JObject o, string key, string ctx)
        {
            var s = RequireString(o, key, ctx);
            if (!IdentRegex.IsMatch(s)) throw new ConfigException(RejectReason.Schema, $"{ctx}.{key} invalid ident {s}");
            return s;
        }

        private static string RequireSemver(JObject o, string key)
        {
            var s = RequireString(o, key, "config");
            if (!SemverRegex.IsMatch(s)) throw new ConfigException(RejectReason.Schema, $"{key} not semver");
            return s;
        }

        private static string RequireEnum(JObject o, string key, string ctx, params string[] allowed)
        {
            var s = RequireString(o, key, ctx);
            if (Array.IndexOf(allowed, s) < 0) throw new ConfigException(RejectReason.Schema, $"{ctx}.{key} invalid {s}");
            return s;
        }

        private static bool RequireBool(JObject o, string key, string ctx)
        {
            var t = Require(o, key, ctx);
            if (t.Type != JTokenType.Boolean) throw new ConfigException(RejectReason.Schema, $"{ctx}.{key} must be bool");
            return t.Value<bool>();
        }

        private static long RequireLong(JObject o, string key, string ctx)
        {
            var t = Require(o, key, ctx);
            if (t.Type != JTokenType.Integer) throw new ConfigException(RejectReason.Schema, $"{ctx}.{key} must be int");
            return t.Value<long>();
        }

        private static int RequireInt(JObject o, string key, string ctx)
        {
            var l = RequireLong(o, key, ctx);
            if (l > int.MaxValue || l < int.MinValue) throw new ConfigException(RejectReason.Schema, $"{ctx}.{key} out of range");
            return (int)l;
        }

        private static int RequireIntRange(JObject o, string key, string ctx, int min, int max)
        {
            var v = RequireInt(o, key, ctx);
            if (v < min || v > max) throw new ConfigException(RejectReason.Schema, $"{ctx}.{key}={v} outside {min}..{max}");
            return v;
        }

        private static double RequireNumberRange(JObject o, string key, string ctx, double min, double max)
        {
            var t = Require(o, key, ctx);
            if (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)
                throw new ConfigException(RejectReason.Schema, $"{ctx}.{key} must be number");
            var v = t.Value<double>();
            if (v < min || v > max) throw new ConfigException(RejectReason.Schema, $"{ctx}.{key}={v} outside {min}..{max}");
            return v;
        }

        #endregion
    }

    /// <summary>Trigger event và qualifier — §8.1.</summary>
    public static class Trigger
    {
        public static readonly string[] TriggerEvents =
            { "SESSION_START", "PROGRESS_COMPLETE", "PROGRESS_FAIL", "PROGRESS_QUIT", "PURCHASE", "SCREEN_OPEN", "CUSTOM_EVENT" };

        public static bool IsValidOnEntry(string entry)
        {
            if (string.IsNullOrEmpty(entry)) return false;
            var idx = entry.IndexOf(':');
            var ev = idx < 0 ? entry : entry.Substring(0, idx);
            if (Array.IndexOf(TriggerEvents, ev) < 0) return false;
            if (idx < 0) return true;
            if (ev != "SCREEN_OPEN" && ev != "CUSTOM_EVENT") return false;
            return ConfigParser.IsIdent(entry.Substring(idx + 1));
        }

        public static void Split(string entry, out string ev, out string qualifier)
        {
            var idx = entry.IndexOf(':');
            if (idx < 0)
            {
                ev = entry;
                qualifier = null;
            }
            else
            {
                ev = entry.Substring(0, idx);
                qualifier = entry.Substring(idx + 1);
            }
        }
    }
}
