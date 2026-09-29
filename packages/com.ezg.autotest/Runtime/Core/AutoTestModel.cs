using System;
using System.Collections.Generic;

namespace Ezg.AutoTest
{
    /// <summary>Trạng thái một case / suite / cả lượt chạy.</summary>
    public enum TestStatus
    {
        Pending = 0,
        Running = 1,
        Passed = 2,
        Warning = 3,
        Failed = 4,
        Error = 5,
        Skipped = 6,
        Cancelled = 7
    }

    /// <summary>Mức nghiêm trọng của một lỗi — map thẳng sang thang severity của QA (Blocker → Info).</summary>
    public enum Severity
    {
        Info = 0,
        Minor = 1,
        Major = 2,
        Critical = 3,
        Blocker = 4
    }

    /// <summary>Suite chạy ở đâu.</summary>
    public enum ExecutionMode
    {
        /// <summary>Edit mode, không vào Play (kiểm tra tĩnh asset/cấu hình).</summary>
        Edit = 0,

        /// <summary>Play mode trong Editor (hoặc trong player build khi chạy trên device).</summary>
        Play = 1,

        /// <summary>Editor điều phối build/cài/chạy trên device thật.</summary>
        Device = 2
    }

    /// <summary>Một lỗi / phát hiện cụ thể — đơn vị QA log bug.</summary>
    [Serializable]
    public class TestIssue
    {
        /// <summary>Fingerprint ổn định giữa các lượt chạy (dùng để so sánh lỗi mới / đã sửa).</summary>
        public string id;

        public Severity severity;

        /// <summary>Nhóm lỗi máy đọc được, vd "MissingReference", "Exception", "Economy.Overspend".</summary>
        public string category;

        public string title;
        public string message;

        /// <summary>Asset path / scene / file:line nơi lỗi xảy ra.</summary>
        public string location;

        /// <summary>Đường dẫn hierarchy trong asset/scene (vd "Canvas/Panel/btn_buy").</summary>
        public string objectPath;

        public string expected;
        public string actual;

        /// <summary>Các bước tái hiện, mỗi dòng một bước.</summary>
        public string steps;

        /// <summary>Đường dẫn ảnh tương đối trong thư mục report.</summary>
        public string screenshot;

        public string stackTrace;

        /// <summary>Lỗi mới xuất hiện so với lượt chạy trước (report diff điền).</summary>
        public bool isNew;
    }

    /// <summary>Một dòng log Unity bắt được trong lúc case chạy.</summary>
    [Serializable]
    public class TestLogEntry
    {
        /// <summary>Giây kể từ lúc case bắt đầu.</summary>
        public float t;

        /// <summary>Log / Warning / Error / Exception / Assert.</summary>
        public string type;

        public string message;
        public string stack;
    }

    /// <summary>Một bước trong case (ctx.Step) — cho QA thấy case dừng ở bước nào.</summary>
    [Serializable]
    public class TestStep
    {
        public string name;
        public TestStatus status;
        public double startMs;
        public double durationMs;
        public string detail;
    }

    /// <summary>Số đo (fps, ms, MB, số lượng…) kèm ngưỡng.</summary>
    [Serializable]
    public class TestMetric
    {
        public string name;
        public double value;
        public string unit;
        public bool hasMax;
        public double max;
        public bool hasMin;
        public double min;
        public bool passed = true;
    }

    /// <summary>Tệp đính kèm (ảnh, log, json) — đường dẫn tương đối trong thư mục report.</summary>
    [Serializable]
    public class TestAttachment
    {
        public string label;
        public string path;

        /// <summary>image / text / json / other.</summary>
        public string kind;
    }

    [Serializable]
    public class TestCaseResult
    {
        public string suiteId;
        public string caseId;
        public string name;
        public string description;
        public string category;
        public TestStatus status;
        public string message;
        public string startedAt;
        public double durationMs;

        /// <summary>Tên device khi case chạy trên device (rỗng = Editor).</summary>
        public string device;

        public List<string> tags = new();
        public List<TestIssue> issues = new();
        public List<TestStep> steps = new();
        public List<TestLogEntry> logs = new();
        public List<TestMetric> metrics = new();
        public List<TestAttachment> attachments = new();

        /// <summary>Severity cao nhất trong các issue (Info nếu không có).</summary>
        public Severity MaxSeverity()
        {
            var max = Severity.Info;
            foreach (var issue in issues)
                if (issue.severity > max)
                    max = issue.severity;
            return max;
        }
    }

    [Serializable]
    public class TestSuiteResult
    {
        public string suiteId;
        public string name;
        public string description;
        public ExecutionMode mode;
        public TestStatus status;
        public string message;
        public double durationMs;
        public List<TestCaseResult> cases = new();
    }

    /// <summary>Môi trường chạy — giúp QA/dev tái hiện đúng điều kiện.</summary>
    [Serializable]
    public class RunEnvironment
    {
        public string projectName;
        public string productName;
        public string companyName;
        public string bundleId;
        public string appVersion;
        public string buildNumber;
        public string unityVersion;
        public string platform;
        public string buildTarget;
        public string renderPipeline;
        public string gitBranch;
        public string gitCommit;
        public bool gitDirty;
        public string machine;
        public string os;
        public string user;
        public string deviceModel;
        public string deviceOs;
        public string gpu;
        public int systemMemoryMb;
        public string resolution;
        public string adapter;
        public string packageVersion;
    }

    [Serializable]
    public class RunSummary
    {
        public int total;
        public int passed;
        public int warning;
        public int failed;
        public int error;
        public int skipped;
        public int cancelled;
        public int issuesBlocker;
        public int issuesCritical;
        public int issuesMajor;
        public int issuesMinor;
        public int issuesInfo;

