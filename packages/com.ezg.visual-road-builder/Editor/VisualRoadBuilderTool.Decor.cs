#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>
    /// Mode Decor: đặt decor (cây, đèn, prop...) theo điểm trên lưới — snap 1/2 ô, 4 hướng
    /// xoay (phím R). Chuột trái: đặt mới (kéo = rải liên tục) / kéo item có sẵn để di chuyển;
    /// chuột phải: xoá. Loại decor chọn từ <see cref="DecorLibrary"/>. Apply spawn prefab
    /// dưới cùng root với đường.
    ///
    /// File này giữ state + control UI; input/model/draw/apply tách ra .DecorInput/.DecorModel/
    /// .DecorDraw/.DecorApply.cs.
    /// </summary>
    public sealed partial class VisualRoadBuilderTool
    {
        // DecorItem struct promoted to top-level internal type in Model/DecorItem.cs

        [SerializeField] private DecorLibrary _decorLibrary;
        // _decors moved to RoadCanvasDoc.Decors; shim in Shims.cs
        [SerializeField] private int _decorEntryIndex;
        [SerializeField] private bool _decorAreaMode;      // brush vùng: khoanh chữ nhật tự rải
        [SerializeField] private float _decorDensity = 0.5f; // số item / ô vuông
        [SerializeField] private bool _decorRandomRot = true;

        private int _draggingDecor = -1;
        private bool _paintingDecor;
        private bool _erasingDecor;
        private bool _decorHover;
        private Vector2Int _decorHoverP2;
        private bool _areaDragging;
        private bool _areaErasing;
        private Vector2 _areaStart; // toạ độ lưới float
        private Vector2 _areaEnd;

        /// <summary>Nhóm foldout Decor độc lập trong cột control.</summary>
        private void DrawDecorSection()
        {
            _foldDecor = EditorGUILayout.Foldout(_foldDecor, "Decor — Trang trí", true, EditorStyles.foldoutHeader);
            if (!_foldDecor) return;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            bool isDecorActive = (_mode == PaintMode.Decor);
            Color prevBg = GUI.backgroundColor;
            if (isDecorActive)
                GUI.backgroundColor = new Color(0.35f, 0.75f, 1f);

            if (GUILayout.Button(isDecorActive ? "✓ Đang chọn mode Decor" : "Kích hoạt mode Decor", GUILayout.Height(24f)))
            {
                if (!isDecorActive)
                {
                    _mode = PaintMode.Decor;
                    _dragging = false;
                    _draggingStation = -1;
                    _draggingParking = -1;
                    _hasHover = false;
                    _movingAll = false;
                    ResetDecorInteraction();
                }
            }
            GUI.backgroundColor = prevBg;
            EditorGUILayout.Space(2f);

            DrawDecorControls();

            EditorGUILayout.Space(4f);
            prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.85f, 0.3f); // vàng
            if (GUILayout.Button(
                    new GUIContent("Apply Decor (chỉ decor)",
                        "Chỉ dựng lại cụm 'Decor' dưới root — KHÔNG đụng đường/station/parking." +
                        " 0 chấm = dọn sạch cụm Decor trong scene."),
                    GUILayout.Height(24f)))
            {
                ApplyDecorsOnly();
            }
            GUI.backgroundColor = prevBg;

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2f);
        }

        /// <summary>Hàng control của mode Decor.</summary>
        private void DrawDecorControls()
        {
            _decorLibrary = (DecorLibrary)EditorGUILayout.ObjectField(
                "Decor Library", _decorLibrary, typeof(DecorLibrary), false);

            if (_decorLibrary == null || _decorLibrary.entries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Gán Decor Library (Create > EZG Technical Art > Decor Library) và thêm entry để vẽ decor.",
                    MessageType.Warning);
                return;
            }

            var names = new string[_decorLibrary.entries.Count];
            for (int i = 0; i < names.Length; i++)
            {
                DecorLibrary.DecorEntry entry = _decorLibrary.entries[i];
                names[i] = string.IsNullOrEmpty(entry.name) ? $"entry {i}" : entry.name;
            }
            _decorEntryIndex = Mathf.Clamp(_decorEntryIndex, 0, names.Length - 1);
            _decorEntryIndex = EditorGUILayout.Popup("Loại decor", _decorEntryIndex, names);

            _decorAreaMode = EditorGUILayout.ToggleLeft(
                new GUIContent("Rải theo vùng (brush chữ nhật)",
                    "Bật → kéo chuột trái khoanh chữ nhật, thả ra tool tự rải random theo mật độ." +
                    " Chuột phải khoanh vùng = xoá mọi decor trong vùng."),
                _decorAreaMode);
            if (_decorAreaMode)
            {
                _decorDensity = EditorGUILayout.Slider(
                    new GUIContent("Mật độ (item/ô²)"), _decorDensity, 0.05f, 4f);
                _decorRandomRot = EditorGUILayout.ToggleLeft("Xoay ngẫu nhiên 4 hướng", _decorRandomRot);
            }

            using (new EditorGUI.DisabledScope(_decors.Count == 0))
            {
                if (GUILayout.Button($"Clear Decor ({_decors.Count})", GUILayout.Height(20f))
                    && EditorUtility.DisplayDialog("Road Grid",
                        $"Xoá toàn bộ {_decors.Count} chấm decor trên lưới? (đường/station giữ nguyên)",
                        "Xoá", "Huỷ"))
                {
                    _decors.Clear();
                }
            }
        }

        private void ResetDecorInteraction()
        {
            _draggingDecor = -1;
            _paintingDecor = false;
            _erasingDecor = false;
            _decorHover = false;
            _areaDragging = false;
            _areaErasing = false;
        }
    }
}
#endif
