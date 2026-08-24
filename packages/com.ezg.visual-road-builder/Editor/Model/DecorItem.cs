#if UNITY_EDITOR
using System;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>One placed decor item on the canvas — position, rotation, scale, library entry index.</summary>
    [Serializable]
    internal struct DecorItem
    {
        public int entry;  // index trong DecorLibrary.entries
        public int x2;     // toạ độ nửa ô
        public int y2;
        public int rot;    // 0..3, yaw = rot * 90
        public float scale; // hệ số scale roll lúc đặt; <= 0 (data cũ) hiểu là 1
    }
}
#endif
