using System;
using System.Collections.Generic;

namespace Ezg.UserSegment.Engine
{
    /// <summary>
    ///     Bước 4–9 của §C.1.9 trên config đã parse: min_sdk, game/env, ref, type, cycle, limit. Điền index, thứ tự topo
    ///     và def_hash thiếu. Thứ tự thực tế: ref → cycle → type → limit (type-check cần topo).
    /// </summary>
    public static class ConfigValidator
    {
        public const int MAX_DEPTH = 32;
        public const int MAX_NODES = 500;

        private const string FEATURE = "feature.", SEGMENT = "segment.", CUSTOM = "custom.", CONST = "const.";

        public static void Validate(SegConfig c, string sdkVersion, string gameId, string env, Manifest manifest)
        {
            if (CompareSemver(c.MinSdkVersion, sdkVersion) > 0)
                throw new ConfigException(RejectReason.MinSdk, $"min_sdk_version {c.MinSdkVersion} > sdk {sdkVersion}");
            if (c.GameId != gameId || c.Env != env)
                throw new ConfigException(RejectReason.GameEnvMismatch, $"{c.GameId}/{c.Env} vs {gameId}/{env}");

            BuildIndexes(c);
            CheckReferences(c, manifest);
            var order = TopoSort(c);
            c.EvalOrder = order;
            TypeCheckAll(c, manifest);
            CheckLimits(c);
            FillDefHashes(c);
            c.RulesOrdered = new List<RuleDef>(c.Rules);
            c.RulesOrdered.Sort((a, b) =>
            {
                var p = b.Priority.CompareTo(a.Priority);
                return p != 0 ? p : string.CompareOrdinal(a.Id, b.Id);
            });
        }

        #region Index + refs

        private static void BuildIndexes(SegConfig c)
        {
            c.FormulaById.Clear();
            c.SegmentById.Clear();
            c.ActionById.Clear();
            c.RuleById.Clear();
            foreach (var f in c.Formulas)
                if (!c.FormulaById.TryAdd(f.Id, f))
                    throw new ConfigException(RejectReason.Ref, $"duplicate formula id {f.Id}");
            foreach (var s in c.Segments)
            {
                if (c.FormulaById.ContainsKey(s.Id) || !c.SegmentById.TryAdd(s.Id, s))
                    throw new ConfigException(RejectReason.Ref, $"duplicate formula/segment id {s.Id}");
            }

            foreach (var a in c.Actions)
                if (!c.ActionById.TryAdd(a.Id, a))
                    throw new ConfigException(RejectReason.Ref, $"duplicate action id {a.Id}");
            foreach (var r in c.Rules)
                if (!c.RuleById.TryAdd(r.Id, r))
                    throw new ConfigException(RejectReason.Ref, $"duplicate rule id {r.Id}");
            var expIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in c.Experiments)
                if (!expIds.Add(e.Id))
                    throw new ConfigException(RejectReason.Ref, $"duplicate experiment id {e.Id}");
        }

