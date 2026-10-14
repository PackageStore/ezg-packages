using System;
using System.IO;
using NUnit.Framework;

namespace Ezg.BugLogger.Tests
{
    /// <summary>Lựa chọn form (BugHub contract §3.3): đọc body server, cache disk, rơi về cache rồi mặc định.</summary>
    public class BugFormOptionsClientTests
    {
        private const string CODE = "SM007";

        private const string SERVER_BODY =
            "{\"schema\":1,\"code\":\"SM007\",\"intakeEnabled\":true,\"categories\":[\"Visual\",\"Logic\"]," +
            "\"tags\":[{\"id\":\"1234\",\"name\":\"UI\"},{\"id\":\"\",\"name\":\"no id\"},{\"id\":\"1234\",\"name\":\"dup\"}]," +
            "\"people\":[{\"id\":\"discord-1\",\"name\":\"AnhNT\"}]," +
            "\"limits\":{\"titleMax\":80,\"fieldMax\":9000,\"maxTags\":4,\"maxMentions\":5,\"screenshotMaxBytes\":3145728}}";

        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "BugLoggerFormTests_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [Test]
        public void ServerBody_IsParsedAndCached()
        {
            var cachePath = BugFormOptionsClient.GetCachePath(_root, CODE);

            var options = BugFormOptionsClient.Resolve(SERVER_BODY, cachePath, CODE);

            Assert.AreEqual(BugFormOptionsSource.Server, options.Source);
            Assert.AreEqual(1, options.Schema);
            Assert.AreEqual("SM007", options.Code);
            Assert.IsTrue(options.IntakeEnabled);
            CollectionAssert.AreEqual(new[] { "Visual", "Logic" }, options.Categories);
            Assert.AreEqual(1, options.Tags.Count);
            Assert.AreEqual("1234", options.Tags[0].Id);
            Assert.AreEqual("UI", options.Tags[0].Name);
            Assert.AreEqual("discord-1", options.People[0].Id);
            Assert.IsTrue(File.Exists(cachePath));
        }

        [Test]
        public void ServerLimits_AreClampedToTheReportLimits()
        {
            var limits = BugFormOptionsClient.Parse(SERVER_BODY, BugFormOptionsSource.Server).Limits;

            Assert.AreEqual(80, limits.TitleMax);
            Assert.AreEqual(BugReportBuilder.MAX_FIELD_LENGTH, limits.FieldMax);
            Assert.AreEqual(4, limits.MaxTags);
            Assert.AreEqual(5, limits.MaxMentions);
            Assert.AreEqual(3145728, limits.ScreenshotMaxBytes);
        }

        [Test]
        public void ServerFailure_FallsBackToCache()
        {
            var cachePath = BugFormOptionsClient.GetCachePath(_root, CODE);
            BugFormOptionsClient.Resolve(SERVER_BODY, cachePath, CODE);

            var options = BugFormOptionsClient.Resolve(null, cachePath, CODE);

            Assert.AreEqual(BugFormOptionsSource.Cache, options.Source);
            Assert.AreEqual("1234", options.Tags[0].Id);
        }

        [Test]
        public void ErrorBody_IsNotAFormAndDoesNotOverwriteCache()
        {
            var cachePath = BugFormOptionsClient.GetCachePath(_root, CODE);
            BugFormOptionsClient.Resolve(SERVER_BODY, cachePath, CODE);

            var options = BugFormOptionsClient.Resolve("{\"error\":\"unknown_project\"}", cachePath, CODE);

            Assert.AreEqual(BugFormOptionsSource.Cache, options.Source);
            StringAssert.Contains("1234", File.ReadAllText(cachePath));
        }

        [Test]
        public void NoServerNoCache_FallsBackToDefaults()
        {
            var options = BugFormOptionsClient.Resolve(null, BugFormOptionsClient.GetCachePath(_root, CODE), CODE);

            AssertDefaults(options);
        }

        [Test]
        public void CorruptCache_FallsBackToDefaults()
        {
            var cachePath = BugFormOptionsClient.GetCachePath(_root, CODE);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
            File.WriteAllText(cachePath, "{ not json");

            AssertDefaults(BugFormOptionsClient.Resolve(string.Empty, cachePath, CODE));
        }

        [Test]
        public void MissingCategories_UseDefaultsButEmptyListStaysEmpty()
        {
            var missing = BugFormOptionsClient.Parse("{\"schema\":1}", BugFormOptionsSource.Server);
            var empty = BugFormOptionsClient.Parse("{\"schema\":1,\"categories\":[]}", BugFormOptionsSource.Server);

            CollectionAssert.AreEqual(new[] { "Visual", "Logic", "GD" }, missing.Categories);
            Assert.IsEmpty(empty.Categories);
        }

        [Test]
        public void CachePath_IsUpperCaseCodeUnderFormFolder()
        {
            Assert.AreEqual(Path.Combine(_root, BugFormOptionsClient.FORM_FOLDER, "SM007.json"), BugFormOptionsClient.GetCachePath(_root, " sm007 "));
            Assert.AreEqual(Path.Combine(_root, BugFormOptionsClient.FORM_FOLDER, "A_B.json"), BugFormOptionsClient.GetCachePath(_root, "a/b"));
            Assert.IsNull(BugFormOptionsClient.GetCachePath(_root, ""));
        }

        [Test]
        public void BuildUrl_EscapesProjectCode()
        {
            Assert.AreEqual("https://hub.dev/v1/projects/SM007/form", BugFormOptionsClient.BuildUrl("https://hub.dev/", "SM007"));
            Assert.AreEqual("https://hub.dev/v1/projects/a%2Fb/form", BugFormOptionsClient.BuildUrl("https://hub.dev", "a/b"));
        }

        private static void AssertDefaults(BugFormOptions options)
        {
            Assert.AreEqual(BugFormOptionsSource.Default, options.Source);
            CollectionAssert.AreEqual(new[] { "Visual", "Logic", "GD" }, options.Categories);
            Assert.IsEmpty(options.Tags);
            Assert.IsEmpty(options.People);
            Assert.AreEqual(100, options.Limits.TitleMax);
            Assert.AreEqual(4000, options.Limits.FieldMax);
            Assert.AreEqual(4, options.Limits.MaxTags);
            Assert.AreEqual(5, options.Limits.MaxMentions);
            Assert.AreEqual(3145728, options.Limits.ScreenshotMaxBytes);
        }
    }
}
