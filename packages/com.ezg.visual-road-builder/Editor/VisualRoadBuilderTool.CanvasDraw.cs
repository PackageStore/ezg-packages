#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Vẽ nền canvas: lưới + trục toạ độ + ô readout theo con trỏ, tô ô đường dạng phẳng
    /// (hw-decor / ghost hover) và điều phối toàn bộ lượt vẽ nội dung.</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        private void DrawCanvasContent(Rect canvas)
        {
            EditorGUI.DrawRect(canvas, new Color(0.13f, 0.13f, 0.13f));
            DrawGridLines(canvas);

            DrawRoadSprites(canvas, _edges);
            DrawHighwaySprites(canvas, _highwayEdges);
            DrawRoad2Sprites(canvas, _road2Edges);
            DrawPathSprites(canvas, _pathEdges);
            DrawRoadTiles(canvas, _hwDecorEdges, TileHwDecor);
            DrawRoadHoverGhost(canvas);
            DrawShiftLineGuide(canvas);

            if (_dragging)
            {
                Vector2 p = PointToPixelF(canvas, _dragPoint.x * 0.5f, _dragPoint.y * 0.5f);
                EditorGUI.DrawRect(new Rect(p.x - 5f, p.y - 5f, 10f, 10f),
                    _erasing ? Color.red : Color.cyan);
            }

            DrawStations(canvas);
            DrawDecors(canvas);
            if (AnyDebugBoundary) DrawDebugBoundaries(canvas);

            if (_eraserMode) DrawEraserCursor(canvas);
            if (_cropMode) DrawCropOverlay(canvas);
            if (_selectMode) DrawSelectOverlay(canvas);
            DrawHoverReadout(canvas);
        }

        /// <summary>Chỉ báo "giữ SHIFT = vẽ thẳng" (chỉ mode Road, mọi lớp nét). CHƯA chốt trục → chữ
        /// THẬP mờ qua điểm snap dưới con trỏ, cho thấy hai trục đều còn khả dụng. Đang kéo và ĐÃ chốt
        /// trục → đường dẫn mờ chạy hết lưới theo trục đó + đoạn ĐẬM từ gốc khoá tới đầu nét đang vẽ.
        /// Cyan = vẽ, đỏ = xoá (khớp chấm đầu nét).</summary>
        private void DrawShiftLineGuide(Rect canvas)
        {
            if (!_shiftHeld || _mode != PaintMode.Road) return;
            if (_eraserMode || _selectMode || _moveAllMode || _cropMode) return;

            bool locked = _dragging && _lineLockActive && _lineLockAxis != 0;
            Vector2Int origin2;
            if (_dragging)
                origin2 = locked ? _lineLockOrigin : _dragPoint;
            else if (_hoverCellValid)
                origin2 = new Vector2Int(
                    Mathf.RoundToInt(_hoverCell.x * 2f), Mathf.RoundToInt(_hoverCell.y * 2f));
            else
                return;

            Color tint = _dragging && _erasing
                ? new Color(1f, 0.35f, 0.30f)
                : new Color(0.35f, 0.90f, 1f);
            var faint = new Color(tint.r, tint.g, tint.b, 0.32f);

            Vector2 o = PointToPixelF(canvas, origin2.x * 0.5f, origin2.y * 0.5f);
            float left = PointToPixelF(canvas, 0f, 0f).x;
            float right = PointToPixelF(canvas, _gridWidth - 1, 0f).x;
            float bottom = PointToPixelF(canvas, 0f, 0f).y;
            float top = PointToPixelF(canvas, 0f, _gridHeight - 1).y;

            if (!locked || _lineLockAxis == 1)
                EditorGUI.DrawRect(new Rect(left, o.y - 0.5f, right - left, 1f), faint);
            if (!locked || _lineLockAxis == 2)
                EditorGUI.DrawRect(new Rect(o.x - 0.5f, top, 1f, bottom - top), faint);

            if (locked)
            {
                Vector2 head = PointToPixelF(canvas, _dragPoint.x * 0.5f, _dragPoint.y * 0.5f);
                const float t = 3f;
                EditorGUI.DrawRect(_lineLockAxis == 1
                    ? new Rect(Mathf.Min(o.x, head.x), o.y - t * 0.5f, Mathf.Abs(head.x - o.x), t)
                    : new Rect(o.x - t * 0.5f, Mathf.Min(o.y, head.y), t, Mathf.Abs(head.y - o.y)), tint);
            }

            EditorGUI.DrawRect(new Rect(o.x - 3f, o.y - 3f, 6f, 6f), tint);
        }

        /// <summary>Lưới ô: nét mờ mỗi ô, nét đậm hơn mỗi AxisStep ô; trục toạ độ 0 (X=0 và Y=0)
        /// tô xanh và DÀY hơn để dễ định vị gốc.</summary>
        private void DrawGridLines(Rect canvas)
        {
            var line = new Color(1f, 1f, 1f, 0.06f);
            var major = new Color(1f, 1f, 1f, 0.13f);
            var axis = new Color(0.45f, 0.72f, 1f, 0.75f);
            int step = AxisStep();
            int zeroX = -_originCell.x, zeroY = -_originCell.y; // ô local có toạ độ hiển thị = 0

            float top = PointToPixelF(canvas, 0f, _gridHeight - 1).y;
            float h = (_gridHeight - 1) * _cellPixelSize;
            for (int x = 0; x < _gridWidth; x++)
            {
                float px = PointToPixelF(canvas, x, 0f).x;
                if (x == zeroX) EditorGUI.DrawRect(new Rect(px - 1f, top, 2f, h), axis);
                else EditorGUI.DrawRect(new Rect(px, top, 1f, h), x % step == 0 ? major : line);
            }
            float left = PointToPixelF(canvas, 0f, 0f).x;
            float w = (_gridWidth - 1) * _cellPixelSize;
            for (int y = 0; y < _gridHeight; y++)
            {
                float py = PointToPixelF(canvas, 0f, y).y;
                if (y == zeroY) EditorGUI.DrawRect(new Rect(left, py - 1f, w, 2f), axis);
                else EditorGUI.DrawRect(new Rect(left, py, w, 1f), y % step == 0 ? major : line);
            }
        }

        /// <summary>Bước đánh số trục (ô) sao cho nhãn cách nhau tối thiểu ~34px theo zoom.</summary>
        private int AxisStep()
        {
            int raw = Mathf.Max(1, Mathf.CeilToInt(34f / _cellPixelSize));
            int[] nice = { 1, 2, 5, 10, 20, 25, 50, 100 };
            foreach (int n in nice)
                if (n >= raw) return n;
            return raw;
        }

        /// <summary>Vẽ thước tỉ lệ trục X (mép trên window pane) & Y (mép trái window pane) cố định kiểu Photoshop.</summary>
        private void DrawAxisRulersOverlay(Rect canvas, Rect scrollRect)
        {
            const float rulerW = 32f;
            const float rulerH = 20f;

            GUI.BeginGroup(scrollRect);

            var bgRuler = new Color(0.17f, 0.17f, 0.17f, 0.96f);
            var bgCorner = new Color(0.14f, 0.14f, 0.14f, 1f);
            var borderColor = new Color(1f, 1f, 1f, 0.15f);
            var tickColor = new Color(1f, 1f, 1f, 0.45f);

            EditorGUI.DrawRect(new Rect(rulerW, 0f, scrollRect.width - rulerW, rulerH), bgRuler);
            EditorGUI.DrawRect(new Rect(0f, rulerH, rulerW, scrollRect.height - rulerH), bgRuler);
            EditorGUI.DrawRect(new Rect(0f, 0f, rulerW, rulerH), bgCorner);

            EditorGUI.DrawRect(new Rect(rulerW, rulerH - 1f, scrollRect.width - rulerW, 1f), borderColor);
            EditorGUI.DrawRect(new Rect(rulerW - 1f, rulerH, 1f, scrollRect.height - rulerH), borderColor);

            int step = AxisStep();
            for (int x = 0; x < _gridWidth; x += step)
            {
                float pxCanvas = PointToPixelF(canvas, x, 0f).x;
                float screenX = pxCanvas - _scroll.x;

                if (screenX >= rulerW && screenX <= scrollRect.width)
                {
                    EditorGUI.DrawRect(new Rect(screenX, rulerH - 6f, 1f, 6f), tickColor);
                    GUI.Label(new Rect(screenX - 20f, 1f, 40f, rulerH - 4f),
                        (x + _originCell.x).ToString(), RulerXStyle);
                }
            }

            for (int y = 0; y < _gridHeight; y += step)
            {
                float pyCanvas = PointToPixelF(canvas, 0f, y).y;
                float screenY = pyCanvas - _scroll.y;

                if (screenY >= rulerH && screenY <= scrollRect.height)
                {
                    EditorGUI.DrawRect(new Rect(rulerW - 6f, screenY, 6f, 1f), tickColor);
                    GUI.Label(new Rect(1f, screenY - 8f, rulerW - 7f, 16f),
                        (y + _originCell.y).ToString(), RulerYStyle);
                }
            }

            Vector2 mouseWin = Event.current.mousePosition;
            float mouseLocalX = mouseWin.x - scrollRect.x;
            float mouseLocalY = mouseWin.y - scrollRect.y;
            var cursorTickColor = new Color(0.4f, 0.8f, 1f, 0.85f);

            if (mouseLocalX >= rulerW && mouseLocalX <= scrollRect.width &&
                mouseLocalY >= 0 && mouseLocalY <= scrollRect.height)
            {
                EditorGUI.DrawRect(new Rect(mouseLocalX, 0f, 1f, rulerH), cursorTickColor);
            }
            if (mouseLocalY >= rulerH && mouseLocalY <= scrollRect.height &&
                mouseLocalX >= 0 && mouseLocalX <= scrollRect.width)
            {
                EditorGUI.DrawRect(new Rect(0f, mouseLocalY, rulerW, 1f), cursorTickColor);
            }

            GUI.Label(new Rect(0f, 2f, rulerW, rulerH - 2f), "XY", RulerXStyle);

            GUI.EndGroup();
        }

        /// <summary>8 handles for Crop Tool: 0=TL, 1=T, 2=TR, 3=R, 4=BR, 5=B, 6=BL, 7=L</summary>
        private Rect[] GetCropHandleRects(Rect canvas, out Rect proposedCropRect)
        {
            float pL = PointToPixelF(canvas, -_cropDeltaLeft, 0).x;
            float pR = PointToPixelF(canvas, _gridWidth - 1 + _cropDeltaRight, 0).x;
            float pB = PointToPixelF(canvas, 0, -_cropDeltaDown).y;
            float pT = PointToPixelF(canvas, 0, _gridHeight - 1 + _cropDeltaUp).y;

            float minX = Mathf.Min(pL, pR);
            float maxX = Mathf.Max(pL, pR);
            float minY = Mathf.Min(pT, pB);
            float maxY = Mathf.Max(pT, pB);

            proposedCropRect = new Rect(minX, minY, maxX - minX, maxY - minY);

            const float hs = 12f;
            const float hhs = hs * 0.5f;

            float midX = (minX + maxX) * 0.5f;
            float midY = (minY + maxY) * 0.5f;

            return new Rect[]
            {
                new Rect(minX - hhs, minY - hhs, hs, hs), // 0: TopLeft
                new Rect(midX - hhs, minY - hhs, hs, hs), // 1: Top
                new Rect(maxX - hhs, minY - hhs, hs, hs), // 2: TopRight
                new Rect(maxX - hhs, midY - hhs, hs, hs), // 3: Right
                new Rect(maxX - hhs, maxY - hhs, hs, hs), // 4: BottomRight
                new Rect(midX - hhs, maxY - hhs, hs, hs), // 5: Bottom
                new Rect(minX - hhs, maxY - hhs, hs, hs), // 6: BottomLeft
                new Rect(minX - hhs, midY - hhs, hs, hs)  // 7: Left
            };
        }

        private void DrawCropOverlay(Rect canvas)
        {
            Rect[] handles = GetCropHandleRects(canvas, out Rect cropRect);

            var dark = new Color(0f, 0f, 0f, 0.55f);
            EditorGUI.DrawRect(new Rect(canvas.x, canvas.y, canvas.width, Mathf.Max(0f, cropRect.y - canvas.y)), dark);
            EditorGUI.DrawRect(new Rect(canvas.x, cropRect.yMax, canvas.width, Mathf.Max(0f, canvas.yMax - cropRect.yMax)), dark);
            EditorGUI.DrawRect(new Rect(canvas.x, cropRect.y, Mathf.Max(0f, cropRect.x - canvas.x), cropRect.height), dark);
            EditorGUI.DrawRect(new Rect(cropRect.xMax, cropRect.y, Mathf.Max(0f, canvas.xMax - cropRect.xMax), cropRect.height), dark);

            var frameColor = new Color(0.2f, 0.85f, 1f, 0.95f);
            DrawRectBorder(cropRect, 2f, frameColor);

            Vector2 mouse = Event.current.mousePosition;
            for (int i = 0; i < handles.Length; i++)
            {
                bool isHover = handles[i].Contains(mouse) || _cropDragHandle == i;
                Color fill = isHover ? new Color(0.35f, 0.85f, 1f) : Color.white;
                EditorGUI.DrawRect(handles[i], fill);
                DrawRectBorder(handles[i], 1f, Color.black);
            }

            int newW = _gridWidth + _cropDeltaLeft + _cropDeltaRight;
            int newH = _gridHeight + _cropDeltaDown + _cropDeltaUp;
            string text = $"Crop: {newW} × {newH}  (L:{_cropDeltaLeft:+#;-#;0} D:{_cropDeltaDown:+#;-#;0} R:{_cropDeltaRight:+#;-#;0} U:{_cropDeltaUp:+#;-#;0})";

            var tagRect = new Rect(cropRect.x, cropRect.y - 22f, 290f, 20f);
            if (tagRect.y < canvas.y + GutterTop) tagRect.y = cropRect.y + 4f;
            EditorGUI.DrawRect(tagRect, new Color(0.1f, 0.1f, 0.1f, 0.85f));
            GUI.Label(tagRect, text, TagStyle);
        }

        /// <summary>Ô toạ độ nhỏ bám theo con trỏ khi rê trên lưới (đọc x/y theo ô).</summary>
        private void DrawHoverReadout(Rect canvas)
        {
            if (!_hoverCellValid) return;
            // Mọi mode giờ snap 1/2 ô (đường/highway/station/parking/decor/move all) → readout nửa ô.
            float hx = Mathf.Clamp(Mathf.RoundToInt(_hoverCell.x * 2f) * 0.5f, 0f, _gridWidth - 1) + _originCell.x;
            float hy = Mathf.Clamp(Mathf.RoundToInt(_hoverCell.y * 2f) * 0.5f, 0f, _gridHeight - 1) + _originCell.y;

            var r = new Rect(_hoverPixel.x + 14f, _hoverPixel.y + 16f, 104f, 17f);
            if (r.xMax > canvas.xMax) r.x = _hoverPixel.x - r.width - 8f;
            if (r.yMax > canvas.yMax) r.y = _hoverPixel.y - r.height - 8f;
            EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.78f));
            GUI.Label(r, $"x {hx:0.#}   y {hy:0.#}", PillStyle);
        }

        /// <summary>Mỗi điểm lattice có nét (mask != 0) tô 1 ô đặc — đúng chỗ Apply đặt 1 mảnh prefab;
        /// điểm kề nhau cách đúng 1 ô nên các ô khít lại thành dải đường liền.</summary>
        private void DrawRoadTiles(Rect canvas, List<int> edges, Color color)
        {
            if (edges.Count == 0) return;
            int[] masks = BuildMasks(edges);
            int lw = LatticeW, lh = LatticeH;
            float cell = _cellPixelSize;
            for (int y = 0; y < lh; y++)
            {
                for (int x = 0; x < lw; x++)
                {
                    if (masks[y * lw + x] == 0) continue;
                    Vector2 p = PointToPixelF(canvas, x * 0.5f, y * 0.5f);
                    EditorGUI.DrawRect(new Rect(p.x - cell * 0.5f, p.y - cell * 0.5f, cell, cell), color);
                }
            }
        }

        /// <summary>Ghost mờ (nửa opacity) tại ô dưới con trỏ — cho user thấy sắp đặt gì. Đường và
        /// highway đều preview bằng 1 Ô ghép từ ô modular; hw-decor không có sprite → ô trắng 50%.
        /// Khi chỗ đó sẽ chồng (_overlapHint) → tô ĐỎ. Chỉ ở mode Road và khi KHÔNG kéo
        /// (lúc kéo có marker riêng).</summary>
        private void DrawRoadHoverGhost(Rect canvas)
        {
            if (_mode != PaintMode.Road || _eraserMode || _selectMode || _dragging || !_hoverCellValid) return;
            // Đường + highway đều snap 1/2 ô (footprint dọc trục vẽ dài nửa ô, bề ngang giữ nguyên).
            float hx = Mathf.Clamp(Mathf.RoundToInt(_hoverCell.x * 2f) * 0.5f, 0f, _gridWidth - 1);
            float hy = Mathf.Clamp(Mathf.RoundToInt(_hoverCell.y * 2f) * 0.5f, 0f, _gridHeight - 1);
            float cell = _cellPixelSize;
            float h = _edgeLayer == 1 ? cell * 2f
                : _edgeLayer == 3 ? cell * Road2CrossSectionWidthCells
                : _edgeLayer == 4 ? cell * 0.5f
                : cell;
            Vector2 p = PointToPixelF(canvas, hx, hy);
            var rect = new Rect(p.x - cell * 0.25f, p.y - h * 0.5f, cell * 0.5f, h);
            bool overlap = _overlapHint;

            EnsureRoadSprites();
            Color ghost = overlap ? new Color(1f, 0.25f, 0.20f, 0.75f) : new Color(1f, 1f, 1f, 0.5f);

            if (_edgeLayer == 0 && _spTileSide != null && _spTileSideRim != null)
            {
                Color prevTile = GUI.color;
                GUI.color = ghost;
                DrawStraightTiles(p, cell, 0f, false);
                GUI.color = prevTile;
                return;
            }

            if (_edgeLayer == 1 && _spHighway != null && _spHighwayRim != null)
            {
                Color prev = GUI.color;
                GUI.color = ghost;
                DrawHighwayColumn(p, cell, 0f);
                GUI.color = prev;
                return;
            }

            if (_edgeLayer == 3 && _spTileSide != null && _spTileSideRim != null)
            {
                Color prevRoad2 = GUI.color;
                GUI.color = ghost;
                DrawRoad2StraightTiles(canvas, hx, hy, 0f, false);
                GUI.color = prevRoad2;
                return;
            }

            Color c = overlap ? TileHighway : _edgeLayer == 2 ? TileHwDecor : _edgeLayer == 3 ? TileRoad2 : _edgeLayer == 4 ? TilePath : TileRoad;
            c.a = 0.5f;
            EditorGUI.DrawRect(rect, c);
        }
    }
}
#endif