        private static void CheckReferences(SegConfig c, Manifest m)
        {
            foreach (var f in c.Formulas) CheckRefs(f.Expr, c, m, $"formula {f.Id}");
            foreach (var s in c.Segments)
            {
                if (s.IsTag)
                {
                    CheckRefs(s.Enter, c, m, $"tag {s.Id}");
                    CheckRefs(s.Exit, c, m, $"tag {s.Id}");
                }
                else
                {
                    foreach (var cs in s.Cases) CheckRefs(cs.When, c, m, $"partition {s.Id}");
                }
            }

            foreach (var r in c.Rules)
            {
                CheckRefs(r.When, c, m, $"rule {r.Id}");
                if (r.Then != RuleDef.NONE && !c.ActionById.ContainsKey(r.Then))
                    throw new ConfigException(RejectReason.Ref, $"rule {r.Id} then {r.Then} not found");
            }

            var ruleOwner = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var e in c.Experiments)
            {
                var rulesInExp = new HashSet<string>(StringComparer.Ordinal);
                foreach (var v in e.Variants)
                foreach (var kv in v.RuleActions)
                {
                    if (!c.RuleById.ContainsKey(kv.Key))
                        throw new ConfigException(RejectReason.Ref, $"experiment {e.Id} rule {kv.Key} not found");
                    if (kv.Value != RuleDef.NONE && !c.ActionById.ContainsKey(kv.Value))
                        throw new ConfigException(RejectReason.Ref, $"experiment {e.Id} action {kv.Value} not found");
                    rulesInExp.Add(kv.Key);
                }

                foreach (var rid in rulesInExp)
                {
                    if (ruleOwner.TryGetValue(rid, out var other))
                        throw new ConfigException(RejectReason.Schema, $"rule {rid} in experiments {other} and {e.Id}");
                    ruleOwner[rid] = e.Id;
                }
            }
        }

        private static void CheckRefs(AstNode n, SegConfig c, Manifest m, string ctx)
        {
            if (n.Kind == AstKind.Op)
            {
                foreach (var a in n.Args) CheckRefs(a, c, m, ctx);
                return;
            }

            if (n.Kind != AstKind.Ref) return;
            var r = n.Ref;
            if (r.StartsWith("state.") || r.StartsWith("context."))
            {
                if (!StateSchema.TryGet(r, m.CustomEvents, out _))
                    throw new ConfigException(RejectReason.Ref, $"{ctx}: unknown ref {r}");
            }
            else if (r.StartsWith(FEATURE))
            {
                if (!c.FormulaById.ContainsKey(r.Substring(FEATURE.Length)))
                    throw new ConfigException(RejectReason.Ref, $"{ctx}: unknown formula {r}");
            }
            else if (r.StartsWith(SEGMENT))
            {
                if (!c.SegmentById.ContainsKey(r.Substring(SEGMENT.Length)))
                    throw new ConfigException(RejectReason.Ref, $"{ctx}: unknown segment {r}");
            }
            else if (r.StartsWith(CUSTOM))
            {
                if (!m.CustomState.ContainsKey(r.Substring(CUSTOM.Length)))
                    throw new ConfigException(RejectReason.Ref, $"{ctx}: custom key not in manifest {r}");
            }
            else if (r.StartsWith(CONST))
            {
                if (!c.Const.ContainsKey(r.Substring(CONST.Length)))
                    throw new ConfigException(RejectReason.Ref, $"{ctx}: const missing {r}");
            }
            else
            {
                throw new ConfigException(RejectReason.Ref, $"{ctx}: forbidden/unknown namespace {r}");
            }
        }

        #endregion

        #region Topo

        /// <summary>DFS trên formula ∪ segment; vòng → cycle. Tag đọc chính nó không tạo vòng (đọc giá trị persist) — §6.3.</summary>
        private static List<string> TopoSort(SegConfig c)
        {
            var order = new List<string>();
            var color = new Dictionary<string, int>(StringComparer.Ordinal); // 1 visiting, 2 done
            foreach (var f in c.Formulas) Visit(FEATURE + f.Id, c, color, order);
            foreach (var s in c.Segments) Visit(SEGMENT + s.Id, c, color, order);
            return order;
        }

        private static void Visit(string node, SegConfig c, Dictionary<string, int> color, List<string> order)
        {
            if (color.TryGetValue(node, out var st))
            {
                if (st == 1) throw new ConfigException(RejectReason.Cycle, $"dependency cycle at {node}");
                return;
            }

            color[node] = 1;
            foreach (var dep in Deps(node, c))
                if (dep != node) Visit(dep, c, color, order);
            color[node] = 2;
            order.Add(node);
        }

        private static IEnumerable<string> Deps(string node, SegConfig c)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (node.StartsWith(FEATURE))
            {
                Collect(c.FormulaById[node.Substring(FEATURE.Length)].Expr, set);
            }
            else
            {
                var s = c.SegmentById[node.Substring(SEGMENT.Length)];
                if (s.IsTag)
                {
                    Collect(s.Enter, set);
                    Collect(s.Exit, set);
                }
                else
                {
                    foreach (var cs in s.Cases) Collect(cs.When, set);
                }
            }

            return set;
        }

        private static void Collect(AstNode n, HashSet<string> set)
        {
            if (n.Kind == AstKind.Ref)
            {
                if (n.Ref.StartsWith(FEATURE) || n.Ref.StartsWith(SEGMENT)) set.Add(n.Ref);
                return;
            }

            if (n.Kind == AstKind.Op)
                foreach (var a in n.Args)
                    Collect(a, set);
        }

        #endregion

        #region Type check

        private sealed class TypeCtx
        {
            public SegConfig Config;
            public Manifest Manifest;
            public Dictionary<string, SegType> FormulaTypes = new Dictionary<string, SegType>(StringComparer.Ordinal);
        }

        private static void TypeCheckAll(SegConfig c, Manifest m)
        {
            var ctx = new TypeCtx { Config = c, Manifest = m };
            foreach (var node in c.EvalOrder)
            {
                if (node.StartsWith(FEATURE))
                {
                    var f = c.FormulaById[node.Substring(FEATURE.Length)];
                    var t = Check(f.Expr, ctx);
                    if (t == SegType.String) throw new ConfigException(RejectReason.Type, $"formula {f.Id} result STRING");
                    ctx.FormulaTypes[f.Id] = t;
                }
                else
                {
                    var s = c.SegmentById[node.Substring(SEGMENT.Length)];
                    if (s.IsTag)
                    {
                        ExpectBool(s.Enter, ctx, $"tag {s.Id} enter");
                        ExpectBool(s.Exit, ctx, $"tag {s.Id} exit");
                    }
                    else
                    {
                        foreach (var cs in s.Cases) ExpectBool(cs.When, ctx, $"partition {s.Id} case {cs.Value}");
                    }
                }
            }

            foreach (var r in c.Rules) ExpectBool(r.When, ctx, $"rule {r.Id} when");
        }

        private static void ExpectBool(AstNode n, TypeCtx ctx, string what)
        {
            if (Check(n, ctx) != SegType.Bool) throw new ConfigException(RejectReason.Type, $"{what} must be BOOL");
        }

        private static bool Numberish(SegType t) => t != SegType.String;

        private static SegType Check(AstNode n, TypeCtx ctx)
        {
            SegType result;
            switch (n.Kind)
            {
                case AstKind.Value:
                    result = n.Value.Type;
                    break;
                case AstKind.Ref:
                    result = RefType(n, ctx);
                    break;
                default:
                    result = OpType(n, ctx);
                    break;
            }

            n.ResultType = result;
            return result;
        }

        private static SegType RefType(AstNode n, TypeCtx ctx)
        {
            var r = n.Ref;
            SegType t;
            var windowed = false;
            if (r.StartsWith("state.") || r.StartsWith("context."))
            {
                StateSchema.TryGet(r, ctx.Manifest.CustomEvents, out var info);
                t = info.Type;
                windowed = info.Windowed;
            }
            else if (r.StartsWith(FEATURE))
            {
                var id = r.Substring(FEATURE.Length);
                if (!ctx.FormulaTypes.TryGetValue(id, out t))
                    throw new ConfigException(RejectReason.Cycle, $"formula {id} referenced before evaluation");
            }
            else if (r.StartsWith(SEGMENT))
            {
                t = ctx.Config.SegmentById[r.Substring(SEGMENT.Length)].IsTag ? SegType.Bool : SegType.String;
            }
            else if (r.StartsWith(CUSTOM))
            {
                t = ctx.Manifest.CustomStateType(r.Substring(CUSTOM.Length));
            }
            else
            {
                t = SegType.Number; // const.*
            }

            if (n.Window != RefWindow.None && !windowed)
                throw new ConfigException(RejectReason.Type, $"window on non-windowed ref {r}");
            return t;
        }

        private static SegType OpType(AstNode n, TypeCtx ctx)
        {
            var op = n.Op;
            var args = n.Args;
            var types = new SegType[args.Length];
            for (var i = 0; i < args.Length; i++) types[i] = Check(args[i], ctx);

            switch (op)
            {
                case AstOps.ADD:
                case AstOps.MUL:
                case AstOps.MIN:
                case AstOps.MAX:
                    Arity(op, args.Length, 2, int.MaxValue);
                    AllNumber(op, types);
                    return SegType.Number;
                case AstOps.SUB:
                case AstOps.DIV:
                case AstOps.MOD:
                    Arity(op, args.Length, 2, 2);
                    AllNumber(op, types);
                    return SegType.Number;
                case AstOps.GT:
                case AstOps.GTE:
                case AstOps.LT:
                case AstOps.LTE:
                    Arity(op, args.Length, 2, 2);
                    AllNumber(op, types);
                    return SegType.Bool;
                case AstOps.EQ:
                case AstOps.NEQ:
                    Arity(op, args.Length, 2, 2);
                    if (types[0] == SegType.String || types[1] == SegType.String)
                    {
                        if (types[0] != SegType.String || types[1] != SegType.String)
                            throw new ConfigException(RejectReason.Type, $"{op}: STRING vs non-STRING");
                    }

                    return SegType.Bool;
                case AstOps.AND:
                case AstOps.OR:
                    Arity(op, args.Length, 2, int.MaxValue);
                    AllBool(op, types);
                    return SegType.Bool;
                case AstOps.NOT:
                    Arity(op, args.Length, 1, 1);
                    AllBool(op, types);
                    return SegType.Bool;
                case AstOps.IF:
                    Arity(op, args.Length, 3, 3);
                    if (types[0] != SegType.Bool) throw new ConfigException(RejectReason.Type, "IF cond must be BOOL");
                    if (types[1] == SegType.String || types[2] == SegType.String)
                        throw new ConfigException(RejectReason.Type, "IF branches must be NUMBER or BOOL");
                    return types[1] == SegType.Bool && types[2] == SegType.Bool ? SegType.Bool : SegType.Number;
                case AstOps.ABS:
                case AstOps.TIME_SINCE:
                    Arity(op, args.Length, 1, 1);
                    AllNumber(op, types);
                    return SegType.Number;
                case AstOps.CLAMP:
                    Arity(op, args.Length, 3, 3);
                    AllNumber(op, types);
                    return SegType.Number;
                case AstOps.DAYS_BETWEEN:
                    Arity(op, args.Length, 2, 2);
                    AllNumber(op, types);
                    return SegType.Number;
                case AstOps.NOW:
                    Arity(op, args.Length, 0, 0);
                    return SegType.Number;
                default:
                    throw new ConfigException(RejectReason.Schema, $"unknown op {op}");
            }
        }

        private static void Arity(string op, int n, int min, int max)
        {
            if (n < min || n > max) throw new ConfigException(RejectReason.Type, $"{op}: arity {n}");
        }

        private static void AllNumber(string op, SegType[] types)
        {
            foreach (var t in types)
                if (!Numberish(t))
                    throw new ConfigException(RejectReason.Type, $"{op}: STRING operand");
        }

        private static void AllBool(string op, SegType[] types)
        {
            foreach (var t in types)
                if (t != SegType.Bool)
                    throw new ConfigException(RejectReason.Type, $"{op}: operand must be BOOL");
        }

        #endregion

        #region Limits + hash

        private static void CheckLimits(SegConfig c)
        {
            foreach (var f in c.Formulas) Limit(f.Expr, $"formula {f.Id}");
            foreach (var s in c.Segments)
            {
                if (s.IsTag)
                {
                    Limit(s.Enter, $"tag {s.Id}");
                    Limit(s.Exit, $"tag {s.Id}");
                }
                else
                {
                    foreach (var cs in s.Cases) Limit(cs.When, $"partition {s.Id}");
                }
            }

            foreach (var r in c.Rules) Limit(r.When, $"rule {r.Id}");
        }

        private static void Limit(AstNode n, string ctx)
        {
            var nodes = 0;
            var depth = Measure(n, 1, ref nodes);
            if (depth > MAX_DEPTH) throw new ConfigException(RejectReason.Limit, $"{ctx}: depth {depth} > {MAX_DEPTH}");
            if (nodes > MAX_NODES) throw new ConfigException(RejectReason.Limit, $"{ctx}: nodes {nodes} > {MAX_NODES}");
        }

        private static int Measure(AstNode n, int depth, ref int nodes)
        {
            nodes++;
            var max = depth;
            if (n.Kind == AstKind.Op)
                foreach (var a in n.Args)
                    max = Math.Max(max, Measure(a, depth + 1, ref nodes));
            return max;
        }

        private static void FillDefHashes(SegConfig c)
        {
            foreach (var s in c.Segments)
                if (s.IsTag && string.IsNullOrEmpty(s.DefHash))
                    s.DefHash = DefHash.ForTag(s.Enter, s.Exit);
            foreach (var r in c.Rules)
                if (string.IsNullOrEmpty(r.DefHash))
                    r.DefHash = DefHash.ForRule(r.On, r.When);
        }

        #endregion

        /// <summary>So sánh semver bộ ba số. &gt; 0 nếu a &gt; b.</summary>
        public static int CompareSemver(string a, string b)
        {
            var pa = a.Split('.');
            var pb = b.Split('.');
            for (var i = 0; i < 3; i++)
            {
                var x = i < pa.Length && int.TryParse(pa[i], out var xi) ? xi : 0;
                var y = i < pb.Length && int.TryParse(pb[i], out var yi) ? yi : 0;
                if (x != y) return x.CompareTo(y);
            }

            return 0;
        }
    }
}
