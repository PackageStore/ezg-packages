namespace Ezg.Feature.IAP
{
    /// <summary>
    /// Phía Unity IAP / game của <see cref="PendingOrderProcessor{TOrder}"/> — bản thật là <c>InAppManager.PendingOrderHost</c>.
    /// Tách ra để luồng chốt đơn test được ngoài Unity (cùng cách với <see cref="IStoreKit2FirebaseNative"/>).
    /// </summary>
    internal interface IPendingOrderHost<TOrder>
    {
        /// <summary>Ledger của host (null = không có lớp chặn cấp đôi giữa các phiên).</summary>
        IIapOrderLedger Ledger { get; }

        /// <summary>Id product của đơn; null / rỗng = đơn không có product.</summary>
        string GetProductId(TOrder order);

        /// <summary>TransactionID của đơn — đọc TRƯỚC confirm (sau ConfirmPurchase SDK trả rỗng).</summary>
        string GetTransactionId(TOrder order);

        /// <summary>Catalog đã fetch có product này chưa.</summary>
        bool IsInCatalog(string productId);

        /// <summary>true = receipt hợp lệ; false = từ chối dứt khoát (đóng đơn); ném = lỗi tạm thời (giữ pending).</summary>
        bool Validate(TOrder order);

        /// <summary>Cấp quà + lưu. true = đã cấp; false hoặc ném = chưa cấp được (giữ pending).</summary>
        bool Grant(TOrder order);

        /// <summary>Analytics + yêu cầu đồng bộ của đơn vừa cấp (chạy SAU khi ghi ledger).</summary>
        void SendAnalytics(TOrder order);

        /// <summary>ConfirmPurchase — store đóng đơn (Google consume / acknowledge, Apple finish).</summary>
        void Confirm(TOrder order);

        /// <summary>true = confirm được hoãn (iOS StoreKit 2 chờ Firebase ghi), host tự confirm sau.</summary>
        bool TryDeferConfirm(TOrder order, string transactionId);

        /// <summary>Confirm của giao dịch này đang được hoãn.</summary>
        bool IsConfirmDeferred(string transactionId);

        /// <summary>Store giao lại đúng giao dịch đang hoãn confirm (không confirm lần hai).</summary>
        void OnRedeliveredWhileDeferred(string productId);

        /// <summary>Callback thành công của <c>Buy()</c> không còn dùng tới cho đơn này.</summary>
        void DropBuyCallback();

        /// <summary>Báo UI đơn bị từ chối (receipt không hợp lệ).</summary>
        void ReportRejected();

        /// <summary>Đơn đang giữ vì catalog chưa có product — catalog về thì fetch lại.</summary>
        void OnAwaitingCatalog();
    }
}
