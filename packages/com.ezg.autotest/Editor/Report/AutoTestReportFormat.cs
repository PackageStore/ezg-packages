using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Tiện ích định dạng dùng chung cho mọi file report: thời lượng, ngày giờ, nhãn suite/case, duyệt list an toàn
    ///     với null (JsonUtility có thể để null khi đọc report cũ/hỏng), escape HTML/JSON.
    /// </summary>
    internal static class AutoTestReportFormat
    {
        #region Hằng số

        internal const string DATE_FORMAT = "dd/MM/yyyy HH:mm:ss";
        internal const string DEFAULT_TITLE = "Lượt chạy auto test";
        internal const string EDITOR_DEVICE = "Unity Editor";
        internal const string UNNAMED_CASE = "(không tên)";
        internal const string UNNAMED_SUITE = "Suite";
        internal const string UNTITLED_ISSUE = "(không tiêu đề)";
        internal const string CASE_LABEL_SEPARATOR = " / ";
        internal const int COMMIT_SHORT_LENGTH = 8;

        const double MS_PER_SECOND = 1000d;
        const long SECONDS_PER_MINUTE = 60;
        const long SECONDS_PER_HOUR = 3600;
        const string ELLIPSIS = "…";

        internal static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        #endregion

        #region Duyệt list an toàn

        /// <summary>Duyệt list bỏ qua null (list null ⇒ rỗng).</summary>
        internal static IEnumerable<T> Items<T>(List<T> list) where T : class
        {
            if (list == null) yield break;
            foreach (var item in list)
                if (item != null)
                    yield return item;
        }

        internal static IEnumerable<TestSuiteResult> Suites(TestRunReport report)
        {
            return Items(report?.suites);
        }

        internal static IEnumerable<TestCaseResult> Cases(TestSuiteResult suite)
        {
            return Items(suite?.cases);
        }

        internal static IEnumerable<TestIssue> Issues(TestCaseResult testCase)
        {
            return Items(testCase?.issues);
        }

        /// <summary>Mọi case của lượt chạy.</summary>
        internal static IEnumerable<TestCaseResult> AllCases(TestRunReport report)
        {
            foreach (var suite in Suites(report))
            foreach (var c in Cases(suite))
                yield return c;
        }

        /// <summary>Thay mọi list/object null bằng rỗng — gọi sau khi đọc report từ JSON cũ hoặc thiếu field.</summary>
        internal static void EnsureNotNull(TestRunReport report)
        {
            if (report == null) return;
            report.env ??= new RunEnvironment();
            report.summary ??= new RunSummary();
            report.diff ??= new RunDiff();
            report.diff.newlyFailingCases ??= new List<string>();
            report.diff.fixedCases ??= new List<string>();
            report.diff.fixedIssueList ??= new List<TestIssue>();
            report.suites ??= new List<TestSuiteResult>();
            report.suites.RemoveAll(s => s == null);
            foreach (var suite in report.suites)
            {
                suite.cases ??= new List<TestCaseResult>();
                suite.cases.RemoveAll(c => c == null);
                foreach (var c in suite.cases)
                {
                    c.tags ??= new List<string>();
                    c.issues ??= new List<TestIssue>();
                    c.steps ??= new List<TestStep>();
                    c.logs ??= new List<TestLogEntry>();
                    c.metrics ??= new List<TestMetric>();
                    c.attachments ??= new List<TestAttachment>();
                    c.issues.RemoveAll(i => i == null);
                }
            }
        }

        #endregion

        #region Nhãn & trạng thái

        internal static string Safe(string s)
        {
            return s ?? string.Empty;
        }

        internal static string Title(TestRunReport report)
        {
            return string.IsNullOrWhiteSpace(report?.title) ? DEFAULT_TITLE : report.title;
        }

        internal static string SuiteLabel(TestSuiteResult suite)
        {
            if (suite == null) return UNNAMED_SUITE;
            if (!string.IsNullOrEmpty(suite.name)) return suite.name;
            return string.IsNullOrEmpty(suite.suiteId) ? UNNAMED_SUITE : suite.suiteId;
        }

        internal static string CaseLabel(TestCaseResult testCase)
        {
            if (testCase == null) return UNNAMED_CASE;
            if (!string.IsNullOrEmpty(testCase.name)) return testCase.name;
            return string.IsNullOrEmpty(testCase.caseId) ? UNNAMED_CASE : testCase.caseId;
        }

        /// <summary>
        ///     "Suite / Case" — định dạng dùng trong <see cref="RunDiff.newlyFailingCases" />; report.html tra ngược đúng
        ///     chuỗi này để nhảy tới case, nên đổi ở đây thì phải đổi cả JS (findCaseByLabel).
        /// </summary>
        internal static string CaseLabel(TestSuiteResult suite, TestCaseResult testCase)
        {
            return SuiteLabel(suite) + CASE_LABEL_SEPARATOR + CaseLabel(testCase);
        }

        internal static string IssueTitle(TestIssue issue)
        {
            return string.IsNullOrEmpty(issue?.title) ? UNTITLED_ISSUE : issue.title;
        }

        internal static string SeverityName(Severity severity)
        {
            switch (severity)
            {
                case Severity.Blocker: return "Blocker";
                case Severity.Critical: return "Critical";
                case Severity.Major: return "Major";
                case Severity.Minor: return "Minor";
                case Severity.Info: return "Info";
                default: return ((int)severity).ToString(Inv);
            }
        }

        internal static string StatusLabel(TestStatus status)
        {
            return AutoTestStatusUtil.Label(status);
        }

        /// <summary>Trạng thái gộp của cả lượt chạy (cùng luật với suite).</summary>
        internal static TestStatus OverallStatus(TestRunReport report)
        {
            return AutoTestStatusUtil.Aggregate(AllCases(report));
        }

        /// <summary>Severity cao nhất của case — an toàn khi list issue null (khác <see cref="TestCaseResult.MaxSeverity" />).</summary>
        internal static Severity MaxSeverity(TestCaseResult testCase)
        {
            var max = Severity.Info;
            foreach (var issue in Issues(testCase))
                if (issue.severity > max)
                    max = issue.severity;
            return max;
        }

        /// <summary>Issue nặng nhất (issue đầu tiên nếu bằng nhau), null nếu case không có issue.</summary>
        internal static TestIssue TopIssue(TestCaseResult testCase)
        {
            TestIssue top = null;
            foreach (var issue in Issues(testCase))
                if (top == null || issue.severity > top.severity)
                    top = issue;
            return top;
        }

        internal static int IssueCount(TestCaseResult testCase)
        {
            return testCase?.issues?.Count ?? 0;
        }

        internal static string DeviceText(RunEnvironment env)
        {
            if (env == null) return string.Empty;
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(env.deviceModel)) parts.Add(env.deviceModel);
            if (!string.IsNullOrEmpty(env.deviceOs)) parts.Add(env.deviceOs);
            return string.Join(" · ", parts);
        }

        /// <summary>Device của case: device riêng của case → device của lượt chạy → "Unity Editor".</summary>
        internal static string CaseDevice(TestCaseResult testCase, RunEnvironment env)
        {
            if (!string.IsNullOrEmpty(testCase?.device)) return testCase.device;
            var device = DeviceText(env);
            return string.IsNullOrEmpty(device) ? EDITOR_DEVICE : device;
        }

        internal static string GitText(RunEnvironment env)
        {
            if (env == null || (string.IsNullOrEmpty(env.gitBranch) && string.IsNullOrEmpty(env.gitCommit))) return string.Empty;
            var sb = new StringBuilder(Safe(env.gitBranch));
            if (!string.IsNullOrEmpty(env.gitCommit)) sb.Append('@').Append(ShortCommit(env.gitCommit));
            if (env.gitDirty) sb.Append(" (dirty)");
            return sb.ToString();
        }

        internal static string ShortCommit(string commit)
        {
            if (string.IsNullOrEmpty(commit)) return string.Empty;
            return commit.Length > COMMIT_SHORT_LENGTH ? commit.Substring(0, COMMIT_SHORT_LENGTH) : commit;
        }

        /// <summary>Một dòng môi trường cho bug report: app + version, device, platform, Unity, git.</summary>
        internal static string EnvironmentLine(RunEnvironment env, TestCaseResult testCase)
        {
            env ??= new RunEnvironment();
            var parts = new List<string>();
            var app = new StringBuilder(!string.IsNullOrEmpty(env.productName) ? env.productName : Safe(env.projectName));
            if (!string.IsNullOrEmpty(env.appVersion)) app.Append(" v").Append(env.appVersion);
            if (!string.IsNullOrEmpty(env.buildNumber)) app.Append(" (build ").Append(env.buildNumber).Append(')');
            if (app.Length > 0) parts.Add(app.ToString().Trim());
            parts.Add(CaseDevice(testCase, env));
            if (!string.IsNullOrEmpty(env.platform)) parts.Add(env.platform);
            if (!string.IsNullOrEmpty(env.unityVersion)) parts.Add("Unity " + env.unityVersion);
            var git = GitText(env);
            if (!string.IsNullOrEmpty(git)) parts.Add("git " + git);
            return string.Join(" · ", parts);
        }

        #endregion

        #region Thời gian

        /// <summary>ms → "850 ms" / "12.3s" / "1m 23s" / "2h 5m" (giống hàm fmtDur trong report.html).</summary>
        internal static string Duration(double ms)
        {
            if (double.IsNaN(ms) || double.IsInfinity(ms) || ms < 0) ms = 0;
            if (ms < MS_PER_SECOND) return Math.Round(ms).ToString("0", Inv) + " ms";
            var sec = ms / MS_PER_SECOND;
            if (sec < SECONDS_PER_MINUTE) return sec.ToString("0.#", Inv) + "s";
            var whole = (long)Math.Floor(sec);
            var h = whole / SECONDS_PER_HOUR;
            var m = whole % SECONDS_PER_HOUR / SECONDS_PER_MINUTE;
            var s = whole % SECONDS_PER_MINUTE;
            return h > 0 ? $"{h}h {m}m" : $"{m}m {s}s";
        }

        /// <summary>ms → giây dạng "12.345" (JUnit).</summary>
        internal static string Seconds(double ms)
        {
            if (double.IsNaN(ms) || double.IsInfinity(ms) || ms < 0) ms = 0;
            return (ms / MS_PER_SECOND).ToString("0.###", Inv);
        }

        internal static bool TryParseDate(string iso, out DateTimeOffset value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(iso)) return false;
            return DateTimeOffset.TryParse(iso.Trim(), Inv, DateTimeStyles.AssumeLocal, out value);
        }

        /// <summary>ISO → "dd/MM/yyyy HH:mm:ss" giờ máy; không parse được thì trả nguyên chuỗi.</summary>
        internal static string Date(string iso)
        {
            return TryParseDate(iso, out var d) ? d.ToLocalTime().ToString(DATE_FORMAT, Inv) : Safe(iso);
        }

        #endregion

        #region Chuỗi

        /// <summary>Gộp về một dòng (bỏ xuống dòng, khoảng trắng thừa) và cắt tối đa <paramref name="max" /> ký tự.</summary>
        internal static string OneLine(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(Math.Min(s.Length, max + 1));
            var space = false;
            foreach (var ch in s)
            {
                if (char.IsWhiteSpace(ch))
                {
                    space = sb.Length > 0;
                    continue;
                }

                if (space)
                {
                    sb.Append(' ');
                    space = false;
                }

                sb.Append(ch);
                if (sb.Length > max) break;
            }

            return sb.Length > max ? sb.ToString(0, Math.Max(0, max - 1)) + ELLIPSIS : sb.ToString();
        }

        /// <summary>Cắt chuỗi nhiều dòng tối đa <paramref name="max" /> ký tự (giữ xuống dòng).</summary>
        internal static string Clip(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return Safe(s);
            return s.Substring(0, Math.Max(0, max - 1)) + ELLIPSIS;
        }

        internal static string NormalizeNewlines(string s)
        {
            return string.IsNullOrEmpty(s) ? string.Empty : s.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        internal static string HtmlEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length + 16);
            foreach (var ch in s)
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&#39;"); break;
                    default: sb.Append(ch); break;
                }

            return sb.ToString();
        }

        /// <summary>Chuỗi JSON có nháy (dùng cho khối meta nhỏ tự dựng, không qua JsonUtility).</summary>
        internal static void AppendJsonString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var ch in Safe(s))
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4", Inv));
                        else sb.Append(ch);
                        break;
                }

            sb.Append('"');
        }

        #endregion
    }
}
