// Editor/CsvManager/CsvManagerWindow.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Ezg.Package.CsvReader.Editor
{
    public partial class CsvManagerWindow : EditorWindow
    {
        private const float LEFT_PANEL_WIDTH = 260f;
        private const float PANEL_GUTTER = 8f;

        [MenuItem("Ezg/Csv Manager")]
        public static void Open()
        {
            var win = GetWindow<CsvManagerWindow>("CSV Manager");
            win.minSize = new Vector2(760f, 500f);
            win.Focus();
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
            RefreshAll();
        }

        private void OnGUI()
        {
            EnsureStyles();

            if (Event.current.type == EventType.MouseMove)
                Repaint();

            DrawToolbar();

            const float TOP_BAR_HEIGHT = 30f;
            Rect bodyRect = new Rect(8f, TOP_BAR_HEIGHT + 8f, position.width - 16f, position.height - TOP_BAR_HEIGHT - 16f);
            if (bodyRect.width <= 0f || bodyRect.height <= 0f) return;

            Rect leftRect  = new Rect(bodyRect.x, bodyRect.y, LEFT_PANEL_WIDTH, bodyRect.height);
            Rect rightRect = new Rect(leftRect.xMax + PANEL_GUTTER, bodyRect.y,
                Mathf.Max(0f, bodyRect.width - LEFT_PANEL_WIDTH - PANEL_GUTTER), bodyRect.height);

            DrawPanelBackground(leftRect);
            DrawPanelBackground(rightRect);

            GUILayout.BeginArea(leftRect);
            try   { DrawLeftPanel(); }
            finally { GUILayout.EndArea(); }

            GUILayout.BeginArea(rightRect);
            try   { DrawRightPanel(); }
            finally { GUILayout.EndArea(); }
        }

        private void DrawToolbar()
        {
            Rect toolbarRect = new Rect(0f, 0f, position.width, 30f);
            EditorGUI.DrawRect(toolbarRect, _windowBgColor);

            GUILayout.BeginArea(new Rect(8f, 4f, position.width - 16f, 22f));
            using (new EditorGUILayout.HorizontalScope())
            {
                _searchQuery = DrawSearchField(
                    _searchQuery,
                    "Search by feature or CSV name...",
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(22f));

                if (GUILayout.Button("↺ Refresh", GUILayout.Width(80f), GUILayout.Height(22f)))
                    RefreshAll();
            }
            GUILayout.EndArea();
        }
    }
}
#endif
