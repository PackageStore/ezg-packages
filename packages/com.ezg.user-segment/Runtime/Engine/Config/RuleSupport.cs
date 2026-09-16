namespace Ezg.UserSegment.Engine
{
    /// <summary>Lý do rule_unsupported — §C.8.2. Rule bị disable, config vẫn chấp nhận.</summary>
    public static class UnsupportedReason
    {
        public const string ActionType = "action_type";
        public const string Reward = "reward";
        public const string Screen = "screen";
        public const string CustomEvent = "custom_event";
        public const string CustomState = "custom_state";
        public const string OneShotNoStore = "one_shot_no_store";
        public const string Expired = "expired";
    }

    /// <summary>Kiểm từng rule sau khi config được chấp nhận và assignment đã resolve `then` hiệu lực — §8.5, §C.12 mục 2.</summary>
    public static class RuleSupport
    {
        /// <returns>null nếu rule chạy được; ngược lại reason.</returns>
        public static string Check(RuleDef rule, ActionDef effectiveAction, Manifest manifest, bool hasHistoryStore, long now)
        {
            if (rule.ValidUntil <= now) return UnsupportedReason.Expired;

            foreach (var entry in rule.On)
            {
                Trigger.Split(entry, out var ev, out var q);
                if (q == null) continue;
                if (ev == "SCREEN_OPEN" && !manifest.Screens.Contains(q)) return UnsupportedReason.Screen;
                if (ev == "CUSTOM_EVENT" && !manifest.CustomEvents.Contains(q)) return UnsupportedReason.CustomEvent;
            }

            if (effectiveAction == null) return null;
            if (!manifest.Actions.Contains(effectiveAction.Type.ToString())) return UnsupportedReason.ActionType;
            if (effectiveAction.Type == ActionType.GIVE_REWARD)
            {
                var rewardId = effectiveAction.Params["reward_id"] as string;
                if (rewardId == null || !manifest.Rewards.Contains(rewardId)) return UnsupportedReason.Reward;
            }

            if (effectiveAction.OneShot && !hasHistoryStore) return UnsupportedReason.OneShotNoStore;
            return null;
        }
    }
}
