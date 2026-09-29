using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using F = Ezg.AutoTest.Editor.AutoTestReportFormat;
using T = Ezg.AutoTest.Editor.AutoTestReportHtmlTemplate;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Ghi bộ báo cáo của một lượt chạy: report.json (dữ liệu gốc, đọc lại được), report.html (dashboard cho QA, tự
    ///     chứa, mở offline), junit.xml (GitLab/CI), issues.csv (Excel, log bug hàng loạt), summary.md (dán vào MR/Discord).
    ///     Người gọi phải <see cref="TestRunReport.Recalculate" /> (và <see cref="AutoTestReportDiff.Apply" /> nếu muốn so
    ///     sánh với lần trước) TRƯỚC khi ghi.
    /// </summary>
    public static partial class AutoTestReportWriter
    {
        #region Hằng số

        public const string JSON_FILE = "report.json";
        public const string HTML_FILE = "report.html";
        public const string JUNIT_FILE = "junit.xml";
        public const string CSV_FILE = "issues.csv";
        public const string MARKDOWN_FILE = "summary.md";

        const string TITLE_SUFFIX = " · EZG Auto Test";

        /// <summary>Dung lượng ước lượng của phần template (CSS+JS) để cấp StringBuilder một lần.</summary>
        const int TEMPLATE_CAPACITY = 128 * 1024;

        static readonly UTF8Encoding Utf8NoBom = new(false);
        static readonly UTF8Encoding Utf8Bom = new(true);

        /// <summary>
        ///     JsonUtility có thể ghi NaN/Infinity trần (JSON không hợp lệ) cho số đo lỗi ⇒ JSON.parse trong trình duyệt
        ///     chết cả trang. Chỉ thay ở vị trí giá trị sau một key (không đụng nội dung chuỗi).
        /// </summary>
        static readonly Regex NonFiniteValue = new("(?<!\\\\)(\"[^\"\\\\]*\"\\s*:\\s*)-?(?:NaN|Infinity)(?=\\s*[,}\\]])",
            RegexOptions.CultureInvariant);

        /// <summary>
        ///     "&lt;!--" trong khối script có thể làm trình duyệt không nhận thẻ đóng &lt;/script&gt;. Thay bằng escape
        ///     JSON tương đương của "!" (backslash + u0021) — nối chuỗi cho dễ đọc, JSON.parse vẫn ra đúng "&lt;!--".
        /// </summary>
        static readonly string HtmlCommentOpenEscaped = string.Concat("<", "\\", "u0021--");

        #endregion

        #region Public

        /// <summary>
        ///     Ghi đủ bộ file vào <paramref name="outputDir" /> (đường dẫn tuyệt đối, tự tạo nếu chưa có). Một file lỗi
        ///     không chặn các file còn lại (lỗi được log). Trả về đường dẫn report.html.
        /// </summary>
        public static string WriteAll(TestRunReport report, string outputDir)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var dir = PrepareDir(outputDir);
            if (string.IsNullOrEmpty(report.outputDir)) report.outputDir = dir;

            TryWrite(JSON_FILE, () => WriteJson(report, dir));
            var html = TryWrite(HTML_FILE, () => WriteHtml(report, dir));
            TryWrite(JUNIT_FILE, () => WriteJUnit(report, dir));
            TryWrite(CSV_FILE, () => WriteIssuesCsv(report, dir));
            TryWrite(MARKDOWN_FILE, () => WriteMarkdown(report, dir));
            return html ?? Path.Combine(dir, HTML_FILE);
        }

        /// <summary>report.json — <c>JsonUtility.ToJson(report, true)</c>, nguồn dữ liệu cho lịch sử + so sánh lần sau.</summary>
        public static string WriteJson(TestRunReport report, string outputDir)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var path = Path.Combine(PrepareDir(outputDir), JSON_FILE);
            File.WriteAllText(path, JsonUtility.ToJson(report, true), Utf8NoBom);
            return path;
        }

        /// <summary>
        ///     report.html — một file tự chứa: template CSS/JS (<see cref="AutoTestReportHtmlTemplate" />) + dữ liệu report
        ///     nhúng trong &lt;script type="application/json" id="data"&gt;, JS render lười phía trình duyệt.
        /// </summary>
        public static string WriteHtml(TestRunReport report, string outputDir)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var path = Path.Combine(PrepareDir(outputDir), HTML_FILE);
            var data = EscapeForScript(SanitizeNonFinite(JsonUtility.ToJson(report, false)));
            var meta = EscapeForScript(BuildMetaJson(report));

            var sb = new StringBuilder(data.Length + TEMPLATE_CAPACITY);
            sb.Append(T.HEAD_START)
                .Append(F.HtmlEscape(F.Title(report) + TITLE_SUFFIX))
                .Append(T.HEAD_STYLE_OPEN)
                .Append(T.CSS)
                .Append(T.HEAD_END)
                .Append(T.DATA_META_OPEN)
                .Append(meta)
                .Append(T.DATA_REPORT_OPEN)
                .Append(data)
                .Append(T.SCRIPT_OPEN);
            foreach (var js in T.JsParts) sb.Append(js);
            sb.Append(T.DOC_END);

            File.WriteAllText(path, sb.ToString(), Utf8NoBom);
            return path;
        }

        #endregion

        #region Private

        static string PrepareDir(string outputDir)
        {
            if (string.IsNullOrWhiteSpace(outputDir)) throw new ArgumentException("outputDir rỗng.", nameof(outputDir));
            var dir = Path.GetFullPath(outputDir);
            Directory.CreateDirectory(dir);
            return dir;
        }

        static string TryWrite(string fileName, Func<string> write)
        {
            try
            {
                return write();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AutoTest] Không ghi được {fileName}: {e.Message}\n{e.StackTrace}");
                return null;
            }
        }

        /// <summary>Chặn chuỗi JSON thoát khỏi thẻ &lt;script&gt; ("&lt;/" và "&lt;!--"), vẫn là JSON hợp lệ.</summary>
        static string EscapeForScript(string json)
        {
            if (string.IsNullOrEmpty(json)) return "null";
            return json.Replace("</", "<\\/").Replace("<!--", HtmlCommentOpenEscaped);
        }

        static string SanitizeNonFinite(string json)
        {
            if (string.IsNullOrEmpty(json) || (json.IndexOf("NaN", StringComparison.Ordinal) < 0 &&
                                              json.IndexOf("Infinity", StringComparison.Ordinal) < 0))
                return json;
            return NonFiniteValue.Replace(json, "$1null");
        }

        /// <summary>Khối meta: nhãn trạng thái (AutoTestStatusUtil.Label), tên severity, thời điểm tạo, version package.</summary>
        static string BuildMetaJson(TestRunReport report)
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"statusLabels\":[");
            var statuses = (TestStatus[])Enum.GetValues(typeof(TestStatus));
            Array.Sort(statuses);
            for (var i = 0; i < statuses.Length; i++)
            {
                if (i > 0) sb.Append(',');
                F.AppendJsonString(sb, F.StatusLabel(statuses[i]));
            }

            sb.Append("],\"severityNames\":[");
            var severities = (Severity[])Enum.GetValues(typeof(Severity));
            Array.Sort(severities);
            for (var i = 0; i < severities.Length; i++)
            {
                if (i > 0) sb.Append(',');
                F.AppendJsonString(sb, F.SeverityName(severities[i]));
            }

            sb.Append("],\"generatedAt\":");
            F.AppendJsonString(sb, DateTimeOffset.Now.ToString("o", F.Inv));
            sb.Append(",\"packageVersion\":");
            F.AppendJsonString(sb, PackageVersion(report));
            sb.Append('}');
            return sb.ToString();
        }

        static string PackageVersion(TestRunReport report)
        {
            if (!string.IsNullOrEmpty(report?.env?.packageVersion)) return report.env.packageVersion;
            try
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AutoTestReportWriter).Assembly);
                return info != null ? info.version : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        #endregion
    }
}
