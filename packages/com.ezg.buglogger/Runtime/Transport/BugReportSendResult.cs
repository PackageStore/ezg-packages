namespace Ezg.BugLogger
{
    /// <summary>Uploader làm gì với report sau một lần gửi.</summary>
    public enum BugReportSendStatus
    {
        /// <summary>Đã nhận (201 bug / 202 feedback) — xoá khỏi outbox.</summary>
        Success,

        /// <summary>Đã nhận từ trước (200 <c>duplicate:true</c>) — xoá khỏi outbox.</summary>
        Duplicate,

        /// <summary>Bị từ chối vĩnh viễn (400/404/413/4xx khác) — chuyển vào <c>failed/</c>, không thử lại.</summary>
        Permanent,

        /// <summary>Thử lại sau (403 intake_disabled, 429, 5xx, 3xx, lỗi mạng, timeout, cấu hình local sai) — giữ lại và backoff.</summary>
        Transient,
    }

    /// <summary>Kết quả đã phân loại của một lần gửi.</summary>
    public readonly struct BugReportSendResult
    {
        #region Fields

        /// <summary>Uploader nên làm gì tiếp.</summary>
        public readonly BugReportSendStatus Status;

        /// <summary>HTTP status; 0 khi lỗi ở tầng mạng.</summary>
        public readonly long StatusCode;

        /// <summary>Số issue khi thành công/trùng; 0 với feedback hoặc khi body không có.</summary>
        public readonly int Number;

        /// <summary>URL issue khi thành công/trùng, còn lại rỗng.</summary>
        public readonly string Url;

        /// <summary>Chẩn đoán ngắn cho log — không bao giờ chứa dữ liệu nhạy cảm.</summary>
        public readonly string Reason;

        /// <summary>Số giây server yêu cầu chờ (<c>Retry-After</c>); 0 khi không có.</summary>
        public readonly int RetryAfterSeconds;

        #endregion

        #region Public

        /// <summary>Tạo kết quả.</summary>
        public BugReportSendResult(BugReportSendStatus status, long statusCode, int number, string url, string reason, int retryAfterSeconds = 0)
        {
            Status = status;
            StatusCode = statusCode;
            Number = number;
            Url = url ?? string.Empty;
            Reason = reason ?? string.Empty;
            RetryAfterSeconds = retryAfterSeconds < 0 ? 0 : retryAfterSeconds;
        }

        /// <summary>True khi report đã tới Worker (mới hoặc trùng).</summary>
        public bool IsDelivered => Status == BugReportSendStatus.Success || Status == BugReportSendStatus.Duplicate;

        /// <summary>Lỗi tạm ở tầng mạng (không có HTTP response).</summary>
        public static BugReportSendResult NetworkError(string reason)
        {
            return new BugReportSendResult(BugReportSendStatus.Transient, 0, 0, null, reason);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{Status} (HTTP {StatusCode}) #{Number} {Reason}";
        }

        #endregion
    }
}
