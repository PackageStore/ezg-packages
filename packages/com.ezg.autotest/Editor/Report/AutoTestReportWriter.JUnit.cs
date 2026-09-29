using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using F = Ezg.AutoTest.Editor.AutoTestReportFormat;

namespace Ezg.AutoTest.Editor
{
    public static partial class AutoTestReportWriter
    {
        #region JUnit — hằng số

        const string JUNIT_CLASS_PREFIX = "EZG.AutoTest.";
        const string JUNIT_TIMESTAMP_FORMAT = "yyyy-MM-ddTHH:mm:ss";

        /// <summary>Số issue tối đa ghi chi tiết trong một testcase (GitLab giới hạn kích thước report).</summary>
        const int JUNIT_MAX_ISSUES_PER_CASE = 50;

        /// <summary>Số dòng log lỗi tối đa đưa vào system-err của một testcase.</summary>
        const int JUNIT_MAX_ERROR_LOGS = 50;

        const int JUNIT_MESSAGE_MAX = 500;
        const int JUNIT_FIELD_MAX = 4000;
        const string INDENT = "  ";

        #endregion

        /// <summary>
        ///     junit.xml theo schema JUnit mà GitLab đọc được: testsuites → testsuite (mỗi suite) → testcase. Failed ⇒
        ///     &lt;failure&gt;, Error ⇒ &lt;error&gt;, Skipped/Cancelled/Pending ⇒ &lt;skipped&gt;; Warning tính là đạt
        ///     nhưng issue được ghi vào &lt;system-out&gt;.
        /// </summary>
        public static string WriteJUnit(TestRunReport report, string outputDir)
        {
            if (report == null) throw new System.ArgumentNullException(nameof(report));
            var path = Path.Combine(PrepareDir(outputDir), JUNIT_FILE);
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = INDENT,
                NewLineChars = "\n",
                Encoding = Utf8NoBom,
                CheckCharacters = true
            };

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var w = XmlWriter.Create(stream, settings))
            {
                var all = CountJUnit(F.AllCases(report));
                w.WriteStartDocument();
                w.WriteStartElement("testsuites");
                w.WriteAttributeString("name", Xml(F.Title(report)));
                WriteCountAttributes(w, all);
                w.WriteAttributeString("time", F.Seconds(report.durationMs));
                var timestamp = Timestamp(report.startedAt);
                if (timestamp != null) w.WriteAttributeString("timestamp", timestamp);

                foreach (var suite in F.Suites(report)) WriteJUnitSuite(w, report, suite, timestamp);

                w.WriteEndElement();
                w.WriteEndDocument();
            }

