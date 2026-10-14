using NUnit.Framework;
using UnityEngine;

namespace Ezg.BugLogger.Tests
{
    /// <summary>Log ring buffer: giữ các dòng mới nhất theo thứ tự, stack trace chỉ cho exception.</summary>
    public class BugLogRingBufferTests
    {
        [Test]
        public void Overflow_KeepsNewestLinesOldestFirst()
        {
            var buffer = new BugLogRingBuffer(3);
            for (var i = 0; i < 5; i++)
                buffer.Add($"line{i}", null, LogType.Log);

            var snapshot = buffer.TakeSnapshot();

            Assert.AreEqual(3, snapshot.Length);
            Assert.AreEqual("line2", snapshot[0].Condition);
            Assert.AreEqual("line4", snapshot[2].Condition);
        }

        [Test]
        public void SharedBuffer_HoldsTwoHundredLines()
        {
            var buffer = new BugLogRingBuffer(BugLogRingBuffer.DEFAULT_CAPACITY);
            for (var i = 0; i < 250; i++)
                buffer.Add("x", null, LogType.Log);

            Assert.AreEqual(200, buffer.Count);
        }

        [Test]
        public void StackTrace_KeptForExceptionsOnly()
        {
            var buffer = new BugLogRingBuffer(4);
            buffer.Add("info", "info stack", LogType.Log);
            buffer.Add("boom", "Foo.Bar ()\nBaz.Qux ()", LogType.Exception);

            var text = BugLogRingBuffer.Format(buffer.TakeSnapshot());

            StringAssert.DoesNotContain("info stack", text);
            StringAssert.Contains("[X] boom", text);
            StringAssert.Contains("    Foo.Bar ()", text);
            StringAssert.Contains("    Baz.Qux ()", text);
        }

        [Test]
        public void Format_TagsEachLevel()
        {
            var buffer = new BugLogRingBuffer(4);
            buffer.Add("a", null, LogType.Log);
            buffer.Add("b", null, LogType.Warning);
            buffer.Add("c", null, LogType.Error);

            var text = BugLogRingBuffer.Format(buffer.TakeSnapshot());

            StringAssert.Contains("[I] a", text);
            StringAssert.Contains("[W] b", text);
            StringAssert.Contains("[E] c", text);
        }

        [Test]
        public void Clear_DropsEveryLine()
        {
            var buffer = new BugLogRingBuffer(4);
            buffer.Add("a", null, LogType.Log);
            buffer.Clear();

            Assert.AreEqual(0, buffer.Count);
            Assert.IsEmpty(buffer.TakeSnapshot());
        }

        [Test]
        public void Empty_FormatsToEmptyString()
        {
            Assert.AreEqual(string.Empty, BugLogRingBuffer.Format(new BugLogRingBuffer(2).TakeSnapshot()));
        }
    }
}
