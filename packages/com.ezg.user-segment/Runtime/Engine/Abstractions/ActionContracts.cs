using System;
using System.Collections.Generic;

namespace Ezg.UserSegment
{
    /// <summary>5 action type MVP — §8.4.</summary>
    public enum ActionType
    {
        GIVE_REWARD,
        SHOW_POPUP,
        SHOW_OFFER,
        CHANGE_DIFFICULTY,
        SCHEDULE_LOCAL_NOTIFICATION
    }

    /// <summary>Executor do game đăng ký theo action type — §8.6.</summary>
    public interface IActionExecutor
    {
        ActionType ActionType { get; }

        /// <summary>Gọi trên main thread, ngay sau resolver. Phải gọi ReportExecuted / ReportFailed trong session.</summary>
        void Execute(ActionRequest request);
    }

    /// <summary>Typed view + raw của params action — §C.6.5.</summary>
    public sealed class ActionParams
    {
        public string RewardId;
        public int Amount;
        public string PopupId;
        public string TextKey;
        public string OfferId;
        public int Delta;
        public string Scope;
        public string TemplateId;
        public double DelayH;

        public IReadOnlyDictionary<string, object> Raw { get; }

        public ActionParams(IReadOnlyDictionary<string, object> raw)
        {
            Raw = raw ?? new Dictionary<string, object>();
        }
    }

    /// <summary>Lý do action_failed — §C.8.4. Chỉ offline / no_permission hoàn cooldown / cap / one-shot.</summary>
    public static class FailReason
    {
        public const string Offline = "offline";
        public const string NoPermission = "no_permission";
        public const string Exception = "exception";
        public const string UnknownId = "unknown_id";
        public const string InvalidParams = "invalid_params";
        public const string NotAvailable = "not_available";
        public const string Other = "other";

        public static readonly string[] All =
            { Offline, NoPermission, Exception, UnknownId, InvalidParams, NotAvailable, Other };

        public static bool IsKnown(string reason) => Array.IndexOf(All, reason) >= 0;
        public static bool IsRefundable(string reason) => reason == Offline || reason == NoPermission;
    }

    /// <summary>Bộ giá trị result theo action type — §C.8.6.</summary>
    public static class ExecResult
    {
        public const string Other = "other";

        private static readonly Dictionary<ActionType, string[]> Allowed = new Dictionary<ActionType, string[]>
        {
            { ActionType.GIVE_REWARD, new[] { "granted", "inventory_full" } },
            { ActionType.SHOW_POPUP, new[] { "clicked", "dismissed", "timeout" } },
            { ActionType.SHOW_OFFER, new[] { "purchased", "closed", "not_loaded" } },
            { ActionType.CHANGE_DIFFICULTY, new[] { "applied", "no_unit_pending" } },
            { ActionType.SCHEDULE_LOCAL_NOTIFICATION, new[] { "scheduled", "replaced" } }
        };

        public static bool IsAllowed(ActionType type, string result)
        {
            return Allowed.TryGetValue(type, out var list) && Array.IndexOf(list, result) >= 0;
        }
    }

    /// <summary>
    ///     Một action đã được resolver chọn, giao cho executor. Terminal đầu tiên thắng, phần sau bỏ — §C.6.5.
    /// </summary>
    public sealed class ActionRequest
    {
        private readonly Action<ActionRequest> _onPresented;
        private readonly Action<ActionRequest, string> _onExecuted;
        private readonly Action<ActionRequest, string> _onFailed;

        public string ActionId { get; }
        public string ExecutionId { get; }
        public ActionType Type { get; }
        public ActionParams Params { get; }
        public string RuleId { get; }
        public string Group { get; }

        /// <summary>Lần thứ mấy user nhận action này theo lịch sử (one-shot luôn là 1).</summary>
        public int Nth { get; }

        public bool IsTerminal { get; private set; }
        public bool IsPresented { get; private set; }

        public ActionRequest(string actionId, string executionId, ActionType type, ActionParams parameters,
            string ruleId, string group, int nth, Action<ActionRequest> onPresented,
            Action<ActionRequest, string> onExecuted, Action<ActionRequest, string> onFailed)
        {
            ActionId = actionId;
            ExecutionId = executionId;
            Type = type;
            Params = parameters;
            RuleId = ruleId;
            Group = group;
            Nth = nth;
            _onPresented = onPresented;
            _onExecuted = onExecuted;
            _onFailed = onFailed;
        }

        /// <summary>Gọi lúc bắt đầu hiển thị / áp.</summary>
        public void ReportPresented()
        {
            if (IsTerminal || IsPresented) return;
            IsPresented = true;
            _onPresented?.Invoke(this);
        }

        /// <summary>Gọi khi user đã phản hồi xong. result ∈ <see cref="ExecResult" /> theo Type.</summary>
        public void ReportExecuted(string result)
        {
            if (IsTerminal) return;
            IsTerminal = true;
            _onExecuted?.Invoke(this, result);
        }

        /// <summary>reason ∈ <see cref="FailReason" />.</summary>
        public void ReportFailed(string reason)
        {
            if (IsTerminal) return;
            IsTerminal = true;
            _onFailed?.Invoke(this, reason);
        }
    }
}
