namespace Ezg.Feature.IAP
{
    /// <summary>
    /// Sổ giao dịch bền vững để grant idempotent — chống double-grant khi store re-deliver một
    /// order chưa confirm (vd: app chết SAU khi grant + save nhưng TRƯỚC khi ConfirmPurchase).
    /// Khóa theo transactionId (chụp lúc PendingOrder — sau khi confirm SDK trả rỗng).
    /// Game implement bằng cơ chế persist của nó (PlayerData) và PHẢI Save() ngay trong MarkGranted.
    /// Inject qua <see cref="InAppManager.Configure"/> (nullable — null thì bỏ qua idempotent guard).
    /// </summary>
    public interface IIapOrderLedger
    {
        /// <summary>Order (theo transactionId) đã được grant thành công và ghi bền vững chưa?</summary>
        bool IsGranted(string transactionId);

        /// <summary>Đánh dấu order đã grant + đã save quà. PHẢI flush xuống đĩa ngay (Save()).</summary>
        void MarkGranted(string transactionId, string productId);
    }
}
