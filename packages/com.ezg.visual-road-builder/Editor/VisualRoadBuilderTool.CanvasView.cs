#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Khung phải: pane canvas (legend + hint + vùng vẽ cuộn được) và thanh trạng thái đáy.</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        private void DrawCanvasPane(float paneWidth)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(paneWidth), GUILayout.ExpandHeight(true));
            DrawCanvasHeader();
            DrawCanvas(paneWidth);
            EditorGUILayout.EndVertical();
        }

        /// <summary>Header trên canvas: dòng hint thao tác mờ.</summary>
        private void DrawCanvasHeader()
        {
            EditorGUILayout.LabelField(CurrentHint(), HintStyle);
        }

        /// <summary>Dòng hint thao tác theo mode/lớp đang chọn.</summary>
        private string CurrentHint()
        {
            if (_selectMode)
                return EnsureSelectTool().HasSelection
                    ? "SELECT (Q) — Drag the gizmo to move everything selected (road · highway · HW decor · Road 2 · lối đi bộ · station · park · decor, snap 1/2 cell; 1 cell when roads need bridging) · only the Road layer auto-links with straight connectors · Esc: clear · Q: exit"
                    : "SELECT (Q) — Left-drag a box to select everything inside it · then drag to move · Esc/Q: exit";
            if (_eraserMode)
                return "ERASER (E) — Left drag to erase anything you touch (road · highway · HW decor · Road 2 · lối đi bộ · station · park · decor) · Press E to exit";
            if (_cropMode)
                return "CROP TOOL (C) — Drag 8 edge/corner handles to resize canvas · Press C or Enter to apply · Esc to cancel";
            if (_moveAllMode)
                return "MOVE ALL — Left drag to move entire layout (snap ½ cell)";
            return _mode switch
            {
                PaintMode.Road => _edgeLayer switch
                {
                    1 => "Highway (red) — Left drag: draw · Right drag: erase (snap 1/2 cell) · Hold Shift: straight line · F: flip ramp under cursor",
                    2 => "HW Decor (white, 1 prefab per segment) — Left: draw · Right: erase · Hold Shift: straight line",
                    3 => "Road 2 (wide, 3-cell) — Left drag: draw · Right drag: erase (snap 1/2 cell) · Hold Shift: straight line",
                    4 => "Lối đi bộ (0.5 ô) — Kéo trái: vẽ · Kéo phải: xoá (snap 1/2 ô) · Giữ Shift: đường thẳng",
                    _ => "Road — Left drag: draw · Right drag: erase (snap 1/2 cell) · Hold Shift: straight line · F: flip Highway→Road ramp under cursor",
                },
                PaintMode.Decor => "Decor — Left: place/scatter (drag item to move) · Right: erase · R: rotate",
                _ => "Station/Parking — Left: place/drag block (snap ½ cell) · Right: erase · R: rotate",
            };
        }

        private void DrawCanvas(float paneWidth)
        {
            float ps = _cellPixelSize;
            float cw = GutterLeft + GutterRight + OuterMargin * 2f + (_gridWidth - 1) * ps;
            float ch = GutterTop + GutterBottom + OuterMargin * 2f + (_gridHeight - 1) * ps;

            _scroll = EditorGUILayout.BeginScrollView(
                _scroll, GUILayout.Width(paneWidth), GUILayout.ExpandHeight(true));
            Rect canvas = GUILayoutUtility.GetRect(cw, ch, GUILayout.Width(cw), GUILayout.Height(ch));
            HandleCanvasInput(canvas);
            if (Event.current.type == EventType.Repaint)
                DrawCanvasContent(canvas);
            EditorGUILayout.EndScrollView();

            Rect scrollRect = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.Repaint)
                DrawAxisRulersOverlay(canvas, scrollRect);
        }

        /// <summary>Thanh trạng thái đáy: đếm số mảnh + kích thước lưới.</summary>
        private void DrawStatusBar()
        {
            int total = CountPieces(BuildMasks(_edges));
            int highwayTotal = CountPieces(BuildMasks(_highwayEdges));

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                $"Pieces {total}     Highway {highwayTotal}     Station {_stations.Count}     " +
                $"Parking {_parkings.Count}     Decor {_decors.Count}",
                EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (_overlapHint)
                EditorGUILayout.LabelField("Overlapping", OverlapStyle, GUILayout.Width(80f));
            // Layout nạp lên đã chồng sẵn: guard chống chồng tự tắt (xem SetEdge) — báo để còn dọn.
            if (LayoutAlreadyOverlaps())
                EditorGUILayout.LabelField(
                    new GUIContent("Layout overlap",
                        "Layout hiện tại đã có chỗ chồng (road↔road, road↔highway hoặc highway↔highway). " +
                        "Guard chống chồng tạm tắt để vẫn vẽ được — hãy xoá/dời chỗ chồng rồi vẽ tiếp."),
                    OverlapStyle, GUILayout.Width(90f));
            EditorGUILayout.LabelField($"Grid {_gridWidth} × {_gridHeight}",
                EditorStyles.miniLabel, GUILayout.Width(80f));
            EditorGUILayout.LabelField("Zoom", EditorStyles.miniLabel, GUILayout.Width(30f));
            _cellPixelSize = GUILayout.HorizontalSlider(_cellPixelSize, 10f, 48f, GUILayout.Width(60f));
            EditorGUILayout.LabelField($"{_cellPixelSize:F0}",
                EditorStyles.miniLabel, GUILayout.Width(22f));
            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
