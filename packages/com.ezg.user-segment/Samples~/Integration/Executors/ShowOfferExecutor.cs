using Cysharp.Threading.Tasks;
using Ezg.Feature.Shared;
using Ezg.Feature.Shared.UI.Framework;
using Ezg.UserSegment;
using TigerForge;

namespace Ezg.Feature.System.UserSegment
{
    /// <summary>
    ///     SHOW_OFFER → mở màn shop / offer của game; mua bán vẫn qua IAP hiện có. result: purchased nếu có
    ///     PurchasedIapSuccess trong lúc màn mở, closed khi đóng, not_loaded khi không mở được.
    /// </summary>
    public sealed class ShowOfferExecutor : IActionExecutor
    {
        public ActionType ActionType => ActionType.SHOW_OFFER;

        public void Execute(ActionRequest request)
        {
            var entry = UserSegmentCatalog.Current?.FindOffer(request.Params.OfferId);
            if (entry == null)
            {
                UnityEngine.Debug.LogWarning($"[UserSegment] ShowOffer: offer_id '{request.Params.OfferId}' không có trong catalog → unknown_id");
                request.ReportFailed(FailReason.UnknownId);
                return;
            }

            if (UnityEngine.Debug.isDebugBuild) UnityEngine.Debug.Log($"[UserSegment] ShowOffer #{request.ExecutionId}: {request.Params.OfferId} → UIManager.Show({entry.feature}) product={entry.productId}");
            ShowAsync(request, entry).Forget();
        }

        private static async UniTaskVoid ShowAsync(ActionRequest request, UserSegmentCatalog.OfferEntry entry)
        {
            var ui = UIManager.Instance;
            if (ui == null)
            {
                request.ReportFailed(FailReason.NotAvailable);
                return;
            }

            var purchased = false;
            void OnPurchased() => purchased = true;
            EventManager.StartListening(EventName.PurchasedIapSuccess, OnPurchased);
            ui.AddActionCloseFeature(entry.feature, () =>
            {
                EventManager.StopListening(EventName.PurchasedIapSuccess, OnPurchased);
                request.ReportExecuted(purchased ? "purchased" : "closed");
            });

            var go = await ui.Show(entry.feature, data: string.IsNullOrEmpty(entry.productId) ? null : entry.productId);
            if (go == null)
            {
                EventManager.StopListening(EventName.PurchasedIapSuccess, OnPurchased);
                request.ReportExecuted("not_loaded");
                return;
            }

            request.ReportPresented();
        }
    }
}
