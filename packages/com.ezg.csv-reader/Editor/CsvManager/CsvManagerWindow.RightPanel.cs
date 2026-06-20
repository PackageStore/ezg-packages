// Editor/CsvManager/CsvManagerWindow.RightPanel.cs
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ezg.Package.CsvReader.Editor
{
    public partial class CsvManagerWindow
    {
        private void DrawRightPanel()
        {
            List<CsvEntry> visible = GetFilteredEntries();

            GUILayout.Space(10f);
            using (new EditorGUILayout.VerticalScope(_panelStyle))
            {
                string catLabel = _selectedCategory ?? "All";
                string query    = (_searchQuery ?? string.Empty).Trim();
                string subtitle = string.IsNullOrEmpty(query)
                    ? $"{visible.Count} files"
                    : $"{visible.Count} matches for \"{query}\"";

                DrawCardHeader(catLabel, subtitle);

                if (visible.Count == 0)
                {
                    GUILayout.Space(8f);
                    EditorGUILayout.HelpBox("No CSV files match the current filter.", MessageType.Info);
                    return;
                }

                using (var scroll = new EditorGUILayout.ScrollViewScope(_rightScroll))
                {
                    _rightScroll = scroll.scrollPosition;
                    for (int i = 0; i < visible.Count; i++)
                    {
                        DrawCsvCard(visible[i]);
                        GUILayout.Space(4f);
                    }
                }
            }
        }

        private void DrawCsvCard(CsvEntry entry)
        {
            using (new EditorGUILayout.VerticalScope(_cardStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                    {
                        EditorGUILayout.LabelField(entry.name, _sectionTitleStyle);
                        EditorGUILayout.LabelField(entry.displayPath, _mutedMiniStyle);
                    }

                    GUILayout.Space(4f);

                    if (GUILayout.Button("Ping", GUILayout.Width(46f)))
                    {
                        var asset = AssetDatabase.LoadMainAssetAtPath(entry.assetPath);
                        if (asset != null)
                        {
                            EditorGUIUtility.PingObject(asset);
                            Selection.activeObject = asset;
                        }
                    }

                    if (GUILayout.Button("Open", GUILayout.Width(46f)))
                    {
                        string fullPath = System.IO.Path.GetFullPath(entry.assetPath);
                        EditorUtility.OpenWithDefaultApp(fullPath);
                    }

                    if (GUILayout.Button("Copy", GUILayout.Width(46f)))
                        EditorGUIUtility.systemCopyBuffer = entry.assetPath;
                }
            }
        }

        private List<CsvEntry> GetFilteredEntries()
        {
            string query = (_searchQuery ?? string.Empty).Trim();

            IEnumerable<CsvEntry> result = _allEntries;

            if (_selectedCategory != null)
                result = result.Where(e => e.category == _selectedCategory);

            if (!string.IsNullOrEmpty(query))
                result = result.Where(e =>
                    e.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    e.category.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);

            return result.OrderBy(e => e.name).ToList();
        }
    }
}
#endif
