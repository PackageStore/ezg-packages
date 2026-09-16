using Ezg.Feature.Shared.Config;
using Ezg.UserSegment;
using TigerForge;
using UnityEngine;

namespace Ezg.Feature.System.UserSegment
{
    /// <summary>
    ///     Emit PURCHASE chỉ từ luồng MUA MỚI (§4.1): core phát <c>PurchaseOnlineRequested</c> khi bấm mua và
    ///     <c>IapTransactionGranted</c> khi cấp quà. Restore / recover order không đi qua bước "bấm mua" nên không emit.
    /// </summary>
    public static class UserSegmentPurchaseBridge
    {
        private static string _pendingProductId;

        public static void OnPurchaseRequested()
        {
            _pendingProductId = EventManager.GetData<string>(EventName.PurchaseOnlineRequested);
            if (Debug.isDebugBuild) Debug.Log($"[UserSegment] PurchaseBridge: bấm mua {_pendingProductId} → chờ IapTransactionGranted");
        }

        public static void OnTransactionGranted()
        {
            var data = EventManager.GetData<string[]>(EventName.IapTransactionGranted);
            if (data == null || data.Length < 2) return;
            var transactionId = data[0];
            var productId = data[1];
            if (string.IsNullOrEmpty(_pendingProductId) || _pendingProductId != productId)
            {
                if (Debug.isDebugBuild) Debug.Log($"[UserSegment] PurchaseBridge: granted {productId} tx={transactionId} KHÔNG khớp pending ({_pendingProductId ?? "none"}) → coi là restore/recover, không emit PURCHASE");
                return;
            }

            _pendingProductId = null;
            if (!SegmentationSdk.IsInitialized) return;

            var pack = ShopService.GetPackByProductId(productId);
            var usd = pack != null ? pack.iapCost : 0f;
            if (pack == null) Debug.LogWarning($"[UserSegment] PURCHASE {productId}: không tìm thấy pack → usd = 0");
            if (Debug.isDebugBuild) Debug.Log($"[UserSegment] PurchaseBridge: emit PURCHASE {productId} usd={usd} tx={transactionId}");
            SegmentationSdk.Purchase(productId, usd, transactionId);
        }
    }
}
