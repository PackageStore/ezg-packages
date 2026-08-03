using System;

namespace Ezg.Package.AdsManager
{
    /// <summary>
    /// Bitmask các format quảng cáo mà một project bật. Dùng để opt-in: project nào không dùng banner
    /// thì banner không được tạo, không đăng ký callback, không load — thay vì luôn khởi tạo mọi format.
    /// Khác với <see cref="AdFormat"/> (enum đơn trị, dùng để gắn nhãn 1 impression cho analytics).
    /// </summary>
    [Flags]
    public enum AdFormats
    {
        /// <summary> Không bật format nào. </summary>
        None = 0,

        /// <summary> Banner. </summary>
        Banner = 1 << 0,

        /// <summary> Interstitial (quảng cáo xen kẽ toàn màn hình). </summary>
        Interstitial = 1 << 1,

        /// <summary> Rewarded video. </summary>
        Rewarded = 1 << 2,

        /// <summary> MRec (banner chữ nhật cỡ trung). </summary>
        MRec = 1 << 3,

        /// <summary> Native ads. </summary>
        Native = 1 << 4,
    }
}
