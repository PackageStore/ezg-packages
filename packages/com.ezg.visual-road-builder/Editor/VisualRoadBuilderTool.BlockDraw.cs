#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Vẽ khối Station/Parking bằng art thật (station_area / parking_area) + chấm pivot, viền
    /// và mũi tên hướng mặt chỉ hiện khi hover/kéo; kèm ghost hover và primitive rect dùng chung
    /// (BlockRect / DrawRectBorder). Thiếu sprite → fallback ô màu như bản cũ.</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        private void DrawStations(Rect canvas)
        {
            EnsureRoadSprites();

            var fill = new Color(0.30f, 0.50f, 0.95f, 0.40f);
            var border = new Color(0.45f, 0.65f, 1f);
            var pFill = new Color(0.25f, 0.75f, 0.3f, 0.35f);
            var pBorder = new Color(0.35f, 0.95f, 0.4f);

            // Khối dưới con trỏ mới hiện viền + mũi tên (station ưu tiên hơn parking, khớp hit-test
            // của HandleStationInput) — nền canvas giữ sạch như art thật.
            int hoverStation = -1, hoverParking = -1;
            if (_mode == PaintMode.Station && !_eraserMode && _hoverCellValid)
            {
                hoverStation = FindStationAt(_hoverCell);
                if (hoverStation < 0) hoverParking = FindParkingAt(_hoverCell);
            }

            for (int i = 0; i < _stations.Count; i++)
            {
                DecodeStation(_stations[i], out int x, out int y, out int rot);
                int s = StationSize;
                Rect r = BlockRect(canvas, new Vector2Int(x, y), s, s);
                bool active = i == _draggingStation || i == hoverStation;

                if (_spStationArea != null) DrawBlockSprite(r, _spStationArea, rot, 1f);
                else EditorGUI.DrawRect(r, fill);

                if (active)
                {
                    DrawRectBorder(r, 2f, border);
                    DrawFacingArrow(r, rot, border);
                }
                DrawStationHookDots(canvas, x, y, rot, 1f);
            }

            for (int i = 0; i < _parkings.Count; i++)
            {
                DecodeParking(_parkings[i], out int x2, out int y2, out int rot);
                Vector2Int k = ParkingCells(rot);
                Rect block = BlockRect(canvas, new Vector2Int(x2, y2), k.x, k.y);
                bool active = i == _draggingParking || i == hoverParking;

                if (_spParkingArea != null)
                    DrawBlockSprite(ParkingSlabRect(canvas, new Vector2Int(x2, y2), rot), _spParkingArea, rot, 1f);
                else
                    EditorGUI.DrawRect(block, pFill);

                if (active)
                {
                    DrawRectBorder(block, 2f, pBorder);
                    DrawFacingArrow(block, rot, pBorder);
                }
                DrawParkingHookDots(canvas, x2, y2, rot, 1f);
            }

            if (!TryGhostBlock(out int ghostId, out bool ghostIsStation)) return;

            if (ghostIsStation)
            {
                DecodeStation(ghostId, out int gx, out int gy, out int grot);
                int s = StationSize;
                Rect r = BlockRect(canvas, new Vector2Int(gx, gy), s, s);
                if (_spStationArea != null) DrawBlockSprite(r, _spStationArea, grot, 0.5f);
                else EditorGUI.DrawRect(r, new Color(0.40f, 0.60f, 1f, 0.15f));
                DrawStationHookDots(canvas, gx, gy, grot, 0.7f);
            }
            else
            {
                DecodeParking(ghostId, out int gx, out int gy, out int grot);
                var a2 = new Vector2Int(gx, gy);
                if (_spParkingArea != null)
                    DrawBlockSprite(ParkingSlabRect(canvas, a2, grot), _spParkingArea, grot, 0.5f);
                else
                {
                    Vector2Int k = ParkingCells(grot);
                    EditorGUI.DrawRect(BlockRect(canvas, a2, k.x, k.y), new Color(0.3f, 0.85f, 0.35f, 0.15f));
                }
                DrawParkingHookDots(canvas, gx, gy, grot, 0.7f);
            }
        }

        /// <summary>Art khối phủ kín rect. Base cả station_area lẫn parking_area vẽ MẶT quay XUỐNG
        /// (nam) → rot r cần xoay thêm (r + 2) nấc CW.</summary>
        private void DrawBlockSprite(Rect r, Sprite sprite, int rot, float alpha)
        {
            Color prev = GUI.color;
            if (alpha < 1f) GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(r, GetRoadPieceTex(sprite, rot + 2), ScaleMode.StretchToFill, true);
            GUI.color = prev;
        }

        /// <summary>Rect của slab parking: dày 1 ô, mép trước cách pivot NỬA Ô — mesh parking ăn SÁT
        /// dải đường (đo được z -0.5 tại mép trước), không chừa khe 1 ô như station. Anchor vì thế lùi
        /// 1.5 ô (3 nửa ô) so với pivot theo hướng mặt; phần khối còn lại là dải đường mesh tự chứa.</summary>
        private Rect ParkingSlabRect(Rect canvas, Vector2Int a2, int rot)
        {
            Vector2Int k = ParkingCells(rot);
            return rot switch
            {
                1 => BlockRect(canvas, new Vector2Int(a2.x + k.x * 2 - 3, a2.y), 1, k.y),
                2 => BlockRect(canvas, new Vector2Int(a2.x, a2.y + 1), k.x, 1),
                3 => BlockRect(canvas, new Vector2Int(a2.x + 1, a2.y), 1, k.y),
                _ => BlockRect(canvas, new Vector2Int(a2.x, a2.y + k.y * 2 - 3), k.x, 1),
            };
        }

        /// <summary>Mũi tên chỉ hướng MẶT của station: rot 0 = +Z (lên trên canvas), quay theo chiều
        /// kim đồng hồ mỗi nấc 90° (khớp yaw = rot * 90 khi Apply).</summary>
        private static void DrawFacingArrow(Rect r, int rot, Color color)
        {
            Vector2 dir = rot switch
            {
                1 => new Vector2(1f, 0f),   // E
                2 => new Vector2(0f, 1f),   // S (pixel y xuống)
                3 => new Vector2(-1f, 0f),  // W
                _ => new Vector2(0f, -1f),  // N
            };
            Vector2 c = r.center;
            float len = Mathf.Min(r.width, r.height) * 0.32f;
            const float thick = 3f;

            // Thân mũi tên: rect mảnh từ tâm về hướng mặt.
            Vector2 tip = c + dir * len;
            if (dir.x == 0f)
            {
                EditorGUI.DrawRect(new Rect(c.x - thick * 0.5f, Mathf.Min(c.y, tip.y), thick, len), color);
            }
            else
            {
                EditorGUI.DrawRect(new Rect(Mathf.Min(c.x, tip.x), c.y - thick * 0.5f, len, thick), color);
            }

            // Đầu mũi tên: ô vuông nhỏ ở tip.
            const float head = 7f;
            EditorGUI.DrawRect(new Rect(tip.x - head * 0.5f, tip.y - head * 0.5f, head, head), color);
        }

        /// <summary>Vẽ cặp hook dot (road1 cyan 7px + road2 tím 5px) cho station.
        /// Road2 vẽ trước (nền) vì xa hơn; road1 vẽ sau, trùng pivot bake.</summary>
        private void DrawStationHookDots(Rect canvas, int sx2, int sy2, int rot, float alpha)
        {
            var c1 = Color.cyan;
            var c2 = TileRoad2;
            if (alpha < 1f) { c1.a = alpha; c2.a = alpha; }
            DrawPivotDot(canvas, StationHookCell(sx2, sy2, StationSize, rot, true), c2, 5f);
            DrawPivotDot(canvas, StationHookCell(sx2, sy2, StationSize, rot, false), c1);
        }

        /// <summary>Hook dot của parking — MỘT chấm cho cả 2 road type, vì hook parking không giãn theo
        /// bề rộng đường (xem <see cref="ParkingHookCell"/>); vẽ 2 chấm trùng nhau chỉ gây hiểu nhầm là
        /// có 2 chỗ neo khác nhau như station.</summary>
        private void DrawParkingHookDots(Rect canvas, int ax2, int ay2, int rot, float alpha)
        {
            var c = Color.cyan;
            if (alpha < 1f) c.a = alpha;
            DrawPivotDot(canvas, ParkingHookCell(ax2, ay2, rot), c);
        }

        /// <summary>Chấm pivot tại một điểm lưới bất kỳ (dùng chung station + parking).</summary>
        private void DrawPivotDot(Rect canvas, Vector2 pivotCell, Color color)
            => DrawPivotDot(canvas, pivotCell, color, 7f);

        private void DrawPivotDot(Rect canvas, Vector2 pivotCell, Color color, float size)
        {
            Vector2 p = PointToPixelF(canvas, pivotCell.x, pivotCell.y);
            var dot = new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size);
            EditorGUI.DrawRect(dot, color);
            DrawRectBorder(dot, 1f, Color.black);
        }

        /// <summary>Rect pixel của khối w x h ô, anchor theo NỬA ô (span anchor → anchor+w/h).</summary>
        private Rect BlockRect(Rect canvas, Vector2Int anchor2, int w, int h)
        {
            float ax = anchor2.x * 0.5f, ay = anchor2.y * 0.5f;
            Vector2 bottomLeft = PointToPixelF(canvas, ax, ay);
            Vector2 topRight = PointToPixelF(canvas, ax + w, ay + h);
            return new Rect(bottomLeft.x, topRight.y, w * _cellPixelSize, h * _cellPixelSize);
        }

        private static void DrawRectBorder(Rect r, float t, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }
    }
}
#endif