        /// <summary>Điểm sức khoẻ 0–100 (trừ điểm theo severity) — con số một-dòng cho dashboard.</summary>
        public int healthScore;
    }

    /// <summary>So sánh với lượt chạy trước cùng bộ suite.</summary>
    [Serializable]
    public class RunDiff
    {
        public string previousRunId;
        public string previousStartedAt;
        public int newIssues;
        public int fixedIssues;
        public int persistingIssues;
        public List<string> newlyFailingCases = new();
        public List<string> fixedCases = new();
        public List<TestIssue> fixedIssueList = new();
    }

    [Serializable]
    public class TestRunReport
    {
        public string runId;
        public string title;
        public string startedAt;
        public string finishedAt;
        public double durationMs;

        /// <summary>Editor / CLI / Device.</summary>
        public string trigger;

        public bool cancelled;
        public string cancelReason;
        public string outputDir;
        public RunEnvironment env = new();
        public List<TestSuiteResult> suites = new();
        public RunSummary summary = new();
        public RunDiff diff = new();

        /// <summary>Tính lại <see cref="summary" /> + trạng thái suite từ danh sách case.</summary>
        public void Recalculate()
        {
            var s = new RunSummary();
            foreach (var suite in suites)
            {
                suite.status = AutoTestStatusUtil.Aggregate(suite.cases);
                foreach (var c in suite.cases)
                {
                    s.total++;
                    switch (c.status)
                    {
                        case TestStatus.Passed: s.passed++; break;
                        case TestStatus.Warning: s.warning++; break;
                        case TestStatus.Failed: s.failed++; break;
                        case TestStatus.Error: s.error++; break;
                        case TestStatus.Skipped: s.skipped++; break;
                        case TestStatus.Cancelled: s.cancelled++; break;
                        case TestStatus.Pending: s.cancelled++; break;
                        case TestStatus.Running: s.error++; break;
                    }

                    foreach (var issue in c.issues)
                        switch (issue.severity)
                        {
                            case Severity.Blocker: s.issuesBlocker++; break;
                            case Severity.Critical: s.issuesCritical++; break;
                            case Severity.Major: s.issuesMajor++; break;
                            case Severity.Minor: s.issuesMinor++; break;
                            default: s.issuesInfo++; break;
                        }
                }
            }

            s.healthScore = AutoTestStatusUtil.HealthScore(s);
            summary = s;
        }
    }

    public static class AutoTestStatusUtil
    {
        const double WARNING_CASE_WEIGHT = 0.7;
        const int PENALTY_PER_BLOCKER = 15;
        const int PENALTY_PER_CRITICAL = 4;
        const int MAX_ISSUE_PENALTY = 40;
        const int MAX_SCORE = 100;

        /// <summary>Trạng thái gộp: lỗi nặng nhất thắng (Error > Failed > Cancelled > Warning > Passed > Skipped).</summary>
        public static TestStatus Aggregate(IEnumerable<TestCaseResult> cases)
        {
            var any = false;
            var hasPassed = false;
            var hasWarning = false;
            var hasFailed = false;
            var hasError = false;
            var hasCancelled = false;
            foreach (var c in cases)
            {
                any = true;
                switch (c.status)
                {
                    case TestStatus.Passed: hasPassed = true; break;
                    case TestStatus.Warning: hasWarning = true; break;
                    case TestStatus.Failed: hasFailed = true; break;
                    case TestStatus.Error:
                    case TestStatus.Running: hasError = true; break;
                    case TestStatus.Cancelled:
                    case TestStatus.Pending: hasCancelled = true; break;
                }
            }

            if (!any) return TestStatus.Skipped;
            if (hasError) return TestStatus.Error;
            if (hasFailed) return TestStatus.Failed;
            if (hasCancelled) return TestStatus.Cancelled;
            if (hasWarning) return TestStatus.Warning;
            return hasPassed ? TestStatus.Passed : TestStatus.Skipped;
        }

        /// <summary>
        ///     Điểm sức khoẻ 0–100 = tỉ lệ case đạt (case Cảnh báo tính 70%, Crash tính 0) trừ phạt cho lỗi
        ///     Blocker/Critical (tối đa 40 điểm). Không bão hoà về 0 chỉ vì nhiều lỗi nhỏ — số lỗi nằm ở bảng đếm.
        /// </summary>
        public static int HealthScore(RunSummary s)
        {
            var counted = s.passed + s.warning + s.failed + s.error;
            if (counted == 0) return MAX_SCORE;
            var caseScore = (s.passed + WARNING_CASE_WEIGHT * s.warning) / counted * MAX_SCORE;
            var penalty = Math.Min(MAX_ISSUE_PENALTY, s.issuesBlocker * PENALTY_PER_BLOCKER + s.issuesCritical * PENALTY_PER_CRITICAL);
            return (int)Math.Max(0, Math.Round(caseScore - penalty));
        }

        public static bool IsFinal(TestStatus s)
        {
            return s != TestStatus.Pending && s != TestStatus.Running;
        }

        public static bool IsBad(TestStatus s)
        {
            return s == TestStatus.Failed || s == TestStatus.Error;
        }

        /// <summary>Nhãn tiếng Việt cho UI/report.</summary>
        public static string Label(TestStatus s)
        {
            switch (s)
            {
                case TestStatus.Pending: return "Chờ";
                case TestStatus.Running: return "Đang chạy";
                case TestStatus.Passed: return "Đạt";
                case TestStatus.Warning: return "Cảnh báo";
                case TestStatus.Failed: return "Lỗi";
                case TestStatus.Error: return "Crash";
                case TestStatus.Skipped: return "Bỏ qua";
                case TestStatus.Cancelled: return "Đã dừng";
                default: return s.ToString();
            }
        }
    }
}
