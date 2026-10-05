using System.Collections.Generic;

namespace Ezg.Feature.IAP
{
    /// <summary>
    /// Tuỳ chọn (0.3.3) — class implement <see cref="IPurchasing"/> implement THÊM interface này để nhận kết quả Restore
    /// kèm danh sách product, thay vì chỉ một cờ bool. Không implement → giữ nguyên hành vi cũ
    /// (<see cref="IPurchasing.OnTransactionRestored"/>), nên host đang có không phải sửa gì.
    ///
    /// Khi host implement interface này, <see cref="InAppManager"/> gọi <see cref="OnRestoreCompleted"/> THAY cho
    /// <see cref="IPurchasing.OnTransactionRestored"/> (không gọi cả hai → không bị 2 toast).
    /// <see cref="IPurchasing.RestoreItem"/> vẫn được gọi trước khi báo kết quả thành công, như cũ.
    /// </summary>
    public interface IIapRestoreListener
    {
        /// <summary>
        /// Lượt Restore đã xong và danh sách đơn của store đã về.
        /// </summary>
        /// <param name="success">false: store chưa sẵn sàng / restore lỗi / không lấy được danh sách đơn.</param>
        /// <param name="restoredProductIds">
        /// Product id mà store trả về cho lượt này: non-consumable / subscription người chơi đang sở hữu + đơn pending
        /// vừa được cấp quà. Rỗng khi <paramref name="success"/> = true nghĩa là KHÔNG có gì để khôi phục. Không bao giờ null.
        /// </param>
        void OnRestoreCompleted(bool success, IReadOnlyList<string> restoredProductIds);
    }
}
