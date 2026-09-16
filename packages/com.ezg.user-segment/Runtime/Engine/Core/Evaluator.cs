using System;
using System.Collections.Generic;

namespace Ezg.UserSegment.Engine
{
    /// <summary>Ngữ cảnh evaluate một trigger: state + config + giá trị feature / segment đã tính trong pass này.</summary>
    public sealed class EvalContext
    {
        public EngineState State;
        public SegConfig Config;
        public Manifest Manifest;
        public long Now;
        public long Today;
        public double SessionTimeS;
        public readonly Dictionary<string, SegValue> Features = new Dictionary<string, SegValue>(StringComparer.Ordinal);
        public readonly Dictionary<string, SegValue> Segments = new Dictionary<string, SegValue>(StringComparer.Ordinal);

        public void ResetPass(long now, long today, double sessionTimeS)
        {
            Now = now;
            Today = today;
            SessionTimeS = sessionTimeS;
            Features.Clear();
            Segments.Clear();
        }
    }

    /// <summary>Evaluate AST không null — §6.3, §C.2.3. Chia 0 → 0; EQ số dùng epsilon 1e-9; TIME_SINCE clamp ≥ 0.</summary>
    public static class Evaluator
    {
        public const double EPS = 1e-9;

        public static SegValue Eval(AstNode n, EvalContext ctx)
        {
            switch (n.Kind)
            {
                case AstKind.Value: return n.Value;
                case AstKind.Ref: return ResolveRef(n.Ref, n.Window, ctx);
                default: return EvalOp(n, ctx);
            }
        }

        private static SegValue EvalOp(AstNode n, EvalContext ctx)
        {
            var a = n.Args;
            switch (n.Op)
            {
                case AstOps.ADD:
                {
                    double s = 0;
                    foreach (var x in a) s += Eval(x, ctx).AsNumber;
                    return SegValue.Number(s);
                }
                case AstOps.MUL:
                {
                    double s = 1;
                    foreach (var x in a) s *= Eval(x, ctx).AsNumber;
                    return SegValue.Number(s);
                }
                case AstOps.SUB: return SegValue.Number(Eval(a[0], ctx).AsNumber - Eval(a[1], ctx).AsNumber);
                case AstOps.DIV:
                {
                    var d = Eval(a[1], ctx).AsNumber;
                    return SegValue.Number(d == 0 ? 0 : Eval(a[0], ctx).AsNumber / d);
                }
                case AstOps.MOD:
                {
                    var d = Eval(a[1], ctx).AsNumber;
                    return SegValue.Number(d == 0 ? 0 : Eval(a[0], ctx).AsNumber % d);
                }
                case AstOps.GT: return SegValue.Bool(Eval(a[0], ctx).AsNumber > Eval(a[1], ctx).AsNumber);
                case AstOps.GTE: return SegValue.Bool(Eval(a[0], ctx).AsNumber >= Eval(a[1], ctx).AsNumber);
                case AstOps.LT: return SegValue.Bool(Eval(a[0], ctx).AsNumber < Eval(a[1], ctx).AsNumber);
                case AstOps.LTE: return SegValue.Bool(Eval(a[0], ctx).AsNumber <= Eval(a[1], ctx).AsNumber);
                case AstOps.EQ: return SegValue.Bool(Equal(Eval(a[0], ctx), Eval(a[1], ctx)));
                case AstOps.NEQ: return SegValue.Bool(!Equal(Eval(a[0], ctx), Eval(a[1], ctx)));
                case AstOps.AND:
                {
                    var r = true;
                    foreach (var x in a) r &= Eval(x, ctx).AsBool; // evaluate hết, không short-circuit
                    return SegValue.Bool(r);
                }
                case AstOps.OR:
                {
                    var r = false;
                    foreach (var x in a) r |= Eval(x, ctx).AsBool;
                    return SegValue.Bool(r);
                }
                case AstOps.NOT: return SegValue.Bool(!Eval(a[0], ctx).AsBool);
                case AstOps.IF:
                {
                    var v = Eval(a[0], ctx).AsBool ? Eval(a[1], ctx) : Eval(a[2], ctx);
                    return n.ResultType == SegType.Bool ? SegValue.Bool(v.AsBool) : SegValue.Number(v.AsNumber);
                }
                case AstOps.MIN:
                {
                    var m = double.MaxValue;
                    foreach (var x in a) m = Math.Min(m, Eval(x, ctx).AsNumber);
                    return SegValue.Number(m);
                }
                case AstOps.MAX:
                {
                    var m = double.MinValue;
                    foreach (var x in a) m = Math.Max(m, Eval(x, ctx).AsNumber);
                    return SegValue.Number(m);
                }
                case AstOps.ABS: return SegValue.Number(Math.Abs(Eval(a[0], ctx).AsNumber));
                case AstOps.CLAMP:
                {
                    var x = Eval(a[0], ctx).AsNumber;
                    var lo = Eval(a[1], ctx).AsNumber;
                    var hi = Eval(a[2], ctx).AsNumber;
                    if (lo > hi) return SegValue.Number(lo);
                    return SegValue.Number(Math.Min(Math.Max(x, lo), hi));
                }
                case AstOps.NOW: return SegValue.Number(ctx.Now);
                case AstOps.TIME_SINCE:
                {
                    var ts = Eval(a[0], ctx).AsNumber;
                    return SegValue.Number(Math.Max(0, ctx.Now - ts));
                }
                case AstOps.DAYS_BETWEEN:
                {
                    var x = Eval(a[0], ctx).AsNumber;
                    var y = Eval(a[1], ctx).AsNumber;
                    return SegValue.Number(Math.Floor(y / Clock.DAY_S) - Math.Floor(x / Clock.DAY_S));
                }
                default:
                    throw new InvalidOperationException("unknown op " + n.Op);
            }
        }

