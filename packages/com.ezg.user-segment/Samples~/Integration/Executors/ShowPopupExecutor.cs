using Cysharp.Threading.Tasks;
using Ezg.Feature.Shared.UI.Framework;
using Ezg.UserSegment;

namespace Ezg.Feature.System.UserSegment
{
    /// <summary>SHOW_POPUP → UIManager.Show(feature). result: dismissed khi màn đóng (không phân biệt click trong MVP).</summary>
    public sealed class ShowPopupExecutor : IActionExecutor
    {
        public ActionType ActionType => ActionType.SHOW_POPUP;

        public void Execute(ActionRequest request)
        {
            var entry = UserSegmentCatalog.Current?.FindPopup(request.Params.PopupId);
            if (entry == null)
            {
                UnityEngine.Debug.LogWarning($"[UserSegment] ShowPopup: popup_id '{request.Params.PopupId}' không có trong catalog → unknown_id");
                request.ReportFailed(FailReason.UnknownId);
                return;
            }

            if (UnityEngine.Debug.isDebugBuild) UnityEngine.Debug.Log($"[UserSegment] ShowPopup #{request.ExecutionId}: {request.Params.PopupId} → UIManager.Show({entry.feature})");
            ShowAsync(request, entry).Forget();
        }

        private static async UniTaskVoid ShowAsync(ActionRequest request, UserSegmentCatalog.PopupEntry entry)
        {
            var ui = UIManager.Instance;
            if (ui == null)
            {
                request.ReportFailed(FailReason.NotAvailable);
                return;
            }

            ui.AddActionCloseFeature(entry.feature, () => request.ReportExecuted("dismissed"));
            var go = await ui.Show(entry.feature, data: request.Params.TextKey);
            if (go == null)
            {
                request.ReportFailed(FailReason.UnknownId);
                return;
            }

            request.ReportPresented();
        }
    }
}
