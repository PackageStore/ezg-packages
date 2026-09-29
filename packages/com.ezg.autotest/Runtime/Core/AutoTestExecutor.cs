using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Chạy MỘT case: timeout, bắt exception, cắt lát log, quy đổi trạng thái, tính fingerprint issue.
    ///     Dùng chung cho Editor runner và device runner để kết quả đồng nhất.
    /// </summary>
    public static class AutoTestExecutor
    {
        const double UNWIND_SECONDS = 2;
        const int MAX_LOG_ENTRIES_PER_CASE = 400;
        const int MAX_STACK_LINES = 12;

        public static async Task<TestCaseResult> RunCase(AutoTestSession session, AutoTestSuite suite,
            AutoTestCase testCase, CancellationToken runToken)
        {
            var config = session.Config;
            var result = new TestCaseResult
            {
                suiteId = suite.Id,
                caseId = testCase.Id,
                name = testCase.Name,
                description = testCase.Description,
                category = testCase.Category,
                status = TestStatus.Running,
                startedAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
                device = session.DeviceName ?? ""
            };
            if (testCase.Tags != null) result.tags.AddRange(testCase.Tags);

            if (!config.IsCaseEnabled(suite.Id, testCase.Id))
            {
                result.status = TestStatus.Skipped;
                result.message = "Case bị tắt trong Settings.";
                return result;
            }

            session.ReportProgress(result, null);
            var timeout = testCase.TimeoutSeconds > 0 ? testCase.TimeoutSeconds : config.general.caseTimeoutSeconds;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(runToken);
            var ctx = new AutoTestContext(session, result, cts.Token);
            session.CurrentToken = cts.Token;
            var logCursor = AutoTestLogCapture.Cursor;
            var startTime = AutoTestClock.Now;
            var timedOut = false;
            Exception error = null;
            Task task = null;

            try
            {
                task = testCase.Run != null ? testCase.Run(ctx) : Task.CompletedTask;
                if (task == null) task = Task.CompletedTask;
                if (!task.IsCompleted)
                {
                    var timeoutTask = AutoTestClock.Seconds(timeout, cts.Token);
                    var done = await Task.WhenAny(task, timeoutTask);
                    if (done != task)
                    {
                        timedOut = !runToken.IsCancellationRequested;
                        cts.Cancel();
                        // Cho case vài giây để dọn dẹp (finally/TearDown) sau khi bị huỷ.
                        await Task.WhenAny(task, AutoTestClock.Seconds(UNWIND_SECONDS));
                        Observe(task);
                    }
                    else
                    {
                        cts.Cancel(); // dừng timer timeout còn treo
                    }
                }

                if (task.IsCompleted) await task;
            }
            catch (Exception e)
            {
                error = e;
            }

            session.CurrentToken = default;
            result.durationMs = (AutoTestClock.Now - startTime) * 1000.0;
            Resolve(ctx, result, error, timedOut, timeout, runToken.IsCancellationRequested);
            CollectLogs(result, config, logCursor, long.MaxValue, startTime, ctx.LogSeverityCap);
            FinalizeStatus(result, config);
            AssignFingerprints(result);
            session.ReportProgress(result, null);
            return result;
        }

        /// <summary>Tạo kết quả bỏ qua cho case không chạy (vì dừng giữa chừng, thiếu điều kiện…).</summary>
        public static TestCaseResult Placeholder(AutoTestSuite suite, AutoTestCase testCase, TestStatus status,
            string message)
        {
            return new TestCaseResult
            {
                suiteId = suite.Id,
                caseId = testCase.Id,
                name = testCase.Name,
                description = testCase.Description,
                category = testCase.Category,
                status = status,
                message = message,
                startedAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture)
            };
        }

        static void Resolve(AutoTestContext ctx, TestCaseResult result, Exception error, bool timedOut,
            float timeout, bool userCancelled)
        {
            if (timedOut)
            {
                ctx.CloseAllSteps(TestStatus.Failed);
                var step = LastStepName(result);
                result.message = $"Quá thời gian {timeout:0.#}s" + (step != null ? $" ở bước \"{step}\"" : "");
                result.issues.Add(new TestIssue
                {
                    severity = Severity.Major,
                    category = "Timeout",
                    title = "Case chạy quá thời gian cho phép",
                    message = result.message,
                    expected = $"≤ {timeout:0.#}s",
                    actual = "chưa xong",
                    steps = ctx.DescribeSteps()
                });
                result.status = TestStatus.Failed;
                return;
            }

            if (error == null)
            {
                ctx.CloseAllSteps(TestStatus.Passed);
                return;
            }

            error = Unwrap(error);
            switch (error)
            {
                case AutoTestSkipException skip:
                    ctx.CloseAllSteps(TestStatus.Skipped);
                    result.status = TestStatus.Skipped;
                    result.message = skip.Message;
                    return;
                case OperationCanceledException when userCancelled:
                    ctx.CloseAllSteps(TestStatus.Cancelled);
                    result.status = TestStatus.Cancelled;
                    result.message = "Người dùng dừng lượt chạy.";
                    return;
                case AutoTestAssertException assert:
                    MarkFailedStep(ctx, result);
                    result.message = assert.Message;
                    result.issues.Add(new TestIssue
                    {
                        severity = assert.Severity,
                        category = "Assert",
                        title = assert.Message,
                        expected = assert.Expected,
                        actual = assert.Actual,
                        steps = ctx.DescribeSteps(),
                        stackTrace = ShortStack(assert.StackTrace)
                    });
                    result.status = TestStatus.Failed;
                    return;
                default:
                    MarkFailedStep(ctx, result);
                    result.message = $"{error.GetType().Name}: {error.Message}";
                    result.issues.Add(new TestIssue
                    {
                        severity = Severity.Critical,
                        category = "Exception",
                        title = $"Exception khi chạy case: {error.GetType().Name}",
                        message = error.Message,
                        steps = ctx.DescribeSteps(),
                        stackTrace = ShortStack(error.StackTrace)
                    });
                    result.status = TestStatus.Error;
                    return;
            }
        }

        static void MarkFailedStep(AutoTestContext ctx, TestCaseResult result)
        {
            ctx.CloseAllSteps(TestStatus.Failed);
            // using(ctx.Step) không biết có exception bay qua → đánh bước cuối cùng là bước hỏng.
            var anyFailed = false;
            foreach (var s in result.steps)
                if (s.status == TestStatus.Failed)
                    anyFailed = true;
            if (!anyFailed && result.steps.Count > 0) result.steps[result.steps.Count - 1].status = TestStatus.Failed;
        }

        static string LastStepName(TestCaseResult result)
        {
            return result.steps.Count > 0 ? result.steps[result.steps.Count - 1].name : null;
        }

        /// <summary>
        ///     Gắn log trong khoảng [<paramref name="fromCursor" />, <paramref name="toCursor" />) vào case + quy ra
        ///     issue theo policy. Case boot dùng để lấy log phát sinh trước khi case bắt đầu.
        /// </summary>
        public static void CollectLogs(TestCaseResult result, AutoTestConfig config, long fromCursor, long toCursor,
            double startTime, Severity? severityCap = null)
        {
            var entries = AutoTestLogCapture.Since(fromCursor);
            entries.RemoveAll(e => e.Index >= toCursor);
            var grouped = new Dictionary<string, (TestIssue issue, int count)>();
            var added = 0;
            // Lỗi log mà case đã tự gán cho thao tác cụ thể (button sweep, monkey…) thì không tạo issue Log.* trùng.
            var alreadyReported = new List<string>();
            foreach (var i in result.issues)
                alreadyReported.Add((i.title ?? "") + "\n" + (i.message ?? "") + "\n" + (i.stackTrace ?? ""));
            foreach (var e in entries)
            {
                var isError = AutoTestLogCapture.IsErrorType(e.Type);
                if (e.Type == LogType.Warning && !config.general.captureWarnings) continue;
                var ignored = AutoTestLogCapture.IsIgnored(e.Message);

                if (added < MAX_LOG_ENTRIES_PER_CASE)
                {
                    result.logs.Add(new TestLogEntry
                    {
                        t = e.Time < 0 ? 0 : (float)(e.Time - startTime),
                        type = e.Type.ToString(),
                        message = ignored ? "(bỏ qua) " + e.Message : e.Message,
                        stack = isError ? ShortStack(e.Stack) : null
                    });
                    added++;
                }

                if (!isError || ignored) continue;
                var firstLine = FirstLine(e.Message);
                if (firstLine.Length > 0 && alreadyReported.Exists(t => t.Contains(firstLine))) continue;
                var policy = e.Type == LogType.Exception ? config.general.exceptionPolicy : config.general.errorLogPolicy;
                if (policy == LogErrorPolicy.Ignore) continue;

                var key = e.Type + "|" + FirstLine(e.Message);
                if (grouped.TryGetValue(key, out var existing))
                {
                    grouped[key] = (existing.issue, existing.count + 1);
                    continue;
                }

                var severity = policy == LogErrorPolicy.FailCase
                    ? e.Type == LogType.Exception ? Severity.Critical : Severity.Major
                    : Severity.Minor;
                if (severityCap.HasValue && severity > severityCap.Value) severity = severityCap.Value;
                var issue = new TestIssue
                {
                    severity = severity,
                    category = e.Type == LogType.Exception ? "Log.Exception" : "Log.Error",
                    title = (e.Type == LogType.Exception ? "Exception: " : "Error log: ") + Truncate(FirstLine(e.Message), 160),
                    message = e.Message,
                    location = GuessLocation(e.Stack),
                    stackTrace = ShortStack(e.Stack),
                    steps = DescribeStepsAt(result, e.Time - startTime)
                };
                result.issues.Add(issue);
                grouped[key] = (issue, 1);
            }

            foreach (var pair in grouped.Values)
                if (pair.count > 1)
                    pair.issue.message = $"(lặp {pair.count} lần) {pair.issue.message}";

            if (entries.Count > MAX_LOG_ENTRIES_PER_CASE)
                result.logs.Add(new TestLogEntry
                {
                    type = "Test",
                    message = $"… còn {entries.Count - MAX_LOG_ENTRIES_PER_CASE} dòng log bị lược bớt."
                });
        }

        static string DescribeStepsAt(TestCaseResult result, double tSeconds)
        {
            if (result.steps.Count == 0) return null;
            var sb = new StringBuilder();
            var i = 1;
            foreach (var s in result.steps)
            {
                if (s.startMs / 1000.0 > tSeconds + 0.001) break;
                sb.Append(i++).Append(". ").Append(s.name).Append('\n');
            }

            return sb.Length == 0 ? null : sb.ToString().TrimEnd();
        }

        /// <summary>Quy đổi trạng thái cuối từ issue (trừ khi đã Skipped/Cancelled/Error/Failed-timeout).</summary>
        public static void FinalizeStatus(TestCaseResult result, AutoTestConfig config)
        {
            if (result.status == TestStatus.Skipped || result.status == TestStatus.Cancelled ||
                result.status == TestStatus.Error)
                return;

            var max = result.MaxSeverity();
            var hasIssue = false;
            foreach (var i in result.issues)
                if (i.severity >= Severity.Minor)
                    hasIssue = true;

            if (result.issues.Count > 0 && max >= config.general.failSeverity) result.status = TestStatus.Failed;
            else if (result.status == TestStatus.Failed) result.status = TestStatus.Failed;
            else if (hasIssue) result.status = TestStatus.Warning;
            else result.status = TestStatus.Passed;

            if (string.IsNullOrEmpty(result.message) && result.issues.Count > 0)
            {
                var top = result.issues[0];
                foreach (var i in result.issues)
                    if (i.severity > top.severity)
                        top = i;
                result.message = result.issues.Count == 1 ? top.title : $"{result.issues.Count} vấn đề — nặng nhất: {top.title}";
            }
        }

        public static void AssignFingerprints(TestCaseResult result)
        {
            foreach (var issue in result.issues)
                if (string.IsNullOrEmpty(issue.id))
                    issue.id = Fingerprint(result.suiteId, result.caseId, issue.category, issue.title, issue.location,
                        issue.objectPath);
        }

        /// <summary>FNV-1a 64-bit — ổn định giữa các lượt chạy, máy và platform.</summary>
        public static string Fingerprint(params string[] parts)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            foreach (var p in parts)
            {
                var s = Normalize(p);
                foreach (var ch in s)
                {
                    hash ^= ch;
                    hash *= prime;
                }

                hash ^= '|';
                hash *= prime;
            }

            return hash.ToString("x16");
        }

        /// <summary>Bỏ số (id instance, địa chỉ bộ nhớ, thời gian) để fingerprint không đổi giữa các lượt.</summary>
        static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            var lastDigit = false;
            foreach (var ch in s)
            {
                if (char.IsDigit(ch))
                {
                    if (!lastDigit) sb.Append('#');
                    lastDigit = true;
                    continue;
                }

                lastDigit = false;
                sb.Append(ch);
            }

            return sb.ToString();
        }

        static Exception Unwrap(Exception e)
        {
            while (e is AggregateException agg && agg.InnerExceptions.Count == 1) e = agg.InnerExceptions[0];
            while (e is System.Reflection.TargetInvocationException tie && tie.InnerException != null)
                e = tie.InnerException;
            return e;
        }

        static void Observe(Task task)
        {
            if (task == null) return;
            task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }

        public static string ShortStack(string stack)
        {
            if (string.IsNullOrEmpty(stack)) return null;
            var lines = stack.Split('\n');
            var sb = new StringBuilder();
            var n = 0;
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd();
                if (line.Length == 0) continue;
                if (line.Contains("System.Runtime.CompilerServices") || line.Contains("System.Threading.Tasks") ||
                    line.Contains("UnityEngine.UnitySynchronizationContext") ||
                    line.Contains("Ezg.AutoTest.AutoTestExecutor"))
                    continue;
                sb.Append(line).Append('\n');
                if (++n >= MAX_STACK_LINES) break;
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>Lấy "file:dòng" đầu tiên thuộc code game trong stack trace.</summary>
        static string GuessLocation(string stack)
        {
            if (string.IsNullOrEmpty(stack)) return null;
            foreach (var raw in stack.Split('\n'))
            {
                var idx = raw.IndexOf("(at ", StringComparison.Ordinal);
                if (idx < 0) continue;
                var loc = raw.Substring(idx + 4).TrimEnd(')', ' ', '\r');
                if (loc.StartsWith("Assets/", StringComparison.Ordinal)) return loc;
            }

            return null;
        }

        static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var i = s.IndexOf('\n');
            return i < 0 ? s : s.Substring(0, i);
        }

        static string Truncate(string s, int max)
        {
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
