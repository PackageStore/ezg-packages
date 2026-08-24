#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Tài nguyên vẽ dùng chung: bảng màu ô (khớp legend), tham chiếu + cache sprite piece
    /// đường, và các GUIStyle lazy-init.</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        // Màu ô canvas (khớp legend). Đường/highway/hw-decor tô theo điểm có nét; station/parking
        // tô theo khối trong DrawStations.
        private static readonly Color TileRoad    = new(0.95f, 0.65f, 0.15f);
        private static readonly Color TileHighway = new(0.90f, 0.25f, 0.20f);
        private static readonly Color TileHwDecor = new(0.95f, 0.95f, 0.95f);
        private static readonly Color TileRoad2   = new(0.55f, 0.35f, 0.85f); // tím — phân biệt Road2 khỏi 3 lớp cũ
        // xanh ngọc — tách biệt cam Road, đỏ Highway, trắng HW Decor, tím Road2
        private static readonly Color TilePath   = new(0.25f, 0.78f, 0.72f);
        internal static readonly Color TileStation = new(0.30f, 0.50f, 0.95f);
        internal static readonly Color TileParking = new(0.30f, 0.80f, 0.40f);
        private static readonly Color TileEmpty   = new(0.16f, 0.16f, 0.16f);

        // Icon thật của lớp Đường: sprite ô modular nạp từ atlas trong RoadPartLibrary,
        // vẽ đúng hình + xoay theo mask thay cho ô vuông cam.
        /// <summary>Asset path của atlas đường. Library tự lo phần fallback về _road_plan.psd ship kèm
        /// package khi field <see cref="RoadPartLibrary.roadPlanAtlas"/> trống. Trả "" khi chưa gán library.</summary>
        private string RoadPlanAtlasPath =>
            _library != null ? _library.ResolveRoadPlanAtlasPath() : string.Empty;
        // Mọi slice trong _road_plan.psd vẽ ở cùng tỉ lệ này → kích thước ô của 1 sprite suy ra
        // từ chính rect của nó (station_area 512 = 4 ô).
        private const float SpritePixelsPerCell = 128f;
        // Piece đã xoay sẵn thành Texture2D → vẽ AXIS-ALIGNED (không GUI.matrix) để được scroll-view clip
        // như ô phẳng; nét xoay bằng GUI.matrix KHÔNG bị clip nên tràn sang panel trái khi pan.
        // Key: (sprite, turns CW, mirrorY) — mirrorY dùng cho ramp hway_to_road bị lật gương (phím F).
        private readonly Dictionary<(Sprite, int, bool), Texture2D> _roadPieceTex = new Dictionary<(Sprite, int, bool), Texture2D>();
        private readonly Dictionary<Texture, Texture2D> _roadReadable = new Dictionary<Texture, Texture2D>();

        // Hot-reload serialize khôi phục GUIStyle cache cũ (textColor đen) qua mỗi domain reload nên
        // phải NonSerialized để getter dựng lại.
        [System.NonSerialized] private GUIStyle _hintStyle, _rulerXStyle, _rulerYStyle, _pillStyle;

        private GUIStyle HintStyle => _hintStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontStyle = FontStyle.Italic,
            normal = { textColor = new Color(0.60f, 0.60f, 0.60f) },
        };

        private GUIStyle RulerXStyle => _rulerXStyle ??= new GUIStyle(EditorStyles.whiteMiniLabel)
        {
            fontSize = 9,
            alignment = TextAnchor.UpperCenter,
            normal = { textColor = new Color(0.85f, 0.85f, 0.85f) },
            hover = { textColor = new Color(0.85f, 0.85f, 0.85f) },
            active = { textColor = new Color(0.85f, 0.85f, 0.85f) },
            focused = { textColor = new Color(0.85f, 0.85f, 0.85f) },
        };

        private GUIStyle RulerYStyle => _rulerYStyle ??= new GUIStyle(EditorStyles.whiteMiniLabel)
        {
            fontSize = 9,
            alignment = TextAnchor.MiddleRight,
            normal = { textColor = new Color(0.85f, 0.85f, 0.85f) },
            hover = { textColor = new Color(0.85f, 0.85f, 0.85f) },
            active = { textColor = new Color(0.85f, 0.85f, 0.85f) },
            focused = { textColor = new Color(0.85f, 0.85f, 0.85f) },
        };

        private GUIStyle PillStyle => _pillStyle ??= new GUIStyle(EditorStyles.whiteMiniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white },
            hover = { textColor = Color.white },
            active = { textColor = Color.white },
            focused = { textColor = Color.white },
            onNormal = { textColor = Color.white },
            onHover = { textColor = Color.white },
            onActive = { textColor = Color.white },
            onFocused = { textColor = Color.white },
        };

        [System.NonSerialized] private GUIStyle _overlapStyle;
        private GUIStyle OverlapStyle => _overlapStyle ??= new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleRight,
            normal = { textColor = new Color(1f, 0.28f, 0.24f) },
        };

        [System.NonSerialized] private GUIStyle _tagStyle;
        private GUIStyle TagStyle => _tagStyle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            normal = { textColor = Color.white },
            hover = { textColor = Color.white },
            active = { textColor = Color.white },
            focused = { textColor = Color.white },
        };

        [System.NonSerialized] private GUIStyle _miniTagStyle;
        private GUIStyle MiniTagStyle => _miniTagStyle ??= new GUIStyle(EditorStyles.miniBoldLabel)
        {
            normal = { textColor = Color.white },
            hover = { textColor = Color.white },
            active = { textColor = Color.white },
            focused = { textColor = Color.white },
        };
    }
}
#endif
