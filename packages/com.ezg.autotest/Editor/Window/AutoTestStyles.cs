using UnityEditor;
using UnityEngine;

namespace Ezg.AutoTest.Editor
{
    /// <summary>Màu + style dùng chung của cửa sổ Auto Test (tự đổi theo skin sáng/tối).</summary>
    internal static class AutoTestStyles
    {
        static GUIStyle _title;
        static GUIStyle _subtle;
        static GUIStyle _pill;
        static GUIStyle _wrap;
        static GUIStyle _mono;
        static GUIStyle _bigNumber;
        static GUIStyle _card;
        static GUIStyle _rowButton;
        static GUIStyle _sectionHeader;
        static Texture2D _white;

        public static bool Dark => EditorGUIUtility.isProSkin;

        public static GUIStyle Title => _title ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };

        public static GUIStyle SectionHeader =>
            _sectionHeader ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, margin = new RectOffset(4, 4, 8, 4) };

        public static GUIStyle Subtle => _subtle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            wordWrap = true,
            normal = { textColor = Dark ? new Color(0.62f, 0.62f, 0.62f) : new Color(0.38f, 0.38f, 0.38f) }
        };

        public static GUIStyle Wrap => _wrap ??= new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true };

        public static GUIStyle Mono => _mono ??= new GUIStyle(EditorStyles.label)
        {
            font = EditorStyles.standardFont, fontSize = 11, wordWrap = true, richText = false
        };

        public static GUIStyle PillStyle => _pill ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(6, 6, 1, 1),
            normal = { textColor = Color.white }
        };

        public static GUIStyle BigNumber => _bigNumber ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 26, alignment = TextAnchor.MiddleCenter
        };

        public static GUIStyle Card => _card ??= new GUIStyle("HelpBox") { padding = new RectOffset(8, 8, 6, 6) };

        public static GUIStyle RowButton => _rowButton ??= new GUIStyle(EditorStyles.label)
        {
            padding = new RectOffset(4, 4, 2, 2), richText = true
        };

        public static Texture2D White
        {
            get
            {
                if (_white != null) return _white;
                _white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
                return _white;
            }
        }

        public static Color StatusColor(TestStatus s)
        {
            switch (s)
            {
                case TestStatus.Passed: return new Color(0.20f, 0.66f, 0.33f);
                case TestStatus.Warning: return new Color(0.93f, 0.62f, 0.10f);
                case TestStatus.Failed: return new Color(0.86f, 0.24f, 0.22f);
                case TestStatus.Error: return new Color(0.62f, 0.12f, 0.55f);
                case TestStatus.Running: return new Color(0.20f, 0.52f, 0.90f);
                case TestStatus.Cancelled: return new Color(0.45f, 0.45f, 0.45f);
                default: return new Color(0.55f, 0.55f, 0.55f);
            }
        }

        public static Color SeverityColor(Severity s)
        {
            switch (s)
            {
                case Severity.Blocker: return new Color(0.55f, 0.05f, 0.35f);
                case Severity.Critical: return new Color(0.84f, 0.16f, 0.16f);
                case Severity.Major: return new Color(0.93f, 0.45f, 0.10f);
                case Severity.Minor: return new Color(0.85f, 0.68f, 0.10f);
                default: return new Color(0.30f, 0.55f, 0.85f);
            }
        }

        public static Color HealthColor(int score)
        {
            if (score >= 90) return StatusColor(TestStatus.Passed);
            if (score >= 70) return StatusColor(TestStatus.Warning);
            return StatusColor(TestStatus.Failed);
        }

        public static void DrawPill(Rect r, string text, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, White, ScaleMode.StretchToFill, false, 0, color, 0, 6);
            GUI.color = prev;
            GUI.Label(r, text, PillStyle);
        }

        public static void Pill(string text, Color color, float width = 70)
        {
            var r = GUILayoutUtility.GetRect(width, 16, GUILayout.Width(width), GUILayout.Height(16));
            r.y += 1;
            DrawPill(r, text, color);
        }

        public static void Separator(float thickness = 1)
        {
            var r = GUILayoutUtility.GetRect(1, thickness + 6, GUILayout.ExpandWidth(true));
            r.y += 3;
            r.height = thickness;
            EditorGUI.DrawRect(r, Dark ? new Color(0.1f, 0.1f, 0.1f) : new Color(0.7f, 0.7f, 0.7f));
        }

        public static void Bar(Rect r, float progress, Color color)
        {
            EditorGUI.DrawRect(r, Dark ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.82f, 0.82f, 0.82f));
            var fill = new Rect(r.x, r.y, r.width * Mathf.Clamp01(progress), r.height);
            EditorGUI.DrawRect(fill, color);
        }

        static readonly System.Collections.Generic.Dictionary<string, Texture> _iconCache = new();

        /// <summary>Icon built-in theo tên (FindTexture không log lỗi khi thiếu như IconContent).</summary>
        public static GUIContent Icon(string name, string tooltip = null)
        {
            if (!_iconCache.TryGetValue(name ?? "", out var tex))
            {
                tex = string.IsNullOrEmpty(name) ? null : EditorGUIUtility.FindTexture(name);
                if (tex == null) tex = EditorGUIUtility.FindTexture("d_UnityEditor.ConsoleWindow");
                _iconCache[name ?? ""] = tex;
            }

            return new GUIContent(tex, tooltip);
        }
    }
}
