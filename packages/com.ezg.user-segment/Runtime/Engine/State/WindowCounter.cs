using System;
using Newtonsoft.Json;

namespace Ezg.UserSegment.Engine
{
    /// <summary>
    ///     Counter có window — §C.4.2: life (lifetime, giá trị của ref không window; Seed ghi vào đây), session (reset tại
    ///     SESSION_START), days[0] = bucket ngày head_day, days[i] = head_day − i, luôn đúng 30 phần tử.
    /// </summary>
    public sealed class WindowCounter
    {
        public const int DAYS = 30;

        [JsonProperty("life")] public double Life;
        [JsonProperty("session")] public double Session;
        [JsonProperty("head_day")] public long HeadDay;
        [JsonProperty("days")] public double[] Days = new double[DAYS];

        public static WindowCounter Create(long headDay) => new WindowCounter { HeadDay = headDay };

        /// <summary>Đảm bảo đúng 30 phần tử sau khi deserialize fixture rút gọn.</summary>
        public void Normalize()
        {
            if (Days != null && Days.Length == DAYS) return;
            var d = new double[DAYS];
            if (Days != null) Array.Copy(Days, d, Math.Min(Days.Length, DAYS));
            Days = d;
        }

        public void Add(double x)
        {
            Life += x;
            Session += x;
            Days[0] += x;
        }

        /// <summary>Rollover trước reducer mỗi event — §C.12 mục 9. Ngược ngày: không shift, ghi vào days[0].</summary>
        public void Rollover(long day)
        {
            if (day <= HeadDay) return;
            var shift = (int)Math.Min(day - HeadDay, DAYS);
            if (shift >= DAYS)
            {
                Array.Clear(Days, 0, DAYS);
            }
            else
            {
                for (var i = DAYS - 1; i >= shift; i--) Days[i] = Days[i - shift];
                for (var i = 0; i < shift; i++) Days[i] = 0;
            }

            HeadDay = day;
        }

        public double Get(RefWindow w)
        {
            switch (w)
            {
                case RefWindow.Session: return Session;
                case RefWindow.Days7: return Sum(7);
                case RefWindow.Days30: return Sum(DAYS);
                default: return Life;
            }
        }

        private double Sum(int n)
        {
            double s = 0;
            for (var i = 0; i < n; i++) s += Days[i];
            return s;
        }
    }
}
