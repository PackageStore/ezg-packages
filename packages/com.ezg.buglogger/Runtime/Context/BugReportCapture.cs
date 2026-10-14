using System.Collections.Generic;

namespace Ezg.BugLogger
{
    /// <summary>
    /// Mọi thứ của một report phải đọc trên main thread, chụp lúc submit (<see cref="BugReportBuilder.Capture"/>).
    /// Phần còn lại của pipeline chỉ đọc nó, ngoài main thread.
    /// </summary>
    internal sealed class BugReportCapture
    {
        public string Id;
        public BugReportKind Kind;
        public string Title;
        public string FeatureKey;
        public string Category;
        public string ReproduceRate;
        public string Actual;
        public string Expected;
        public string Steps;
        public string Description;
        public string[] TagIds;
        public string[] MentionIds;
        public string CreatedAtUtc;
        public string AppVersion;
        public string BuildNumber;
        public string CommitSha;
        public string Branch;
        public string Platform;
        public string UnityVersion;
        public string DeviceId;
        public string DeviceModel;
        public string OperatingSystem;
        public int RamMb;
        public string Screen;
        public string Scene;
        public string PlayerId;
        public List<KeyValuePair<string, string>> Extra;
        public BugLogLine[] LogLines;
        public byte[] Screenshot;

        /// <summary>Ghi chú của package về lần chụp (ví dụ provider ném lỗi), gộp vào <c>extra</c>.</summary>
        public List<KeyValuePair<string, string>> Notes;
    }
}
