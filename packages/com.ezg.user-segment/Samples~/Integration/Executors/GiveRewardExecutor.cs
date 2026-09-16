using System;
using Ezg.Core.Utils;
using Ezg.UserSegment;
using UnityEngine;

namespace Ezg.Feature.System.UserSegment
{
    /// <summary>GIVE_REWARD → RewardsService. reward_id tra <see cref="UserSegmentCatalog" />; ngoài catalog → unknown_id.</summary>
    public sealed class GiveRewardExecutor : IActionExecutor
    {
        public const string SOURCE = "user_segment";
        public ActionType ActionType => ActionType.GIVE_REWARD;

        public void Execute(ActionRequest request)
        {
            var entry = UserSegmentCatalog.Current?.FindReward(request.Params.RewardId);
            if (entry == null)
            {
                Debug.LogWarning($"[UserSegment] GiveReward: reward_id '{request.Params.RewardId}' không có trong catalog → unknown_id");
                request.ReportFailed(FailReason.UnknownId);
                return;
            }

            var amount = Math.Max(1, request.Params.Amount) * Math.Max(1, entry.amountPerUnit);
            if (Debug.isDebugBuild) Debug.Log($"[UserSegment] GiveReward #{request.ExecutionId}: {request.Params.RewardId} → resType={entry.resType} resId={entry.resId} amount={amount}");
            var reward = new Resource(entry.resType, entry.resId, amount);
            request.ReportPresented();
            RewardsService.ReceiveReward(reward, PurchaseType.Free, SOURCE, request.RuleId, request.ActionId,
                onClose: () => request.ReportExecuted("granted"));
            // Popup có thể không hiện (isShowPopup theo project) → đảm bảo terminal trong session.
            if (!request.IsTerminal) request.ReportExecuted("granted");
        }
    }
}
