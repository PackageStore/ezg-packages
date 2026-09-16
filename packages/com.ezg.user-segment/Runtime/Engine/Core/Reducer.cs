using System;

namespace Ezg.UserSegment.Engine
{
    /// <summary>Reducer chuẩn — §4.2. Event đã được engine kiểm hợp lệ (payload, whitelist) trước khi tới đây.</summary>
    public static class Reducer
    {
        public const double QUIT_AFTER_FAIL_WINDOW_S = 60;

        /// <summary>Rollover mọi counter theo utcDay(now) trước reducer — §C.12 mục 9.</summary>
        public static void Rollover(EngineState s, long today)
        {
            foreach (var c in s.Counters.Values) c.Rollover(today);
        }

        /// <returns>false nếu event bị bỏ (PURCHASE trùng transaction_id).</returns>
        public static bool Apply(SegEvent e, EngineState s, long now, long today)
        {
            var sc = s.Scalars;
            switch (e.Type)
            {
                case SegEventType.SESSION_START:
                    if (sc.InstallAt == 0 && !s.Seeded) sc.InstallAt = now;
                    s.Session.DaysSinceLastActive = sc.LastActiveAt == 0
                        ? 0
                        : Math.Max(0, Clock.UtcDay(now) - Clock.UtcDay(sc.LastActiveAt));
                    sc.LastActiveAt = now;
                    foreach (var c in s.Counters.Values) c.Session = 0;
                    s.Counter("session_count", today).Add(1);
                    s.Session.Open = true;
                    s.Session.StartedAt = now;
                    s.Clock.ForegroundAnchor = now;
                    break;
                case SegEventType.SESSION_END:
                    s.Session.Open = false;
                    break;
                case SegEventType.PROGRESS_START:
                    if (Math.Abs(e.UnitId - sc.ProgressCurrent) > Evaluator.EPS) sc.AttemptCountCurrentUnit = 0;
                    sc.ProgressCurrent = e.UnitId;
                    s.Counter("attempt_count", today).Add(1);
                    sc.AttemptCountCurrentUnit += 1;
                    break;
                case SegEventType.PROGRESS_COMPLETE:
                    s.Counter("complete_count", today).Add(1);
                    sc.WinStreak += 1;
                    sc.FailStreak = 0;
                    sc.ProgressMax = Math.Max(sc.ProgressMax, e.UnitId);
                    sc.AttemptCountCurrentUnit = 0;
                    break;
                case SegEventType.PROGRESS_FAIL:
                    s.Counter("fail_count", today).Add(1);
                    sc.FailStreak += 1;
                    sc.WinStreak = 0;
                    sc.LastFailAt = now;
                    break;
                case SegEventType.PROGRESS_QUIT:
                    s.Counter("quit_count", today).Add(1);
                    if (sc.LastFailAt > 0 && now - sc.LastFailAt <= QUIT_AFTER_FAIL_WINDOW_S) sc.QuitAfterFailCount += 1;
                    break;
                case SegEventType.PURCHASE:
                    if (s.TxRing.Contains(e.TransactionId)) return false;
                    s.TxRing.Add(e.TransactionId);
                    while (s.TxRing.Count > EngineState.TX_RING_SIZE) s.TxRing.RemoveAt(0);
                    s.Counter("purchase_count", today).Add(1);
                    sc.TotalSpendUsd += e.Usd;
                    sc.LastPurchaseAt = now;
                    if (sc.FirstPurchaseAt == 0) sc.FirstPurchaseAt = now;
                    break;
                case SegEventType.AD_REWARDED:
                    s.Counter("ad_rewarded_count", today).Add(1);
                    break;
                case SegEventType.AD_INTERSTITIAL:
                    s.Counter("ad_interstitial_count", today).Add(1);
                    break;
                case SegEventType.SCREEN_OPEN:
                    s.Context.Screen = e.ScreenId;
                    break;
                case SegEventType.CUSTOM_EVENT:
                    s.Counter("custom_event_count." + e.Name, today).Add(1);
                    break;
                case SegEventType.CUSTOM_STATE:
                    switch (e.Value.Type)
                    {
                        case SegType.Bool: s.Custom[e.Key] = e.Value.AsBool; break;
                        case SegType.String: s.Custom[e.Key] = e.Value.Str; break;
                        default: s.Custom[e.Key] = e.Value.Num; break;
                    }

                    break;
            }

            s.Clock.LastEventAt = now;
            s.Context.LastEvent = e.LastEventName;
            return true;
        }
    }
}
