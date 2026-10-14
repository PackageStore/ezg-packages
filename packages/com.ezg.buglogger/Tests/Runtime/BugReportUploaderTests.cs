using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace Ezg.BugLogger.Tests
{
    /// <summary>
    /// Vòng gửi với transport giả trên outbox tạm: đã nhận → xoá + ReportSent; 400 → failed/ + ReportFailed;
    /// lỗi tạm → giữ lại, backoff 5/10/20 s kể cả khi timeScale = 0, tôn trọng Retry-After; bỏ cuộc sau 50 lần /
    /// 24 h; chưa cấu hình → giữ tới khi được cấu hình. Mọi event trên main thread.
    /// </summary>
    public class BugReportUploaderTests
    {
        private const double TIMEOUT_SECONDS = 10;
        private const string FAKE_ENDPOINT = "https://fake.bughub.test";
        private const string PROJECT = "TEST";

        /// <summary>Thu backoff thật (5 s…) xuống mức mili giây để test chạy nhanh.</summary>
        private const float FAST_WAIT_SCALE = 0.002f;

        /// <summary>Retry-After 30 s → 0.5 s thật: đủ dài để kiểm Wake không cắt ngắn được.</summary>
        private const float RETRY_AFTER_WAIT_SCALE = 1f / 60f;

        private const double EARLY_CHECK_SECONDS = 0.2;
        private const double MIN_SCALED_RETRY_AFTER_SECONDS = 0.45;

        private sealed class FakeTransport : IBugReportTransport
        {
            public readonly List<BugReportSendResult> Results = new List<BugReportSendResult>();
            public readonly List<string> SentIds = new List<string>();

            /// <summary>Trả lời theo từng report; null = lấy lần lượt trong <see cref="Results"/>.</summary>
            public Func<BugReportPayload, BugReportSendResult> Respond;

            public int Calls;
            public BugReportPayload LastPayload;
            public BugReportTarget LastTarget;

            public UniTask<BugReportSendResult> SendAsync(BugReportPayload payload, BugReportTarget target, CancellationToken cancellationToken)
            {
                var result = Respond != null ? Respond(payload) : Results[Calls < Results.Count ? Calls : Results.Count - 1];
                Calls++;
                SentIds.Add(payload.Id);
                LastPayload = payload;
                LastTarget = target;
                return UniTask.FromResult(result);
            }
        }

        private string _root;
        private BugReportOutbox _outbox;
        private int _mainThreadId;
        private float _timeScale;

        [SetUp]
        public void SetUp()
        {
            BugLogger.Reset();
            _root = Path.Combine(Path.GetTempPath(), "BugLoggerUploaderTests_" + Guid.NewGuid().ToString("N"));
            _outbox = new BugReportOutbox(_root);
            BugReportOutbox.Default = _outbox;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            _timeScale = Time.timeScale;
        }

        [TearDown]
        public void TearDown()
        {
            BugLogger.Reset();
            BugLoggerClock.Override = null;
            BugReportOutbox.Default = null;
            Time.timeScale = _timeScale;
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [UnityTest]
        public IEnumerator Submit_WritesOutboxThenSendsOnMainThread() => UniTask.ToCoroutine(async () =>
        {
            var events = new List<string>();
            var isMainThread = true;
            var wasOnDisk = false;
            string id = null;
            var transport = NewTransport(new BugReportSendResult(BugReportSendStatus.Success, 201, 9, "u", "created"));
            BugLogger.ReportQueued += () =>
            {
                events.Add("queued");
                isMainThread &= Thread.CurrentThread.ManagedThreadId == _mainThreadId;
                wasOnDisk = File.Exists(Path.Combine(_outbox.GetReportDirectory(id), BugReportOutbox.REPORT_FILE));
            };
            BugLogger.ReportSent += number =>
            {
                events.Add("sent:" + number);
                isMainThread &= Thread.CurrentThread.ManagedThreadId == _mainThreadId;
            };
            BugLoggerConfig.Transport = transport;
            BugLogger.Initialize(FAKE_ENDPOINT, PROJECT);
            Debug.Log("[BugLoggerTest] line captured into log.txt");

            id = BugLogger.Submit(NewDraft(), new byte[] { 1, 2, 3 });
            await WaitFor(() => events.Count == 2 && !BugReportUploader.IsRunning);

            CollectionAssert.AreEqual(new[] { "queued", "sent:9" }, events);
            Assert.IsTrue(isMainThread, "events must be raised on the main thread");
            Assert.IsTrue(wasOnDisk, "report.json must exist when ReportQueued fires");
            Assert.AreEqual(id, transport.LastPayload.Id);
            Assert.AreEqual(PROJECT, transport.LastTarget.ProjectCode);
            StringAssert.Contains("\"kind\":\"bug\"", Encoding.UTF8.GetString(transport.LastPayload.Report));
            StringAssert.Contains("[BugLoggerTest] line captured", Encoding.UTF8.GetString(transport.LastPayload.Log));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, transport.LastPayload.Screenshot);
            Assert.IsFalse(Directory.Exists(_outbox.GetReportDirectory(id)), "folder is deleted once delivered");
            Assert.AreEqual(0, BugLogger.PendingCount);
        });

        [UnityTest]
        public IEnumerator Duplicate_IsTreatedAsDelivered() => UniTask.ToCoroutine(async () =>
        {
            Enqueue();
            var sentNumber = -1;
            BugLogger.ReportSent += number => sentNumber = number;
            BugLoggerConfig.Transport = NewTransport(new BugReportSendResult(BugReportSendStatus.Duplicate, 200, 5, "u", "duplicate"));

            BugLoggerConfig.Configure(FAKE_ENDPOINT, PROJECT);
            await WaitFor(() => sentNumber >= 0);

            Assert.AreEqual(5, sentNumber);
            Assert.AreEqual(0, _outbox.CountReady());
        });

        [UnityTest]
        public IEnumerator Rejected_MovesToFailedAndRaisesFailed() => UniTask.ToCoroutine(async () =>
        {
            var id = Enqueue();
            string failedId = null;
            BugLogger.ReportFailed += (reportId, _) => failedId = reportId;
            var transport = NewTransport(BugHubTransport.Classify(400, false, "{\"error\":\"invalid_report\",\"field\":\"title\"}"));
            LogAssert.Expect(LogType.Error, new Regex("rejected"));

            BugLoggerConfig.Transport = transport;
            BugLoggerConfig.Configure(FAKE_ENDPOINT, PROJECT);
            await WaitFor(() => failedId != null && !BugReportUploader.IsRunning);

            Assert.AreEqual(id, failedId);
            Assert.AreEqual(1, transport.Calls);
            Assert.AreEqual(0, _outbox.CountReady());
            Assert.IsTrue(File.Exists(Path.Combine(_outbox.GetFailedDirectory(id), BugReportOutbox.REPORT_FILE)));
        });

        [UnityTest]
        public IEnumerator ServerError_BacksOff5Then10Then20EvenWhenPaused() => UniTask.ToCoroutine(async () =>
        {
            var id = Enqueue();
            var delays = new List<int>();
            var sent = false;
            var serverError = BugHubTransport.Classify(503, false, "{\"error\":\"unavailable\"}");
            var transport = NewTransport(serverError, serverError, serverError, new BugReportSendResult(BugReportSendStatus.Success, 201, 3, "u", "created"));
            BugReportUploader.WaitTimeScale = FAST_WAIT_SCALE;
            BugReportUploader.RetryScheduled += (_, seconds) => delays.Add(seconds);
            BugLogger.ReportSent += _ => sent = true;
            Time.timeScale = 0f;

            BugLoggerConfig.Transport = transport;
            BugLoggerConfig.Configure(FAKE_ENDPOINT, PROJECT);
            await WaitFor(() => sent && !BugReportUploader.IsRunning);

            CollectionAssert.AreEqual(new[] { 5, 10, 20 }, delays);
            Assert.AreEqual(4, transport.Calls);
            Assert.IsFalse(Directory.Exists(_outbox.GetReportDirectory(id)));
        });

        [UnityTest]
        public IEnumerator ServerError_OnOldestReport_DoesNotBlockNewerOnes() => UniTask.ToCoroutine(async () =>
        {
            // Report cũ server cứ trả 500 (vd. feedback vào project chỉ có forum) không được chặn bug mới phía sau.
            var stuck = Enqueue();
            var fresh = Enqueue();
            MakeOlder(stuck);
            var sentNumber = -1;
            var transport = new FakeTransport
            {
                Respond = payload => payload.Id == stuck
                    ? BugHubTransport.Classify(500, false, "{\"error\":\"internal\"}")
                    : new BugReportSendResult(BugReportSendStatus.Success, 201, 8, "u", "created"),
            };
            BugLogger.ReportSent += number => sentNumber = number;

            BugLoggerConfig.Transport = transport;
            BugLoggerConfig.Configure(FAKE_ENDPOINT, PROJECT);
            await WaitFor(() => sentNumber >= 0);

            Assert.AreEqual(8, sentNumber);
            CollectionAssert.AreEqual(new[] { stuck, fresh }, transport.SentIds, "oldest first, then moves on");
            Assert.IsFalse(Directory.Exists(_outbox.GetReportDirectory(fresh)));
            Assert.AreEqual(1, _outbox.CountReady());
            Assert.AreEqual(1, _outbox.GetAttemptCount(stuck));
            Assert.IsTrue(BugReportUploader.IsRunning, "still waiting to retry the stuck report");
        });

        [UnityTest]
        public IEnumerator NetworkError_HoldsWholeQueue() => UniTask.ToCoroutine(async () =>
        {
            // Không có response: report sau cũng sẽ hỏng y vậy — chờ chung, không đốt lượt thử của cả outbox.
            var first = Enqueue();
            Enqueue();
            MakeOlder(first);
            var transport = NewTransport(BugReportSendResult.NetworkError("timeout"));

            BugLoggerConfig.Transport = transport;
            BugLoggerConfig.Configure(FAKE_ENDPOINT, PROJECT);
            await WaitFor(() => transport.Calls == 1);
            await UniTask.Delay(TimeSpan.FromSeconds(EARLY_CHECK_SECONDS), DelayType.Realtime);

            Assert.AreEqual(1, transport.Calls);
            Assert.AreEqual(2, _outbox.CountReady());
        });

        [UnityTest]
        public IEnumerator IntakeDisabled_KeepsReportAndCountsAttempt() => UniTask.ToCoroutine(async () =>
        {
            var id = Enqueue();
            var transport = NewTransport(BugHubTransport.Classify(403, false, "{\"error\":\"intake_disabled\"}"));

            BugLoggerConfig.Transport = transport;
            BugLoggerConfig.Configure(FAKE_ENDPOINT, PROJECT);
            await WaitFor(() => transport.Calls == 1 && _outbox.GetAttemptCount(id) == 1);

            Assert.AreEqual(1, _outbox.CountReady());
            Assert.IsFalse(Directory.Exists(_outbox.GetFailedDirectory(id)));
            Assert.IsTrue(BugReportUploader.IsRunning, "waiting for the retry, not stopped");
        });

        [UnityTest]
        public IEnumerator RateLimited_WaitsAtLeastRetryAfter() => UniTask.ToCoroutine(async () =>
        {
            Enqueue();
            var delays = new List<int>();
            var sent = false;
            var transport = NewTransport(
                BugHubTransport.Classify(429, false, "{\"error\":\"rate_limited\"}", null, "30"),
                new BugReportSendResult(BugReportSendStatus.Success, 201, 4, "u", "created"));
            BugReportUploader.WaitTimeScale = FAST_WAIT_SCALE;
            BugReportUploader.RetryScheduled += (_, seconds) => delays.Add(seconds);
            BugLogger.ReportSent += _ => sent = true;

            BugLoggerConfig.Transport = transport;
            BugLoggerConfig.Configure(FAKE_ENDPOINT, PROJECT);
            await WaitFor(() => sent);

            CollectionAssert.AreEqual(new[] { 30 }, delays);
        });

        [UnityTest]
        public IEnumerator RateLimited_WakeDoesNotCutRetryAfterShort() => UniTask.ToCoroutine(async () =>
        {
            Enqueue();
            var sent = false;
            var transport = NewTransport(
                BugHubTransport.Classify(429, false, "{\"error\":\"rate_limited\"}", null, "30"),
                new BugReportSendResult(BugReportSendStatus.Success, 201, 6, "u", "created"));
            BugReportUploader.WaitTimeScale = RETRY_AFTER_WAIT_SCALE;
            BugLogger.ReportSent += _ => sent = true;

            BugLoggerConfig.Transport = transport;
            BugLoggerConfig.Configure(FAKE_ENDPOINT, PROJECT);
            await WaitFor(() => transport.Calls == 1);
            var held = Stopwatch.StartNew();

            // Submit mới / app quay lại foreground trong lúc server bắt chờ.
            for (var i = 0; i < 3; i++)
            {
                BugReportUploader.Wake();
                await UniTask.Yield();
            }

            await UniTask.Delay(TimeSpan.FromSeconds(EARLY_CHECK_SECONDS), DelayType.Realtime);
            Assert.AreEqual(1, transport.Calls, "Wake must not cut the Retry-After wait short");

            await WaitFor(() => sent);
            Assert.GreaterOrEqual(held.Elapsed.TotalSeconds, MIN_SCALED_RETRY_AFTER_SECONDS);
        });

        [UnityTest]
        public IEnumerator GivesUp_AfterFiftyAttempts() => UniTask.ToCoroutine(async () =>
        {
            var id = Enqueue();
            for (var i = 0; i < BugBackoffPolicy.MAX_ATTEMPTS - 1; i++)
                _outbox.IncrementAttempt(id, "HTTP 503", out _);

            string failedReason = null;
            BugLogger.ReportFailed += (_, reason) => failedReason = reason;
            var transport = NewTransport(BugHubTransport.Classify(503, false, null));
            LogAssert.Expect(LogType.Error, new Regex("gave up after 50 attempts"));

            BugLoggerConfig.Transport = transport;
            BugLoggerConfig.Configure(FAKE_ENDPOINT, PROJECT);
            await WaitFor(() => failedReason != null && !BugReportUploader.IsRunning);

            Assert.AreEqual(1, transport.Calls);
            StringAssert.Contains("HTTP 503", failedReason);
            Assert.AreEqual(0, _outbox.CountReady());
            Assert.IsTrue(File.Exists(Path.Combine(_outbox.GetFailedDirectory(id), BugReportOutbox.FAILURE_FILE)));
        });

        [UnityTest]
        public IEnumerator GivesUp_TwentyFourHoursAfterFirstFailure() => UniTask.ToCoroutine(async () =>
        {
            var id = Enqueue();
            var now = BugLoggerClock.UtcNow;
            BugLoggerClock.Override = () => now.AddHours(-25);
            _outbox.IncrementAttempt(id, "network: offline", out _);
            BugLoggerClock.Override = null;

            string failedId = null;
            BugLogger.ReportFailed += (reportId, _) => failedId = reportId;
            LogAssert.Expect(LogType.Error, new Regex("gave up after 2 attempts"));

            BugLoggerConfig.Transport = NewTransport(BugReportSendResult.NetworkError("offline"));
            BugLoggerConfig.Configure(FAKE_ENDPOINT, PROJECT);
            await WaitFor(() => failedId != null && !BugReportUploader.IsRunning);

            Assert.AreEqual(id, failedId);
            Assert.AreEqual(0, _outbox.CountReady());
        });

        [UnityTest]
        public IEnumerator NotConfigured_HoldsReportUntilConfigured() => UniTask.ToCoroutine(async () =>
        {
            Enqueue();
            var transport = NewTransport(new BugReportSendResult(BugReportSendStatus.Success, 201, 1, "u", "created"));

            BugLoggerConfig.Transport = transport;
            BugLoggerConfig.Configure(FAKE_ENDPOINT, string.Empty);
            await WaitFor(() => !BugReportUploader.IsRunning);
            await UniTask.DelayFrame(3);

            Assert.AreEqual(0, transport.Calls);
            Assert.AreEqual(1, _outbox.CountReady());
            Assert.AreEqual(1, BugLogger.PendingCount);

            BugLoggerConfig.Configure(FAKE_ENDPOINT, PROJECT);
            await WaitFor(() => transport.Calls == 1 && !BugReportUploader.IsRunning);
            await UniTask.DelayFrame(3);

            Assert.AreEqual(1, transport.Calls);
            Assert.AreEqual(0, _outbox.CountReady());
        });

        [UnityTest]
        public IEnumerator FormOptions_UnreachableHost_ReturnsCacheOrDefaults() => UniTask.ToCoroutine(async () =>
        {
            BugLogger.Initialize("https://bughub.invalid", "SM007");

            var first = await BugLogger.GetFormOptionsAsync();

            Assert.AreEqual(BugFormOptionsSource.Default, first.Source);
            CollectionAssert.AreEqual(new[] { "Visual", "Logic", "GD" }, first.Categories);

            var cachePath = BugFormOptionsClient.GetCachePath(_outbox.RootDirectory, "SM007");
            BugFormOptionsClient.Resolve("{\"schema\":1,\"code\":\"SM007\",\"categories\":[\"Visual\"],\"tags\":[{\"id\":\"7\",\"name\":\"UI\"}]}", cachePath, "SM007");

            var second = await BugLogger.GetFormOptionsAsync();

            Assert.AreEqual(BugFormOptionsSource.Cache, second.Source);
            Assert.AreEqual("7", second.Tags[0].Id);
        });

        #region Helpers

        private static BugReportDraft NewDraft()
        {
            return new BugReportDraft
            {
                Title = "Shop purchase does nothing",
                FeatureKey = "Shop",
                Category = "Logic",
                Actual = "Tap Buy - nothing happens",
                Expected = "Purchase popup opens",
                TagIds = new[] { "1234" },
            };
        }

        private static FakeTransport NewTransport(params BugReportSendResult[] results)
        {
            var transport = new FakeTransport();
            transport.Results.AddRange(results);
            return transport;
        }

        private string Enqueue()
        {
            var id = BugReportBuilder.NewReportId();
            _outbox.Enqueue(new BugReportFiles { Id = id, Report = Encoding.UTF8.GetBytes("{\"schema\":1}") });
            return id;
        }

        /// <summary>Đẩy report về trước một phút để thứ tự cũ nhất không phụ thuộc id ngẫu nhiên.</summary>
        private void MakeOlder(string id)
        {
            var path = Path.Combine(_outbox.GetReportDirectory(id), BugReportOutbox.REPORT_FILE);
            File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(-1));
        }

        private static async UniTask WaitFor(Func<bool> condition)
        {
            await UniTask.WaitUntil(condition).Timeout(TimeSpan.FromSeconds(TIMEOUT_SECONDS), DelayType.Realtime);
        }

        #endregion
    }
}
