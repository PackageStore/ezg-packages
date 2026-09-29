using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Mọi thứ một case / kịch bản cần: ghi bước, assert, ghi lỗi, chụp màn hình, đo số liệu, chờ,
    ///     điều khiển UI (<see cref="Ui" />) và game (<see cref="Game" />). Mỗi case có một context riêng.
    /// </summary>
    public sealed class AutoTestContext
    {
        readonly Stopwatch _watch = Stopwatch.StartNew();
        readonly Stack<TestStep> _openSteps = new();
        int _screenshotIndex;

        public AutoTestContext(AutoTestSession session, TestCaseResult result, CancellationToken token)
        {
            Session = session;
            Result = result;
            Token = token;
        }

        /// <summary>Phiên chạy (dùng chung giữa các case trong cùng một phiên Play).</summary>
        public AutoTestSession Session { get; }

        public TestCaseResult Result { get; }

        /// <summary>Huỷ khi user bấm Dừng hoặc case quá timeout — truyền vào mọi lệnh chờ tự viết.</summary>
        public CancellationToken Token { get; }

        public AutoTestConfig Config => Session.Config;
        public IGameAdapter Game => Session.Game;
        public IAutoTestProjectHooks Hooks => Session.Hooks;
        public UiDriver Ui => Session.Ui;
        public bool IsDevice => Session.IsDevice;

        /// <summary>Thời gian từ lúc case bắt đầu (ms).</summary>
        public double ElapsedMs => _watch.Elapsed.TotalMilliseconds;

        /// <summary>
        ///     Giới hạn severity của lỗi log tự bắt trong case này (vd case load scene ngoài luồng game → tối đa Minor).
        /// </summary>
        public Severity? LogSeverityCap { get; set; }

        /// <summary>Bước đang chạy (null nếu không có).</summary>
        public string CurrentStep => _openSteps.Count > 0 ? _openSteps.Peek().name : null;

        #region Ghi chép

        /// <summary>Ghi một dòng vào log của case (hiện trong report).</summary>
        public void Log(string message)
        {
            Result.logs.Add(new TestLogEntry
            {
                t = (float)(_watch.Elapsed.TotalSeconds),
                type = "Test",
                message = message
            });
            AutoTestLog.Info($"[{Result.caseId}] {message}");
        }

        /// <summary>
        ///     Mở một bước: <c>using (ctx.Step("Mở shop")) { ... }</c>. Bước ghi thời lượng + trạng thái; exception
        ///     bay qua sẽ đánh bước đó Lỗi.
        /// </summary>
        public IDisposable Step(string name, string detail = null)
        {
            var step = new TestStep
            {
                name = name,
                detail = detail,
                status = TestStatus.Running,
                startMs = ElapsedMs
            };
            Result.steps.Add(step);
            _openSteps.Push(step);
            Session.ReportProgress(Result, name);
            return new StepScope(this, step);
        }

        /// <summary>Chạy một bước async có tên.</summary>
        public async Task Step(string name, Func<Task> body)
        {
            using var scope = (StepScope)Step(name);
            try
            {
                await body();
            }
            catch
            {
                scope.MarkFailed();
                throw;
            }
        }

        internal void CloseStep(TestStep step, bool failed)
        {
            step.durationMs = ElapsedMs - step.startMs;
            if (step.status == TestStatus.Running)
                step.status = failed ? TestStatus.Failed : TestStatus.Passed;
            if (_openSteps.Count > 0 && _openSteps.Peek() == step) _openSteps.Pop();
        }

        /// <summary>Đánh mọi bước còn mở (khi case lỗi/dừng giữa chừng).</summary>
        internal void CloseAllSteps(TestStatus status)
        {
            while (_openSteps.Count > 0)
            {
                var step = _openSteps.Pop();
                step.durationMs = ElapsedMs - step.startMs;
                step.status = status;
            }
        }

        /// <summary>Ghi một lỗi (không dừng case). Trả về issue để bổ sung field.</summary>
        public TestIssue Report(Severity severity, string category, string title, string message = null,
            string location = null, string objectPath = null, string expected = null, string actual = null,
            string steps = null)
        {
            var issue = new TestIssue
            {
                severity = severity,
                category = category,
                title = title,
                message = message,
                location = location,
                objectPath = objectPath,
                expected = expected,
                actual = actual,
                steps = steps ?? DescribeSteps()
            };
            Result.issues.Add(issue);
            return issue;
        }

        /// <summary>Kiểm tra mềm: sai thì ghi issue và CHẠY TIẾP. Trả lại <paramref name="condition" />.</summary>
        public bool Check(bool condition, string title, string detail = null, Severity severity = Severity.Major,
            string location = null, string expected = null, string actual = null, string category = "Check")
        {
            if (!condition) Report(severity, category, title, detail, location, null, expected, actual);
            return condition;
        }

        /// <summary>Kiểm tra cứng: sai thì case dừng và chuyển Lỗi.</summary>
        public void Assert(bool condition, string message, Severity severity = Severity.Major)
        {
            if (!condition) throw new AutoTestAssertException(message, severity);
        }

        public void AreEqual<T>(T expected, T actual, string message, Severity severity = Severity.Major)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new AutoTestAssertException(message, severity, Format(expected), Format(actual));
        }

        public void AreApproximatelyEqual(double expected, double actual, string message, double epsilon = 0.0001,
            Severity severity = Severity.Major)
        {
            if (Math.Abs(expected - actual) > epsilon)
                throw new AutoTestAssertException(message, severity, Format(expected), Format(actual));
        }

        public void IsNotNull(object value, string message, Severity severity = Severity.Major)
        {
            // Object Unity bị Destroy vẫn khác null trong C# thuần → dùng so sánh của UnityEngine.Object.
            var isNull = value == null || (value is UnityEngine.Object uo && uo == null);
            if (isNull) throw new AutoTestAssertException(message, severity, "khác null", "null");
        }

        public void Fail(string message, Severity severity = Severity.Major)
        {
            throw new AutoTestAssertException(message, severity);
        }

        /// <summary>Bỏ qua case có lý do (thiếu điều kiện chạy).</summary>
        public void Skip(string reason)
        {
            throw new AutoTestSkipException(reason);
        }

        public void Warn(string title, string detail = null, string category = "Warning")
        {
            Report(Severity.Minor, category, title, detail);
        }

        public void Info(string title, string detail = null, string category = "Info")
        {
            Report(Severity.Info, category, title, detail);
        }

        /// <summary>Ghi số đo; vượt ngưỡng max/min ⇒ ghi issue theo <paramref name="severity" />.</summary>
        public TestMetric Metric(string name, double value, string unit = "", double? max = null, double? min = null,
            Severity severity = Severity.Major)
        {
            var metric = new TestMetric
            {
                name = name,
                value = Math.Round(value, 3),
                unit = unit,
                hasMax = max.HasValue,
                max = max ?? 0,
                hasMin = min.HasValue,
                min = min ?? 0
            };
            metric.passed = (!max.HasValue || value <= max.Value) && (!min.HasValue || value >= min.Value);
            Result.metrics.Add(metric);
            if (!metric.passed)
            {
                var bound = max.HasValue && value > max.Value ? $"≤ {Format(max.Value)}" : $"≥ {Format(min ?? 0)}";
                Report(severity, "Metric", $"{name} vượt ngưỡng", null, null, null, $"{bound} {unit}",
                    $"{Format(value)} {unit}");
            }

            return metric;
        }

        /// <summary>Đính kèm file văn bản (json, log, csv…) vào report.</summary>
        public string Attach(string fileName, string content, string label = null)
        {
            try
            {
                var ext = Path.GetExtension(fileName);
                var baseName = Sanitize(Path.GetFileNameWithoutExtension(fileName)) + ext;
                var rel = Path.Combine("attachments", Sanitize(Result.caseId) + "_" + baseName).Replace('\\', '/');
                var abs = Path.Combine(Session.OutputDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
                File.WriteAllText(abs, content ?? "");
                Result.attachments.Add(new TestAttachment
                    { label = label ?? fileName, path = rel, kind = GuessKind(fileName) });
                return rel;
            }
            catch (Exception e)
            {
                Debug.LogWarning(AutoTestLog.PREFIX + "Không ghi được file đính kèm: " + e.Message);
                return null;
            }
        }

        #endregion

        #region Chụp màn hình

        /// <summary>
        ///     Chụp Game view (Play mode) và đính kèm vào case. Trả đường dẫn tương đối hoặc null nếu không chụp
        ///     được (Edit mode, batchmode không có GPU, tắt trong settings).
        /// </summary>
        public async Task<string> Screenshot(string label, bool force = false)
        {
            if (!force && !Config.general.captureScreenshots) return null;
            if (!Application.isPlaying) return null;
            var tex = await AutoTestCapture.CaptureScreen(Token);
            if (tex == null) return null;
            try
            {
                _screenshotIndex++;
                var fileName =
                    $"{Sanitize(Result.suiteId)}_{Sanitize(Result.caseId)}_{_screenshotIndex:00}_{Sanitize(label)}.png";
                var rel = ("screenshots/" + fileName).Replace('\\', '/');
                var abs = Path.Combine(Session.OutputDir, rel);
                AutoTestCapture.SavePng(tex, abs, Config.general.screenshotScale);
                Result.attachments.Add(new TestAttachment { label = label, path = rel, kind = "image" });
                return rel;
            }
            finally
            {
                UnityEngine.Object.Destroy(tex);
            }
        }

        /// <summary>Chụp màn hình rồi gắn vào issue (bằng chứng cho QA).</summary>
        public async Task<TestIssue> ReportWithScreenshot(Severity severity, string category, string title,
            string message = null, string location = null, string objectPath = null)
        {
            var issue = Report(severity, category, title, message, location, objectPath);
            issue.screenshot = await Screenshot("issue_" + title);
            return issue;
        }

        #endregion

        #region Chờ

        public Task NextFrame()
        {
            return AutoTestClock.NextFrame(Token);
        }

        public Task WaitFrames(int frames)
        {
            return AutoTestClock.Frames(frames, Token);
        }

        public Task WaitSeconds(double seconds)
        {
            return AutoTestClock.Seconds(seconds, Token);
        }

        /// <summary>
        ///     Chờ tới khi điều kiện đúng. Hết giờ: <paramref name="failOnTimeout" /> = true ⇒ case Lỗi, ngược lại
        ///     trả false.
        /// </summary>
        public async Task<bool> WaitUntil(Func<bool> condition, double timeoutSeconds, string description,
            bool failOnTimeout = true, Severity severity = Severity.Major)
        {
            var ok = await AutoTestClock.Until(condition, timeoutSeconds, Token);
            if (!ok && failOnTimeout)
                throw new AutoTestAssertException($"Hết {timeoutSeconds:0.#}s chờ: {description}", severity);
            return ok;
        }

        public void ThrowIfCancelled()
        {
            Token.ThrowIfCancellationRequested();
        }

        #endregion

        #region Helpers

        /// <summary>Mô tả các bước đã đi qua — dùng làm "steps to reproduce" cho issue.</summary>
        public string DescribeSteps()
        {
            if (Result.steps.Count == 0) return null;
            var lines = new List<string>();
            var i = 1;
            foreach (var s in Result.steps) lines.Add($"{i++}. {s.name}");
            return string.Join("\n", lines);
        }

        public static string Format(object value)
        {
            switch (value)
            {
                case null: return "null";
                case double d: return d.ToString("0.####", CultureInfo.InvariantCulture);
                case float f: return f.ToString("0.####", CultureInfo.InvariantCulture);
                case IFormattable fm: return fm.ToString(null, CultureInfo.InvariantCulture);
                default: return value.ToString();
            }
        }

        public static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "x";
            var chars = s.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_')) chars[i] = '_';
            }

            var result = new string(chars);
            return result.Length > 60 ? result.Substring(0, 60) : result;
        }

        static string GuessKind(string fileName)
        {
            var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
            switch (ext)
            {
                case ".png":
                case ".jpg": return "image";
                case ".json": return "json";
                case ".txt":
                case ".log":
                case ".csv": return "text";
                default: return "other";
            }
        }

        sealed class StepScope : IDisposable
        {
            readonly AutoTestContext _ctx;
            readonly TestStep _step;
            bool _failed;
            bool _disposed;

            public StepScope(AutoTestContext ctx, TestStep step)
            {
                _ctx = ctx;
                _step = step;
            }

            public void MarkFailed()
            {
                _failed = true;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _ctx.CloseStep(_step, _failed);
            }
        }

        #endregion
    }
}
