using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace Ezg.BugLogger.Tests
{
    /// <summary>Outbox trên disk: layout nguyên tử, thứ tự, trần, failed/ và sổ sách lần thử.</summary>
    public class BugReportOutboxTests
    {
        /// <summary>Mốc quá khứ cố định: thứ tự chỉ cần thời gian file tương đối.</summary>
        private static readonly DateTime PAST = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "BugLoggerOutboxTests_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            BugLoggerClock.Override = null;
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [Test]
        public void Enqueue_WritesEveryPartAndNoTempFiles()
        {
            var outbox = new BugReportOutbox(_root);
            var files = NewFiles();
            files.Screenshot = new byte[] { 1, 2, 3 };
            files.Log = Encoding.UTF8.GetBytes("log");
            files.Save = Encoding.UTF8.GetBytes("{}");

            outbox.Enqueue(files);

            var directory = outbox.GetReportDirectory(files.Id);
            Assert.AreEqual(Path.Combine(_root, BugReportOutbox.OUTBOX_FOLDER, files.Id), directory);
            Assert.IsTrue(File.Exists(Path.Combine(directory, BugReportOutbox.REPORT_FILE)));
            Assert.IsTrue(File.Exists(Path.Combine(directory, BugReportOutbox.SCREENSHOT_FILE)));
            Assert.IsTrue(File.Exists(Path.Combine(directory, BugReportOutbox.LOG_FILE)));
            Assert.IsTrue(File.Exists(Path.Combine(directory, BugReportOutbox.SAVE_FILE)));
            Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
            Assert.AreEqual(1, outbox.CountReady());
        }

        [Test]
        public void FolderWithoutReport_IsNotReady()
        {
            var outbox = new BugReportOutbox(_root);
            var incomplete = outbox.GetReportDirectory(BugReportBuilder.NewReportId());
            Directory.CreateDirectory(incomplete);
            File.WriteAllText(Path.Combine(incomplete, BugReportOutbox.LOG_FILE), "half written");

            Assert.AreEqual(0, outbox.CountReady());
            Assert.IsNull(outbox.PeekOldest(out _));
        }

        [Test]
        public void PeekOldest_ReturnsFirstQueued()
        {
            var outbox = new BugReportOutbox(_root);
            var first = NewFiles();
            var second = NewFiles();
            outbox.Enqueue(first);
            outbox.Enqueue(second);
            SetQueuedTime(outbox, first.Id, PAST.AddMinutes(-5));

            var oldest = outbox.PeekOldest(out var count);

            Assert.AreEqual(2, count);
            Assert.AreEqual(first.Id, oldest.Value.Id);
        }

        [Test]
        public void PeekOldest_SkipsIdsWaitingForRetry()
        {
            var outbox = new BugReportOutbox(_root);
            var first = NewFiles();
            var second = NewFiles();
            outbox.Enqueue(first);
            outbox.Enqueue(second);
            SetQueuedTime(outbox, first.Id, PAST.AddMinutes(-5));

            var next = outbox.PeekOldest(out var count, new HashSet<string> { first.Id });
            var none = outbox.PeekOldest(out var allCount, new HashSet<string> { first.Id, second.Id });

            Assert.AreEqual(2, count, "skipped reports still count as pending");
            Assert.AreEqual(second.Id, next.Value.Id);
            Assert.IsNull(none);
            Assert.AreEqual(2, allCount);
        }

        [Test]
        public void CountCap_DropsOldest()
        {
            var outbox = new BugReportOutbox(_root, 3);
            var ids = new string[5];
            for (var i = 0; i < ids.Length; i++)
            {
                var files = NewFiles();
                ids[i] = files.Id;
                outbox.Enqueue(files);
                SetQueuedTime(outbox, files.Id, PAST.AddMinutes(i - 10));
            }

            Assert.AreEqual(3, outbox.CountReady());
            Assert.IsFalse(Directory.Exists(outbox.GetReportDirectory(ids[0])));
            Assert.IsFalse(Directory.Exists(outbox.GetReportDirectory(ids[1])));
            Assert.IsTrue(Directory.Exists(outbox.GetReportDirectory(ids[4])));
        }

        [Test]
        public void ByteCap_DropsOldestButNeverTheNewReport()
        {
            var outbox = new BugReportOutbox(_root, BugReportOutbox.MAX_REPORTS, 2500);
            var old = NewFiles();
            old.Log = new byte[1000];
            outbox.Enqueue(old);
            SetQueuedTime(outbox, old.Id, PAST.AddMinutes(-5));

            var fresh = NewFiles();
            fresh.Log = new byte[2000];
            var dropped = outbox.Enqueue(fresh);

            Assert.AreEqual(1, dropped);
            Assert.IsFalse(Directory.Exists(outbox.GetReportDirectory(old.Id)));
            Assert.IsTrue(Directory.Exists(outbox.GetReportDirectory(fresh.Id)));
        }

        [Test]
        public void DefaultCaps_AreFiftyReportsAndFiftyMegabytes()
        {
            Assert.AreEqual(50, BugReportOutbox.MAX_REPORTS);
            Assert.AreEqual(50L * 1024 * 1024, BugReportOutbox.MAX_TOTAL_BYTES);
        }

        [Test]
        public void Load_ReturnsPartsOrNullWhenGone()
        {
            var outbox = new BugReportOutbox(_root);
            var files = NewFiles();
            files.Log = Encoding.UTF8.GetBytes("log");
            outbox.Enqueue(files);

            var payload = outbox.Load(files.Id);
            Assert.AreEqual(files.Id, payload.Id);
            CollectionAssert.AreEqual(files.Report, payload.Report);
            CollectionAssert.AreEqual(files.Log, payload.Log);
            Assert.IsNull(payload.Screenshot);
            Assert.IsNull(payload.Save);

            Assert.IsTrue(outbox.Exists(files.Id));
            Assert.IsTrue(outbox.Complete(files.Id));
            Assert.IsFalse(outbox.Exists(files.Id));
            Assert.IsNull(outbox.Load(files.Id));
            Assert.AreEqual(0, outbox.CountReady());
        }

        [Test]
        public void MoveToFailed_ParksReportOutsideTheQueue()
        {
            var outbox = new BugReportOutbox(_root);
            var files = NewFiles();
            outbox.Enqueue(files);

            Assert.IsTrue(outbox.MoveToFailed(files.Id, "HTTP 400 invalid_report/title", 400));

            Assert.AreEqual(0, outbox.CountReady());
            var failed = outbox.GetFailedDirectory(files.Id);
            Assert.AreEqual(Path.Combine(_root, BugReportOutbox.FAILED_FOLDER, files.Id), failed);
            Assert.IsTrue(File.Exists(Path.Combine(failed, BugReportOutbox.REPORT_FILE)));
            StringAssert.Contains("invalid_report", File.ReadAllText(Path.Combine(failed, BugReportOutbox.FAILURE_FILE)));
        }

        [Test]
        public void IncrementAttempt_PersistsAcrossInstances()
        {
            var files = NewFiles();
            new BugReportOutbox(_root).Enqueue(files);

            Assert.AreEqual(1, new BugReportOutbox(_root).IncrementAttempt(files.Id, "HTTP 503", out _));
            Assert.AreEqual(2, new BugReportOutbox(_root).IncrementAttempt(files.Id, "HTTP 503", out _));
            Assert.AreEqual(2, new BugReportOutbox(_root).GetAttemptCount(files.Id));
            Assert.AreEqual(1, new BugReportOutbox(_root).CountReady());
        }

        [Test]
        public void IncrementAttempt_KeepsTheFirstFailureTime()
        {
            var files = NewFiles();
            var outbox = new BugReportOutbox(_root);
            outbox.Enqueue(files);

            BugLoggerClock.Override = () => PAST;
            outbox.IncrementAttempt(files.Id, "HTTP 503", out var first);

            BugLoggerClock.Override = () => PAST.AddHours(25);
            outbox.IncrementAttempt(files.Id, "HTTP 503", out var stillFirst);

            Assert.AreEqual(PAST, first);
            Assert.AreEqual(PAST, stillFirst);
            Assert.IsTrue(BugBackoffPolicy.ShouldGiveUp(2, stillFirst, BugLoggerClock.UtcNow));
        }

        [Test]
        public void AttemptState_KilledMidRewrite_IsRecoveredFromTemp()
        {
            var files = NewFiles();
            var outbox = new BugReportOutbox(_root);
            outbox.Enqueue(files);
            outbox.IncrementAttempt(files.Id, "HTTP 503", out _);

            // Kill giữa "xoá attempt.json cũ" và "rename *.tmp": chỉ còn bản mới trong *.tmp.
            var attemptPath = Path.Combine(outbox.GetReportDirectory(files.Id), BugReportOutbox.ATTEMPT_FILE);
            File.Move(attemptPath, attemptPath + ".tmp");

            Assert.AreEqual(1, outbox.GetAttemptCount(files.Id));
            Assert.AreEqual(2, outbox.IncrementAttempt(files.Id, "HTTP 503", out _));
        }

        [Test]
        public void IncrementAttempt_OnMissingReport_ReturnsZero()
        {
            Assert.AreEqual(0, new BugReportOutbox(_root).IncrementAttempt(BugReportBuilder.NewReportId(), "x", out _));
        }

        #region Helpers

        private static BugReportFiles NewFiles()
        {
            var id = BugReportBuilder.NewReportId();
            return new BugReportFiles { Id = id, Report = Encoding.UTF8.GetBytes("{\"schema\":1,\"id\":\"" + id + "\"}") };
        }

        private static void SetQueuedTime(BugReportOutbox outbox, string id, DateTime utc)
        {
            File.SetLastWriteTimeUtc(Path.Combine(outbox.GetReportDirectory(id), BugReportOutbox.REPORT_FILE), utc);
        }

        #endregion
    }
}
