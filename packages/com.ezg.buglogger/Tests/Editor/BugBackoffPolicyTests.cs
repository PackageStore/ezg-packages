using System;
using NUnit.Framework;

namespace Ezg.BugLogger.Tests
{
    /// <summary>Thời gian chờ thử lại 5 → 10 → 20 … trần 300 s, tôn trọng Retry-After, bỏ cuộc sau 50 lần / 24 h.</summary>
    public class BugBackoffPolicyTests
    {
        private static readonly DateTime FIRST_ATTEMPT = new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc);

        [TestCase(1, 5)]
        [TestCase(2, 10)]
        [TestCase(3, 20)]
        [TestCase(4, 40)]
        [TestCase(5, 80)]
        [TestCase(6, 160)]
        [TestCase(7, 300)]
        [TestCase(50, 300)]
        [TestCase(int.MaxValue, 300)]
        public void Delay_DoublesUntilCap(int failedAttempts, int expectedSeconds)
        {
            Assert.AreEqual(expectedSeconds, BugBackoffPolicy.GetDelaySeconds(failedAttempts));
        }

        [TestCase(0)]
        [TestCase(-3)]
        public void NonPositiveAttempts_UseBaseDelay(int failedAttempts)
        {
            Assert.AreEqual(BugBackoffPolicy.BASE_DELAY_SECONDS, BugBackoffPolicy.GetDelaySeconds(failedAttempts));
        }

        [Test]
        public void RetryAfter_LongerThanBackoff_Wins()
        {
            Assert.AreEqual(60, BugBackoffPolicy.GetRetryDelaySeconds(1, 60));
        }

        [Test]
        public void RetryAfter_ShorterThanBackoff_KeepsBackoff()
        {
            Assert.AreEqual(20, BugBackoffPolicy.GetRetryDelaySeconds(3, 2));
            Assert.AreEqual(5, BugBackoffPolicy.GetRetryDelaySeconds(1, 0));
        }

        [Test]
        public void GivesUp_AtFiftyAttempts()
        {
            Assert.IsFalse(BugBackoffPolicy.ShouldGiveUp(49, FIRST_ATTEMPT, FIRST_ATTEMPT.AddMinutes(30)));
            Assert.IsTrue(BugBackoffPolicy.ShouldGiveUp(50, FIRST_ATTEMPT, FIRST_ATTEMPT.AddMinutes(30)));
        }

        [Test]
        public void GivesUp_TwentyFourHoursAfterFirstFailure()
        {
            Assert.IsFalse(BugBackoffPolicy.ShouldGiveUp(3, FIRST_ATTEMPT, FIRST_ATTEMPT.AddHours(23.9)));
            Assert.IsTrue(BugBackoffPolicy.ShouldGiveUp(3, FIRST_ATTEMPT, FIRST_ATTEMPT.AddHours(24)));
        }
    }
}
