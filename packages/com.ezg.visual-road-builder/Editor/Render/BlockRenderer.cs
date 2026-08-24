#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Station/Parking block sprites, hook dots, ghost hover, and shared
    /// BlockRect / ParkingSlabRect geometry used by DebugTab.</summary>
    internal sealed class BlockRenderer
    {
        private readonly ToolContext _ctx;
        private readonly Action _ensureRoadSprites;
        private readonly Func<Sprite, int, Texture2D> _getRoadPieceTex;
        private readonly Func<Sprite> _getSpStationArea;
        private readonly Func<Sprite> _getSpParkingArea;
        private readonly Func<Vector2, int> _findStationAt;
        private readonly Func<Vector2, int> _findParkingAt;
        private readonly Func<(bool found, int id, bool isStation)> _tryGhostBlock;
        private readonly Func<int, int, int, int, bool, Vector2> _stationHookCell;
        private readonly Func<int, int, int, Vector2> _parkingHookCell;

        internal BlockRenderer(ToolContext ctx,
            Action ensureRoadSprites,
            Func<Sprite, int, Texture2D> getRoadPieceTex,
            Func<Sprite> getSpStationArea, Func<Sprite> getSpParkingArea,
            Func<Vector2, int> findStationAt, Func<Vector2, int> findParkingAt,
            Func<(bool found, int id, bool isStation)> tryGhostBlock,
            Func<int, int, int, int, bool, Vector2> stationHookCell,
            Func<int, int, int, Vector2> parkingHookCell)
        {
            _ctx = ctx;
            _ensureRoadSprites = ensureRoadSprites;
            _getRoadPieceTex = getRoadPieceTex;
            _getSpStationArea = getSpStationArea;
            _getSpParkingArea = getSpParkingArea;
            _findStationAt = findStationAt;
            _findParkingAt = findParkingAt;
            _tryGhostBlock = tryGhostBlock;
            _stationHookCell = stationHookCell;
            _parkingHookCell = parkingHookCell;
        }

        internal void DrawStations(Rect canvas)
        {
            var view = _ctx.View;
            var doc = _ctx.Doc;
            _ensureRoadSprites();

            var fill = new Color(0.30f, 0.50f, 0.95f, 0.40f);
            var border = new Color(0.45f, 0.65f, 1f);
            var pFill = new Color(0.25f, 0.75f, 0.3f, 0.35f);
            var pBorder = new Color(0.35f, 0.95f, 0.4f);

            // Khối dưới con trỏ mới hiện viền + mũi tên (station ưu tiên hơn parking, khớp hit-test
            // của HandleStationInput) — nền canvas giữ sạch như art thật.
            int hoverStation = -1, hoverParking = -1;
            if (view.Mode == PaintMode.Station && !view.EraserMode && view.HoverCellValid)
            {
                hoverStation = _findStationAt(view.HoverCell);
                if (hoverStation < 0) hoverParking = _findParkingAt(view.HoverCell);
            }

            Sprite spStation = _getSpStationArea();
            Sprite spParking = _getSpParkingArea();

            for (int i = 0; i < doc.Stations.Count; i++)
            {
                BlockCodec.DecodeStation(doc.Stations[i], out int x, out int y, out int rot);
                int s = GridConst.StationSize;
                Rect r = BlockRect(canvas, new Vector2Int(x, y), s, s);
                bool active = i == view.DraggingStation || i == hoverStation;

                if (spStation != null) DrawBlockSprite(r, spStation, rot, 1f);
                else EditorGUI.DrawRect(r, fill);

                if (active)
                {
                    DrawPrimitives.DrawRectBorder(r, 2f, border);
                    DrawPrimitives.DrawFacingArrow(r, rot, border);
                }
                DrawStationHookDots(canvas, x, y, rot, 1f);
            }

            for (int i = 0; i < doc.Parkings.Count; i++)
            {
                BlockCodec.DecodeParking(doc.Parkings[i], out int x2, out int y2, out int rot);
                Vector2Int k = GridConst.ParkingCells(rot);
                Rect block = BlockRect(canvas, new Vector2Int(x2, y2), k.x, k.y);
                bool active = i == view.DraggingParking || i == hoverParking;

                if (spParking != null)
                    DrawBlockSprite(ParkingSlabRect(canvas, new Vector2Int(x2, y2), rot), spParking, rot, 1f);
                else
                    EditorGUI.DrawRect(block, pFill);

                if (active)
                {
                    DrawPrimitives.DrawRectBorder(block, 2f, pBorder);
                    DrawPrimitives.DrawFacingArrow(block, rot, pBorder);
                }
                DrawParkingHookDots(canvas, x2, y2, rot, 1f);
            }

            var ghost = _tryGhostBlock();
            if (!ghost.found) return;

            if (ghost.isStation)
            {
                BlockCodec.DecodeStation(ghost.id, out int gx, out int gy, out int grot);
                int s = GridConst.StationSize;
                Rect r = BlockRect(canvas, new Vector2Int(gx, gy), s, s);
                if (spStation != null) DrawBlockSprite(r, spStation, grot, 0.5f);
                else EditorGUI.DrawRect(r, new Color(0.40f, 0.60f, 1f, 0.15f));
                DrawStationHookDots(canvas, gx, gy, grot, 0.7f);
            }
            else
            {
                BlockCodec.DecodeParking(ghost.id, out int gx, out int gy, out int grot);
                var a2 = new Vector2Int(gx, gy);
                if (spParking != null)
                    DrawBlockSprite(ParkingSlabRect(canvas, a2, grot), spParking, grot, 0.5f);
                else
                {
                    Vector2Int k = GridConst.ParkingCells(grot);
                    EditorGUI.DrawRect(BlockRect(canvas, a2, k.x, k.y), new Color(0.3f, 0.85f, 0.35f, 0.15f));
                }
                DrawParkingHookDots(canvas, gx, gy, grot, 0.7f);
            }
        }

        /// <summary>Art khối phủ kín rect. Base cả station_area lẫn parking_area vẽ MẶT quay XUỐNG
        /// (nam) -> rot r cần xoay thêm (r + 2) nấc CW.</summary>
        private void DrawBlockSprite(Rect r, Sprite sprite, int rot, float alpha)
        {
            Color prev = GUI.color;
            if (alpha < 1f) GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(r, _getRoadPieceTex(sprite, rot + 2), ScaleMode.StretchToFill, true);
            GUI.color = prev;
        }

        /// <summary>Rect của slab parking: dày 1 ô, mép trước cách pivot NỬA Ô — mesh parking ăn SÁT
        /// dải đường (đo được z -0.5 tại mép trước), không chừa khe 1 ô như station. Anchor vì thế lùi
        /// 1.5 ô (3 nửa ô) so với pivot theo hướng mặt; phần khối còn lại là dải đường mesh tự chứa.</summary>
        internal Rect ParkingSlabRect(Rect canvas, Vector2Int a2, int rot)
        {
            Vector2Int k = GridConst.ParkingCells(rot);
            return rot switch
            {
                1 => BlockRect(canvas, new Vector2Int(a2.x + k.x * 2 - 3, a2.y), 1, k.y),
                2 => BlockRect(canvas, new Vector2Int(a2.x, a2.y + 1), k.x, 1),
                3 => BlockRect(canvas, new Vector2Int(a2.x + 1, a2.y), 1, k.y),
                _ => BlockRect(canvas, new Vector2Int(a2.x, a2.y + k.y * 2 - 3), k.x, 1),
            };
        }

        /// <summary>Vẽ cặp hook dot (road1 cyan 7px + road2 tím 5px) cho station.
        /// Road2 vẽ trước (nền) vì xa hơn; road1 vẽ sau, trùng pivot bake.</summary>
        private void DrawStationHookDots(Rect canvas, int sx2, int sy2, int rot, float alpha)
        {
            var c1 = Color.cyan;
            var c2 = ToolStyles.TileRoad2;
            if (alpha < 1f) { c1.a = alpha; c2.a = alpha; }
            DrawPivotDot(canvas, _stationHookCell(sx2, sy2, GridConst.StationSize, rot, true), c2, 5f);
            DrawPivotDot(canvas, _stationHookCell(sx2, sy2, GridConst.StationSize, rot, false), c1);
        }

        /// <summary>Hook dot của parking — MỘT chấm cho cả 2 road type, vì hook parking không giãn theo
        /// bề rộng đường (xem ParkingHookCell); vẽ 2 chấm trùng nhau chỉ gây hiểu nhầm là
        /// có 2 chỗ neo khác nhau như station.</summary>
        private void DrawParkingHookDots(Rect canvas, int ax2, int ay2, int rot, float alpha)
        {
            var c = Color.cyan;
            if (alpha < 1f) c.a = alpha;
            DrawPivotDot(canvas, _parkingHookCell(ax2, ay2, rot), c);
        }

        private void DrawPivotDot(Rect canvas, Vector2 pivotCell, Color color)
            => DrawPivotDot(canvas, pivotCell, color, 7f);

        private void DrawPivotDot(Rect canvas, Vector2 pivotCell, Color color, float size)
        {
            Vector2 p = CoordHelper.PointToPixelF(canvas, pivotCell.x, pivotCell.y, _ctx.Doc, _ctx.View);
            var dot = new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size);
            EditorGUI.DrawRect(dot, color);
            DrawPrimitives.DrawRectBorder(dot, 1f, Color.black);
        }

        /// <summary>Rect pixel của khối w x h ô, anchor theo NỬA ô (span anchor -> anchor+w/h).</summary>
        internal Rect BlockRect(Rect canvas, Vector2Int anchor2, int w, int h)
        {
            float ax = anchor2.x * 0.5f, ay = anchor2.y * 0.5f;
            Vector2 bottomLeft = CoordHelper.PointToPixelF(canvas, ax, ay, _ctx.Doc, _ctx.View);
            Vector2 topRight = CoordHelper.PointToPixelF(canvas, ax + w, ay + h, _ctx.Doc, _ctx.View);
            return new Rect(bottomLeft.x, topRight.y, w * _ctx.View.CellPixelSize, h * _ctx.View.CellPixelSize);
        }
    }
}
#endif
