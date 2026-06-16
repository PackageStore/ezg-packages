using UnityEngine;

namespace Ezg.Core.Utils
{
    public class ColorUtils
    {
        #region Fields

        public const string Color1 = "#FFFFFF"; // trang
        public const string Color2 = "#A7B7E0"; // xanh duong nhat
        public const string Color3 = "#60FF00"; // xanh la cay
        public const string Color4 = "#7B7B7B"; // xam
        public const string Color5 = "#FE5151"; // do
        public const string Color6 = "#5E5E5E"; // hoa da
        public const string BLUE_GAME_STAT = "#684424";

        public const string Grey = "#D4D4D4";
        public const string Violet = "#F063F6";
        public const string Blue = "#00C5FF";
        public const string Yellow = "#9ef542";
        public const string Green = "#2FFF33";
        public const string GreenSoft = "#8cdb79";
        public const string GreySoft = "#E5E5E5";

        public const string ColorLockMap = "#808080";

        #endregion

        #region Public Methods

        /// <summary>
        ///     Wraps a given string content with a Unity rich text color tag using the specified hex color code.
        /// </summary>
        /// <param name="hexColor">The hexadecimal color code (e.g., "#FFFFFF").</param>
        /// <param name="content">The text content to be colored.</param>
        /// <returns>A string formatted with the rich text color tag if the hex is valid; otherwise, the original content.</returns>
        public static string GetStringWithColor(string hexColor, string content)
        {
            Color color;
            if (ColorUtility.TryParseHtmlString(hexColor, out color)) return $"<color={hexColor}>{content}</color>";

            return content;
        }

        /// <summary>
        ///     Converts a hexadecimal color string to a Unity Color object.
        /// </summary>
        /// <param name="hexColor">The hexadecimal color code (e.g., "#FFFFFF").</param>
        /// <returns>The parsed Color object, or Color.white if parsing fails.</returns>
        public static Color GetColorWithHex(string hexColor)
        {
            if (ColorUtility.TryParseHtmlString(hexColor, out var newCol))
                return newCol;

            Debug.Log("GetColorWithHex: Convert hex fail");
            return Color.white;
        }

        #endregion
    }
}