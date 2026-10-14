using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ezg.BugLogger
{
    /// <summary>
    /// Gửi một report đang chờ. Uploader gọi trên main thread, mỗi lần một report, rồi dựa vào
    /// <see cref="BugReportSendResult"/> trả về để xoá, chuyển vào <c>failed/</c> hay thử lại. Implementation
    /// không bao giờ được ném vì lỗi HTTP/mạng — báo bằng <see cref="BugReportSendStatus.Transient"/> hoặc
    /// <see cref="BugReportSendStatus.Permanent"/>.
    /// </summary>
    public interface IBugReportTransport
    {
        /// <summary>Gửi <paramref name="payload"/> tới <paramref name="target"/>.</summary>
        /// <param name="payload">Các file của report, đã cắt kích thước.</param>
        /// <param name="target">Endpoint + mã project của lần gửi này.</param>
        /// <param name="cancellationToken">Bị huỷ khi app thoát.</param>
        /// <returns>Kết quả đã phân loại.</returns>
        UniTask<BugReportSendResult> SendAsync(BugReportPayload payload, BugReportTarget target, CancellationToken cancellationToken);
    }
}
