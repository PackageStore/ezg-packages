// Editor/CsvManager/CsvManagerWindow.LeftPanel.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Ezg.Package.CsvReader.Editor
{
    public partial class CsvManagerWindow
    {
        private void DrawLeftPanel()
        {
            GUILayout.Space(10f);
            using (new EditorGUILayout.VerticalScope(_panelStyle))
            {
                DrawCardHeader("Categories", $"{_allEntries.Count} CSVs · {_categories.Count} features");
                GUILayout.Space(4f);

                using (var scroll = new EditorGUILayout.ScrollViewScope(_leftScroll))
                {
                    _leftScroll = scroll.scrollPosition;

                    DrawCategoryRow(null, _allEntries.Count, 0);
                    GUILayout.Space(4f);

                    for (int i = 0; i < _categories.Count; i++)
                    {
                        DrawCategoryRow(_categories[i], CountForCategory(_categories[i]), i + 1);
                        GUILayout.Space(4f);
                    }
                }
            }
        }

        private void DrawCategoryRow(string category, int count, int rowIndex)
        {
            bool isSelected = category == _selectedCategory;
            Rect  rect      = GUILayoutUtility.GetRect(10f, 40f, GUILayout.ExpandWidth(true));
            bool  isHovered = rect.Contains(Event.current.mousePosition);

            DrawCategoryCardBackground(rect, isSelected, isHovered, (rowIndex & 1) == 1);

            GUI.Label(
                new Rect(rect.x + 12f, rect.y + 11f, rect.width - 70f, 18f),
                category ?? "All",
                EditorStyles.boldLabel);

            float chipW    = 52f;
            Color chipColor = isSelected ? _accentColor : _surfaceAltColor;
            DrawChip(new Rect(rect.xMax - chipW - 6f, rect.y + 11f, chipW, 18f), count.ToString(), chipColor);

            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                _selectedCategory = category;
                _rightScroll      = Vector2.zero;
                Repaint();
            }
        }

        private int CountForCategory(string category)
        {
            int count = 0;
            for (int i = 0; i < _allEntries.Count; i++)
                if (_allEntries[i].category == category) count++;
            return count;
        }
    }
}
#endif
