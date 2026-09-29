using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using F = Ezg.AutoTest.Editor.AutoTestReportFormat;

namespace Ezg.AutoTest.Editor
{
    public static partial class AutoTestReportWriter
    {
        #region Markdown — hằng số

        /// <summary>Số case lỗi tối đa liệt kê trong bảng (MR/Discord không cần hết).</summary>
        const int MD_MAX_FAILED_CASES = 30;

        /// <summary>Số case tối đa liệt kê trong dòng "Case mới lỗi / hết lỗi".</summary>
        const int MD_MAX_DIFF_CASES = 10;

        const int MD_CELL_MAX = 120;
        const int MD_INLINE_MAX = 300;
        const int MD_CAPACITY = 4096;

        #endregion

        /// <summary>
        ///     summary.md — bản tóm tắt ngắn để dán vào Merge Request / Discord: trạng thái, điểm sức khoẻ, số đếm,
        ///     severity, so sánh lần trước, bảng case lỗi (tối đa 30) kèm lỗi nặng nhất, link tới report.html.
        /// </summary>
        public static string WriteMarkdown(TestRunReport report, string outputDir)
        {
            if (report == null) throw new System.ArgumentNullException(nameof(report));
            var path = Path.Combine(PrepareDir(outputDir), MARKDOWN_FILE);
            var s = report.summary;
            if (s == null)
            {
                s = new RunSummary();
                s.healthScore = AutoTestStatusUtil.HealthScore(s);
            }

            var env = report.env ?? new RunEnvironment();
            var overall = F.OverallStatus(report);
            var sb = new StringBuilder(MD_CAPACITY);

            // Tiêu đề + dòng meta
            sb.Append("## ").Append(StatusEmoji(report.cancelled ? TestStatus.Cancelled : overall)).Append(' ')
                .Append(MdInline(F.Title(report))).Append(" — ").Append(F.StatusLabel(overall));
            if (report.cancelled && overall != TestStatus.Cancelled) sb.Append(" · ").Append(F.StatusLabel(TestStatus.Cancelled));
            sb.Append("\n\n");
            var meta = new List<string> { $"**Sức khoẻ: {s.healthScore}/100**", "⏱ " + F.Duration(report.durationMs) };
            if (!string.IsNullOrEmpty(report.startedAt)) meta.Add("📅 " + F.Date(report.startedAt));
            var git = F.GitText(env);
            if (!string.IsNullOrEmpty(git)) meta.Add("🌿 `" + git.Replace("`", "'") + "`");
            if (!string.IsNullOrEmpty(env.unityVersion)) meta.Add("Unity " + MdInline(env.unityVersion));
            if (!string.IsNullOrEmpty(env.platform)) meta.Add(MdInline(env.platform));
            meta.Add(MdInline(F.CaseDevice(null, env)));
            if (!string.IsNullOrEmpty(report.trigger)) meta.Add("Kích hoạt: " + MdInline(report.trigger));
            sb.Append(string.Join(" · ", meta)).Append("\n\n");

            if (report.cancelled)
                sb.Append("> ⏹️ **Lượt chạy bị dừng giữa chừng** — ")
                    .Append(MdInline(string.IsNullOrEmpty(report.cancelReason) ? "không rõ lý do" : report.cancelReason))
                    .Append("\n\n");

            // Bảng số đếm
            sb.Append("| Tổng | ✅ ").Append(F.StatusLabel(TestStatus.Passed))
                .Append(" | ⚠️ ").Append(F.StatusLabel(TestStatus.Warning))
                .Append(" | ❌ ").Append(F.StatusLabel(TestStatus.Failed))
                .Append(" | 💥 ").Append(F.StatusLabel(TestStatus.Error))
                .Append(" | ⏭️ ").Append(F.StatusLabel(TestStatus.Skipped))
                .Append(" | ⏹️ ").Append(F.StatusLabel(TestStatus.Cancelled)).Append(" |\n");
            sb.Append("|:-:|:-:|:-:|:-:|:-:|:-:|:-:|\n");
            sb.Append("| ").Append(s.total).Append(" | ").Append(s.passed).Append(" | ").Append(s.warning)
                .Append(" | ").Append(s.failed).Append(" | ").Append(s.error).Append(" | ").Append(s.skipped)
                .Append(" | ").Append(s.cancelled).Append(" |\n\n");

            // Severity
            sb.Append("**Lỗi theo mức độ:** ")
                .Append("🟥 Blocker ").Append(s.issuesBlocker)
                .Append(" · 🔴 Critical ").Append(s.issuesCritical)
                .Append(" · 🟠 Major ").Append(s.issuesMajor)
                .Append(" · 🟡 Minor ").Append(s.issuesMinor)
                .Append(" · 🔵 Info ").Append(s.issuesInfo).Append("\n\n");

            AppendMarkdownDiff(sb, report.diff);
            AppendMarkdownFailedCases(sb, report);

            sb.Append("📄 [Mở báo cáo đầy đủ](").Append(HTML_FILE).Append(") · [").Append(CSV_FILE).Append("](").Append(CSV_FILE)
                .Append(") · [").Append(JUNIT_FILE).Append("](").Append(JUNIT_FILE).Append(")\n");

            File.WriteAllText(path, sb.ToString(), Utf8NoBom);
            return path;
        }

