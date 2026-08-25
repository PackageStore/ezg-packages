#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>
    /// NGUỒN DUY NHẤT của map Visual Road Builder — file `.asset` git-tracked cho mỗi Level Prefab
    /// (thư mục `RoadCanvasSaves/`, tên `&lt;prefab&gt;_RoadCanvas.asset`). Tool ghi bằng Apply / Save /
    /// Auto Save, đọc lại bằng Load từ prefab / Restore. Editor-only (không ship vào build); mesh vẫn
    /// bake trong prefab, còn DỮ LIỆU map chỉ nằm ở đây. Encode toạ độ: edge (y2&lt;&lt;13)|(x2&lt;&lt;1)|orient,
    /// station/parking (rot&lt;&lt;24)|(y2&lt;&lt;12)|x2, toạ độ theo NỬA Ô (x2 = x*2).
    /// </summary>
    public sealed class RoadCanvasSave : ScriptableObject
    {
        [Serializable]
        public struct DecorPlacement
        {
            public int entry;
            public int x2;
            public int y2;
            public int rot;
            public float scale;
        }

        public int version = 3;
        /// <summary>0 (mặc định/absent — mọi asset cũ trên đĩa) = edge dài 1 ô (span 2 nấc lattice);
        /// 1 = edge dài nửa ô (span 1 nấc). ReadFrom split edge cũ khi &lt; 1, WriteInto luôn ghi 1.</summary>
        public int edgeSpanVersion = 0;
        public int width = 50;
        public int height = 50;
        public float cellWorldSize = 1f;

        public List<int> edges = new();
        public List<int> highwayEdges = new();
        public List<int> hwDecorEdges = new();
        public List<int> road2Edges = new();
        public List<int> pathEdges = new();
        public List<int> stations = new();
        public List<int> stations2 = new();
        public List<int> parkings = new();
        public List<DecorPlacement> decors = new();

        /// <summary>Ramp Highway→Road bị LẬT HƯỚNG thủ công (phím F). Mỗi phần tử = anchor điểm ramp
        /// (nửa ô) encode <c>(y2 &lt;&lt; 13) | x2</c>. Solver đảo stem của ramp có anchor trong đây → mesh +
        /// span cột + cầu + preview cùng quay 180° đồng bộ (chỉ yaw, KHÔNG scale âm).</summary>
        public List<int> rampFlips = new();

        public Vector2Int originCell;

        public string libraryGuid;
        public string decorLibraryGuid;
        public string targetPrefabGuid;
        public string savedAt;
    }
}
#endif
