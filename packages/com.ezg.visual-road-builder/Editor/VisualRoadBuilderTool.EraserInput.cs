#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Eraser tool (phím E): mode toggle độc lập với brush — kéo chuột trên lưới để XOÁ mọi
    /// thứ con trỏ chạm phải, không phân biệt lớp (đường + highway + hw decor + road 2 + station +
    /// parking + decor). Không đặt gì, chỉ xoá.</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        // Bán kính (ô) tính từ con trỏ để bắt edge/decor cần xoá — khớp cỡ con trỏ 1 ô (nửa cạnh 0.5).
        private const float EraseRadiusCells = 0.45f;

        private bool _eraserPainting;

        /// <summary>Bật/tắt Eraser; bật thì tắt Crop/Move All và reset mọi trạng thái kéo dở.</summary>
        private void ToggleEraser()
        {
            _eraserMode = !_eraserMode;
            if (_eraserMode)
            {
                _cropMode = false;
                _moveAllMode = false;
                _selectMode = false;
                ClearSelection();
            }
            _eraserPainting = false;
            _dragging = false;
            _draggingStation = -1;
            _draggingStation2 = -1;
            _draggingParking = -1;
            _hasHover = false;
            _movingAll = false;
        }

        private void HandleEraserInput(Rect canvas)
        {
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when (e.button == 0 || e.button == 1)
                                              && canvas.Contains(e.mousePosition):
                    _eraserPainting = true;
                    EraseAt(canvas, e.mousePosition);
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseDrag when _eraserPainting:
                    EraseAt(canvas, e.mousePosition);
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseUp when _eraserPainting:
                    _eraserPainting = false;
                    e.Use();
                    Repaint();
                    break;
            }
        }

        /// <summary>Xoá mọi phần tử của MỌI lớp nằm dưới con trỏ tại một vị trí chuột.</summary>
        private void EraseAt(Rect canvas, Vector2 mouse)
        {
            Vector2 f = MouseToGridF(canvas, mouse);
            EraseEdgesAt(_edges, f);
            EraseEdgesAt(_highwayEdges, f);
            EraseEdgesAt(_hwDecorEdges, f);
            EraseEdgesAt(_road2Edges, f);
            EraseEdgesAt(_pathEdges, f);
            EraseBlocksAt(f);
            EraseDecorsAt(f);
        }

        private static void EraseEdgesAt(List<int> edges, Vector2 f)
            => edges.RemoveAll(id => DistPointToEdge(id, f) <= EraseRadiusCells);

        /// <summary>Khoảng cách (ô) từ điểm f tới đoạn edge (dài nửa ô, ngang hoặc dọc).</summary>
        private static float DistPointToEdge(int id, Vector2 f)
        {
            DecodeEdge(id, out int x2, out int y2, out int orient);
            var a = new Vector2(x2 * 0.5f, y2 * 0.5f);
            Vector2 ab = (orient == 0 ? Vector2.right : Vector2.up) * 0.5f; // edge dài đúng nửa ô
            float t = Mathf.Clamp01(Vector2.Dot(f - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(f, a + ab * t);
        }

        private void EraseBlocksAt(Vector2 f)
        {
            float px2 = f.x * 2f, py2 = f.y * 2f;
            int s2 = StationSize * 2;
            _stations.RemoveAll(id =>
            {
                DecodeStation(id, out int x2, out int y2, out _);
                return px2 >= x2 && px2 <= x2 + s2 && py2 >= y2 && py2 <= y2 + s2;
            });
            _stations2.RemoveAll(id =>
            {
                DecodeStation(id, out int x2, out int y2, out _);
                return px2 >= x2 && px2 <= x2 + s2 && py2 >= y2 && py2 <= y2 + s2;
            });
            _parkings.RemoveAll(id =>
            {
                DecodeParking(id, out int x2, out int y2, out int orient);
                Vector2Int k = ParkingCells(orient);
                return px2 >= x2 && px2 <= x2 + k.x * 2 && py2 >= y2 && py2 <= y2 + k.y * 2;
            });
        }

        private void EraseDecorsAt(Vector2 f)
            => _decors.RemoveAll(d =>
                Vector2.Distance(f, new Vector2(d.x2 * 0.5f, d.y2 * 0.5f)) <= EraseRadiusCells);

        /// <summary>Con trỏ eraser: ô đỏ mờ cỡ 1 ô tại vị trí chuột cho thấy vùng sẽ xoá.</summary>
        private void DrawEraserCursor(Rect canvas)
        {
            if (!_hoverCellValid) return;
            float r = _cellPixelSize * EraseRadiusCells;
            var rect = new Rect(_hoverPixel.x - r, _hoverPixel.y - r, r * 2f, r * 2f);
            EditorGUI.DrawRect(rect, new Color(1f, 0.3f, 0.25f, 0.22f));
            DrawRectBorder(rect, 1.5f, new Color(1f, 0.35f, 0.3f, 0.9f));
        }
    }
}
#endif
