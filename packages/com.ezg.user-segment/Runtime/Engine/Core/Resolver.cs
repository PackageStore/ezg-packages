using System;
using System.Collections.Generic;

namespace Ezg.UserSegment.Engine
{
    public static class DropReason
    {
        public const string OneShot = "one_shot";
        public const string Cooldown = "cooldown";
        public const string Cap = "cap";
        public const string Group = "group";
        public const string DailyCap = "daily_cap";
    }

    public sealed class Candidate
    {
        public RuleDef Rule;
        public ActionDef Action;
    }

    public sealed class SelectedAction
    {
        public ActionDef Action;
        public RuleDef Rule;
        public string ExecutionId;
        public long SelectedAt;
        public int Nth;
        public bool CountsToDailyCap;
    }

    public sealed class ResolveResult
    {
        public readonly List<SelectedAction> Selected = new List<SelectedAction>();

        /// <summary>Theo thứ tự bị loại, dạng action_id:reason.</summary>
        public readonly List<string> Dropped = new List<string>();
    }

    /// <summary>Resolver — §8.7. Ghi timestamp / shown_today / history ngay tại selected.</summary>
    public static class Resolver
    {
        public static ResolveResult Resolve(List<Candidate> candidates, EngineState s, SegConfig c, ActionHistory history,
            long now, long today)
        {
            var result = new ResolveResult();
            if (s.ShownToday.Day != today)
            {
                s.ShownToday.Day = today;
                s.ShownToday.Count = 0;
            }

            // 1. Gộp trùng action_id — candidates đã theo priority giảm nên lần đầu thắng
            var unique = new List<Candidate>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var cd in candidates)
                if (seen.Add(cd.Action.Id))
                    unique.Add(cd);

            // 2. one_shot → 3. cooldown / cap
            var eligible = new List<Candidate>();
            foreach (var cd in unique)
            {
                var a = cd.Action;
                if (a.OneShot && history.Count(a.Id) >= 1)
                {
                    result.Dropped.Add(a.Id + ":" + DropReason.OneShot);
                    continue;
                }

                s.ActionTs.TryGetValue(a.Id, out var ts);
                var last = Math.Max(LastOf(ts), history.LastAt(a.Id));
                if (a.CooldownS > 0 && last > 0 && now - last < a.CooldownS)
                {
                    result.Dropped.Add(a.Id + ":" + DropReason.Cooldown);
                    continue;
                }

                if (a.Cap != null && ts != null)
                {
                    var inWindow = 0;
                    foreach (var t in ts)
                        if (now - t < a.Cap.WindowS)
                            inWindow++;
                    if (inWindow >= a.Cap.Count)
                    {
                        result.Dropped.Add(a.Id + ":" + DropReason.Cap);
                        continue;
                    }
                }

                eligible.Add(cd);
            }

            // 4. Một action mỗi group (đã sort priority)
            var groups = new HashSet<string>(StringComparer.Ordinal);
            var perGroup = new List<Candidate>();
            foreach (var cd in eligible)
            {
                if (!groups.Add(cd.Action.Group))
                {
                    result.Dropped.Add(cd.Action.Id + ":" + DropReason.Group);
                    continue;
                }

                perGroup.Add(cd);
            }

            // 5. Cap ngày (không tính notification) → 6. commit
            foreach (var cd in perGroup)
            {
                var a = cd.Action;
                var counts = a.Group != ActionGroup.Notification;
                if (counts && s.ShownToday.Count >= c.MaxActionsPerDay)
                {
                    result.Dropped.Add(a.Id + ":" + DropReason.DailyCap);
                    continue;
                }

                s.ExecSeq++;
                var sel = new SelectedAction
                {
                    Action = a,
                    Rule = cd.Rule,
                    ExecutionId = s.ExecSeq.ToString("x8"),
                    SelectedAt = now,
                    CountsToDailyCap = counts
                };

                if (!s.ActionTs.TryGetValue(a.Id, out var list))
                {
                    list = new List<long>();
                    s.ActionTs[a.Id] = list;
                }

                list.Add(now);
                var keep = Math.Max(1, a.Cap?.Count ?? 1);
                while (list.Count > keep) list.RemoveAt(0);

                if (counts) s.ShownToday.Count++;
                history.Record(a.Id, now);
                sel.Nth = history.Count(a.Id);
                result.Selected.Add(sel);
            }

            return result;
        }

        /// <summary>Hoàn lại khi action_failed offline / no_permission — §8.3, §C.12 mục 19.</summary>
        public static void Refund(SelectedAction sel, EngineState s, ActionHistory history, long today)
        {
            if (s.ActionTs.TryGetValue(sel.Action.Id, out var list))
            {
                var idx = list.LastIndexOf(sel.SelectedAt);
                if (idx >= 0) list.RemoveAt(idx);
                if (list.Count == 0) s.ActionTs.Remove(sel.Action.Id);
            }

            if (sel.CountsToDailyCap && s.ShownToday.Day == today && s.ShownToday.Count > 0) s.ShownToday.Count--;
            history.Refund(sel.Action.Id);
        }

        private static long LastOf(List<long> ts) => ts == null || ts.Count == 0 ? 0 : ts[ts.Count - 1];
    }
}
