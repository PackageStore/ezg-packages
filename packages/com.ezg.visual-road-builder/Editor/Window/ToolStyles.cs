#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Tài nguyên vẽ dùng chung: bảng màu ô (khớp legend) và các GUIStyle lazy-init.</summary>
    internal sealed class ToolStyles
    {
        // Màu ô canvas (khớp legend). Đường/highway/hw-decor tô theo điểm có nét; station/parking
        // tô theo khối trong DrawStations.
        internal static readonly Color TileRoad    = new(0.95f, 0.65f, 0.15f);
        internal static readonly Color TileHighway = new(0.90f, 0.25f, 0.20f);
        internal static readonly Color TileHwDecor = new(0.95f, 0.95f, 0.95f);
        internal static readonly Color TileRoad2   = new(0.55f, 0.35f, 0.85f); // tím — phân biệt Road2 khỏi 3 lớp cũ
        // xanh ngọc — tách biệt cam Road, đỏ Highway, trắng HW Decor, tím Road2
        internal static readonly Color TilePath   = new(0.25f, 0.78f, 0.72f);
        internal static readonly Color TileStation = new(0.30f, 0.50f, 0.95f);
        internal static readonly Color TileParking = new(0.30f, 0.80f, 0.40f);
        internal static readonly Color TileEmpty   = new(0.16f, 0.16f, 0.16f);

        // Hot-reload serialize khôi phục GUIStyle cache cũ (textColor đen) qua mỗi domain reload nên
        // phải NonSerialized để getter dựng lại.
        [System.NonSerialized] private GUIStyle _hintStyle, _rulerXStyle, _rulerYStyle, _pillStyle;

        internal GUIStyle HintStyle => _hintStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontStyle = FontStyle.Italic,
            normal = { textColor = new Color(0.60f, 0.60f, 0.60f) },
        };

        internal GUIStyle RulerXStyle => _rulerXStyle ??= new GUIStyle(EditorStyles.whiteMiniLabel)
        {
            fontSize = 9,
            alignment = TextAnchor.UpperCenter,
            normal = { textColor = new Color(0.85f, 0.85f, 0.85f) },
            hover = { textColor = new Color(0.85f, 0.85f, 0.85f) },
            active = { textColor = new Color(0.85f, 0.85f, 0.85f) },
            focused = { textColor = new Color(0.85f, 0.85f, 0.85f) },
        };

        internal GUIStyle RulerYStyle => _rulerYStyle ??= new GUIStyle(EditorStyles.whiteMiniLabel)
        {
            fontSize = 9,
            alignment = TextAnchor.MiddleRight,
            normal = { textColor = new Color(0.85f, 0.85f, 0.85f) },
            hover = { textColor = new Color(0.85f, 0.85f, 0.85f) },
            active = { textColor = new Color(0.85f, 0.85f, 0.85f) },
            focused = { textColor = new Color(0.85f, 0.85f, 0.85f) },
        };

        internal GUIStyle PillStyle => _pillStyle ??= new GUIStyle(EditorStyles.whiteMiniLabel)
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
        internal GUIStyle OverlapStyle => _overlapStyle ??= new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleRight,
            normal = { textColor = new Color(1f, 0.28f, 0.24f) },
        };

        [System.NonSerialized] private GUIStyle _tagStyle;
        internal GUIStyle TagStyle => _tagStyle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            normal = { textColor = Color.white },
            hover = { textColor = Color.white },
            active = { textColor = Color.white },
            focused = { textColor = Color.white },
        };

        [System.NonSerialized] private GUIStyle _miniTagStyle;
        internal GUIStyle MiniTagStyle => _miniTagStyle ??= new GUIStyle(EditorStyles.miniBoldLabel)
        {
            normal = { textColor = Color.white },
            hover = { textColor = Color.white },
            active = { textColor = Color.white },
            focused = { textColor = Color.white },
        };
    }
}
#endif
