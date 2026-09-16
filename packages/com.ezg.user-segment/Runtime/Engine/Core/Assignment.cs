using System;
using System.Collections.Generic;

namespace Ezg.UserSegment.Engine
{
    /// <summary>Kết quả resolve experiment cho config hiện tại: then hiệu lực từng rule + rule thuộc experiment nào.</summary>
    public sealed class ExperimentRuntime
    {
        /// <summary>rule_id → action_id hoặc "none" sau khi áp variant.</summary>
        public readonly Dictionary<string, string> EffectiveThen = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>rule_id → experiment đang active chứa rule đó (để log exp_exposure, kể cả user chưa được assign).</summary>
        public readonly Dictionary<string, ExperimentDef> ExperimentByRule = new Dictionary<string, ExperimentDef>(StringComparer.Ordinal);

        /// <summary>layer → (exp_id, variant) đã assign.</summary>
        public readonly Dictionary<string, AssignmentState> Assigned = new Dictionary<string, AssignmentState>(StringComparer.Ordinal);

        /// <summary>Chuỗi `exp_id:variant;…` cho seg_snapshot.</summary>
        public string ExpsString()
        {
            var parts = new List<string>();
            foreach (var kv in Assigned) parts.Add(kv.Value.ExpId + ":" + kv.Value.Variant);
            parts.Sort(StringComparer.Ordinal);
            return string.Join(";", parts);
        }
    }

    /// <summary>Holdout + assignment sticky theo layer — §9.1, §9.2, §C.5.1.</summary>
    public static class Assignment
    {
        public const string HOLDOUT_KEY = "holdout";
        public const string PROP_HOLDOUT = "seg_holdout";
        public const string PROP_EXP_PREFIX = "exp_";

        public static void DecideHoldout(EngineState s, SegConfig c, TrackingEmitter t)
        {
            if (s.Holdout.Decided) return;
            var bucket = HashUtil.Bucket(s.UserId, HOLDOUT_KEY);
            s.Holdout.Value = bucket < c.HoldoutAllocation * HashUtil.BUCKETS;
            s.Holdout.Decided = true;
            t.SetUserProperty(PROP_HOLDOUT, s.Holdout.Value ? "1" : null);
        }

        /// <summary>
        ///     Từng layer: experiment còn hạn → giữ assignment cũ nếu còn hợp lệ, không thì hash mới; layer không còn experiment
        ///     → prune. User holdout không được assign. Trả về then hiệu lực của mọi rule.
        /// </summary>
        public static ExperimentRuntime Resolve(EngineState s, SegConfig c, long now, TrackingEmitter t, out bool changed)
        {
            changed = false;
            var rt = new ExperimentRuntime();
            foreach (var r in c.Rules) rt.EffectiveThen[r.Id] = r.Then;

            var activeByLayer = new Dictionary<string, ExperimentDef>(StringComparer.Ordinal);
            foreach (var e in c.Experiments)
                if (e.EndsAt > now && !activeByLayer.ContainsKey(e.Layer))
                    activeByLayer[e.Layer] = e;

            // Prune layer không còn experiment / experiment đổi id
            var layers = new List<string>(s.Assignments.Keys);
            foreach (var layer in layers)
            {
                var a = s.Assignments[layer];
                if (activeByLayer.TryGetValue(layer, out var e) && e.Id == a.ExpId && FindVariant(e, a.Variant) != null && !s.Holdout.Value)
                    continue;
                s.Assignments.Remove(layer);
                t.SetUserProperty(PROP_EXP_PREFIX + layer, null);
                changed = true;
            }

            foreach (var kv in activeByLayer)
            {
                var layer = kv.Key;
                var e = kv.Value;
                foreach (var v in e.Variants)
                foreach (var ruleId in v.RuleActions.Keys)
                    rt.ExperimentByRule[ruleId] = e;

                if (s.Holdout.Value) continue;

                if (!s.Assignments.TryGetValue(layer, out var a))
                {
                    var variant = Pick(s.UserId, e);
                    if (variant == null) continue; // ngoài allocation
                    a = new AssignmentState { ExpId = e.Id, Variant = variant.Id, At = now };
                    s.Assignments[layer] = a;
                    changed = true;
                }

                // Set lại mỗi lần load config: idempotent, đảm bảo property khớp assignment kể cả khi bị ép từ debug
                t.SetUserProperty(PROP_EXP_PREFIX + layer, a.ExpId + ":" + a.Variant);

                rt.Assigned[layer] = a;
                var vd = FindVariant(e, a.Variant);
                foreach (var ra in vd.RuleActions) rt.EffectiveThen[ra.Key] = ra.Value;
            }

            return rt;
        }

        public static VariantDef FindVariant(ExperimentDef e, string id)
        {
            foreach (var v in e.Variants)
                if (v.Id == id)
                    return v;
            return null;
        }

        /// <summary>bucket ≥ allocation × 10000 → null; trong allocation: u = bucket / (allocation × 10000), variant đầu có cum_weight &gt; u.</summary>
        public static VariantDef Pick(string userId, ExperimentDef e)
        {
            var bucket = HashUtil.Bucket(userId, e.Id);
            var range = e.Allocation * HashUtil.BUCKETS;
            if (bucket >= range) return null;
            var u = bucket / range;
            double cum = 0;
            foreach (var v in e.Variants)
            {
                cum += v.Weight;
                if (cum > u) return v;
            }

            return e.Variants[e.Variants.Count - 1];
        }
    }
}