            return path;
        }

        #region JUnit — private

        struct JUnitCounts
        {
            public int tests;
            public int failures;
            public int errors;
            public int skipped;
        }

        static JUnitCounts CountJUnit(IEnumerable<TestCaseResult> cases)
        {
            var c = new JUnitCounts();
            foreach (var tc in cases)
            {
                c.tests++;
                switch (tc.status)
                {
                    case TestStatus.Failed: c.failures++; break;
                    case TestStatus.Error:
                    case TestStatus.Running: c.errors++; break;
                    case TestStatus.Skipped:
                    case TestStatus.Cancelled:
                    case TestStatus.Pending: c.skipped++; break;
                }
            }

            return c;
        }

        static void WriteCountAttributes(XmlWriter w, JUnitCounts c)
        {
            w.WriteAttributeString("tests", c.tests.ToString(F.Inv));
            w.WriteAttributeString("failures", c.failures.ToString(F.Inv));
            w.WriteAttributeString("errors", c.errors.ToString(F.Inv));
            w.WriteAttributeString("skipped", c.skipped.ToString(F.Inv));
        }

        static void WriteJUnitSuite(XmlWriter w, TestRunReport report, TestSuiteResult suite, string timestamp)
        {
            var suiteId = string.IsNullOrEmpty(suite.suiteId) ? "suite" : suite.suiteId;
            w.WriteStartElement("testsuite");
            w.WriteAttributeString("name", Xml(F.SuiteLabel(suite)));
            w.WriteAttributeString("id", Xml(suiteId));
            WriteCountAttributes(w, CountJUnit(F.Cases(suite)));
            w.WriteAttributeString("time", F.Seconds(suite.durationMs));
            if (timestamp != null) w.WriteAttributeString("timestamp", timestamp);
            var env = report.env;
            if (!string.IsNullOrEmpty(env?.machine)) w.WriteAttributeString("hostname", Xml(env.machine));

            WriteJUnitProperties(w, report, suite);

            foreach (var c in F.Cases(suite)) WriteJUnitCase(w, report, suiteId, c);

            var suiteOut = new StringBuilder();
            if (!string.IsNullOrEmpty(suite.description)) suiteOut.Append(suite.description).Append('\n');
            if (!string.IsNullOrEmpty(suite.message)) suiteOut.Append(suite.message).Append('\n');
            if (suiteOut.Length > 0) w.WriteElementString("system-out", Xml(suiteOut.ToString()));
            w.WriteEndElement();
        }

        static void WriteJUnitProperties(XmlWriter w, TestRunReport report, TestSuiteResult suite)
        {
            var env = report.env ?? new RunEnvironment();
            var props = new List<KeyValuePair<string, string>>
            {
                new("runId", report.runId),
                new("mode", suite.mode.ToString()),
                new("status", F.StatusLabel(suite.status)),
                new("unityVersion", env.unityVersion),
                new("platform", env.platform),
                new("device", F.CaseDevice(null, env)),
                new("git", F.GitText(env)),
                new("appVersion", env.appVersion),
                new("adapter", env.adapter)
            };
            w.WriteStartElement("properties");
            foreach (var p in props)
            {
                if (string.IsNullOrEmpty(p.Value)) continue;
                w.WriteStartElement("property");
                w.WriteAttributeString("name", p.Key);
                w.WriteAttributeString("value", Xml(p.Value));
                w.WriteEndElement();
            }

            w.WriteEndElement();
        }

        static void WriteJUnitCase(XmlWriter w, TestRunReport report, string suiteId, TestCaseResult c)
        {
            w.WriteStartElement("testcase");
            w.WriteAttributeString("classname", Xml(JUNIT_CLASS_PREFIX + suiteId));
            w.WriteAttributeString("name", Xml(F.CaseLabel(c)));
            w.WriteAttributeString("time", F.Seconds(c.durationMs));

            var hasIssues = F.IssueCount(c) > 0;
            switch (c.status)
            {
                case TestStatus.Failed:
                    WriteJUnitProblem(w, "failure", c, report);
                    break;
                case TestStatus.Error:
                case TestStatus.Running:
                    WriteJUnitProblem(w, "error", c, report);
                    break;
                case TestStatus.Skipped:
                case TestStatus.Cancelled:
                case TestStatus.Pending:
                    w.WriteStartElement("skipped");
                    w.WriteAttributeString("message", Xml(F.OneLine(
                        string.IsNullOrEmpty(c.message) ? F.StatusLabel(c.status) : c.message, JUNIT_MESSAGE_MAX)));
                    w.WriteEndElement();
                    break;
                default:
                    // Passed / Warning: tính là đạt, issue (nếu có) để trong system-out cho QA đọc.
                    if (hasIssues) w.WriteElementString("system-out", Xml(CaseDetails(c, report)));
                    break;
            }

            var errLogs = ErrorLogs(c);
            if (errLogs.Length > 0) w.WriteElementString("system-err", Xml(errLogs));
            w.WriteEndElement();
        }

        static void WriteJUnitProblem(XmlWriter w, string element, TestCaseResult c, TestRunReport report)
        {
            var top = F.TopIssue(c);
            var message = !string.IsNullOrEmpty(c.message) ? c.message : top != null ? F.IssueTitle(top) : F.StatusLabel(c.status);
            w.WriteStartElement(element);
            w.WriteAttributeString("message", Xml(F.OneLine(message, JUNIT_MESSAGE_MAX)));
            w.WriteAttributeString("type", top != null ? F.SeverityName(F.MaxSeverity(c)) : F.StatusLabel(c.status));
            w.WriteString(Xml(CaseDetails(c, report)));
            w.WriteEndElement();
        }

        /// <summary>Chi tiết case dạng text: thông điệp, từng issue (đủ field để log bug), các bước, số đo.</summary>
        static string CaseDetails(TestCaseResult c, TestRunReport report)
        {
            var sb = new StringBuilder();
            sb.Append("Trạng thái: ").Append(F.StatusLabel(c.status)).Append('\n');
            if (!string.IsNullOrEmpty(c.message)) sb.Append("Thông điệp: ").Append(c.message).Append('\n');
            sb.Append("Môi trường: ").Append(F.EnvironmentLine(report.env, c)).Append('\n');

            var issues = new List<TestIssue>(F.Issues(c));
            issues.Sort((a, b) => b.severity.CompareTo(a.severity));
            if (issues.Count > 0)
            {
                sb.Append("\n--- ").Append(issues.Count).Append(" lỗi ---\n");
                var shown = 0;
                foreach (var issue in issues)
                {
                    if (shown++ >= JUNIT_MAX_ISSUES_PER_CASE)
                    {
                        sb.Append("… và ").Append(issues.Count - JUNIT_MAX_ISSUES_PER_CASE).Append(" lỗi khác (xem report.html)\n");
                        break;
                    }

                    AppendIssueText(sb, issue);
                }
            }

            var steps = new List<TestStep>(F.Items(c.steps));
            if (steps.Count > 0)
            {
                sb.Append("\n--- Các bước ---\n");
                for (var i = 0; i < steps.Count; i++)
                    sb.Append(i + 1).Append(". [").Append(F.StatusLabel(steps[i].status)).Append("] ")
                        .Append(steps[i].name).Append(" (").Append(F.Duration(steps[i].durationMs)).Append(")\n");
            }

            var metrics = new List<TestMetric>(F.Items(c.metrics));
            if (metrics.Count > 0)
            {
                sb.Append("\n--- Số đo ---\n");
                foreach (var m in metrics) sb.Append(MetricText(m)).Append('\n');
            }

            return sb.ToString();
        }

        static void AppendIssueText(StringBuilder sb, TestIssue issue)
        {
            sb.Append('[').Append(F.SeverityName(issue.severity)).Append("] ").Append(F.IssueTitle(issue));
            if (issue.isNew) sb.Append(" (MỚI)");
            sb.Append('\n');
            AppendField(sb, "Nhóm", issue.category);
            AppendField(sb, "Mô tả", issue.message);
            AppendField(sb, "Vị trí", issue.location);
            AppendField(sb, "Object", issue.objectPath);
            AppendField(sb, "Kỳ vọng", issue.expected);
            AppendField(sb, "Thực tế", issue.actual);
            AppendBlock(sb, "Các bước tái hiện", issue.steps);
            AppendField(sb, "Ảnh", issue.screenshot);
            AppendField(sb, "ID", issue.id);
            AppendBlock(sb, "Stack", issue.stackTrace);
            sb.Append('\n');
        }

        static void AppendField(StringBuilder sb, string label, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            sb.Append(INDENT).Append(label).Append(": ").Append(F.Clip(value, JUNIT_FIELD_MAX)).Append('\n');
        }

        static void AppendBlock(StringBuilder sb, string label, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            sb.Append(INDENT).Append(label).Append(":\n");
            foreach (var line in F.NormalizeNewlines(F.Clip(value, JUNIT_FIELD_MAX)).Split('\n'))
                sb.Append(INDENT).Append(INDENT).Append(line).Append('\n');
        }

        static string MetricText(TestMetric m)
        {
            var sb = new StringBuilder();
            sb.Append(m.name).Append(" = ").Append(m.value.ToString("0.###", F.Inv));
            if (!string.IsNullOrEmpty(m.unit)) sb.Append(' ').Append(m.unit);
            if (m.hasMin || m.hasMax)
            {
                sb.Append(" (ngưỡng");
                if (m.hasMin) sb.Append(" ≥ ").Append(m.min.ToString("0.###", F.Inv));
                if (m.hasMax) sb.Append(" ≤ ").Append(m.max.ToString("0.###", F.Inv));
                sb.Append(')');
            }

            sb.Append(m.passed ? " — Đạt" : " — Không đạt");
            return sb.ToString();
        }

        static string ErrorLogs(TestCaseResult c)
        {
            var sb = new StringBuilder();
            var count = 0;
            foreach (var log in F.Items(c.logs))
            {
                var type = F.Safe(log.type);
                if (type != "Error" && type != "Exception" && type != "Assert") continue;
                if (count++ >= JUNIT_MAX_ERROR_LOGS) break;
                sb.Append('[').Append(log.t.ToString("0.00", F.Inv)).Append("s ").Append(type).Append("] ")
                    .Append(F.Clip(log.message, JUNIT_FIELD_MAX)).Append('\n');
                if (!string.IsNullOrEmpty(log.stack)) sb.Append(F.Clip(log.stack, JUNIT_FIELD_MAX)).Append('\n');
            }

            return sb.ToString();
        }

        static string Timestamp(string iso)
        {
            return F.TryParseDate(iso, out var d) ? d.ToLocalTime().ToString(JUNIT_TIMESTAMP_FORMAT, F.Inv) : null;
        }

        /// <summary>Bỏ ký tự XML 1.0 không cho phép (control char trong log…) — XmlWriter sẽ ném lỗi nếu gặp.</summary>
        static string Xml(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            StringBuilder sb = null;
            for (var i = 0; i < s.Length; i++)
            {
                var ch = s[i];
                var ok = XmlConvert.IsXmlChar(ch);
                if (!ok && char.IsHighSurrogate(ch) && i + 1 < s.Length && XmlConvert.IsXmlSurrogatePair(s[i + 1], ch))
                {
                    sb?.Append(ch).Append(s[i + 1]);
                    i++;
                    continue;
                }

                if (ok)
                {
                    sb?.Append(ch);
                    continue;
                }

                if (sb == null)
                {
                    sb = new StringBuilder(s.Length);
                    sb.Append(s, 0, i);
                }
            }

            return sb == null ? s : sb.ToString();
        }

        #endregion
    }
}
