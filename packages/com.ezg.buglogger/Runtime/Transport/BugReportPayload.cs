namespace Ezg.BugLogger
{
    /// <summary>
    /// Các file của một report đang chờ, đọc từ folder outbox. Phần tuỳ chọn là null khi report không có.
    /// Kích thước đã được bộ dựng report cắt dưới giới hạn của Worker.
    /// </summary>
    public sealed class BugReportPayload
    {
        #region Fields

        /// <summary>Id report (uuid v4) — cũng là tên folder outbox và khoá chống trùng của Worker.</summary>
        public readonly string Id;

        /// <summary>Byte UTF-8 của <c>report.json</c> (schema 1).</summary>
        public readonly byte[] Report;

        /// <summary>Byte của <c>screenshot.jpg</c>, hoặc null.</summary>
        public readonly byte[] Screenshot;

        /// <summary>Byte UTF-8 của <c>log.txt</c>, hoặc null.</summary>
        public readonly byte[] Log;

        /// <summary>Byte UTF-8 của <c>save.json</c>, hoặc null.</summary>
        public readonly byte[] Save;

        #endregion

        #region Public

        /// <summary>Tạo payload.</summary>
        public BugReportPayload(string id, byte[] report, byte[] screenshot, byte[] log, byte[] save)
        {
            Id = id;
            Report = report;
            Screenshot = screenshot;
            Log = log;
            Save = save;
        }

        #endregion
    }
}
