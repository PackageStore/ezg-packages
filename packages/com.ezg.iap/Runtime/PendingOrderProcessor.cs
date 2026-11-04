using System;

namespace Ezg.Feature.IAP
{
    /// <summary>
    /// Chốt một PendingOrder của store: chống xử lý trùng trong phiên, ledger chống cấp đôi giữa các phiên, catalog, validate,
    /// cấp quà, ConfirmPurchase. Bản thật: <c>InAppManager.OnPurchasePending</c>.
    /// <para>
    /// LUẬT CONFIRM: chỉ ConfirmPurchase đơn đã CHỐT DỨT KHOÁT — vừa cấp quà, ledger báo đã cấp, hoặc receipt bị từ chối. Đơn
    /// chưa cấp được (không có product, catalog thiếu, validate lỗi tạm thời, cấp quà ném lỗi / báo không cấp được) thì KHÔNG
    /// confirm, KHÔNG ghi ledger: đơn giữ pending, store giao lại ở lần fetch sau (quay lại app / mở lại app). Google tự hoàn
    /// tiền đơn chưa acknowledge sau 3 ngày; Apple giao lại tới khi đơn được finish.
    /// </para>
    /// <para>Không phụ thuộc UnityEngine / Unity IAP (nằm sau <see cref="IPendingOrderHost{TOrder}"/>) để test được ngoài Unity.</para>
    /// </summary>
    internal sealed class PendingOrderProcessor<TOrder> where TOrder : class
    {
        private readonly IPendingOrderHost<TOrder> _host;
        private readonly Action<string> _log;
        private readonly Action<string> _logWarning;
        private readonly Action<string> _logError;
        private readonly SessionOrderMemory<TOrder> _session = new SessionOrderMemory<TOrder>();

        internal PendingOrderProcessor(IPendingOrderHost<TOrder> host, Action<string> log, Action<string> logWarning,
            Action<string> logError)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _log = log ?? (_ => { });
            _logWarning = logWarning ?? (_ => { });
            _logError = logError ?? (_ => { });
        }

        /// <summary>Xử lý một đơn store giao tới. Exception của ledger / analytics / báo UI lọt ra như bản cũ (sau khi đã chốt đơn).</summary>
        internal void Process(TOrder order)
        {
            if (order == null || _session.IsHandledInstance(order))
            {
                return; // cùng instance vừa xử lý trong lượt fetch này
            }

            // Đọc NGAY — sau ConfirmPurchase, TransactionID rỗng.
            var transactionId = _host.GetTransactionId(order);
            var productId = _host.GetProductId(order);

            if (_session.TryAdoptSettled(order, transactionId))
            {
                if (_host.IsConfirmDeferred(transactionId))
                {
                    // 0.3.4: gate sẽ confirm khi Firebase ghi xong — confirm thêm = 2 lệnh confirm cho một giao dịch.
                    _log("[IAP] Order đang chờ Firebase ghi, confirm sau: " + transactionId);
                    _host.OnRedeliveredWhileDeferred(productId);
                    return;
                }

                _log("[IAP] Order đã xử lý trong phiên này, chỉ confirm lại: " + transactionId);
                _host.Confirm(order);
                return;
            }

            // Đã cấp + lưu ở phiên trước (app chết trước confirm) → CHỈ confirm. Ledger ném → lọt ra, đơn giữ pending như cũ.
            var ledger = _host.Ledger;
            if (ledger != null && !string.IsNullOrEmpty(transactionId) && ledger.IsGranted(transactionId))
            {
                _log("[IAP] Order đã grant trước đó, chỉ confirm lại (chống double-grant): " + transactionId);
                _host.DropBuyCallback();
                _session.Settle(order, transactionId, OrderOutcome.AlreadyGranted);
                _host.Confirm(order);
                return;
            }

            if (string.IsNullOrEmpty(productId))
            {
                // Bản cũ confirm luôn để "đóng giao dịch" → người chơi trả tiền, không có quà. Giờ giữ pending.
                _host.DropBuyCallback();
                _logError("[IAP] OnPurchasePending: đơn không có product (transaction " + transactionId +
                          ") → KHÔNG confirm, giữ đơn pending.");
                return;
            }

            if (!_host.IsInCatalog(productId))
            {
                // 0.3.3: fetch purchases về trước fetch products → giữ pending, catalog về thì fetch lại.
                _logWarning("[IAP] Catalog chưa có product '" + productId + "' → giữ order pending, cấp khi catalog đã fetch.");
                _host.OnAwaitingCatalog();
                return;
            }

            bool valid;
            try
            {
                valid = _host.Validate(order);
            }
            catch (Exception e)
            {
                // Lỗi tạm thời → KHÔNG confirm, store giao lại lần sau. Chưa cấp nên không lo cấp đôi.
                _logError("[IAP] Validation error, leaving purchase pending: " + e);
                return;
            }

            if (valid)
            {
                GrantAndConfirm(order, productId, transactionId);
            }
            else
            {
                Reject(order, transactionId);
            }
        }

        /// <summary>Đơn đã được cấp quà (trong phiên này, hoặc ledger báo đã cấp ở phiên trước).</summary>
        internal bool HasBeenGranted(TOrder order)
        {
            return order != null && _session.HasBeenGranted(order, _host.GetTransactionId(order));
        }

        private void GrantAndConfirm(TOrder order, string productId, string transactionId)
        {
            if (!TryGrant(order, productId, transactionId))
            {
                return;
            }

            // Đã cấp: ConfirmPurchase chạy ĐÚNG 1 lần như cũ — kể cả khi ledger / analytics ném sau đó.
            // THỨ TỰ: 1) cấp + Save  2) ghi ledger  3) analytics → app chết giữa (2) và (3): lần giao sau ledger chặn cấp lại.
            var ledgerMarked = false;
            try
            {
                var ledger = _host.Ledger;
                if (ledger != null && !string.IsNullOrEmpty(transactionId))
                {
                    ledger.MarkGranted(transactionId, productId);
                    ledgerMarked = true;
                }

                _host.SendAnalytics(order);
                _log("[IAP] ProcessPurchase: PASS. Product: '" + productId + "'");
            }
            finally
            {
                _session.Settle(order, transactionId, OrderOutcome.Granted);

                // Ledger chưa ghi được → confirm ngay: TransactionLog của Unity IAP (ghi lúc confirm) là lớp chặn cấp đôi còn lại.
                if (!ledgerMarked || !_host.TryDeferConfirm(order, transactionId))
                {
                    _host.Confirm(order);
                }
            }
        }

        /// <summary>Cấp quà; false = chưa cấp được (đơn giữ pending, không ghi ledger, không chốt). Không ném.</summary>
        private bool TryGrant(TOrder order, string productId, string transactionId)
        {
            Exception error = null;
            try
            {
                if (_host.Grant(order))
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                error = e;
            }

            _session.MarkGrantFailed(order);
            _logError("[IAP] Chưa cấp được quà '" + productId + "' (transaction " + transactionId +
                      ") → KHÔNG confirm, không ghi ledger, giữ đơn pending; store giao lại ở lần fetch sau. " +
                      (error != null ? "Lỗi: " + error : "Game báo không cấp được."));
            return false;
        }

        private void Reject(TOrder order, string transactionId)
        {
            try
            {
                _host.DropBuyCallback();
                _log("[IAP] Invalid receipt, not unlocking content.");
                _host.ReportRejected();
            }
            finally
            {
                _session.Settle(order, transactionId, OrderOutcome.Rejected);
                _host.Confirm(order);
            }
        }
    }
}
