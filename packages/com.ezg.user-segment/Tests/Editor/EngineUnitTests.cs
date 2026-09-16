using System.Collections.Generic;
using Ezg.UserSegment.Engine;
using NUnit.Framework;

namespace Ezg.UserSegment.Tests
{
    public sealed class EngineUnitTests
    {
        [Test]
        public void Bucket_IsDeterministicAndInRange()
        {
            var a = HashUtil.Bucket("3f2a9c", "booster_when_frustrated_v1");
            var b = HashUtil.Bucket("3f2a9c", "booster_when_frustrated_v1");
            Assert.AreEqual(a, b);
            Assert.That(a, Is.InRange(0, 9999));
            Assert.AreNotEqual(a, HashUtil.Bucket("3f2a9c", "holdout"));
        }

        [Test]
        public void WindowCounter_RolloverShiftsBuckets()
        {
            var c = WindowCounter.Create(100);
            c.Add(3);
            c.Rollover(102);
            c.Add(1);
            Assert.AreEqual(1, c.Days[0]);
            Assert.AreEqual(3, c.Days[2]);
            Assert.AreEqual(4, c.Get(RefWindow.Days7));
            Assert.AreEqual(4, c.Life);
            c.Rollover(100); // ngược ngày: không shift
            Assert.AreEqual(102, c.HeadDay);
            c.Rollover(200); // > 30 ngày → xoá hết bucket, life giữ
            Assert.AreEqual(0, c.Get(RefWindow.Days30));
            Assert.AreEqual(4, c.Life);
        }

        [Test]
        public void ActionHistory_MergeUsesSeedRules()
        {
            var a = ActionHistory.Parse("{\"v\":1,\"uid\":\"u1\",\"actions\":{\"x\":{\"n\":1,\"f\":100,\"l\":100}}}");
            var b = ActionHistory.Parse("{\"v\":1,\"actions\":{\"x\":{\"n\":2,\"f\":50,\"l\":300},\"y\":{\"n\":1,\"f\":0,\"l\":10}}}");
            var m = ActionHistory.Merge(a, b);
            Assert.AreEqual("u1", m.Uid);
            Assert.AreEqual(2, m.Actions["x"].N);
            Assert.AreEqual(50, m.Actions["x"].F);
            Assert.AreEqual(300, m.Actions["x"].L);
            Assert.AreEqual(1, m.Actions["y"].N);
            Assert.IsNull(ActionHistory.Parse("not json"));
            Assert.IsNull(ActionHistory.Parse(""));
        }

        [Test]
        public void Truncate_CutsAtSeparator()
        {
            var parts = new List<string>();
            for (var i = 0; i < 30; i++) parts.Add("rule_" + i);
            var s = TrackingEmitter.Join(parts);
            var t = TrackingEmitter.Truncate(s);
            Assert.LessOrEqual(t.Length, 100);
            Assert.IsFalse(t.EndsWith(","));
            Assert.IsTrue(s.StartsWith(t));
            StringAssert.DoesNotContain("rule_1,rule_", t.Substring(t.LastIndexOf(',') + 1));
        }

        [Test]
        public void DefHash_IsStableAndCanonical()
        {
            var e1 = ConfigParser.ParseAst(Newtonsoft.Json.Linq.JToken.Parse("{\"op\":\"GT\",\"args\":[{\"ref\":\"feature.x\"},{\"value\":0.7}]}"));
            var e2 = ConfigParser.ParseAst(Newtonsoft.Json.Linq.JToken.Parse("{ \"args\": [ {\"ref\":\"feature.x\"}, {\"value\":0.70} ], \"op\":\"GT\" }"));
            Assert.AreEqual(DefHash.ForTag(e1, e1), DefHash.ForTag(e2, e2));
            Assert.AreEqual(16, DefHash.ForTag(e1, e1).Length);
            Assert.AreEqual(DefHash.ForRule(new[] { "SCREEN_OPEN:result", "PURCHASE" }, e1), DefHash.ForRule(new[] { "PURCHASE", "SCREEN_OPEN:result" }, e1));
        }

        [Test]
        public void Parser_RejectsUnknownFieldAndBadEnum()
        {
            var ex = Assert.Throws<ConfigException>(() => ConfigParser.Parse("{\"schema_version\":3,\"foo\":1}"));
            Assert.AreEqual(RejectReason.Schema, ex.Reason);
            ex = Assert.Throws<ConfigException>(() => ConfigParser.Parse("{\"schema_version\":2}"));
            Assert.AreEqual(RejectReason.Schema, ex.Reason);
            ex = Assert.Throws<ConfigException>(() => ConfigParser.Parse("{not json"));
            Assert.AreEqual(RejectReason.Parse, ex.Reason);
        }

        [Test]
        public void Semver_Compare()
        {
            Assert.Greater(ConfigValidator.CompareSemver("1.2.0", "1.0.9"), 0);
            Assert.AreEqual(0, ConfigValidator.CompareSemver("1.0.0", "1.0.0"));
            Assert.Less(ConfigValidator.CompareSemver("0.9.9", "1.0.0"), 0);
        }

        [Test]
        public void Trigger_OnEntryValidation()
        {
            Assert.IsTrue(Trigger.IsValidOnEntry("SESSION_START"));
            Assert.IsTrue(Trigger.IsValidOnEntry("SCREEN_OPEN:result"));
            Assert.IsFalse(Trigger.IsValidOnEntry("PROGRESS_START"));
            Assert.IsFalse(Trigger.IsValidOnEntry("PURCHASE:x"));
            Assert.IsFalse(Trigger.IsValidOnEntry("SCREEN_OPEN:Result"));
        }
    }
}
