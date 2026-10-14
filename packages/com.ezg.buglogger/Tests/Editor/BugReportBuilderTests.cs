using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Ezg.BugLogger.Tests
{
    /// <summary>
    /// <c>report.json</c> schema 1 (BugHub contract §3.4): đủ field, escape, giới hạn từng trường, và các trần
    /// giữ cho Worker không trả 400/413.
    /// </summary>
    public class BugReportBuilderTests
    {
        #region Mirror types (JsonUtility đọc ngược report)

        [Serializable]
        private class ReportMirror
        {
            public int schema;
            public string id;
            public string kind;
            public string title;
            public string featureKey;
            public string category;
            public string reproduceRate;
            public string actual;
            public string expected;
            public string steps;
            public string description;
            public string[] tagIds;
            public string[] mentionIds;
            public string createdAtUtc;
            public AppMirror app;
            public DeviceMirror device;
            public PlayerMirror player;
        }

        [Serializable]
        private class AppMirror
        {
            public string version;
            public string build;
            public string commitSha;
            public string branch;
            public string platform;
            public string unityVersion;
        }

        [Serializable]
        private class DeviceMirror
        {
            public string id;
            public string model;
            public string os;
            public int ramMb;
            public string screen;
        }

        [Serializable]
        private class PlayerMirror
        {
            public string id;
        }

        #endregion

        private static readonly Regex UuidV4 = new Regex("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$");

        private static readonly string[] ContractKeys =
        {
            "\"schema\":1", "\"id\":", "\"kind\":", "\"title\":", "\"featureKey\":", "\"category\":", "\"reproduceRate\":",
            "\"actual\":", "\"expected\":", "\"steps\":", "\"description\":", "\"tagIds\":[", "\"mentionIds\":[", "\"createdAtUtc\":",
            "\"app\":{\"version\":", "\"build\":", "\"commitSha\":", "\"branch\":", "\"platform\":", "\"unityVersion\":",
            "\"device\":{\"id\":", "\"model\":", "\"os\":", "\"ramMb\":", "\"screen\":",
            "\"player\":{\"id\":", "\"extra\":{",
        };

        [Test]
        public void ReportId_IsLowercaseUuidV4()
        {
            for (var i = 0; i < 200; i++)
                Assert.IsTrue(UuidV4.IsMatch(BugReportBuilder.NewReportId()));
        }

        [Test]
        public void Report_HasEveryContractField()
        {
            var json = ReportText(BugReportBuilder.Build(NewCapture(), null));

            foreach (var key in ContractKeys)
                StringAssert.Contains(key, json);
        }

        [Test]
        public void Report_RoundTripsEveryField()
        {
            var capture = NewCapture();
            capture.Title = "Crash \"quoted\" \\ back";
            capture.Actual = "line1\nline2\ttab \u0001 ctrl — tiếng Việt 🐞";

            var report = Parse(BugReportBuilder.Build(capture, null));

            Assert.AreEqual(1, report.schema);
            Assert.AreEqual(capture.Id, report.id);
            Assert.AreEqual(BugReportBuilder.KIND_BUG, report.kind);
            Assert.AreEqual(capture.Title, report.title);
            Assert.AreEqual("HomeScreen", report.featureKey);
            Assert.AreEqual("Logic", report.category);
            Assert.AreEqual("3/10", report.reproduceRate);
            Assert.AreEqual(capture.Actual, report.actual);
            Assert.AreEqual("Expected", report.expected);
            Assert.AreEqual("Steps", report.steps);
            Assert.AreEqual("Description", report.description);
            CollectionAssert.AreEqual(new[] { "111", "222" }, report.tagIds);
            CollectionAssert.AreEqual(new[] { "999" }, report.mentionIds);
            Assert.AreEqual("2026-10-05T10:00:00.000Z", report.createdAtUtc);
            Assert.AreEqual("1.2.3", report.app.version);
            Assert.AreEqual("42", report.app.build);
            Assert.AreEqual("abc123", report.app.commitSha);
            Assert.AreEqual("develop", report.app.branch);
            Assert.AreEqual("Android", report.app.platform);
            Assert.AreEqual("device-1", report.device.id);
            Assert.AreEqual("Pixel 8", report.device.model);
            Assert.AreEqual(4096, report.device.ramMb);
            Assert.AreEqual("1080x2400", report.device.screen);
            Assert.AreEqual("player-1", report.player.id);
        }

        [Test]
        public void FeedbackKind_IsWritten()
        {
            var capture = NewCapture();
            capture.Kind = BugReportKind.Feedback;

            Assert.AreEqual(BugReportBuilder.KIND_FEEDBACK, Parse(BugReportBuilder.Build(capture, null)).kind);
        }

        [Test]
        public void Extra_IsEscapedMapWithScene()
        {
            var capture = NewCapture();
            capture.Extra.Add(new KeyValuePair<string, string>("stage", "12"));
            capture.Extra.Add(new KeyValuePair<string, string>("quote\"key", "line\nbreak"));

            var json = ReportText(BugReportBuilder.Build(capture, null));

            StringAssert.Contains("\"extra\":{\"stage\":\"12\",\"quote\\\"key\":\"line\\nbreak\",\"scene\":\"SplashScene\"}", json);
        }

        [Test]
        public void ControlCharacters_AreEscaped()
        {
            var capture = NewCapture();
            capture.Description = "a\u0001b\u001fc\u2028d";

            var json = ReportText(BugReportBuilder.Build(capture, null));

            StringAssert.Contains("a\\u0001b\\u001fc\\u2028d", json);
        }

        [Test]
        public void LongTextFields_AreClippedToContractLimits()
        {
            var capture = NewCapture();
            capture.Title = new string('t', 500);
            capture.Actual = new string('a', 5000);
            capture.Expected = new string('e', 5000);
            capture.Steps = new string('s', 5000);
            capture.Description = new string('d', 5000);
            capture.ReproduceRate = new string('r', 40);
            capture.FeatureKey = new string('f', 400);

            var report = Parse(BugReportBuilder.Build(capture, null));

            Assert.AreEqual(100, report.title.Length);
            Assert.AreEqual(4000, report.actual.Length);
            Assert.AreEqual(4000, report.expected.Length);
            Assert.AreEqual(4000, report.steps.Length);
            Assert.AreEqual(4000, report.description.Length);
            Assert.AreEqual(16, report.reproduceRate.Length);
            Assert.AreEqual(BugReportBuilder.MAX_SHORT_FIELD_LENGTH, report.featureKey.Length);
        }

        [Test]
        public void Title_IsTrimmedLikeTheWorker()
        {
            var capture = NewCapture();
            capture.Title = (char)0xFEFF + "  Shop crash \t";

            Assert.AreEqual("Shop crash", Parse(BugReportBuilder.Build(capture, null)).title);
        }

        [Test]
        public void Capture_DedupesAndCapsTagAndMentionIds()
        {
            var draft = NewDraft();
            draft.TagIds = new[] { "1", " 1 ", "", null, "2", "3", "4", "5" };
            draft.MentionIds = new List<string> { "a", "b", "c", "d", "e", "f" };

            var capture = BugReportBuilder.Capture(draft, null);

            CollectionAssert.AreEqual(new[] { "1", "2", "3", "4" }, capture.TagIds);
            CollectionAssert.AreEqual(new[] { "a", "b", "c", "d", "e" }, capture.MentionIds);
        }

        [Test]
        public void Capture_CopiesDraftAndReadsContext()
        {
            var draft = NewDraft();
            var screenshot = new byte[] { 1, 2, 3 };

            var capture = BugReportBuilder.Capture(draft, screenshot);

            Assert.IsTrue(UuidV4.IsMatch(capture.Id));
            Assert.AreEqual(draft.Title, capture.Title);
            Assert.AreEqual(draft.Actual, capture.Actual);
            Assert.AreSame(screenshot, capture.Screenshot);
            Assert.AreEqual(Application.unityVersion, capture.UnityVersion);
            Assert.IsNotEmpty(capture.Screen);
        }

        [Test]
        public void Truncate_NeverSplitsSurrogatePair()
        {
            var value = new string('a', 99) + "🐞";

            var result = BugReportBuilder.Truncate(value, 100);

            Assert.AreEqual(99, result.Length);
        }

        [Test]
        public void ExtraEntries_RespectContractLimits()
        {
            var capture = NewCapture();
            capture.Scene = string.Empty;
            capture.Extra.Add(new KeyValuePair<string, string>("long", new string('v', 400)));
            capture.Extra.Add(new KeyValuePair<string, string>(new string('K', 100), "long key"));
            capture.Extra.Add(new KeyValuePair<string, string>("", "empty key"));
            for (var i = 0; i < 60; i++)
                capture.Extra.Add(new KeyValuePair<string, string>($"k{i}", "v"));

            var json = ReportText(BugReportBuilder.Build(capture, null));

            Assert.AreEqual(BugReportBuilder.MAX_EXTRA_ENTRIES, CountExtraEntries(json));
            StringAssert.Contains(BugReportBuilder.NOTE_EXTRA, json);
            StringAssert.Contains(new string('v', 256), json);
            StringAssert.DoesNotContain(new string('v', 257), json);
            StringAssert.Contains("\"" + new string('K', 64) + "\":", json);
            StringAssert.DoesNotContain("empty key", json);
        }

        [Test]
        public void OversizedSave_IsDroppedAndNoted()
        {
            var save = new string('s', BugReportBuilder.MAX_SAVE_BYTES + 1);

            var files = BugReportBuilder.Build(NewCapture(), save);

            Assert.IsNull(files.Save);
            StringAssert.Contains(BugReportBuilder.NOTE_SAVE, ReportText(files));
        }

        [Test]
        public void SmallSave_IsKept()
        {
            var files = BugReportBuilder.Build(NewCapture(), "{\"gold\":1}");

            Assert.AreEqual("{\"gold\":1}", Encoding.UTF8.GetString(files.Save));
        }

        [Test]
        public void OversizedScreenshot_IsDroppedAndNoted()
        {
            var capture = NewCapture();
            capture.Screenshot = new byte[BugReportBuilder.MAX_SCREENSHOT_BYTES + 1];

            var files = BugReportBuilder.Build(capture, null);

            Assert.IsNull(files.Screenshot);
            StringAssert.Contains(BugReportBuilder.NOTE_SCREENSHOT, ReportText(files));
        }

        [Test]
        public void Log_IsClippedToItsTail()
        {
            var capture = NewCapture();
            var buffer = new BugLogRingBuffer(BugLogRingBuffer.DEFAULT_CAPACITY);
            for (var i = 0; i < BugLogRingBuffer.DEFAULT_CAPACITY; i++)
                buffer.Add($"{i:D3} " + new string('é', 4000), null, LogType.Log);
            capture.LogLines = buffer.TakeSnapshot();

            var files = BugReportBuilder.Build(capture, null);
            var log = Encoding.UTF8.GetString(files.Log);

            Assert.LessOrEqual(files.Log.Length, BugReportBuilder.MAX_LOG_BYTES);
            StringAssert.Contains("199 ", log);
            Assert.AreEqual(-1, log.IndexOf((char)0xFFFD), "log tail starts mid-character");
            StringAssert.Contains(BugReportBuilder.NOTE_LOG, ReportText(files));
        }

        [Test]
        public void ReportJson_WorstCase_StaysUnderWorkerCap()
        {
            // Mọi ký tự đều phải escape \uXXXX (6 byte) và mọi trường đều đầy tới giới hạn.
            const char ESCAPED = '\u0001';
            var capture = NewCapture();
            capture.Title = new string(ESCAPED, 500);
            capture.Actual = capture.Expected = capture.Steps = capture.Description = new string(ESCAPED, 5000);
            capture.ReproduceRate = new string(ESCAPED, 40);
            capture.FeatureKey = capture.Category = capture.AppVersion = capture.BuildNumber = capture.CommitSha =
                capture.Branch = capture.Platform = capture.UnityVersion = capture.DeviceId = capture.DeviceModel =
                    capture.OperatingSystem = capture.PlayerId = new string(ESCAPED, 400);
            capture.TagIds = NewIds(BugReportBuilder.MAX_TAG_IDS, ESCAPED);
            capture.MentionIds = NewIds(BugReportBuilder.MAX_MENTION_IDS, ESCAPED);
            for (var i = 0; i < 80; i++)
                capture.Extra.Add(new KeyValuePair<string, string>(i.ToString("D2") + new string(ESCAPED, 100), new string(ESCAPED, 400)));

            var files = BugReportBuilder.Build(capture, null);

            Assert.LessOrEqual(files.Report.Length, BugReportBuilder.MAX_REPORT_BYTES);
        }

        [Test]
        public void Validate_BugWithoutTitleOrActual_Throws()
        {
            var noTitle = NewDraft();
            noTitle.Title = " \uFEFF ";
            var noActual = NewDraft();
            noActual.Actual = null;

            Assert.Throws<ArgumentException>(() => BugReportBuilder.Validate(noTitle));
            Assert.Throws<ArgumentException>(() => BugReportBuilder.Validate(noActual));
            Assert.Throws<ArgumentNullException>(() => BugReportBuilder.Validate(null));
        }

        [Test]
        public void Validate_FeedbackWithoutTitle_IsAllowed()
        {
            var feedback = new BugReportDraft { Kind = BugReportKind.Feedback, Description = "Nice shop" };

            Assert.DoesNotThrow(() => BugReportBuilder.Validate(feedback));
            Assert.DoesNotThrow(() => BugReportBuilder.Validate(NewDraft()));
        }

        #region Helpers

        private static BugReportDraft NewDraft()
        {
            return new BugReportDraft
            {
                Kind = BugReportKind.Bug,
                Title = "Title",
                FeatureKey = "HomeScreen",
                Category = "Logic",
                ReproduceRate = "3/10",
                Actual = "Actual",
                Expected = "Expected",
                Steps = "Steps",
                Description = "Description",
            };
        }

        private static BugReportCapture NewCapture()
        {
            return new BugReportCapture
            {
                Id = BugReportBuilder.NewReportId(),
                Kind = BugReportKind.Bug,
                Title = "Title",
                FeatureKey = "HomeScreen",
                Category = "Logic",
                ReproduceRate = "3/10",
                Actual = "Actual",
                Expected = "Expected",
                Steps = "Steps",
                Description = "Description",
                TagIds = new[] { "111", "222" },
                MentionIds = new[] { "999" },
                CreatedAtUtc = "2026-10-05T10:00:00.000Z",
                AppVersion = "1.2.3",
                BuildNumber = "42",
                CommitSha = "abc123",
                Branch = "develop",
                Platform = "Android",
                UnityVersion = "6000.2.6f2",
                DeviceId = "device-1",
                DeviceModel = "Pixel 8",
                OperatingSystem = "Android OS 14",
                RamMb = 4096,
                Screen = "1080x2400",
                Scene = "SplashScene",
                PlayerId = "player-1",
                Extra = new List<KeyValuePair<string, string>>(),
                Notes = new List<KeyValuePair<string, string>>(),
            };
        }

        private static string[] NewIds(int count, char fill)
        {
            var ids = new string[count];
            for (var i = 0; i < count; i++)
                ids[i] = i + new string(fill, BugReportBuilder.MAX_ID_LENGTH - 1);

            return ids;
        }

        private static string ReportText(BugReportFiles files) => Encoding.UTF8.GetString(files.Report);

        private static ReportMirror Parse(BugReportFiles files) => JsonUtility.FromJson<ReportMirror>(ReportText(files));

        private static int CountExtraEntries(string json)
        {
            var start = json.IndexOf("\"extra\":{", StringComparison.Ordinal);
            var section = json.Substring(start);
            return Regex.Matches(section, "\":\"").Count;
        }

        #endregion
    }
}
