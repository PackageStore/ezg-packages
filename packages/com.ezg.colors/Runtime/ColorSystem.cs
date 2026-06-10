using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Ezg.Package.ColorSystem
{
    public static class ColorSystem
    {
        #region Fields

        public static List<ColorModel> _colorsDefault;

        public static List<ColorModel> ColorsDefault => _colorsDefault ?? (_colorsDefault = CreateColorsDefault());

        #endregion

        #region Public Methods

        /// <summary>
        ///     Creates the default list of color models.
        /// </summary>
        /// <returns>A list of default color models.</returns>
        public static List<ColorModel> CreateColorsDefault()
        {
            return new List<ColorModel>(148)
            {
                new() { Name = ColorEnum.AliceBlue, ClrHex = "F0F8FF" },
                new() { Name = ColorEnum.AntiqueWhite, ClrHex = "FAEBD7" },
                new() { Name = ColorEnum.Aqua, ClrHex = "00FFFF" },
                new() { Name = ColorEnum.Aquamarine, ClrHex = "7FFFD4" },
                new() { Name = ColorEnum.Azure, ClrHex = "F0FFFF" },
                new() { Name = ColorEnum.Beige, ClrHex = "F5F5DC" },
                new() { Name = ColorEnum.Bisque, ClrHex = "FFE4C4" },
                new() { Name = ColorEnum.Black, ClrHex = "000000" },
                new() { Name = ColorEnum.BlanchedAlmond, ClrHex = "FFEBCD" },
                new() { Name = ColorEnum.Blue, ClrHex = "0000FF" },
                new() { Name = ColorEnum.BlueViolet, ClrHex = "8A2BE2" },
                new() { Name = ColorEnum.Brown, ClrHex = "A52A2A" },
                new() { Name = ColorEnum.BurlyWood, ClrHex = "DEB887" },
                new() { Name = ColorEnum.CadetBlue, ClrHex = "5F9EA0" },
                new() { Name = ColorEnum.Chartreuse, ClrHex = "7FFF00" },
                new() { Name = ColorEnum.Chocolate, ClrHex = "D2691E" },
                new() { Name = ColorEnum.Coral, ClrHex = "FF7F50" },
                new() { Name = ColorEnum.CornflowerBlue, ClrHex = "6495ED" },
                new() { Name = ColorEnum.Cornsilk, ClrHex = "FFF8DC" },
                new() { Name = ColorEnum.Crimson, ClrHex = "DC143C" },
                new() { Name = ColorEnum.Cyan, ClrHex = "00FFFF" },
                new() { Name = ColorEnum.DarkBlue, ClrHex = "00008B" },
                new() { Name = ColorEnum.DarkCyan, ClrHex = "008B8B" },
                new() { Name = ColorEnum.DarkGoldenRod, ClrHex = "B8860B" },
                new() { Name = ColorEnum.DarkGray, ClrHex = "A9A9A9" },
                new() { Name = ColorEnum.DarkGrey, ClrHex = "A9A9A9" },
                new() { Name = ColorEnum.DarkGreen, ClrHex = "006400" },
                new() { Name = ColorEnum.DarkKhaki, ClrHex = "BDB76B" },
                new() { Name = ColorEnum.DarkMagenta, ClrHex = "8B008B" },
                new() { Name = ColorEnum.DarkOliveGreen, ClrHex = "556B2F" },
                new() { Name = ColorEnum.DarkOrange, ClrHex = "FF8C00" },
                new() { Name = ColorEnum.DarkOrchid, ClrHex = "9932CC" },
                new() { Name = ColorEnum.DarkRed, ClrHex = "8B0000" },
                new() { Name = ColorEnum.DarkSalmon, ClrHex = "E9967A" },
                new() { Name = ColorEnum.DarkSeaGreen, ClrHex = "8FBC8F" },
                new() { Name = ColorEnum.DarkSlateBlue, ClrHex = "483D8B" },
                new() { Name = ColorEnum.DarkSlateGray, ClrHex = "2F4F4F" },
                new() { Name = ColorEnum.DarkSlateGrey, ClrHex = "2F4F4F" },
                new() { Name = ColorEnum.DarkTurquoise, ClrHex = "00CED1" },
                new() { Name = ColorEnum.DarkViolet, ClrHex = "9400D3" },
                new() { Name = ColorEnum.DeepPink, ClrHex = "FF1493" },
                new() { Name = ColorEnum.DeepSkyBlue, ClrHex = "00BFFF" },
                new() { Name = ColorEnum.DimGray, ClrHex = "696969" },
                new() { Name = ColorEnum.DimGrey, ClrHex = "696969" },
                new() { Name = ColorEnum.DodgerBlue, ClrHex = "1E90FF" },
                new() { Name = ColorEnum.FireBrick, ClrHex = "B22222" },
                new() { Name = ColorEnum.FloralWhite, ClrHex = "FFFAF0" },
                new() { Name = ColorEnum.ForestGreen, ClrHex = "228B22" },
                new() { Name = ColorEnum.Fuchsia, ClrHex = "FF00FF" },
                new() { Name = ColorEnum.Gainsboro, ClrHex = "DCDCDC" },
                new() { Name = ColorEnum.GhostWhite, ClrHex = "F8F8FF" },
                new() { Name = ColorEnum.Gold, ClrHex = "FFD700" },
                new() { Name = ColorEnum.GoldenRod, ClrHex = "DAA520" },
                new() { Name = ColorEnum.Gray, ClrHex = "808080" },
                new() { Name = ColorEnum.Grey, ClrHex = "808080" },
                new() { Name = ColorEnum.Green, ClrHex = "008000" },
                new() { Name = ColorEnum.GreenYellow, ClrHex = "ADFF2F" },
                new() { Name = ColorEnum.HoneyDew, ClrHex = "F0FFF0" },
                new() { Name = ColorEnum.HotPink, ClrHex = "FF69B4" },
                new() { Name = ColorEnum.IndianRed, ClrHex = "CD5C5C" },
                new() { Name = ColorEnum.Indigo, ClrHex = "4B0082" },
                new() { Name = ColorEnum.Ivory, ClrHex = "FFFFF0" },
                new() { Name = ColorEnum.Khaki, ClrHex = "F0E68C" },
                new() { Name = ColorEnum.Lavender, ClrHex = "E6E6FA" },
                new() { Name = ColorEnum.LavenderBlush, ClrHex = "FFF0F5" },
                new() { Name = ColorEnum.LawnGreen, ClrHex = "7CFC00" },
                new() { Name = ColorEnum.LemonChiffon, ClrHex = "FFFACD" },
                new() { Name = ColorEnum.LightBlue, ClrHex = "ADD8E6" },
                new() { Name = ColorEnum.LightCoral, ClrHex = "F08080" },
                new() { Name = ColorEnum.LightCyan, ClrHex = "E0FFFF" },
                new() { Name = ColorEnum.LightGoldenRodYellow, ClrHex = "FAFAD2" },
                new() { Name = ColorEnum.LightGray, ClrHex = "D3D3D3" },
                new() { Name = ColorEnum.LightGrey, ClrHex = "D3D3D3" },
                new() { Name = ColorEnum.LightGreen, ClrHex = "90EE90" },
                new() { Name = ColorEnum.LightPink, ClrHex = "FFB6C1" },
                new() { Name = ColorEnum.LightSalmon, ClrHex = "FFA07A" },
                new() { Name = ColorEnum.LightSeaGreen, ClrHex = "20B2AA" },
                new() { Name = ColorEnum.LightSkyBlue, ClrHex = "87CEFA" },
                new() { Name = ColorEnum.LightSlateGray, ClrHex = "778899" },
                new() { Name = ColorEnum.LightSlateGrey, ClrHex = "778899" },
                new() { Name = ColorEnum.LightSteelBlue, ClrHex = "B0C4DE" },
                new() { Name = ColorEnum.LightYellow, ClrHex = "FFFFE0" },
                new() { Name = ColorEnum.Lime, ClrHex = "00FF00" },
                new() { Name = ColorEnum.LimeGreen, ClrHex = "32CD32" },
                new() { Name = ColorEnum.Linen, ClrHex = "FAF0E6" },
                new() { Name = ColorEnum.Magenta, ClrHex = "FF00FF" },
                new() { Name = ColorEnum.Maroon, ClrHex = "800000" },
                new() { Name = ColorEnum.MediumAquaMarine, ClrHex = "66CDAA" },
                new() { Name = ColorEnum.MediumBlue, ClrHex = "0000CD" },
                new() { Name = ColorEnum.MediumOrchid, ClrHex = "BA55D3" },
                new() { Name = ColorEnum.MediumPurple, ClrHex = "9370DB" },
                new() { Name = ColorEnum.MediumSeaGreen, ClrHex = "3CB371" },
                new() { Name = ColorEnum.MediumSlateBlue, ClrHex = "7B68EE" },
                new() { Name = ColorEnum.MediumSpringGreen, ClrHex = "00FA9A" },
                new() { Name = ColorEnum.MediumTurquoise, ClrHex = "48D1CC" },
                new() { Name = ColorEnum.MediumVioletRed, ClrHex = "C71585" },
                new() { Name = ColorEnum.MidnightBlue, ClrHex = "191970" },
                new() { Name = ColorEnum.MintCream, ClrHex = "F5FFFA" },
                new() { Name = ColorEnum.MistyRose, ClrHex = "FFE4E1" },
                new() { Name = ColorEnum.Moccasin, ClrHex = "FFE4B5" },
                new() { Name = ColorEnum.NavajoWhite, ClrHex = "FFDEAD" },
                new() { Name = ColorEnum.Navy, ClrHex = "000080" },
                new() { Name = ColorEnum.OldLace, ClrHex = "FDF5E6" },
                new() { Name = ColorEnum.Olive, ClrHex = "808000" },
                new() { Name = ColorEnum.OliveDrab, ClrHex = "6B8E23" },
                new() { Name = ColorEnum.Orange, ClrHex = "FFA500" },
                new() { Name = ColorEnum.OrangeRed, ClrHex = "FF4500" },
                new() { Name = ColorEnum.Orchid, ClrHex = "DA70D6" },
                new() { Name = ColorEnum.PaleGoldenRod, ClrHex = "EEE8AA" },
                new() { Name = ColorEnum.PaleGreen, ClrHex = "98FB98" },
                new() { Name = ColorEnum.PaleTurquoise, ClrHex = "AFEEEE" },
                new() { Name = ColorEnum.PaleVioletRed, ClrHex = "DB7093" },
                new() { Name = ColorEnum.PapayaWhip, ClrHex = "FFEFD5" },
                new() { Name = ColorEnum.PeachPuff, ClrHex = "FFDAB9" },
                new() { Name = ColorEnum.Peru, ClrHex = "CD853F" },
                new() { Name = ColorEnum.Pink, ClrHex = "FFC0CB" },
                new() { Name = ColorEnum.Plum, ClrHex = "DDA0DD" },
                new() { Name = ColorEnum.PowderBlue, ClrHex = "B0E0E6" },
                new() { Name = ColorEnum.Purple, ClrHex = "800080" },
                new() { Name = ColorEnum.RebeccaPurple, ClrHex = "663399" },
                new() { Name = ColorEnum.Red, ClrHex = "FF0000" },
                new() { Name = ColorEnum.RosyBrown, ClrHex = "BC8F8F" },
                new() { Name = ColorEnum.RoyalBlue, ClrHex = "4169E1" },
                new() { Name = ColorEnum.SaddleBrown, ClrHex = "8B4513" },
                new() { Name = ColorEnum.Salmon, ClrHex = "FA8072" },
                new() { Name = ColorEnum.SandyBrown, ClrHex = "F4A460" },
                new() { Name = ColorEnum.SeaGreen, ClrHex = "2E8B57" },
                new() { Name = ColorEnum.SeaShell, ClrHex = "FFF5EE" },
                new() { Name = ColorEnum.Sienna, ClrHex = "A0522D" },
                new() { Name = ColorEnum.Silver, ClrHex = "C0C0C0" },
                new() { Name = ColorEnum.SkyBlue, ClrHex = "87CEEB" },
                new() { Name = ColorEnum.SlateBlue, ClrHex = "6A5ACD" },
                new() { Name = ColorEnum.SlateGray, ClrHex = "708090" },
                new() { Name = ColorEnum.SlateGrey, ClrHex = "708090" },
                new() { Name = ColorEnum.Snow, ClrHex = "FFFAFA" },
                new() { Name = ColorEnum.SpringGreen, ClrHex = "00FF7F" },
                new() { Name = ColorEnum.SteelBlue, ClrHex = "4682B4" },
                new() { Name = ColorEnum.Tan, ClrHex = "D2B48C" },
                new() { Name = ColorEnum.Teal, ClrHex = "008080" },
                new() { Name = ColorEnum.Thistle, ClrHex = "D8BFD8" },
                new() { Name = ColorEnum.Tomato, ClrHex = "FF6347" },
                new() { Name = ColorEnum.Turquoise, ClrHex = "40E0D0" },
                new() { Name = ColorEnum.Violet, ClrHex = "EE82EE" },
                new() { Name = ColorEnum.Wheat, ClrHex = "F5DEB3" },
                new() { Name = ColorEnum.White, ClrHex = "FFFFFF" },
                new() { Name = ColorEnum.WhiteSmoke, ClrHex = "F5F5F5" },
                new() { Name = ColorEnum.Yellow, ClrHex = "FFFF00" },
                new() { Name = ColorEnum.YellowGreen, ClrHex = "9ACD32" }
            };
        }

        /// <summary>
        ///     Converts a Color32 color to its hexadecimal string representation.
        /// </summary>
        /// <param name="color">The color to convert.</param>
        /// <returns>The hexadecimal string representation of the color.</returns>
        public static string ColorToHex(this Color32 color)
        {
            var hex = color.r.ToString("X2") + color.g.ToString("X2") + color.b.ToString("X2");
            return hex;
        }

        /// <summary>
        ///     Converts a hexadecimal string to a Color.
        /// </summary>
        /// <param name="hex">The hexadecimal string to convert.</param>
        /// <returns>The converted Color.</returns>
        public static Color HexToColor(this string hex)
        {
            hex = hex.Replace("0x", ""); //in case the string is formatted 0xFFFFFF
            hex = hex.Replace("#", ""); //in case the string is formatted #FFFFFF
            byte a = 255; //assume fully visible unless specified in hex
            var r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
            var g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
            var b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
            //Only use alpha if the string has enough characters
            if (hex.Length == 8) a = byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber);
            return new Color32(r, g, b, a);
        }

        /// <summary>
        ///     Gets the Color32 representation of a ColorEnum.
        /// </summary>
        /// <param name="color">The color enum value.</param>
        /// <returns>The Color32 corresponding to the enum.</returns>
        public static Color32 GetColor(this ColorEnum color)
        {
            return ColorsDefault.FirstOrDefault(x => x.Name == color).Color;
        }

        /// <summary>
        ///     Gets the Color32 representation of a hexadecimal string.
        /// </summary>
        /// <param name="hex">The hexadecimal string.</param>
        /// <returns>The Color32 corresponding to the hex string.</returns>
        public static Color32 GetColor(this string hex)
        {
            return hex.HexToColor();
        }

        /// <summary>
        ///     Formats a string with a color tag using a hexadecimal string.
        /// </summary>
        /// <param name="str">The string to format.</param>
        /// <param name="hex">The hexadecimal color code.</param>
        /// <returns>The formatted HTML-like color tagged string.</returns>
        public static string SetColor(this string str, string hex)
        {
            hex = hex.Replace("#", "");
            return $"<color=#{hex}>{str}</color>";
        }

        /// <summary>
        ///     Formats a string with a color tag using a color name string.
        /// </summary>
        /// <param name="str">The string to format.</param>
        /// <param name="colorStr">The color name string.</param>
        /// <returns>The formatted HTML-like color tagged string.</returns>
        public static string SetColorStr(this string str, string colorStr)
        {
            return $"<color={colorStr}>{str}</color>";
        }

        /// <summary>
        ///     Formats a string with a color tag using a Color value.
        /// </summary>
        /// <param name="str">The string to format.</param>
        /// <param name="clr">The Color value.</param>
        /// <returns>The formatted HTML-like color tagged string.</returns>
        public static string SetColor(this string str, Color clr)
        {
            return $"<color=#{ColorToHex(clr)}>{str}</color>";
        }

        /// <summary>
        ///     Formats a string with a color tag using a ColorEnum value.
        /// </summary>
        /// <param name="str">The string to format.</param>
        /// <param name="clr">The ColorEnum value.</param>
        /// <returns>The formatted HTML-like color tagged string.</returns>
        public static string SetColor(this string str, ColorEnum clr)
        {
            return $"<color=#{ColorsDefault.Find(x => x.Name == clr).ClrHex}>{str}</color>";
        }

        #endregion
    }
}
