using UnityEngine;

namespace Ezg.BugLogger.UI
{
    /// <summary>
    ///     Bảng màu riêng của UI Bug Logger. Cố ý KHÔNG theo art style của game: form báo lỗi phải trông giống hệt
    ///     nhau ở mọi project EZG, và tách bạch khỏi UI game để QA không nhầm là một màn trong game.
    /// </summary>
    internal static class BugLoggerPalette
    {
        public static readonly Color Scrim = Hex(0x05070D, 0.74f);
        public static readonly Color Card = Hex(0x141924);
        public static readonly Color Surface = Hex(0x1D2431);
        public static readonly Color SurfaceRaised = Hex(0x252D3C);
        public static readonly Color Stroke = Hex(0x2D3647);
        public static readonly Color StrokeStrong = Hex(0x3B4659);
        public static readonly Color TextPrimary = Hex(0xF3F5FA);
        public static readonly Color TextSecondary = Hex(0xA4ADBF);
        public static readonly Color TextMuted = Hex(0x697387);
        public static readonly Color Accent = Hex(0xFF5A6E);
        public static readonly Color AccentSoft = Hex(0xFF5A6E, 0.16f);
        public static readonly Color OnAccent = Hex(0xFFFFFF);
        public static readonly Color Info = Hex(0x4D8DFF);
        public static readonly Color Success = Hex(0x2EC487);
        public static readonly Color Warning = Hex(0xF5B53D);
        public static readonly Color Danger = Hex(0xFF6B6B);
        public static readonly Color Shadow = Hex(0x000000, 0.55f);

        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static Color Hex(int rgb, float alpha = 1f)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);
        }
    }
}