        #region Markdown — private

        static void AppendMarkdownDiff(StringBuilder sb, RunDiff diff)
        {
            if (diff == null || string.IsNullOrEmpty(diff.previousRunId))
            {
                sb.Append("_Chưa có lượt chạy trước cùng bộ suite để so sánh._\n\n");
                return;
            }

            sb.Append("**So với lần chạy trước** (").Append(F.Date(diff.previousStartedAt)).Append("): 🆕 ")
                .Append(diff.newIssues).Append(" lỗi mới · ✅ ").Append(diff.fixedIssues).Append(" đã sửa · ♻️ ")
                .Append(diff.persistingIssues).Append(" còn tồn\n");
            AppendCaseList(sb, "Case mới lỗi", diff.newlyFailingCases);
            AppendCaseList(sb, "Case hết lỗi", diff.fixedCases);
            sb.Append('\n');
        }

        static void AppendCaseList(StringBuilder sb, string label, List<string> cases)
        {
            if (cases == null || cases.Count == 0) return;
            sb.Append("- ").Append(label).Append(" (").Append(cases.Count).Append("): ");
            sb.Append(string.Join(", ", cases.Take(MD_MAX_DIFF_CASES).Select(MdInline)));
            if (cases.Count > MD_MAX_DIFF_CASES) sb.Append(", +").Append(cases.Count - MD_MAX_DIFF_CASES);
            sb.Append('\n');
        }

        static void AppendMarkdownFailedCases(StringBuilder sb, TestRunReport report)
        {
            var bad = new List<(TestSuiteResult suite, TestCaseResult testCase)>();
            foreach (var suite in F.Suites(report))
            foreach (var c in F.Cases(suite))
                if (AutoTestStatusUtil.IsBad(c.status) || c.status == TestStatus.Running)
                    bad.Add((suite, c));
            if (bad.Count == 0)
            {
                sb.Append("Không có case Lỗi / Crash. 🎉\n\n");
                return;
            }

            // Crash trước, rồi theo severity nặng nhất; stable nên giữ thứ tự report khi bằng nhau.
            var ordered = bad.OrderByDescending(b => b.testCase.status == TestStatus.Failed ? 0 : 1)
                .ThenByDescending(b => (int)F.MaxSeverity(b.testCase)).ToList();
            sb.Append("### Case lỗi (").Append(bad.Count).Append(")\n\n");
            sb.Append("| Trạng thái | Suite | Case | Lỗi nặng nhất |\n|---|---|---|---|\n");
            foreach (var b in ordered.Take(MD_MAX_FAILED_CASES))
            {
                var top = F.TopIssue(b.testCase);
                var topText = top != null
                    ? $"**{F.SeverityName(top.severity)}** {MdCell(F.IssueTitle(top))}" + (top.isNew ? " 🆕" : string.Empty)
                    : MdCell(b.testCase.message);
                sb.Append("| ").Append(StatusEmoji(b.testCase.status)).Append(' ').Append(F.StatusLabel(b.testCase.status))
                    .Append(" | ").Append(MdCell(F.SuiteLabel(b.suite)))
                    .Append(" | ").Append(MdCell(F.CaseLabel(b.testCase)))
                    .Append(" | ").Append(topText).Append(" |\n");
            }

            if (bad.Count > MD_MAX_FAILED_CASES)
                sb.Append("\n_… và ").Append(bad.Count - MD_MAX_FAILED_CASES).Append(" case khác — xem report.html._\n");
            sb.Append('\n');
        }

        static string StatusEmoji(TestStatus status)
        {
            switch (status)
            {
                case TestStatus.Passed: return "✅";
                case TestStatus.Warning: return "⚠️";
                case TestStatus.Failed: return "❌";
                case TestStatus.Error:
                case TestStatus.Running: return "💥";
                case TestStatus.Skipped: return "⏭️";
                case TestStatus.Cancelled:
                case TestStatus.Pending: return "⏹️";
                default: return "•";
            }
        }

        /// <summary>Một dòng, bỏ ký tự làm vỡ bảng markdown.</summary>
        static string MdCell(string s)
        {
            return MdInline(F.OneLine(s, MD_CELL_MAX)).Replace("|", "\\|");
        }

        static string MdInline(string s)
        {
            return F.OneLine(s, MD_INLINE_MAX).Replace("<", "&lt;").Replace(">", "&gt;");
        }

        #endregion
    }
}
