using System;
using System.Linq;

namespace Ezg.BugLogger
{
    /// <summary>
    /// Luật thử lại sau lỗi tạm: chờ 5 s × 2^(n-1), trần 5 phút, và không ít hơn <c>Retry-After</c> của
    /// server; bỏ cuộc một report sau <see cref="MAX_ATTEMPTS"/> lần thử hoặc <see cref="GIVE_UP_HOURS"/> giờ
    /// kể từ lần thử thất bại đầu tiên.
    /// </summary>
    internal static class BugBackoffPolicy
    {
        #region Fields

        public const int BASE_DELAY_SECONDS = 5;
        public const int MAX_DELAY_SECONDS = 300;
        public const int MAX_ATTEMPTS = 50;
        public const int GIVE_UP_HOURS = 24;

        /// <summary>2^7 × 5 s đã vượt trần; chặn số mũ để phép dịch không tràn.</summary>
        private const int MAX_EXPONENT = 16;

        #endregion

        #region Public

        /// <summary>Thời gian chờ trước khi thử lại report đã thất bại <paramref name="failedAttempts"/> lần (1 → 5 s, 2 → 10 s, 3 → 20 s …).</summary>
        public static int GetDelaySeconds(int failedAttempts)
        {
            var exponent = Math.Min(Math.Max(failedAttempts - 1, 0), MAX_EXPONENT);
            return (int)Math.Min((long)BASE_DELAY_SECONDS << exponent, MAX_DELAY_SECONDS);
        }

        /// <summary>Như <see cref="GetDelaySeconds"/> nhưng không ngắn hơn <paramref name="retryAfterSeconds"/> server yêu cầu.</summary>
        public static int GetRetryDelaySeconds(int failedAttempts, int retryAfterSeconds)
        {
            return Math.Max(GetDelaySeconds(failedAttempts), Math.Max(0, retryAfterSeconds));
        }

        /// <summary>True khi report đã thử đủ <see cref="MAX_ATTEMPTS"/> lần hoặc đã quá <see cref="GIVE_UP_HOURS"/> giờ kể từ lần thất bại đầu.</summary>
        public static bool ShouldGiveUp(int failedAttempts, DateTime firstAttemptUtc, DateTime nowUtc)
        {
            return failedAttempts >= MAX_ATTEMPTS || nowUtc - firstAttemptUtc >= TimeSpan.FromHours(GIVE_UP_HOURS);
        }

        #endregion
    }
}
