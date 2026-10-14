using System;

namespace Ezg.BugLogger
{
    /// <summary>
    /// Đồng hồ duy nhất của package — ngoại lệ có chủ đích của luật "dùng TimeManager": package không
    /// tham chiếu được code game, và đồng hồ game (sync server / bị cheat dời) sẽ làm sai tuổi folder
    /// outbox. Đây là giờ UTC của thiết bị, cùng đồng hồ file system dùng để ghi mtime, chỉ dùng cho
    /// metadata report (<c>createdAtUtc</c>, giờ dòng log, sổ sách outbox) — không bao giờ cho luật game.
    /// </summary>
    internal static class BugLoggerClock
    {
        /// <summary>Test thay giờ hiện tại (ví dụ nhảy 25 h để kiểm luật bỏ cuộc). Null = giờ thật.</summary>
        internal static Func<DateTime> Override;

        /// <summary>Giờ UTC hiện tại của thiết bị (hoặc của <see cref="Override"/>).</summary>
        public static DateTime UtcNow
        {
            get
            {
                var clock = Override;
                return clock != null ? clock() : DateTimeOffset.UtcNow.UtcDateTime;
            }
        }
    }
}
