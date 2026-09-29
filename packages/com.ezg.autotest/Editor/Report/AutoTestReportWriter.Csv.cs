using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using F = Ezg.AutoTest.Editor.AutoTestReportFormat;

namespace Ezg.AutoTest.Editor
{
    public static partial class AutoTestReportWriter
    {
        #region CSV — hằng số

        const string CSV_NEWLINE = "\r\n";
        const string CSV_YES = "Có";

        /// <summary>Ước lượng số ký tự mỗi dòng để cấp StringBuilder một lần.</summary>
        const int CSV_ROW_CAPACITY = 256;

        static readonly string[] CsvHeader =
        {
            "ID", "Mới?", "Severity", "Suite", "Case", "Nhóm", "Tiêu đề", "Mô tả", "Vị trí", "Object", "Kỳ vọng",
            "Thực tế", "Các bước tái hiện", "Ảnh", "Device", "Trạng thái case"
        };

        #endregion

        /// <summary>
        ///     issues.csv — UTF-8 có BOM (Excel đọc đúng tiếng Việt), quote theo RFC 4180, mỗi issue một dòng, sắp xếp
        ///     severity giảm dần (cùng severity giữ thứ tự suite/case trong report).
        /// </summary>
        public static string WriteIssuesCsv(TestRunReport report, string outputDir)
        {
            if (report == null) throw new System.ArgumentNullException(nameof(report));
            var path = Path.Combine(PrepareDir(outputDir), CSV_FILE);

            var rows = new List<(TestSuiteResult suite, TestCaseResult testCase, TestIssue issue)>();
            foreach (var suite in F.Suites(report))
            foreach (var c in F.Cases(suite))
            foreach (var issue in F.Issues(c))
                rows.Add((suite, c, issue));

            var sb = new StringBuilder(CSV_ROW_CAPACITY * (rows.Count + 1));
            AppendCsvRow(sb, CsvHeader);
            // OrderByDescending là stable ⇒ cùng severity giữ nguyên thứ tự suite/case. Thứ tự cột khớp CsvHeader.
            foreach (var row in rows.OrderByDescending(r => (int)r.issue.severity))
            {
                var i = row.issue;
                AppendCsvRow(sb, new[]
                {
                    i.id,
                    i.isNew ? CSV_YES : string.Empty,
                    F.SeverityName(i.severity),
                    F.SuiteLabel(row.suite),
                    F.CaseLabel(row.testCase),
                    i.category,
                    F.IssueTitle(i),
                    i.message,
                    i.location,
                    i.objectPath,
                    i.expected,
                    i.actual,
                    i.steps,
                    i.screenshot,
                    F.CaseDevice(row.testCase, report.env),
                    F.StatusLabel(row.testCase.status)
                });
            }

            File.WriteAllText(path, sb.ToString(), Utf8Bom);
            return path;
        }

        #region CSV — private

        static void AppendCsvRow(StringBuilder sb, string[] fields)
        {
            for (var i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(',');
                AppendCsvField(sb, fields[i]);
            }

            sb.Append(CSV_NEWLINE);
        }

        /// <summary>
        ///     Luôn bọc nháy kép (RFC 4180), nháy trong nội dung nhân đôi, xuống dòng chuẩn hoá về LF. Ô bắt đầu bằng
        ///     = + - @ (không phải số) được thêm nháy đơn để Excel không hiểu nhầm là công thức.
        /// </summary>
        static void AppendCsvField(StringBuilder sb, string value)
        {
            var s = F.NormalizeNewlines(value);
            sb.Append('"');
            if (s.Length > 0 && IsFormulaLike(s)) sb.Append('\'');
            sb.Append(s.Replace("\"", "\"\""));
            sb.Append('"');
        }

        static bool IsFormulaLike(string s)
        {
            var first = s[0];
            if (first != '=' && first != '+' && first != '-' && first != '@' && first != '\t') return false;
            return !double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }

        #endregion
    }
}
