#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Hover readout tooltip + shift-line guide + road hover ghost.</summary>
    internal sealed class HoverRenderer
    {
        // (Road2FillerLateralOffset + 0.25f) * 2 = 1.75 — bề ngang mặt cắt Road 2 tính theo ô.
        private const float Road2CrossSectionWidthCells = 1.75f;

        private readonly ToolContext _ctx;
        private readonly ToolStyles _styles;
        private readonly Func<bool> _getShiftHeld;
        private readonly Func<bool> _getOverlapHint;

        // Sprite renderers for ghost preview — injected from sprite layer.
        private readonly Action<Vector2, float, float, bool> _drawStraightTiles;
        private readonly Action<Vector2, float, float> _drawHighwayColumn;
        private readonly Action<Rect, float, float, float, bool> _drawRoad2StraightTiles;
        private readonly Action _ensureRoadSprites;
        private readonly Func<Sprite> _getSpTileSide;
        private readonly Func<Sprite> _getSpTileSideRim;
        private readonly Func<Sprite> _getSpHighway;
        private readonly Func<Sprite> _getSpHighwayRim;

        internal HoverRenderer(ToolContext ctx, ToolStyles styles,
            Func<bool> getShiftHeld, Func<bool> getOverlapHint,
            Action<Vector2, float, float, bool> drawStraightTiles,
            Action<Vector2, float, float> drawHighwayColumn,
            Action<Rect, float, float, float, bool> drawRoad2StraightTiles,
            Action ensureRoadSprites,
            Func<Sprite> getSpTileSide, Func<Sprite> getSpTileSideRim,
            Func<Sprite> getSpHighway, Func<Sprite> getSpHighwayRim)
        {
            _ctx = ctx;
            _styles = styles;
            _getShiftHeld = getShiftHeld;
            _getOverlapHint = getOverlapHint;
            _drawStraightTiles = drawStraightTiles;
            _drawHighwayColumn = drawHighwayColumn;
            _drawRoad2StraightTiles = drawRoad2StraightTiles;
            _ensureRoadSprites = ensureRoadSprites;
            _getSpTileSide = getSpTileSide;
            _getSpTileSideRim = getSpTileSideRim;
            _getSpHighway = getSpHighway;
            _getSpHighwayRim = getSpHighwayRim;
        }

        /// <summary>Chỉ báo "giữ SHIFT = vẽ thẳng" (chỉ mode Road, mọi lớp nét). CHƯA chốt trục → chữ
        /// THẬP mờ qua điểm snap dưới con trỏ, cho thấy hai trục đều còn khả dụng. Đang kéo và ĐÃ chốt
        /// trục → đường dẫn mờ chạy hết lưới theo trục đó + đoạn ĐẬM từ gốc khoá tới đầu nét đang vẽ.
        /// Cyan = vẽ, đỏ = xoá (khớp chấm đầu nét).</summary>
        internal void DrawShiftLineGuide(Rect canvas, RoadPaintTool roadPaint)
        {
            var view = _ctx.View;
            var doc = _ctx.Doc;
            if (!_getShiftHeld() || view.Mode != PaintMode.Road) return;
            if (view.EraserMode || view.MoveAllMode || view.CropMode) return;

            bool locked = view.Dragging && roadPaint != null
                          && roadPaint.LineLockActive && roadPaint.LineLockAxis != 0;
            Vector2Int origin2;
            if (view.Dragging)
                origin2 = locked ? roadPaint.LineLockOrigin : view.DragPoint;
            else if (view.HoverCellValid)
                origin2 = new Vector2Int(
                    Mathf.RoundToInt(view.HoverCell.x * 2f), Mathf.RoundToInt(view.HoverCell.y * 2f));
            else
                return;

            Color tint = view.Dragging && view.Erasing
                ? new Color(1f, 0.35f, 0.30f)
                : new Color(0.35f, 0.90f, 1f);
            var faint = new Color(tint.r, tint.g, tint.b, 0.32f);

            Vector2 o = CoordHelper.PointToPixelF(canvas, origin2.x * 0.5f, origin2.y * 0.5f, doc, view);
            float left = CoordHelper.PointToPixelF(canvas, 0f, 0f, doc, view).x;
            float right = CoordHelper.PointToPixelF(canvas, doc.GridWidth - 1, 0f, doc, view).x;
            float bottom = CoordHelper.PointToPixelF(canvas, 0f, 0f, doc, view).y;
            float top = CoordHelper.PointToPixelF(canvas, 0f, doc.GridHeight - 1, doc, view).y;

            int lockAxis = locked ? roadPaint.LineLockAxis : 0;
            if (!locked || lockAxis == 1)
                EditorGUI.DrawRect(new Rect(left, o.y - 0.5f, right - left, 1f), faint);
            if (!locked || lockAxis == 2)
                EditorGUI.DrawRect(new Rect(o.x - 0.5f, top, 1f, bottom - top), faint);

            if (locked)
            {
                Vector2 head = CoordHelper.PointToPixelF(canvas, view.DragPoint.x * 0.5f, view.DragPoint.y * 0.5f, doc, view);
                const float t = 3f;
                EditorGUI.DrawRect(lockAxis == 1
                    ? new Rect(Mathf.Min(o.x, head.x), o.y - t * 0.5f, Mathf.Abs(head.x - o.x), t)
                    : new Rect(o.x - t * 0.5f, Mathf.Min(o.y, head.y), t, Mathf.Abs(head.y - o.y)), tint);
            }

            EditorGUI.DrawRect(new Rect(o.x - 3f, o.y - 3f, 6f, 6f), tint);
        }

        /// <summary>Ô toạ độ nhỏ bám theo con trỏ khi rê trên lưới (đọc x/y theo ô).</summary>
        internal void DrawHoverReadout(Rect canvas)
        {
            var view = _ctx.View;
            var doc = _ctx.Doc;
            if (!view.HoverCellValid) return;
            // Mọi mode giờ snap 1/2 ô (đường/highway/station/parking/decor/move all) → readout nửa ô.
            float hx = Mathf.Clamp(Mathf.RoundToInt(view.HoverCell.x * 2f) * 0.5f, 0f, doc.GridWidth - 1) + doc.OriginCell.x;
            float hy = Mathf.Clamp(Mathf.RoundToInt(view.HoverCell.y * 2f) * 0.5f, 0f, doc.GridHeight - 1) + doc.OriginCell.y;

            var r = new Rect(view.HoverPixel.x + 14f, view.HoverPixel.y + 16f, 104f, 17f);
            if (r.xMax > canvas.xMax) r.x = view.HoverPixel.x - r.width - 8f;
            if (r.yMax > canvas.yMax) r.y = view.HoverPixel.y - r.height - 8f;
            EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.78f));
            GUI.Label(r, $"x {hx:0.#}   y {hy:0.#}", _styles.PillStyle);
        }

        /// <summary>Ghost mờ (nửa opacity) tại ô dưới con trỏ — cho user thấy sắp đặt gì. Đường và
        /// highway đều preview bằng 1 Ô ghép từ ô modular; hw-decor không có sprite → ô trắng 50%.
        /// Khi chỗ đó sẽ chồng (_overlapHint) → tô ĐỎ. Chỉ ở mode Road và khi KHÔNG kéo
        /// (lúc kéo có marker riêng).</summary>
        internal void DrawRoadHoverGhost(Rect canvas)
        {
            var view = _ctx.View;
            var doc = _ctx.Doc;
            if (view.Mode != PaintMode.Road || view.EraserMode || view.Dragging || !view.HoverCellValid) return;
            // Đường + highway đều snap 1/2 ô (footprint dọc trục vẽ dài nửa ô, bề ngang giữ nguyên).
            float hx = Mathf.Clamp(Mathf.RoundToInt(view.HoverCell.x * 2f) * 0.5f, 0f, doc.GridWidth - 1);
            float hy = Mathf.Clamp(Mathf.RoundToInt(view.HoverCell.y * 2f) * 0.5f, 0f, doc.GridHeight - 1);
            float cell = view.CellPixelSize;
            float h = view.EdgeLayer == 1 ? cell * 2f
                : view.EdgeLayer == 3 ? cell * Road2CrossSectionWidthCells
                : view.EdgeLayer == 4 ? cell * 0.5f
                : cell;
            Vector2 p = CoordHelper.PointToPixelF(canvas, hx, hy, doc, view);
            var rect = new Rect(p.x - cell * 0.25f, p.y - h * 0.5f, cell * 0.5f, h);
            bool overlap = _getOverlapHint();

            _ensureRoadSprites();
            Color ghost = overlap ? new Color(1f, 0.25f, 0.20f, 0.75f) : new Color(1f, 1f, 1f, 0.5f);

            if (view.EdgeLayer == 0 && _getSpTileSide() != null && _getSpTileSideRim() != null)
            {
                Color prevTile = GUI.color;
                GUI.color = ghost;
                _drawStraightTiles(p, cell, 0f, false);
                GUI.color = prevTile;
                return;
            }

            if (view.EdgeLayer == 1 && _getSpHighway() != null && _getSpHighwayRim() != null)
            {
                Color prev = GUI.color;
                GUI.color = ghost;
                _drawHighwayColumn(p, cell, 0f);
                GUI.color = prev;
                return;
            }

            if (view.EdgeLayer == 3 && _getSpTileSide() != null && _getSpTileSideRim() != null)
            {
                Color prevRoad2 = GUI.color;
                GUI.color = ghost;
                _drawRoad2StraightTiles(canvas, hx, hy, 0f, false);
                GUI.color = prevRoad2;
                return;
            }

            Color c = overlap ? ToolStyles.TileHighway
                : view.EdgeLayer == 2 ? ToolStyles.TileHwDecor
                : view.EdgeLayer == 3 ? ToolStyles.TileRoad2
                : view.EdgeLayer == 4 ? ToolStyles.TilePath
                : ToolStyles.TileRoad;
            c.a = 0.5f;
            EditorGUI.DrawRect(rect, c);
        }
    }
}
#endif
