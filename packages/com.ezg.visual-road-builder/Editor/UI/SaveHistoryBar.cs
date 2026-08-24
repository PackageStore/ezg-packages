#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Thanh tren cung cot control: Undo/Redo · Auto Save · cham do "chua luu" nay · Save ·
    /// Restore.</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        private void DrawSaveHistoryBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            using (new EditorGUI.DisabledScope(_undo.Count == 0 && !_pendingChange))
                if (GUILayout.Button(new GUIContent("Undo", $"Hoàn tác (Ctrl+Z) — {_undo.Count} bước"),
                        EditorStyles.toolbarButton, GUILayout.Width(46)))
                    PerformUndo();
            using (new EditorGUI.DisabledScope(_redo.Count == 0))
                if (GUILayout.Button(new GUIContent("Redo", $"Làm lại (Ctrl+Shift+Z) — {_redo.Count} bước"),
                        EditorStyles.toolbarButton, GUILayout.Width(46)))
                    PerformRedo();

            GUILayout.Space(6f);

            EditorGUI.BeginChangeCheck();
            bool auto = GUILayout.Toggle(_autoSave,
                new GUIContent("Auto Save", "Bật: tự lưu file SO sau mỗi chỉnh sửa. Tắt: bấm Save thủ công."),
                EditorStyles.toolbarButton, GUILayout.Width(74));
            if (EditorGUI.EndChangeCheck())
            {
                _autoSave = auto;
                EditorPrefs.SetBool(AutoSavePrefKey, _autoSave);
                if (_autoSave && _dirty) SaveToSo(false);
            }

            GUILayout.FlexibleSpace();

            Rect dot = GUILayoutUtility.GetRect(14f, 16f, GUILayout.Width(14f));
            if (_dirty) DrawDirtyDot(dot);

            using (new EditorGUI.DisabledScope(!_dirty || _applyTarget.LevelPrefab == null))
                if (GUILayout.Button(new GUIContent("Save", "Lưu canvas ra file SO (.asset) cạnh prefab."),
                        EditorStyles.toolbarButton, GUILayout.Width(46)))
                    SaveToSo(true);
            using (new EditorGUI.DisabledScope(_applyTarget.LevelPrefab == null))
                if (GUILayout.Button(new GUIContent("Restore", "Nạp lại canvas từ file SO đã lưu."),
                        EditorStyles.toolbarButton, GUILayout.Width(58)))
                    RestoreFromSo();

            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_saveInfo))
                EditorGUILayout.LabelField(_dirty ? "Có thay đổi chưa lưu…" : _saveInfo, EditorStyles.miniLabel);
        }

        private void DrawDirtyDot(Rect area)
        {
            float t = (float)EditorApplication.timeSinceStartup;
            float wave = Mathf.Abs(Mathf.Sin(t * 4f));
            float bounce = wave * 4f;
            float pulse = 0.55f + 0.45f * wave;
            const float size = 9f;
            var rect = new Rect(
                area.x + (area.width - size) * 0.5f,
                area.y + (area.height - size) * 0.5f - bounce,
                size, size);

            Color prev = GUI.color;
            GUI.color = new Color(0.95f, 0.26f, 0.21f, pulse);
            GUI.DrawTexture(rect, DotTex, ScaleMode.StretchToFill, true);
            GUI.color = prev;
        }

        private Texture2D DotTex
        {
            get
            {
                if (_dotTex != null) return _dotTex;
                const int s = 32;
                _dotTex = new Texture2D(s, s, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                float c = (s - 1) * 0.5f, r = s * 0.5f - 1f;
                var px = new Color[s * s];
                for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    px[y * s + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(r - d));
                }
                _dotTex.SetPixels(px);
                _dotTex.Apply();
                return _dotTex;
            }
        }
    }
}
#endif
