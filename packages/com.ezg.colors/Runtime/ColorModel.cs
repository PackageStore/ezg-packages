using UnityEngine;

namespace Ezg.Package.ColorSystem
{
    /// <summary>
    ///     Represents a named color entry with its hexadecimal string value.
    /// </summary>
    public struct ColorModel
    {
        /// <summary>Gets or sets the color enum identifier.</summary>
        public ColorEnum Name { get; set; }

        /// <summary>Gets or sets the hexadecimal color string (e.g. "FF0000").</summary>
        public string ClrHex { get; set; }

        /// <summary>Gets the Unity <see cref="Color32"/> representation derived from <see cref="ClrHex"/>.</summary>
        public Color32 Color => ClrHex.HexToColor();
    }
}
