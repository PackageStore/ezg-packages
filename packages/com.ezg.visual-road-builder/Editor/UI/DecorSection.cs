#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Nhóm foldout Decor trong cột control: chọn loại, mode brush, density, nút Apply Decor.</summary>
    internal sealed class DecorSection
    {
        private readonly ToolContext _ctx;
        private readonly DecorState _ds;
        private readonly Action _activateDecorMode;
        private readonly Action _applyDecorsOnly;

        internal DecorSection(ToolContext ctx, DecorState ds,
            Action activateDecorMode, Action applyDecorsOnly)
        {
            _ctx = ctx;
            _ds = ds;
            _activateDecorMode = activateDecorMode;
            _applyDecorsOnly = applyDecorsOnly;
        }

        internal void DrawDecorSection()
        {
            var view = _ctx.View;
            view.FoldDecor = EditorGUILayout.Foldout(view.FoldDecor, "Decor — Trang trí", true, EditorStyles.foldoutHeader);
            if (!view.FoldDecor) return;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            bool isDecorActive = (view.Mode == PaintMode.Decor);
            Color prevBg = GUI.backgroundColor;
            if (isDecorActive)
                GUI.backgroundColor = new Color(0.35f, 0.75f, 1f);

            if (GUILayout.Button(isDecorActive ? "✓ Đang chọn mode Decor" : "Kích hoạt mode Decor", GUILayout.Height(24f)))
            {
                if (!isDecorActive)
                    _activateDecorMode();
            }
            GUI.backgroundColor = prevBg;
            EditorGUILayout.Space(2f);

            DrawDecorControls();

            EditorGUILayout.Space(4f);
            prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.85f, 0.3f);
            if (GUILayout.Button(
                    new GUIContent("Apply Decor (chỉ decor)",
                        "Chỉ dựng lại cụm 'Decor' dưới root — KHÔNG đụng đường/station/parking." +
                        " 0 chấm = dọn sạch cụm Decor trong scene."),
                    GUILayout.Height(24f)))
            {
                _applyDecorsOnly();
            }
            GUI.backgroundColor = prevBg;

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2f);
        }

        private void DrawDecorControls()
        {
            var doc = _ctx.Doc;
            _ds.Library = (DecorLibrary)EditorGUILayout.ObjectField(
                "Decor Library", _ds.Library, typeof(DecorLibrary), false);

            if (_ds.Library == null || _ds.Library.entries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Gán Decor Library (Create > EZG Technical Art > Decor Library) và thêm entry để vẽ decor.",
                    MessageType.Warning);
                return;
            }

            var names = new string[_ds.Library.entries.Count];
            for (int i = 0; i < names.Length; i++)
            {
                DecorLibrary.DecorEntry entry = _ds.Library.entries[i];
                names[i] = string.IsNullOrEmpty(entry.name) ? $"entry {i}" : entry.name;
            }
            _ds.EntryIndex = Mathf.Clamp(_ds.EntryIndex, 0, names.Length - 1);
            _ds.EntryIndex = EditorGUILayout.Popup("Loại decor", _ds.EntryIndex, names);

            _ds.AreaMode = EditorGUILayout.ToggleLeft(
                new GUIContent("Rải theo vùng (brush chữ nhật)",
                    "Bật → kéo chuột trái khoanh chữ nhật, thả ra tool tự rải random theo mật độ." +
                    " Chuột phải khoanh vùng = xoá mọi decor trong vùng."),
                _ds.AreaMode);
            if (_ds.AreaMode)
            {
                _ds.Density = EditorGUILayout.Slider(
                    new GUIContent("Mật độ (item/ô²)"), _ds.Density, 0.05f, 4f);
                _ds.RandomRot = EditorGUILayout.ToggleLeft("Xoay ngẫu nhiên 4 hướng", _ds.RandomRot);
            }

            using (new EditorGUI.DisabledScope(doc.Decors.Count == 0))
            {
                if (GUILayout.Button($"Clear Decor ({doc.Decors.Count})", GUILayout.Height(20f))
                    && EditorUtility.DisplayDialog("Road Grid",
                        $"Xoá toàn bộ {doc.Decors.Count} chấm decor trên lưới? (đường/station giữ nguyên)",
                        "Xoá", "Huỷ"))
                {
                    doc.Decors.Clear();
                }
            }
        }
    }
}
#endif