        private static bool Equal(SegValue x, SegValue y)
        {
            if (x.Type == SegType.String || y.Type == SegType.String)
                return string.Equals(x.Str, y.Str, StringComparison.Ordinal);
            return Math.Abs(x.AsNumber - y.AsNumber) <= EPS;
        }

        public static SegValue ResolveRef(string r, RefWindow w, EvalContext ctx)
        {
            var s = ctx.State;
            if (r.StartsWith("feature."))
                return ctx.Features.TryGetValue(r.Substring(8), out var f) ? f : SegValue.Zero;
            if (r.StartsWith("segment."))
            {
                var id = r.Substring(8);
                if (ctx.Segments.TryGetValue(id, out var sv)) return sv;
                // Tag tự tham chiếu (hysteresis) hoặc chưa tính: đọc giá trị đã persist — §6.3
                if (ctx.Config.SegmentById.TryGetValue(id, out var def) && def.IsTag)
                    return SegValue.Bool(s.Tags.TryGetValue(id, out var ts) && ts.Value);
                return SegValue.Empty;
            }

            if (r.StartsWith("custom."))
            {
                var key = r.Substring(7);
                var t = ctx.Manifest.CustomStateType(key);
                if (!s.Custom.TryGetValue(key, out var raw)) return SegValue.Default(t);
                switch (t)
                {
                    case SegType.Bool: return SegValue.Bool(raw is bool b ? b : EngineState.ToDouble(raw) != 0);
                    case SegType.String: return SegValue.String(raw as string ?? string.Empty);
                    default: return SegValue.Number(EngineState.ToDouble(raw));
                }
            }

            if (r.StartsWith("const."))
                return SegValue.Number(ctx.Config.Const.TryGetValue(r.Substring(6), out var c) ? c : 0);

            if (r.StartsWith(StateSchema.CUSTOM_EVENT_COUNT_PREFIX))
            {
                var name = "custom_event_count." + r.Substring(StateSchema.CUSTOM_EVENT_COUNT_PREFIX.Length);
                return SegValue.Number(s.Counters.TryGetValue(name, out var cc) ? cc.Get(w) : 0);
            }

            var sc = s.Scalars;
            switch (r)
            {
                case "state.install_at": return SegValue.Number(sc.InstallAt);
                case "state.days_since_install":
                    return SegValue.Number(sc.InstallAt == 0 ? 0 : Clock.UtcDay(ctx.Now) - Clock.UtcDay(sc.InstallAt));
                case "state.days_since_last_active": return SegValue.Number(s.Session.DaysSinceLastActive);
                case "state.last_active_at": return SegValue.Number(sc.LastActiveAt);
                case "state.seeded": return SegValue.Bool(s.Seeded);
                case "state.seeded_at": return SegValue.Number(s.SeededAt);
                case "state.progress.current": return SegValue.Number(sc.ProgressCurrent);
                case "state.progress.max": return SegValue.Number(sc.ProgressMax);
                case "state.attempt_count_current_unit": return SegValue.Number(sc.AttemptCountCurrentUnit);
                case "state.fail_streak": return SegValue.Number(sc.FailStreak);
                case "state.win_streak": return SegValue.Number(sc.WinStreak);
                case "state.quit_after_fail_count": return SegValue.Number(sc.QuitAfterFailCount);
                case "state.last_fail_at": return SegValue.Number(sc.LastFailAt);
                case "state.total_spend_usd": return SegValue.Number(sc.TotalSpendUsd);
                case "state.first_purchase_at": return SegValue.Number(sc.FirstPurchaseAt);
                case "state.last_purchase_at": return SegValue.Number(sc.LastPurchaseAt);
                case "context.screen": return SegValue.String(s.Context.Screen);
                case "context.last_event": return SegValue.String(s.Context.LastEvent);
                case "context.session_time_s": return SegValue.Number(ctx.SessionTimeS);
                case "context.actions_shown_today":
                    return SegValue.Number(s.ShownToday.Day == ctx.Today ? s.ShownToday.Count : 0);
                case "context.clock_suspect": return SegValue.Bool(s.Context.ClockSuspect);
                case "context.config_stale": return SegValue.Bool(s.Context.ConfigStale);
            }

            // Counter có window: state.<name>
            if (r.StartsWith("state."))
            {
                var name = r.Substring(6);
                return SegValue.Number(s.Counters.TryGetValue(name, out var wc) ? wc.Get(w) : 0);
            }

            return SegValue.Zero;
        }
    }
}
