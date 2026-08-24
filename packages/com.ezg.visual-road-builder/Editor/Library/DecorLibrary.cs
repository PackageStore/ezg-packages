#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>
    /// Thư viện decor cho mode Decor của <see cref="VisualRoadBuilderTool"/>.
    /// Mỗi entry là 1 loại decor (cây, cột đèn, thùng rác...) đặt theo điểm trên lưới
    /// (snap 1/2 ô, 4 hướng xoay bằng phím R). Prefab quy ước pivot ở tâm-đáy, mặt về +Z.
    /// </summary>
    [CreateAssetMenu(menuName = "EZG Technical Art/Decor Library", fileName = "DecorLibrary")]
    public sealed class DecorLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class DecorEntry
        {
            public string name = "decor";
            public GameObject prefab;
            [Tooltip("Màu hiển thị điểm decor này trên canvas của tool.")]
            public Color canvasColor = Color.magenta;
            [Tooltip("Scale ngẫu nhiên khi đặt: hệ số nhân đều 3 trục, roll trong [min, max]. " +
                     "Để cả 2 = 1 nếu không muốn random.")]
            public float scaleMin = 1f;
            public float scaleMax = 1f;
        }

        public List<DecorEntry> entries = new();
    }
}
#endif
