#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Encode/decode/snap/hit-test/pivot geometry for stations and parkings.
    /// Pure static codec — instance methods that depend on window state remain as partial class
    /// members during migration.</summary>
    internal static class BlockCodec
    {
        internal static int EncodeStation(Vector2Int a2, int rot) => (rot << 24) | (a2.y << 12) | a2.x;

        internal static void DecodeStation(int id, out int x2, out int y2, out int rot)
        {
            x2 = id & 0xFFF;
            y2 = (id >> 12) & 0xFFF;
            rot = (id >> 24) & 3;
        }

        internal static int EncodeParking(Vector2Int a2, int rot) => (rot << 24) | (a2.y << 12) | a2.x;

        internal static void DecodeParking(int id, out int x2, out int y2, out int rot)
        {
            x2 = id & 0xFFF;
            y2 = (id >> 12) & 0xFFF;
            rot = (id >> 24) & 3;
        }

        /// <summary>Kẹp anchor (nửa ô) của khối w x h ô vào trong lưới.</summary>
        internal static Vector2Int ClampBlockAnchor(Vector2Int a2, int w, int h, int gridWidth, int gridHeight)
        {
            return new Vector2Int(
                Mathf.Clamp(a2.x, 0, Mathf.Max(0, (gridWidth - 1 - w) * 2)),
                Mathf.Clamp(a2.y, 0, Mathf.Max(0, (gridHeight - 1 - h) * 2)));
        }

        /// <summary>Điểm PIVOT của station (đơn vị ô) khi bake + để vẽ marker. Prefab StationArea đặt
        /// pivot TRÊN dải đường trước mặt: đo được cách tâm khối (StationSize/2 + 1) ô theo hướng mặt
        /// (rot 0 = +Z, quay CW mỗi nấc 90°).</summary>
        internal static Vector2 StationPivotCell(int sx2, int sy2, int s, int rot)
        {
            float half = s * 0.5f;
            float cx = sx2 * 0.5f + half;
            float cy = sy2 * 0.5f + half;
            float fwd = half + 1f;
            return rot switch
            {
                1 => new Vector2(cx + fwd, cy),
                2 => new Vector2(cx, cy - fwd),
                3 => new Vector2(cx - fwd, cy),
                _ => new Vector2(cx, cy + fwd),
            };
        }

        /// <summary>Điểm PIVOT của parking (đơn vị ô) khi bake + để vẽ marker — GIỮA MÉP MẶT của khối.</summary>
        internal static Vector2 ParkingPivotCell(int ax2, int ay2, int rot, Vector2Int cells)
        {
            float ax = ax2 * 0.5f, ay = ay2 * 0.5f;
            return rot switch
            {
                1 => new Vector2(ax + cells.x, ay + cells.y * 0.5f),
                2 => new Vector2(ax + cells.x * 0.5f, ay),
                3 => new Vector2(ax, ay + cells.y * 0.5f),
                _ => new Vector2(ax + cells.x * 0.5f, ay + cells.y),
            };
        }
    }
}
#endif
