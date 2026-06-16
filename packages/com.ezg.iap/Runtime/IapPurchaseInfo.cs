namespace Ezg.Feature.IAP
{
    /// <summary>
    /// DTO trung gian truyền dữ liệu một giao dịch IAP từ module ra game layer.
    /// Module chỉ điền dữ liệu thô (đã parse khỏi receipt); mọi xử lý analytics/SDK
    /// do game thực hiện qua <see cref="IIapReporter"/>.
    /// </summary>
    public class IapPurchaseInfo
    {
        public string ProductId;
        public string Source;
        public string SourceId;
        public decimal LocalizedPrice;
        public string IsoCurrencyCode;
        public string Receipt;

        // Android receipt payload
        public string PayloadJson;
        public string Signature;

        // iOS receipt payload
        public string TransactionId;
    }
}
