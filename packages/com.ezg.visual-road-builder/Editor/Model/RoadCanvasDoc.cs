#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Single owner of all map data (edges, stations, parkings, rampFlips, grid size, origin,
    /// decors). Serialized as one field on the EditorWindow.</summary>
    [Serializable]
    internal sealed class RoadCanvasDoc
    {
        [SerializeField] internal int GridWidth = 50;
        [SerializeField] internal int GridHeight = 50;

        // Toạ độ MAP gán cho ô local (0,0) — hiển thị trên trục & readout, VÀ dùng làm gốc bake:
        // piece đặt tại (localCell + OriginCell) nên displayed (0,0) trùng pivot RoadParent. Mở rộng
        // trái/dưới tự dời để số của cell cũ giữ nguyên. Persist trong file SO.
        [SerializeField] internal Vector2Int OriginCell;

        // Version dữ liệu đang mở trong window — dùng migrate khi đổi format giữa các bản tool.
        // 0/thiếu = edge theo ô nguyên (bản cũ); 3 = edge theo nửa ô.
        [SerializeField] internal int DataVersion;

        // Edge encode (độc lập kích thước lưới): (y2 << 13) | (x2 << 1) | orient, toạ độ theo
        // NỬA Ô (x2 = x * 2) để đường snap được bước 1/2 ô. Edge dài NỬA Ô (1 nấc lattice):
        // orient 0 nối (x2,y2)-(x2+1,y2); orient 1 nối (x2,y2)-(x2,y2+1).
        [SerializeField] internal List<int> Edges = new();
        // Lớp đường thứ 2 (highway) — cùng encode, vẽ màu đỏ, resolve bằng bộ prefab highway.
        [SerializeField] internal List<int> HighwayEdges = new();
        // Lớp thứ 3 (highway decor) — nét TRẮNG, mỗi đoạn liên tục spawn đúng 1 prefab.
        [SerializeField] internal List<int> HwDecorEdges = new();
        // Lớp thứ 4 (Road 2 — mặt cắt rộng x1.5) — cùng encode, resolve bằng bộ prefab Road 2 riêng.
        [SerializeField] internal List<int> Road2Edges = new();
        // Lớp thứ 5 (PATH — lối đi bộ, mặt cắt 0.5 ô) — cùng encode, solver riêng.
        [SerializeField] internal List<int> PathEdges = new();

        // Ramp Highway→Road bị LẬT HƯỚNG thủ công (phím F, hover lên ramp). Anchor điểm ramp (nửa ô)
        // encode (y2 << 13) | x2. Solver đảo stem ramp có anchor ở đây → mesh + span + cầu + preview quay
        // 180° đồng bộ (chỉ yaw, KHÔNG scale âm). Giữ SẮP XẾP để signature undo ổn định.
        [SerializeField] internal List<int> RampFlips = new();

        // Station encode: (rot << 24) | (y2 << 12) | x2 — anchor góc trái-dưới của khối NxN,
        // toạ độ theo NỬA Ô (x2 = x * 2) để snap bước 1/2 unit; rot 0..3 = hướng mặt (N/E/S/W,
        // yaw = rot * 90°).
        [SerializeField] internal List<int> Stations = new();
        // Parking encode: (orient << 24) | (y2 << 12) | x2 — orient 0 = cạnh dài theo X, 1 = theo Z.
        [SerializeField] internal List<int> Parkings = new();

        // Decor items — R1: owned here as map data.
        [SerializeField] internal List<DecorItem> Decors = new();

        /// <summary>Lớp edge theo index layer — caller passes layer from ViewState.</summary>
        internal List<int> EdgesFor(int layer) => layer switch
        {
            1 => HighwayEdges,
            2 => HwDecorEdges,
            3 => Road2Edges,
            4 => PathEdges,
            _ => Edges,
        };

        // Lattice nửa ô: mask/index tính trên lưới (2W-1) x (2H-1) điểm cách nhau 0.5 ô.
        internal int LatticeW => (GridWidth - 1) * 2 + 1;
        internal int LatticeH => (GridHeight - 1) * 2 + 1;
    }
}
#endif
