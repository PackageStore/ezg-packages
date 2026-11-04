using System;
using UnityEngine;
using UnityEngine.Purchasing;

namespace Ezg.Feature.IAP
{
    /// <summary>
    /// Nối event <c>OnPurchasePending</c> của Unity IAP vào <see cref="PendingOrderProcessor{TOrder}"/> (luồng chốt đơn + luật
    /// confirm). Phần Unity IAP / game của luồng đó nằm ở <see cref="PendingOrderHost"/>.
    /// </summary>
    public partial class InAppManager
    {
        private PendingOrderProcessor<PendingOrder> m_OrderProcessor;

        private PendingOrderProcessor<PendingOrder> OrderProcessor =>
            m_OrderProcessor ?? (m_OrderProcessor = new PendingOrderProcessor<PendingOrder>(new PendingOrderHost(this),
                Debug.Log, Debug.LogWarning, Debug.LogError));

        private void OnPurchasePending(PendingOrder order)
        {
            // Một purchase đã được store chấp nhận và đang chờ app xử lý + xác nhận.
            m_PurchaseInProgress = false;
            OrderProcessor.Process(order);
        }

        /// <summary>Đơn đã được cấp quà (trong phiên này, hoặc ledger báo đã cấp ở phiên trước).</summary>
        private bool HasBeenGranted(PendingOrder order)
        {
            return OrderProcessor.HasBeenGranted(order);
        }

        /// <summary>Phía Unity IAP / game của <see cref="PendingOrderProcessor{TOrder}"/>.</summary>
        private sealed class PendingOrderHost : IPendingOrderHost<PendingOrder>
        {
            private readonly InAppManager _manager;

            internal PendingOrderHost(InAppManager manager)
            {
                _manager = manager;
            }

            public IIapOrderLedger Ledger => _manager._ledger;

            public string GetProductId(PendingOrder order)
            {
                return _manager.GetFirstProductInOrder(order)?.definition?.id;
            }

            public string GetTransactionId(PendingOrder order)
            {
                return order?.Info?.TransactionID;
            }

            public bool IsInCatalog(string productId)
            {
                var controller = _manager.m_StoreController;
                return controller != null && controller.GetProductById(productId) != null;
            }

            public bool Validate(PendingOrder order)
            {
                return _manager.ValidatePurchase(order, _manager.GetFirstProductInOrder(order));
            }

            public bool Grant(PendingOrder order)
            {
                return _manager.GrantRewards(_manager.GetFirstProductInOrder(order));
            }

            public void SendAnalytics(PendingOrder order)
            {
                _manager.SendPurchaseAnalytics(_manager.GetFirstProductInOrder(order), order.Info);
            }

            public void Confirm(PendingOrder order)
            {
                // v5: ConfirmPurchase finalize transaction (tương đương return Complete ở v4). BẮT BUỘC trên iOS.
                if (_manager.m_StoreController == null)
                {
                    Debug.LogError("[IAP] StoreController null, order sẽ được store giao lại để confirm.");
                    return;
                }

                _manager.m_StoreController.ConfirmPurchase(order);
            }

            public bool TryDeferConfirm(PendingOrder order, string transactionId)
            {
                return _manager.TryDeferConfirmForFirebase(order, transactionId);
            }

            public bool IsConfirmDeferred(string transactionId)
            {
                var gate = _manager.m_FirebaseConfirmGate;
                return gate != null && gate.IsAwaiting(transactionId);
            }

            public void OnRedeliveredWhileDeferred(string productId)
            {
                // 0.3.4: lượt Buy() đang chờ của CHÍNH product này nhận về giao dịch cũ (vd host gọi Buy ngay trong callback mua
                // thành công, trước khi gate nhận đơn) → không có đơn mới, báo thất bại để lượt mua không treo.
                if (_manager.callbackPay == null || _manager.productId != productId)
                {
                    return;
                }

                _manager.callbackPay = null;
                try
                {
                    _manager._purchasing?.OnPurchaseFailed?.Invoke(PurchaseFailureReason.ExistingPurchasePending.ToString());
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
            }

            public void DropBuyCallback()
            {
                _manager.callbackPay = null;
            }

            public void ReportRejected()
            {
                // Báo cho UI biết đơn bị từ chối — người chơi không thấy như "bấm mua không ra gì".
                _manager._purchasing?.OnPurchaseFailed?.Invoke(PurchaseFailureReason.ValidationFailure.ToString());
            }

            public void OnAwaitingCatalog()
            {
                _manager.m_OrdersAwaitingCatalog = true;
            }
        }
    }
}
