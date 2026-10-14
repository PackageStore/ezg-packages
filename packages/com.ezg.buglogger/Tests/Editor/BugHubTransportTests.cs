using System;
using NUnit.Framework;

namespace Ezg.BugLogger.Tests
{
    /// <summary>Phân loại response của Worker theo bảng BugHub contract §3.2.</summary>
    public class BugHubTransportTests
    {
        private static readonly DateTime NOW = new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Created_IsSuccessWithIssueNumber()
        {
            var result = BugHubTransport.Classify(201, false, "{\"number\":42,\"url\":\"https://x/42\",\"duplicate\":false}");

            Assert.AreEqual(BugReportSendStatus.Success, result.Status);
            Assert.AreEqual(42, result.Number);
            Assert.AreEqual("https://x/42", result.Url);
            Assert.IsTrue(result.IsDelivered);
        }

        [Test]
        public void DuplicateFlag_IsDuplicate()
        {
            var result = BugHubTransport.Classify(200, false, "{\"number\":7,\"url\":\"u\",\"duplicate\":true}");

            Assert.AreEqual(BugReportSendStatus.Duplicate, result.Status);
            Assert.AreEqual(7, result.Number);
            Assert.IsTrue(result.IsDelivered);
        }

        [Test]
        public void AcceptedFeedback_IsDeliveredWithoutIssue()
        {
            var result = BugHubTransport.Classify(202, false, "{\"accepted\":true,\"kind\":\"feedback\"}");

            Assert.AreEqual(BugReportSendStatus.Success, result.Status);
            Assert.AreEqual(0, result.Number);
            Assert.IsTrue(result.IsDelivered);
        }

        [Test]
        public void SuccessWithUnreadableBody_IsStillDelivered()
        {
            var result = BugHubTransport.Classify(201, false, "not json");

            Assert.AreEqual(BugReportSendStatus.Success, result.Status);
            Assert.AreEqual(0, result.Number);
        }

        [TestCase(400, "{\"error\":\"invalid_report\",\"field\":\"title\"}")]
        [TestCase(404, "{\"error\":\"unknown_project\"}")]
        [TestCase(413, "{\"error\":\"too_large\",\"part\":\"screenshot\"}")]
        [TestCase(422, null)]
        public void ClientErrors_ArePermanent(long statusCode, string body)
        {
            var result = BugHubTransport.Classify(statusCode, false, body);

            Assert.AreEqual(BugReportSendStatus.Permanent, result.Status);
            StringAssert.Contains(statusCode.ToString(), result.Reason);
        }

        [Test]
        public void IntakeDisabled_IsTransient()
        {
            var result = BugHubTransport.Classify(403, false, "{\"error\":\"intake_disabled\"}");

            Assert.AreEqual(BugReportSendStatus.Transient, result.Status);
            Assert.AreEqual("HTTP 403 intake_disabled", result.Reason);
        }

        [TestCase(429)]
        [TestCase(500)]
        [TestCase(502)]
        [TestCase(503)]
        public void RateLimitAndServerErrors_AreTransient(long statusCode)
        {
            Assert.AreEqual(BugReportSendStatus.Transient, BugHubTransport.Classify(statusCode, false, null).Status);
        }

        [Test]
        public void RateLimited_CarriesRetryAfter()
        {
            var result = BugHubTransport.Classify(429, false, "{\"error\":\"rate_limited\"}", null, "42");

            Assert.AreEqual(BugReportSendStatus.Transient, result.Status);
            Assert.AreEqual(42, result.RetryAfterSeconds);
            Assert.GreaterOrEqual(BugBackoffPolicy.GetRetryDelaySeconds(1, result.RetryAfterSeconds), 42);
        }

        [Test]
        public void RetryAfter_HttpDate_IsSecondsFromNow()
        {
            Assert.AreEqual(90, BugHubTransport.ParseRetryAfterSeconds("Tue, 06 Oct 2026 08:01:30 GMT", NOW));
        }

        [TestCase(null, 0)]
        [TestCase("", 0)]
        [TestCase("soon", 0)]
        [TestCase("-5", 0)]
        [TestCase("999999", BugHubTransport.MAX_RETRY_AFTER_SECONDS)]
        public void RetryAfter_IsClamped(string header, int expected)
        {
            Assert.AreEqual(expected, BugHubTransport.ParseRetryAfterSeconds(header, NOW));
        }

        [Test]
        public void NetworkErrorOrTimeout_IsTransient()
        {
            var result = BugHubTransport.Classify(0, true, null, "Request timeout");

            Assert.AreEqual(BugReportSendStatus.Transient, result.Status);
            StringAssert.Contains("Request timeout", result.Reason);
        }

        [Test]
        public void NoStatusCode_IsTransient()
        {
            Assert.AreEqual(BugReportSendStatus.Transient, BugHubTransport.Classify(0, false, null).Status);
        }

        [Test]
        public void Redirect_IsTransient()
        {
            Assert.AreEqual(BugReportSendStatus.Transient, BugHubTransport.Classify(301, false, null).Status);
        }

        [Test]
        public void BuildUrl_DoesNotDoubleSlash()
        {
            Assert.AreEqual("https://hub.dev/v1/reports", BugHubTransport.BuildUrl("https://hub.dev/"));
            Assert.AreEqual("https://hub.dev/v1/reports", BugHubTransport.BuildUrl("https://hub.dev"));
        }

        [Test]
        public void Target_NeedsEndpointAndProjectCode()
        {
            Assert.IsTrue(new BugReportTarget("https://hub.dev", "SM007").IsConfigured);
            Assert.IsFalse(new BugReportTarget("https://hub.dev", "").IsConfigured);
            Assert.IsFalse(new BugReportTarget(null, "SM007").IsConfigured);
        }
    }
}
