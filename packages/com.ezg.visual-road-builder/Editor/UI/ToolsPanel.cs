#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Nhom Tools cua cot control: chon mode/lop ve + hang nut action (Apply Decor / Clear /
    /// Load) va nut Apply FULL ghim day.</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        private struct ToolBrushItem
        {
            public string Name;
            public PaintMode Mode;
            public int LayerOrKind;
            public bool IsRoadMode;
        }

        private void DrawToolsSection()
        {
            _view.FoldTools = EditorGUILayout.Foldout(_view.FoldTools, "Tools — Brushes & Legend", true, EditorStyles.foldoutHeader);
            if (!_view.FoldTools) return;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EnsureRoadSprites();
            DrawToolBrushGrid();

            EditorGUILayout.Space(6f);
            DrawToolActionButtons();

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2f);
        }

        private void DrawToolBrushGrid()
        {
            ToolBrushItem[] items =
            {
                new() { Name = "Road 1", Mode = PaintMode.Road, LayerOrKind = 0, IsRoadMode = true },
                new() { Name = "Road 2", Mode = PaintMode.Road, LayerOrKind = 3, IsRoadMode = true },
                new() { Name = "Lối đi bộ", Mode = PaintMode.Road, LayerOrKind = 4, IsRoadMode = true },
                new() { Name = "Highway", Mode = PaintMode.Road, LayerOrKind = 1, IsRoadMode = true },
                new() { Name = "HW Decor", Mode = PaintMode.Road, LayerOrKind = 2, IsRoadMode = true },
                // Ô trống cuối hàng đường: giữ 3 brush khối nằm chung một hàng, không để Station 1
                // trôi lên cạnh HW Decor khi lưới tự dồn theo colCount.
                new() { Name = null },
                new() { Name = "Station 1", Mode = PaintMode.Station, LayerOrKind = 0, IsRoadMode = false },
                new() { Name = "Station 2", Mode = PaintMode.Station, LayerOrKind = 3, IsRoadMode = false },
                new() { Name = "Park", Mode = PaintMode.Station, LayerOrKind = 1, IsRoadMode = false },
            };

            const int colCount = 3;
            EditorGUILayout.BeginVertical();
            for (int i = 0; i < items.Length; i += colCount)
            {
                EditorGUILayout.BeginHorizontal();
                for (int j = 0; j < colCount; j++)
                {
                    int index = i + j;
                    if (index < items.Length && items[index].Name != null)
                        DrawToolBrushButton(items[index]);
                    else
                        GUILayout.Label("", GUILayout.ExpandWidth(true));
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawToolBrushButton(ToolBrushItem item)
        {
            bool isSelected = (_view.Mode == item.Mode) && (item.IsRoadMode
                ? _view.EdgeLayer == item.LayerOrKind
                : item.LayerOrKind switch
                {
                    0 => _view.BlockKind == 0,
                    3 => _view.BlockKind == 3,
                    _ => _view.BlockKind == 1 || _view.BlockKind == 2,
                });

            Color prevBg = GUI.backgroundColor;
            if (isSelected)
                GUI.backgroundColor = new Color(0.35f, 0.75f, 1f);

            Rect rect = GUILayoutUtility.GetRect(80f, 44f, GUILayout.ExpandWidth(true));
            if (GUI.Button(rect, GUIContent.none))
            {
                _view.Mode = item.Mode;
                if (item.IsRoadMode)
                    _view.EdgeLayer = item.LayerOrKind;
                else
                    _view.BlockKind = item.LayerOrKind;

                _view.Dragging = false;
                _view.DraggingStation = -1;
                _view.DraggingStation2 = -1;
                _view.DraggingParking = -1;
                _view.HasHover = false;
                _view.MovingAll = false;
                _selectMode = false;
                ClearSelection();
                ResetDecorInteraction();
            }

            float pad = 4f;
            Rect iconRect = new Rect(rect.x + pad, rect.y + pad, rect.width - pad * 2f, 22f);
            Rect labelRect = new Rect(rect.x + 2f, rect.y + 26f, rect.width - 4f, 16f);

            if (item.IsRoadMode && item.LayerOrKind == 0)
            {
                if (_spTileSide != null && _spTileSideRim != null)
                {
                    float side = Mathf.Min(iconRect.width, iconRect.height);
                    var center = new Vector2(iconRect.center.x, iconRect.y + side * 0.5f);
                    DrawStraightTiles(center, side / RoadCellIconSpanCells, 0f, true);
                }
            }
            else if (item.IsRoadMode && item.LayerOrKind == 1)
            {
                Texture2D tex = _spHighway != null ? GetRoadPieceTex(_spHighway, 0) : null;
                if (tex != null)
                {
                    float side = Mathf.Min(iconRect.width, iconRect.height);
                    Rect r = new Rect(iconRect.x + (iconRect.width - side) * 0.5f, iconRect.y, side, side);
                    GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit, true);
                }
            }
            else if (item.IsRoadMode && item.LayerOrKind == 4)
            {
                Texture2D tex = _spPathSide != null ? GetRoadPieceTex(_spPathSide, 0) : null;
                if (tex != null)
                {
                    float side = Mathf.Min(iconRect.width, iconRect.height);
                    Rect r = new Rect(iconRect.x + (iconRect.width - side) * 0.5f, iconRect.y, side, side);
                    GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit, true);
                }
                else
                {
                    float w = 22f, h = 22f;
                    Rect r = new Rect(iconRect.x + (iconRect.width - w) * 0.5f, iconRect.y + (iconRect.height - h) * 0.5f, w, h);
                    EditorGUI.DrawRect(r, TilePath);
                    DrawRectBorder(r, 1f, Color.black);
                }
            }
            else
            {
                Color tileColor = (item.IsRoadMode, item.LayerOrKind) switch
                {
                    (true, 2) => TileHwDecor,
                    (true, 3) => TileRoad2,
                    (false, 0) => TileStation,
                    (false, 3) => TileStation2,
                    (false, _) => TileParking,
                    _ => Color.gray,
                };
                float w = 22f;
                float h = 22f;
                if (!item.IsRoadMode && (item.LayerOrKind == 1 || item.LayerOrKind == 2)) { w = 26f; h = 13f; }

                Rect r = new Rect(iconRect.x + (iconRect.width - w) * 0.5f, iconRect.y + (iconRect.height - h) * 0.5f, w, h);
                EditorGUI.DrawRect(r, tileColor);
                DrawRectBorder(r, 1f, Color.black);
            }

            GUIStyle labelStyle = isSelected ? EditorStyles.boldLabel : EditorStyles.miniLabel;
            GUIStyle textStyle = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(labelRect, item.Name, textStyle);

            GUI.backgroundColor = prevBg;
        }

        private void DrawToolActionButtons()
        {
            Color prevBg = GUI.backgroundColor;
            EditorGUILayout.BeginHorizontal();

            if (_view.CropMode) GUI.backgroundColor = new Color(0.35f, 0.85f, 1f);
            if (GUILayout.Button(new GUIContent(_view.CropMode ? "✓ Crop Active (C)" : "Crop Tool (C)", "Toggle Canvas Crop Tool (C)"), GUILayout.Height(22f)))
            {
                _view.CropMode = !_view.CropMode;
                _view.EraserMode = false;
                _selectMode = false;
                ClearSelection();
                _view.CropDragHandle = -1;
                _view.CropDeltaLeft = _view.CropDeltaDown = _view.CropDeltaRight = _view.CropDeltaUp = 0;
            }
            GUI.backgroundColor = prevBg;

            if (_view.MoveAllMode) GUI.backgroundColor = new Color(0.35f, 0.85f, 1f);
            if (GUILayout.Button(new GUIContent(_view.MoveAllMode ? "✓ Move All (G)" : "Move All (G)",
                    "Toggle Move All (G) — không vẽ nữa; kéo chuột trái trên lưới để dịch chuyển " +
                    "TOÀN BỘ layout (đường + highway + station + station 2 + parking + decor) theo bước 1/2 ô."), GUILayout.Height(22f)))
            {
                _view.MoveAllMode = !_view.MoveAllMode;
                _view.EraserMode = false;
                _selectMode = false;
                ClearSelection();
                _view.Dragging = false;
                _view.DraggingStation = -1;
                _view.DraggingStation2 = -1;
                _view.DraggingParking = -1;
                _view.HasHover = false;
                _view.MovingAll = false;
            }
            GUI.backgroundColor = prevBg;

            EditorGUILayout.EndHorizontal();

            if (_selectMode) GUI.backgroundColor = new Color(0.35f, 0.85f, 1f);
            if (GUILayout.Button(new GUIContent(_selectMode ? "✓ Select & Move (Q)" : "Select & Move (Q)",
                    "Toggle Select & Move (Q) — không vẽ nữa; kéo chuột trái tạo khung chọn các ĐƯỜNG " +
                    "(lớp Road) trong vùng, rồi kéo gizmo để dịch cả nhóm (snap 1/2 ô; 1 ô khi có road cần bridge). " +
                    "Ảnh hưởng road · highway · HW decor · Road 2 · lối đi bộ · station · station 2 · parking · decor. " +
                    "Tool tự nối lại chỗ tách bằng đoạn thẳng (bridge)."), GUILayout.Height(22f)))
            {
                ToggleSelectMode();
            }
            GUI.backgroundColor = prevBg;

            if (_view.EraserMode) GUI.backgroundColor = new Color(1f, 0.45f, 0.4f);
            if (GUILayout.Button(new GUIContent(_view.EraserMode ? "✓ Eraser Active (E)" : "Eraser (E)",
                    "Toggle Eraser (E) — không vẽ nữa; kéo chuột trên lưới để XOÁ mọi thứ con trỏ " +
                    "chạm phải (đường + highway + hw decor + road 2 + lối đi bộ + station + station 2 + parking + decor)."), GUILayout.Height(22f)))
            {
                ToggleEraser();
            }
            GUI.backgroundColor = prevBg;

            bool empty = _doc.Edges.Count == 0 && _doc.HighwayEdges.Count == 0 && _doc.HwDecorEdges.Count == 0
                         && _doc.Road2Edges.Count == 0 && _doc.PathEdges.Count == 0
                         && _doc.Stations.Count == 0 && _doc.Stations2.Count == 0
                         && _doc.Parkings.Count == 0 && _doc.Decors.Count == 0;

            using (new EditorGUI.DisabledScope(empty))
            {
                GUI.backgroundColor = new Color(0.95f, 0.35f, 0.35f);
                if (GUILayout.Button("Clear", GUILayout.Height(22f))
                    && EditorUtility.DisplayDialog("Road Grid", "Xoá toàn bộ đường + highway + road 2 + lối đi bộ + station + station 2 + parking + decor đã vẽ?", "Xoá", "Huỷ"))
                {
                    _doc.Edges.Clear();
                    _doc.HighwayEdges.Clear();
                    _doc.HwDecorEdges.Clear();
                    _doc.Road2Edges.Clear();
                    _doc.PathEdges.Clear();
                    _doc.Stations.Clear();
                    _doc.Stations2.Clear();
                    _doc.Parkings.Clear();
                    _doc.Decors.Clear();
                    _doc.RampFlips.Clear();
                    ClearSelection();
                }
                GUI.backgroundColor = prevBg;
            }
        }

        private void DrawApplyButton()
        {
            bool empty = _doc.Edges.Count == 0 && _doc.HighwayEdges.Count == 0 && _doc.HwDecorEdges.Count == 0
                         && _doc.Road2Edges.Count == 0 && _doc.PathEdges.Count == 0
                         && _doc.Stations.Count == 0 && _doc.Stations2.Count == 0
                         && _doc.Parkings.Count == 0 && _doc.Decors.Count == 0;
            using (new EditorGUI.DisabledScope(_library == null || _applyTarget.LevelPrefab == null || empty))
            {
                Color prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.45f, 0.9f, 0.45f);
                if (GUILayout.Button(
                        new GUIContent("Apply — bake mesh + ghi file SO",
                            "Mở Level Prefab, dựng lại mesh trong node RoadParent + ghi file SO map. " +
                            "Chỉ đụng RoadParent, giữ nguyên object gameplay khác. Có confirm."),
                        GUILayout.Height(30f)))
                {
                    Apply();
                }
                GUI.backgroundColor = prevBg;
            }
        }
    }
}
#endif
