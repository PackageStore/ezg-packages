namespace Ezg.BugLogger
{
    /// <summary>Các file của một report khi ghi vào outbox; phần tuỳ chọn để null.</summary>
    internal sealed class BugReportFiles
    {
        public string Id;

        /// <summary><c>report.json</c> dạng UTF-8.</summary>
        public byte[] Report;

        public byte[] Screenshot;

        /// <summary><c>log.txt</c> dạng UTF-8, đã cắt còn phần đuôi.</summary>
        public byte[] Log;

        /// <summary><c>save.json</c> dạng UTF-8.</summary>
        public byte[] Save;
    }
}
