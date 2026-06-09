using UnityEngine;

namespace BlackFace.Libraries.Modules.Colors
{
    /// <summary>
    /// Represents a color with its identifier and hex code
    /// </summary>
    public struct ColorModel
    {
        #region Properties

        /// <summary>
        /// Identifier name of the color
        /// </summary>
        public ColorEnum Name { get; set; }

        /// <summary>
        /// Hexadecimal color code (e.g., #FFFFFF)
        /// </summary>
        public string ClrHex { get; set; }

        /// <summary>
        /// Color32 value converted from hex code
        /// </summary>
        public Color32 Color => ColorSystem.HexToColor(ClrHex);

        #endregion
    }
}