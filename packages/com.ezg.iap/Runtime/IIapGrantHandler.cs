namespace Ezg.Feature.IAP
{
    /// <summary>
    /// Tuỳ chọn — class implement <see cref="IPurchasing"/> implement THÊM interface này để BÁO được việc cấp quà
    /// thất bại. Khi có, <see cref="InAppManager"/> gọi <see cref="TryGrantPurchase"/> THAY cho
    /// <see cref="IPurchasing.OnPurchaseComplete"/> (không gọi cả hai → không cấp đôi), ở đúng vị trí cũ: sau
    /// <see cref="IPurchasing.OnPurchaseCompleteBeforeCallback"/> và callback thành công của <c>Buy()</c>.
    /// <para>
    /// Không implement → giữ nguyên hành vi cũ (<see cref="IPurchasing.OnPurchaseComplete"/>); handler đó ném exception cũng được
    /// coi là chưa cấp được. Host đang có không phải sửa gì.
    /// </para>
    /// </summary>
    public interface IIapGrantHandler
    {
        /// <summary>
        /// Cấp quà + lưu bền vững cho một đơn đã validate.
        /// </summary>
        /// <returns>
        /// true = đã cấp + đã lưu: module ghi <see cref="IIapOrderLedger"/> rồi ConfirmPurchase như cũ.
        /// false (hoặc ném exception) = CHƯA cấp gì (save chưa load, catalog thiếu, SKU không map ra gói…): module KHÔNG
        /// ConfirmPurchase, KHÔNG ghi ledger; đơn giữ pending và store giao lại ở lần fetch sau (quay lại app / mở lại app).
        /// Chỉ trả false khi CHƯA cấp phần nào — đã cấp mà báo false thì lần giao sau sẽ cấp lần hai.
        /// </returns>
        bool TryGrantPurchase(string productId);
    }
}
