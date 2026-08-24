#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Bộ định tuyến input canvas: pan/zoom chung + vẽ/xoá đường (mode Road), rồi chuyển
    /// tiếp sang các handler mode khác (Move All / Station / Decor).</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        // Nét này bắt đầu trên layout ĐÃ chồng sẵn → miễn guard chống chồng cho cả nét (xem SetEdge).
        private bool _strokeOverlapWaived;

        // Giữ SHIFT khi kéo = khoá nét vào 1 trục (vẽ THẲNG ngang/dọc).
        private bool _lineLockActive;
        private int _lineLockAxis; // 0 = chưa chốt trục, 1 = ngang, 2 = dọc
        private Vector2Int _lineLockOrigin;
        private bool _shiftHeld; // SHIFT đang giữ — canvas hiện chỉ báo vẽ thẳng

        /// <summary>Huỷ mọi tương tác canvas ĐANG DỞ (kéo vẽ/xoá/pan/di chuyển/marquee) — dùng khi đổi
        /// tab hoặc chuyển ngữ cảnh để không kẹt cờ khiến canvas "chết" input. GIỮ NGUYÊN mode đang chọn
        /// (eraser/crop/move-all/select) và vùng chọn (Q) đã chốt.</summary>
        private void CancelCanvasInteractions()
        {
            _dragging = false;
            _erasing = false;
            _panning = false;
            _eraserPainting = false;
            _movingAll = false;
            EnsureSelectTool().Cancel();
            _draggingStation = -1;
            _draggingParking = -1;
            _cropDragHandle = -1;
            _hasHover = false;
            ResetDecorInteraction();
        }

        private void HandleCanvasInput(Rect canvas)
        {
            TrackHoverCell(canvas);
            if (HandlePan(canvas)) return;

            if (_cropMode)
            {
                HandleCropInput(canvas);
                return;
            }

            Event e = Event.current;
            if (e.type == EventType.ScrollWheel && (e.control || e.command) && canvas.Contains(e.mousePosition))
            {
                _cellPixelSize = Mathf.Clamp(_cellPixelSize - e.delta.y * 0.5f, 10f, 48f);
                e.Use();
                Repaint();
                return;
            }

            if (_selectMode)
            {
                HandleSelectInput(canvas);
                return;
            }

            if (_eraserMode)
            {
                HandleEraserInput(canvas);
                return;
            }

            if (_moveAllMode)
            {
                HandleMoveAllInput(canvas);
                return;
            }

            if (_mode == PaintMode.Station)
            {
                HandleStationInput(canvas);
                return;
            }

            if (_mode == PaintMode.Decor)
            {
                HandleDecorInput(canvas);
                return;
            }

            switch (e.type)
            {
                case EventType.MouseDown when (e.button == 0 || e.button == 1)
                                              && canvas.Contains(e.mousePosition):
                    bool hit = TryPixelToHalfPoint(canvas, e.mousePosition, out Vector2Int down2);
                    if (hit)
                    {
                        _dragging = true;
                        _erasing = e.button == 1;
                        _dragPoint = down2; // toạ độ nửa ô
                        _lineLockActive = false;
                        _lineLockAxis = 0;
                        // Chốt 1 lần đầu nét: layout đã chồng sẵn thì guard tắt suốt nét (không thì
                        // không vẽ được gì trên map cũ đang chồng).
                        _strokeOverlapWaived = !_erasing && OverlapGuardedLayer && LayoutAlreadyOverlaps();
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag when _dragging:
                    bool hitDrag = TryPixelToHalfPoint(canvas, e.mousePosition, out Vector2Int drag2);
                    if (hitDrag)
                    {
                        Vector2Int step2 = LineLockTarget(drag2, e.shift);
                        if (step2 != _dragPoint) WalkAndPaint(_dragPoint, step2);
                    }
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseUp when _dragging:
                    _dragging = false;
                    _lineLockActive = false;
                    _lineLockAxis = 0;
                    e.Use();
                    Repaint();
                    break;
            }
        }

        /// <summary>Chốt cờ giữ SHIFT để canvas hiện/ẩn chỉ báo vẽ thẳng ngay lúc bấm/nhả phím. Phải
        /// cache thành cờ vì lượt Repaint (nơi vẽ chỉ báo) không mang modifier đáng tin; đổi trạng thái
        /// thì tự Repaint để chỉ báo hiện/tắt mà không cần rê chuột.</summary>
        private void TrackShiftHeld()
        {
            Event e = Event.current;
            if (e.type == EventType.Repaint || e.type == EventType.Layout) return;
            if (e.shift == _shiftHeld) return;
            _shiftHeld = e.shift;
            Repaint();
        }

        /// <summary>Ép đích của nét về ĐƯỜNG THẲNG khi giữ SHIFT (mọi lớp nét: đường, highway, HW decor,
        /// Road 2 — vẽ trái và xoá phải như nhau). Gốc chốt lúc BẤM shift chứ không phải lúc bấm chuột, nên
        /// bật shift giữa nét vẫn thẳng tiếp từ đầu nét đang vẽ; bước di chuyển đầu tiên chốt trục theo
        /// chiều lệch NHIỀU hơn và trục đó GIỮ tới khi nhả shift (rung tay không làm gãy nét). Nhả shift →
        /// trả nguyên điểm, vẽ tự do như cũ.</summary>
        private Vector2Int LineLockTarget(Vector2Int to2, bool shift)
        {
            if (!shift)
            {
                _lineLockActive = false;
                _lineLockAxis = 0;
                return to2;
            }

            if (!_lineLockActive)
            {
                _lineLockActive = true;
                _lineLockAxis = 0;
                _lineLockOrigin = _dragPoint;
            }

            int dx = to2.x - _lineLockOrigin.x, dy = to2.y - _lineLockOrigin.y;
            if (_lineLockAxis == 0)
            {
                if (dx == 0 && dy == 0) return _lineLockOrigin;
                _lineLockAxis = Math.Abs(dx) >= Math.Abs(dy) ? 1 : 2;
            }

            return _lineLockAxis == 1
                ? new Vector2Int(to2.x, _lineLockOrigin.y)
                : new Vector2Int(_lineLockOrigin.x, to2.y);
        }

        /// <summary>Đi từng bước NỬA Ô từ from → to (toạ độ nửa ô, ưu tiên trục X trước): PAINT/ERASE
        /// đúng 1 edge nửa ô mỗi bước 1 nấc lattice (edge nay dài đúng 1 nấc — không còn gộp/dư). Không
        /// đặt/xoá edge vượt biên lattice; xoá edge chưa tồn tại là no-op (không set blocked).</summary>
        private void WalkAndPaint(Vector2Int from2, Vector2Int rawTo2)
        {
            int maxX2 = (_gridWidth - 1) * 2, maxY2 = (_gridHeight - 1) * 2;
            bool InBounds(Vector2Int p) => p.x >= 0 && p.x <= maxX2 && p.y >= 0 && p.y <= maxY2;

            Vector2Int cur = from2;
            bool blocked = false;

            void PaintAxis(bool isX, int targetCoord)
            {
                int cur0 = isX ? cur.x : cur.y;
                int dir = Math.Sign(targetCoord - cur0);
                int remaining = Math.Abs(targetCoord - cur0);
                while (remaining >= 1)
                {
                    Vector2Int next = cur;
                    if (isX) next.x += dir; else next.y += dir;
                    if (!InBounds(next)) break;
                    if (!SetEdge(cur, next, true)) blocked = true;
                    cur = next;
                    remaining -= 1;
                }
            }

            void EraseAxis(bool isX, int targetCoord)
            {
                int dir = Math.Sign(targetCoord - (isX ? cur.x : cur.y));
                if (dir == 0) return;
                while ((isX ? cur.x : cur.y) != targetCoord)
                {
                    Vector2Int next = cur;
                    if (isX) next.x += dir; else next.y += dir;
                    if (InBounds(cur) && InBounds(next)) SetEdge(cur, next, false);
                    cur = next;
                }
            }

            if (_erasing)
            {
                EraseAxis(true, rawTo2.x);
                EraseAxis(false, rawTo2.y);
            }
            else
            {
                PaintAxis(true, rawTo2.x);
                PaintAxis(false, rawTo2.y);
            }

            _dragPoint = cur;
            _overlapHint = blocked;
        }

        /// <summary>Lớp nét có guard chống chồng (0 = đường, 1 = highway, 3 = Road2; 2 = hw-decor thì
        /// không).</summary>
        // Layer 4 (PATH) cố ý KHÔNG nằm ở đây — D6/D7: PATH không bao giờ nằm chung chỗ với
        // lớp khác, và khung type-1 rộng 1 ô sẽ báo chồng oan cho mặt cắt 0.5 ô.
        private bool OverlapGuardedLayer => _edgeLayer == 0 || _edgeLayer == 1 || _edgeLayer == 3;

        /// <summary>Đặt/xoá 1 edge trên lớp đang vẽ. Với Road/Highway/Road2, nếu THÊM edge gây chồng
        /// (HW↔HW, HW↔Road, Road↔Road hoặc — D10 — Road2↔Highway/Road/Road2) thì HOÀN TÁC và trả false
        /// (chặn) — bất biến: layout luôn không chồng. Guard đo trên TOÀN layout nên chỉ giữ được bất
        /// biến khi layout vào nét đã sạch;
        /// map mở lên mà đã chồng sẵn thì <see cref="_strokeOverlapWaived"/> tắt guard, nếu không mọi
        /// nét mới đều bị hoàn tác và không vẽ thêm được gì (thanh trạng thái báo "Layout overlap").</summary>
        private bool SetEdge(Vector2Int a, Vector2Int b, bool on)
        {
            List<int> edges = ActiveEdges;
            int id = EncodeEdge(a, b);
            if (on)
            {
                if (edges.Contains(id)) return true;
                edges.Add(id);
                if (!_strokeOverlapWaived && OverlapGuardedLayer && HasAnyOverlap())
                {
                    edges.Remove(id);
                    return false;
                }
            }
            else
            {
                edges.Remove(id);
            }
            return true;
        }

        /// <summary>Cập nhật ô lưới dưới con trỏ (mọi mode) cho ô readout; repaint khi rê.</summary>
        private void TrackHoverCell(Rect canvas)
        {
            Event e = Event.current;
            if (e.type != EventType.MouseMove && e.type != EventType.MouseDrag) return;
            if (canvas.Contains(e.mousePosition))
            {
                _hoverCell = MouseToGridF(canvas, e.mousePosition);
                _hoverPixel = e.mousePosition;
                _hoverCellValid = true;
                if (!_dragging) _overlapHint = HoverWouldOverlap();
                Repaint();
            }
            else if (_hoverCellValid)
            {
                _hoverCellValid = false;
                _overlapHint = false;
                Repaint();
            }
        }

        /// <summary>Chuột GIỮA kéo để pan canvas (mọi mode). Trả true khi đã tiêu thụ sự kiện pan.</summary>
        private bool HandlePan(Rect canvas)
        {
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 2 && canvas.Contains(e.mousePosition):
                    _panning = true;
                    e.Use();
                    return true;
                case EventType.MouseDrag when _panning:
                    _scroll -= e.delta;
                    e.Use();
                    Repaint();
                    return true;
                case EventType.MouseUp when _panning && e.button == 2:
                    _panning = false;
                    e.Use();
                    return true;
            }
            return false;
        }

        private void HandleGlobalShortcuts()
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown && !EditorGUIUtility.editingTextField)
            {
                if (e.keyCode == KeyCode.C)
                {
                    _cropMode = !_cropMode;
                    _eraserMode = false;
                    _selectMode = false;
                    ClearSelection();
                    _cropDragHandle = -1;
                    _cropDeltaLeft = _cropDeltaDown = _cropDeltaRight = _cropDeltaUp = 0;
                    e.Use();
                    Repaint();
                }
                else if (e.keyCode == KeyCode.G)
                {
                    _moveAllMode = !_moveAllMode;
                    _eraserMode = false;
                    _selectMode = false;
                    ClearSelection();
                    _dragging = false;
                    _draggingStation = -1;
                    _draggingParking = -1;
                    _hasHover = false;
                    _movingAll = false;
                    e.Use();
                    Repaint();
                }
                else if (e.keyCode == KeyCode.Q)
                {
                    ToggleSelectMode();
                    e.Use();
                    Repaint();
                }
                else if (e.keyCode == KeyCode.E)
                {
                    ToggleEraser();
                    e.Use();
                    Repaint();
                }
                else if (e.keyCode == KeyCode.F)
                {
                    // Lật hướng ramp Highway→Road dưới con trỏ (toggle). Chỉ tiêu thụ phím khi trúng ramp.
                    if (TryToggleRampFlipUnderCursor()) { e.Use(); Repaint(); }
                }
                else if (_selectMode && e.keyCode == KeyCode.Escape)
                {
                    ClearSelection();
                    e.Use();
                    Repaint();
                }
                else if (_cropMode && e.keyCode == KeyCode.Escape)
                {
                    _cropMode = false;
                    _cropDragHandle = -1;
                    _cropDeltaLeft = _cropDeltaDown = _cropDeltaRight = _cropDeltaUp = 0;
                    e.Use();
                    Repaint();
                }
                else if (_cropMode && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
                {
                    if (_cropDeltaLeft != 0 || _cropDeltaDown != 0 || _cropDeltaRight != 0 || _cropDeltaUp != 0)
                    {
                        ExpandGrid(_cropDeltaLeft, _cropDeltaDown, _cropDeltaRight, _cropDeltaUp);
                    }
                    _cropMode = false;
                    _cropDragHandle = -1;
                    _cropDeltaLeft = _cropDeltaDown = _cropDeltaRight = _cropDeltaUp = 0;
                    e.Use();
                    Repaint();
                }
            }
        }

        /// <summary>Phím F: lật hướng ramp Highway→Road GẦN con trỏ nhất (toggle, nhớ theo từng ramp trong
        /// <see cref="_rampFlips"/>). Ramp neo ở đường tâm highway; nhận ramp có anchor trong bán kính ~ nửa
        /// mesh (2 ô) quanh hover. Trả true khi có ramp để lật (dữ liệu đổi → caller tiêu thụ phím + repaint,
        /// bộ theo dõi undo/dirty tự chốt 1 bước). Không trúng ramp nào → false (F không làm gì).</summary>
        private bool TryToggleRampFlipUnderCursor()
        {
            if (!_hoverCellValid || _highwayEdges.Count == 0 || _edges.Count == 0) return false;

            // roadMasks: legacy-view — CHUNG với Apply nên anchor lật (F) khớp đúng ramp sẽ bake.
            // hwMasks GIỮ DENSE (xem Apply.cs).
            int[] hwMasks = BuildMasks(_highwayEdges);
            int[] roadMasks = BuildLegacyMasksFromEdges(_edges);
            List<(int x2, int y2, int stem, int hwMask)> ramps = CollectRampJunctions(hwMasks, roadMasks);
            if (ramps.Count == 0) return false;

            // Hover (ô) → nửa ô; chọn anchor ramp gần nhất. Anchor KHÔNG đổi khi lật nên toggle luôn trúng.
            float hx2 = _hoverCell.x * 2f, hy2 = _hoverCell.y * 2f;
            int best = -1;
            float bestSq = float.MaxValue;
            for (int i = 0; i < ramps.Count; i++)
            {
                float dx = ramps[i].x2 - hx2, dy = ramps[i].y2 - hy2;
                float sq = dx * dx + dy * dy;
                if (sq < bestSq) { bestSq = sq; best = i; }
            }

            float reach = RampDetector.RampHalfWidthCells * 2f + 2f; // nửa ô: quanh mesh 4×4, rộng tay 1 chút
            if (best < 0 || bestSq > reach * reach) return false;

            int key = RampAnchorKey(ramps[best].x2, ramps[best].y2);
            if (!_rampFlips.Remove(key)) _rampFlips.Add(key);
            _rampFlips.Sort();
            return true;
        }

        private void HandleCropInput(Rect canvas)
        {
            Event e = Event.current;
            Rect[] handles = GetCropHandleRects(canvas, out Rect _);

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:
                    for (int i = 0; i < handles.Length; i++)
                    {
                        if (handles[i].Contains(e.mousePosition))
                        {
                            _cropDragHandle = i;
                            _cropDragStartMouse = e.mousePosition;
                            e.Use();
                            Repaint();
                            return;
                        }
                    }
                    break;

                case EventType.MouseDrag when _cropDragHandle >= 0:
                    Vector2 diff = e.mousePosition - _cropDragStartMouse;
                    int cellDx = Mathf.RoundToInt(diff.x / _cellPixelSize);
                    int cellDy = Mathf.RoundToInt(diff.y / _cellPixelSize);

                    int dL = 0, dR = 0, dU = 0, dD = 0;

                    if (_cropDragHandle == 0 || _cropDragHandle == 6 || _cropDragHandle == 7) // Left
                        dL = -cellDx;
                    else if (_cropDragHandle == 2 || _cropDragHandle == 3 || _cropDragHandle == 4) // Right
                        dR = cellDx;

                    if (_cropDragHandle == 0 || _cropDragHandle == 1 || _cropDragHandle == 2) // Top (Up)
                        dU = -cellDy;
                    else if (_cropDragHandle == 4 || _cropDragHandle == 5 || _cropDragHandle == 6) // Bottom (Down)
                        dD = cellDy;

                    dL = Mathf.Clamp(dL, 2 - _gridWidth, MaxGridSize - _gridWidth);
                    dR = Mathf.Clamp(dR, 2 - _gridWidth, MaxGridSize - _gridWidth);
                    dD = Mathf.Clamp(dD, 2 - _gridHeight, MaxGridSize - _gridHeight);
                    dU = Mathf.Clamp(dU, 2 - _gridHeight, MaxGridSize - _gridHeight);

                    _cropDeltaLeft = dL;
                    _cropDeltaRight = dR;
                    _cropDeltaDown = dD;
                    _cropDeltaUp = dU;

                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseUp when _cropDragHandle >= 0 && e.button == 0:
                    if (_cropDeltaLeft != 0 || _cropDeltaDown != 0 || _cropDeltaRight != 0 || _cropDeltaUp != 0)
                    {
                        ExpandGrid(_cropDeltaLeft, _cropDeltaDown, _cropDeltaRight, _cropDeltaUp);
                        _cropDeltaLeft = _cropDeltaDown = _cropDeltaRight = _cropDeltaUp = 0;
                    }
                    _cropDragHandle = -1;
                    e.Use();
                    Repaint();
                    break;
            }
        }
    }
}
#endif
