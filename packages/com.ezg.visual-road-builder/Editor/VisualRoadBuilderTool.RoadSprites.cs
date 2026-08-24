#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Vẽ lớp Đường/Highway bằng icon thật cắt từ _road_plan.psd: chọn piece theo mask,
    /// cắt region + xoay sẵn thành Texture2D (cache) rồi vẽ axis-aligned để được scroll-view clip.</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        /// <summary>Lớp Đường: vẽ icon thật từ _road_plan.psd — mỗi điểm chọn piece theo số nhánh
        /// (thẳng/cua/ngã ba/ngã tư) rồi xoay khớp mask, đúng như lúc bake. Thiếu art → fallback ô cam.</summary>
        private void DrawRoadSprites(Rect canvas, List<int> edges)
        {
            if (edges.Count == 0) return;
            EnsureRoadSprites();
            if (_spTileSide == null || _spTileSideRim == null) return;

            int[] masks = BuildMasks(edges);
            int lw = LatticeW, lh = LatticeH;

            // Ramp Highway→Road: bỏ mảnh road nằm dưới arm ramp + vẽ part 0.5×1 ô bắc cầu — dùng CHUNG
            // helper với bake nên preview KHỚP mesh (không còn ô road đè ramp). Giữ RIÊNG set ramp như
            // solver: điểm dưới arm ramp KHÔNG kích half-straight từ junction kề, khác station-road.
            var suppress = new BlockSuppression();
            var rampSuppress = new HashSet<int>();
            var bridges = new List<(float x, float y, float yaw)>();
            int[] hwMasks = BuildMasks(_highwayEdges);
            // Legacy-view mask (road) cho ramp scan + edge-fill — CHUNG với Apply nên preview khớp
            // bake. hwMasks GIỮ DENSE (xem Apply.cs).
            int[] roadMasksLegacy = BuildLegacyMasksFromEdges(edges);
            foreach (var (rx2, ry2, stem, _) in CollectRampJunctions(hwMasks, roadMasksLegacy))
                if (TryRampRoadBridge(rx2, ry2, stem, roadMasksLegacy, rampSuppress, out float bx, out float by, out float byaw))
                    bridges.Add((bx, by, byaw));

            // Khối Station/Parking ăn vào dải đường: chạy CHUNG solver với Apply (im lặng, không cần
            // Part Library) nên preview khớp mesh — part road trong clearance biến mất, thay bằng bộ ô
            // modular của mảnh trước mặt station. Khối ghost đang hover tính riêng để vẽ mờ.
            var stationRoads = new List<(RoadTilePart part, float x, float y, float yaw)>();
            var parkingRoads = new List<(RoadTilePart part, float x, float y, float yaw)>();
            var blockStrips = new List<BlockStrip>();
            var skin = new BlockRoadSkin();
            CollectStationRoadPlacements(_stations, masks, suppress, stationRoads, blockStrips, skin, null, false);
            CollectParkingRoadKerb(_parkings, masks, suppress, parkingRoads, blockStrips, skin, null, false);

            var ghostRoads = new List<(RoadTilePart part, float x, float y, float yaw)>();
            if (TryGhostBlock(out int ghostId, out bool ghostIsStation))
            {
                if (ghostIsStation)
                    CollectStationRoadPlacements(new[] { ghostId }, masks, suppress, ghostRoads, blockStrips, skin, null, false);
                else
                    CollectParkingRoadKerb(new[] { ghostId }, masks, suppress, ghostRoads, blockStrips, skin, null, false);
            }

            // Đổi ô side→center SAU khi gộp cả khối ghost: kéo khối tới đối diện station khác là thấy
            // ngay vạch mép giữa 2 lối vào biến mất, đúng như bake sẽ ra.
            ApplyApronPlain(stationRoads, skin);
            ApplyApronPlain(ghostRoads, skin);
            ApplyApronPlain(parkingRoads, skin);

            // Ghost gộp chung dải với khối đã đặt nên preview thấy ngay mép trong biến mất khi kéo
            // khối tới sát khối khác.
            var blockEdgeHalves = new List<(float x, float y, float yaw, int sides)>();
            var blockEdgeFulls = new List<(float x, float y, float yaw, int sides)>();
            CollectBlockEdgeFills(roadMasksLegacy, blockStrips, blockEdgeHalves, blockEdgeFulls);

            // Mảnh straight sát junction bị thay bằng part half / kẹp giữa 2 junction bị bỏ — CHUNG
            // helper với bake (trước đây preview bỏ hẳn bước này nên thiếu hẳn các part half).
            bool Blocked(int idx) => hwMasks[idx] != 0 || suppress.Blocked(idx) || rampSuppress.Contains(idx);
            RoadLayout layout = ResolveRoadLayout(edges, Blocked);
            masks = layout.Masks;
            HashSet<int> replacedByHalf = layout.ReplacedByHalf;

            // Mảnh 0.5×1 ô: cầu ramp + lấp mép khối + part half quanh junction (gom sau vòng dưới).
            var halves = new List<(float x, float y, float yaw, int sides)>();
            foreach ((float bx, float by, float byaw) in bridges) halves.Add((bx, by, byaw, DirAll));
            halves.AddRange(blockEdgeHalves);

            for (int y = 0; y < lh; y++)
            {
                for (int x = 0; x < lw; x++)
                {
                    int idx = y * lw + x;
                    int m = masks[idx];
                    if (m == 0 || Blocked(idx) || layout.Skip(idx)) continue;

                    if (IsStraightLikeMask(m))
                    {
                        (float ax, float ay, bool full) = StraightAnchorFor(layout, idx, m, x, y);
                        DrawStraightTiles(canvas, ax, ay, StraightYaw(m), full,
                            StraightSides(m) & ~suppress.Sides(idx), skin,
                            EnsureRoadStraightAnchor().StraightTailNoRim(layout, idx, m));
                        continue;
                    }

                    DrawJunctionTiles(canvas, x * 0.5f, y * 0.5f, m, JunctionArms(masks, x, y, m), layout, skin);

                    ForEachHalfStraight(m, x, y, masks, replacedByHalf, suppress,
                        (hx, hy, hyaw, hsides) => halves.Add((hx, hy, hyaw, hsides)));
                }
            }

            foreach ((float hx, float hy, float yaw, int sides) in halves)
                DrawStraightTiles(canvas, hx, hy, yaw, false, sides, skin);

            foreach ((float fx, float fy, float fyaw, int fsides) in blockEdgeFulls)
                DrawStraightTiles(canvas, fx, fy, fyaw, true, fsides, skin);

            DrawStationRoadSprites(canvas, stationRoads, 1f);
            DrawStationRoadSprites(canvas, ghostRoads, 0.5f);
            DrawStationRoadSprites(canvas, parkingRoads, 1f);
        }

        /// <summary>Mảnh đường trước mặt station bằng ĐÚNG bộ ô modular mà bake đặt (dùng CHUNG
        /// <see cref="ForEachStationRoadTile"/>). Khối ghost vẽ mờ theo <paramref name="alpha"/>.</summary>
        private void DrawStationRoadSprites(
            Rect canvas, List<(RoadTilePart part, float x, float y, float yaw)> parts, float alpha)
        {
            if (parts.Count == 0) return;
            Color prev = GUI.color;
            if (alpha < 1f) GUI.color = new Color(1f, 1f, 1f, alpha);
            foreach ((RoadTilePart part, float px, float py, float yaw) in parts)
                DrawTilePart(canvas, part, px, py, yaw);
            GUI.color = prev;
        }

        /// <summary>Slice side/side_rim vẽ thân về phía TRÁI của pivot (pivot ở mép phải, giữa cạnh),
        /// còn mesh ở yaw 0 vươn về -Z = XUỐNG trên canvas → lệch 3 nấc CW so với hệ yaw của tool.</summary>
        private const int TileSpriteBaseTurns = 3;

        /// <summary>Slice curve/curve_rim có pivot ở góc PHẢI-DƯỚI rect nên vẽ thân về phía TRÁI-TRÊN,
        /// còn mesh yaw 0 chìa về +X/-Z = PHẢI-DƯỚI trên canvas → lệch 2 nấc.</summary>
        private const int CurveSpriteBaseTurns = 2;

        /// <summary>Bề ngang (ô) mà 1 ô đường ghép chiếm KỂ CẢ vỉa hè — icon toolbar thu theo số này
        /// để rim không bị cắt. Thân slice nằm dọc trục pivot (= chiều rộng rect) nên bề ngang đường
        /// là 2 lần slice dài nhất; dọc trục đường luôn đúng 1 ô (2 cột × 0.5 ô).</summary>
        private float RoadCellIconSpanCells
        {
            get
            {
                float widest = 0.5f;
                if (_spTileSide != null) widest = Mathf.Max(widest, _spTileSide.rect.width / SpritePixelsPerCell);
                if (_spTileSideRim != null) widest = Mathf.Max(widest, _spTileSideRim.rect.width / SpritePixelsPerCell);
                return Mathf.Max(1f, widest * 2f);
            }
        }

        /// <summary>Vẽ một mảnh THẲNG bằng đúng bộ ô modular mà bake đặt (dùng CHUNG
        /// <see cref="ForEachStraightTile"/>), canh theo điểm lưới (x, y).</summary>
        private void DrawStraightTiles(Rect canvas, float x, float y, float yaw, bool fullCell,
            int sides = DirAll, BlockRoadSkin skin = null, bool noRim = false)
        {
            if (_spTileSide == null || _spTileSideRim == null) return;
            ForEachStraightTile(x, y, yaw, fullCell, (tx, ty, tyaw) =>
            {
                if (skin != null && skin.PlainAt(tx, ty, tyaw))
                {
                    (float cx, float cy) = PlainCoreCell(tx, ty, tyaw);
                    DrawTilePart(canvas, RoadTilePart.Center, cx, cy, 0f);
                }
                else DrawTilePart(canvas, RoadTilePart.Side, tx, ty, tyaw);

                if (!noRim && (skin == null || !skin.KerbFreeAt(tx, ty, tyaw)))
                    DrawTilePart(canvas, RoadTilePart.SideRim, tx, ty, tyaw);
            }, sides);
        }

        /// <summary>Vẽ mảnh thẳng quanh tâm pixel với <paramref name="cellPixels"/> pixel mỗi ô — canvas
        /// truyền <see cref="_cellPixelSize"/>, icon toolbar truyền cỡ vừa nút.</summary>
        private void DrawStraightTiles(Vector2 center, float cellPixels, float yaw, bool fullCell,
            int sides = DirAll)
        {
            if (_spTileSide == null || _spTileSideRim == null) return;
            ForEachStraightTile(0f, 0f, yaw, fullCell, (tx, ty, tyaw) =>
            {
                // Trục y lưới hướng LÊN màn hình nên offset ô đảo dấu theo y.
                var pivot = new Vector2(center.x + tx * cellPixels, center.y - ty * cellPixels);
                DrawTileSprite(pivot, cellPixels, _spTileSide, tyaw);
                DrawTileSprite(pivot, cellPixels, _spTileSideRim, tyaw);
            }, sides);
        }

        /// <summary>Vẽ MỘT cột cao tốc quanh tâm pixel — dùng CHUNG
        /// <see cref="ForEachHighwayColumnTile"/> với bake nên preview khớp mesh.</summary>
        private void DrawHighwayColumn(Vector2 center, float cellPixels, float yaw)
        {
            if (_spHighway == null || _spHighwayRim == null) return;
            ForEachHighwayColumnTile(0f, 0f, yaw, (tx, ty, tyaw) =>
            {
                // Trục y lưới hướng LÊN màn hình nên offset ô đảo dấu theo y.
                var pivot = new Vector2(center.x + tx * cellPixels, center.y - ty * cellPixels);
                DrawTileSprite(pivot, cellPixels, _spHighway, tyaw);
                DrawTileSprite(pivot, cellPixels, _spHighwayRim, tyaw);
            });
        }

        /// <summary>Vẽ 1 Ô cao tốc đã vẽ (2 cột lệch ±<see cref="RoadTileColumnOffsetCells"/>) — ghost
        /// của brush, tương ứng <see cref="DrawStraightTiles"/> với fullCell của lớp đường.</summary>
        private void DrawHighwayCellTiles(Vector2 center, float cellPixels, float yaw)
        {
            bool alongX = (Mathf.RoundToInt(yaw / 90f) & 1) == 0;
            float dx = (alongX ? RoadTileColumnOffsetCells : 0f) * cellPixels;
            float dy = (alongX ? 0f : RoadTileColumnOffsetCells) * cellPixels;
            DrawHighwayColumn(new Vector2(center.x - dx, center.y + dy), cellPixels, yaw);
            DrawHighwayColumn(new Vector2(center.x + dx, center.y - dy), cellPixels, yaw);
        }

        /// <summary>Vẽ mảnh GIAO (cua / T / ngã tư) bằng đúng bộ ô modular mà bake đặt (dùng CHUNG
        /// <see cref="ForEachJunctionTile"/>). Mọi slice đặt pivot TRÙNG pivot prefab: curve/curve_rim
        /// lệch <see cref="CurveSpriteBaseTurns"/> nấc, còn turn/turn_rim có mesh chìa về -X/-Z y như
        /// side nên dùng <see cref="TileSpriteBaseTurns"/>. Center canh TÂM ô.
        /// Thiếu slice nào thì bỏ ô đó.</summary>
        private void DrawJunctionTiles(
            Rect canvas, float jx, float jy, int mask, int junctionArms, RoadLayout layout = null,
            BlockRoadSkin skin = null)
        {
            ForEachJunctionTile(mask, junctionArms,
                (part, dx, dy, yaw) => DrawTilePart(canvas, part, jx + dx, jy + dy, yaw),
                RimBlockedProbe(layout, skin, jx, jy),
                FilletTurnProbe(layout, jx, jy));
        }

        /// <summary>Vẽ MỘT ô modular tại điểm lưới (x, y) — dùng chung cho mảnh giao và mảnh trước mặt
        /// station nên 2 chỗ luôn ra cùng hình. Thiếu slice nào thì bỏ ô đó.</summary>
        private void DrawTilePart(Rect canvas, RoadTilePart part, float x, float y, float yaw)
        {
            if (part == RoadTilePart.Center)
            {
                if (_spTileCenter != null) DrawSpriteCells(canvas, _spTileCenter, x, y, 0);
                return;
            }

            DrawTileSprite(PointToPixelF(canvas, x, y), _cellPixelSize, TilePartSprite(part), yaw,
                TilePartBaseTurns(part));
        }

        /// <summary>Vẽ 1 ô modular NEO THEO PIVOT của slice — cùng quy ước với mesh (prefab cũng đặt
        /// pivot tại điểm này rồi xoay quanh nó), nên hình trên canvas bám đúng chỗ mesh sẽ nằm.
        /// Kích thước suy từ rect slice ở <see cref="SpritePixelsPerCell"/> px/ô; xoay lẻ nấc thì
        /// hoán đổi rộng/cao và pivot đi theo (a, b) → (1 - b, a) mỗi nấc CW.
        /// <paramref name="baseTurns"/> = lệch hướng vẽ của slice so với mesh yaw 0 (xem
        /// <see cref="TileSpriteBaseTurns"/>).</summary>
        private void DrawTileSprite(Vector2 pivotPixel, float cellPixels, Sprite sprite, float yaw,
            int baseTurns = TileSpriteBaseTurns)
        {
            if (sprite == null || sprite.rect.width <= 0f || sprite.rect.height <= 0f) return;
            int turns = (Mathf.RoundToInt(yaw / 90f) + baseTurns) & 3;
            GUI.DrawTexture(TileSpriteRect(pivotPixel, cellPixels, sprite, turns),
                GetRoadPieceTex(sprite, turns), ScaleMode.StretchToFill, true);
        }

        /// <summary>Rect pixel của 1 ô modular đã xoay <paramref name="turns"/> nấc CW, neo theo pivot
        /// slice — nguồn DUY NHẤT hình học ô cho cả vẽ sprite và debug boundary từng ô.</summary>
        private static Rect TileSpriteRect(Vector2 pivotPixel, float cellPixels, Sprite sprite, int turns)
        {
            float w = sprite.rect.width / SpritePixelsPerCell * cellPixels;
            float h = sprite.rect.height / SpritePixelsPerCell * cellPixels;
            float a = sprite.pivot.x / sprite.rect.width;
            float b = 1f - sprite.pivot.y / sprite.rect.height;
            for (int k = 0; k < (turns & 3); k++) (a, b, w, h) = (1f - b, a, h, w);
            return new Rect(pivotPixel.x - a * w, pivotPixel.y - b * h, w, h);
        }

        /// <summary>Vẽ sprite (xoay sẵn turns nấc CW) canh TÂM tại điểm lưới (cx, cy).</summary>
        private void DrawSpriteCells(Rect canvas, Sprite sprite, float cx, float cy, int turns)
        {
            turns &= 3;
            GUI.DrawTexture(SpriteCellsRect(canvas, sprite, cx, cy, turns),
                GetRoadPieceTex(sprite, turns), ScaleMode.StretchToFill, true);
        }

        /// <summary>Rect pixel của sprite canh TÂM tại điểm lưới (cx, cy). Kích thước đọc từ slice ở
        /// <see cref="SpritePixelsPerCell"/> px/ô; xoay lẻ nấc → hoán đổi rộng/cao.</summary>
        private Rect SpriteCellsRect(Rect canvas, Sprite sprite, float cx, float cy, int turns)
        {
            float w = sprite.rect.width / SpritePixelsPerCell * _cellPixelSize;
            float h = sprite.rect.height / SpritePixelsPerCell * _cellPixelSize;
            if ((turns & 1) != 0) (w, h) = (h, w);
            Vector2 p = PointToPixelF(canvas, cx, cy);
            return new Rect(p.x - w * 0.5f, p.y - h * 0.5f, w, h);
        }

        /// <summary>Lớp Highway: vẽ ĐÚNG bộ ô modular mà bake đặt (cột core + rim, dùng CHUNG
        /// <see cref="CollectHighwayColumns"/> / <see cref="ForEachHighwayColumnTile"/>) + sprite ramp
        /// hway_to_road (4×4 ô) tại mỗi nút road đấu vào highway thẳng (dùng CHUNG
        /// <see cref="CollectRampJunctions"/>) — nên preview khớp mesh: cột nằm dưới ramp không vẽ.
        /// Ramp base = highway DỌC, nhánh road quay TRÁI (N|S|W). Thiếu art → bỏ vẽ.</summary>
        private void DrawHighwaySprites(Rect canvas, List<int> edges)
        {
            if (edges.Count == 0) return;
            EnsureRoadSprites();

            // roadMasks: legacy-view (khớp Apply). hwMasks GIỮ DENSE (xem Apply.cs) — CollectRampJunctions
            // chỉ dùng để phát hiện anchor ramp, không lát cột (CollectHighwayColumns đọc thẳng _highwayEdges).
            int[] hwMasks = BuildMasks(edges);
            int[] roadMasks = BuildLegacyMasksFromEdges(_edges);
            List<(int x2, int y2, int stem, int hwMask)> ramps = CollectRampJunctions(hwMasks, roadMasks);

            float cell = _cellPixelSize;
            foreach ((float cx, float cy, bool horiz) in CollectHighwayColumns(ramps))
                DrawHighwayColumn(PointToPixelF(canvas, cx, cy), cell, horiz ? 0f : 90f);

            if (_spRampHway == null) return;
            float s = cell * 4f; // ramp 4×4 ô
            Vector2 rectSize = _spRampHway.rect.size;
            Vector2 piv = new Vector2(
                rectSize.x > 0f ? _spRampHway.pivot.x / rectSize.x : 0.5f,
                rectSize.y > 0f ? _spRampHway.pivot.y / rectSize.y : 0.5f);
            foreach ((int x2, int y2, int stem, int hwMask) in ramps)
            {
                Vector2 p = PointToPixelF(canvas, x2 * 0.5f, y2 * 0.5f);
                int turns = TurnsFromBase(DirN | DirS | DirW, hwMask | stem);
                bool flipped = RampFlipped(x2, y2); // phím F: lật gương → mirror sprite + đảo pivot Y
                // Neo pivot sprite (đặt = pivot prefab) lên ĐÚNG nút giao: (a,b) = pivot theo gốc
                // TRÁI-TRÊN (screen y-down), xoay CW `turns` nấc cho khớp texture đã xoay sẵn.
                // Lật gương = FlipY trong frame gốc ⇒ pivot.y đảo (b = piv.y thay vì 1 - piv.y) TRƯỚC khi xoay.
                float a = piv.x, b = flipped ? piv.y : 1f - piv.y;
                for (int k = 0; k < (turns & 3); k++) { float na = 1f - b, nb = a; a = na; b = nb; }
                var rect = new Rect(p.x - a * s, p.y - b * s, s, s);
                GUI.DrawTexture(rect, GetRoadPieceTex(_spRampHway, turns, flipped), ScaleMode.StretchToFill, true);
            }
        }

        /// <summary>Road 2 mirror của <see cref="DrawTilePart"/>: Side/SideRim/Center TÁI DÙNG sprite
        /// type-1; Curve/CurveRim/Filler đi qua 3 slice riêng (road2_curve/curve_rim/Road_0.5x1_center,
        /// D8/D9). Thiếu slice nào (curve/curve_rim chưa có art) thì bỏ đúng ô đó (D4), không chặn
        /// phần còn lại của mảnh.</summary>
        private void DrawRoad2TilePart(Rect canvas, Road2TilePart part, float x, float y, float yaw)
        {
            if (part == Road2TilePart.Center)
            {
                if (_spTileCenter != null) DrawSpriteCells(canvas, _spTileCenter, x, y, 0);
                return;
            }

            DrawTileSprite(PointToPixelF(canvas, x, y), _cellPixelSize, Road2TilePartSprite(part), yaw,
                Road2TilePartBaseTurns(part));
        }

        private Sprite Road2TilePartSprite(Road2TilePart part) => part switch
        {
            Road2TilePart.Side => _spTileSide,
            Road2TilePart.SideRim => _spTileSideRim,
            // Fallback sprite type-1 mirror fallback prefab của Road2JunctionTilePrefab.
            Road2TilePart.Curve => _spRoad2Curve != null ? _spRoad2Curve : _spTileCurve,
            Road2TilePart.CurveRim => _spRoad2CurveRim != null ? _spRoad2CurveRim : _spTileCurveRim,
            Road2TilePart.Filler => _spRoad2CenterFiller,
            Road2TilePart.Turn3x3 => _spTileTurn3x3,
            Road2TilePart.Turn3x3Rim => _spTileTurn3x3Rim,
            Road2TilePart.Turn1x1 => _spTileTurn1x1,
            Road2TilePart.Turn1x1Rim => _spTileTurn1x1Rim,
            _ => _spTileCenter,
        };

        private static int Road2TilePartBaseTurns(Road2TilePart part) =>
            part is Road2TilePart.Curve or Road2TilePart.CurveRim
                ? CurveSpriteBaseTurns : TileSpriteBaseTurns;

        /// <summary>Road 2: vẽ mảnh THẲNG — consumer sprite của <see cref="ForEachRoad2StraightPart"/>
        /// (CÙNG enumerator với bake <see cref="AddRoad2StraightTiles"/>, SINGLE-SOLVER INVARIANT).
        /// Thiếu slice nào thì <see cref="DrawRoad2TilePart"/> tự bỏ đúng ô đó (D4).</summary>
        private void DrawRoad2StraightTiles(Rect canvas, float x, float y, float yaw, bool fullCell,
            int sides = DirAll, BlockRoadSkin skin = null,
            System.Func<float, float, float, bool> rimCovered = null)
        {
            if (_spTileSide == null || _spTileSideRim == null) return;

            ForEachRoad2StraightPart(x, y, yaw, fullCell, sides, skin,
                (part, px, py, pyaw) => DrawRoad2TilePart(canvas, part, px, py, pyaw), rimCovered);
        }

        /// <summary>Road 2: vẽ mảnh GIAO đi CHUNG <see cref="ForEachRoad2JunctionTile"/> — mirror TUYỆT
        /// ĐỐI <see cref="AddRoad2JunctionTiles"/> (bake, plan 08).</summary>
        private void DrawRoad2JunctionTiles(Rect canvas, float jx, float jy, int mask, int junctionArms,
            System.Func<float, float, float, bool> rimCovered = null,
            System.Func<float, float, float, (float x, float y, float yaw)?> filletTurn = null,
            BlockRoadSkin skin = null, int sides = DirAll)
        {
            // rimCovered toạ độ tuyệt đối, enumerator toạ độ tương đối tâm giao — mirror RimBlockedProbe.
            System.Func<float, float, float, bool> rimCoveredLocal = rimCovered == null ? null
                : (rx, ry, ryaw) => rimCovered(jx + rx, jy + ry, ryaw);
            System.Func<float, float, float, (float x, float y, float yaw)?> filletTurnLocal =
                filletTurn == null ? null
                : (rx, ry, ryaw) =>
                {
                    (float x, float y, float yaw)? turn = filletTurn(jx + rx, jy + ry, ryaw);
                    return turn == null ? null : (turn.Value.x - jx, turn.Value.y - jy, turn.Value.yaw);
                };
            ForEachRoad2JunctionTile(mask, junctionArms,
                (part, dx, dy, yaw) => DrawRoad2TilePart(canvas, part, jx + dx, jy + dy, yaw),
                rimCoveredLocal, filletTurnLocal, skin, jx, jy, sides);
        }

        /// <summary>Lớp Road 2 (mặt cắt rộng x1.5, D2): vẽ icon thật đi CHUNG solver Road 2 (plan 07/08)
        /// nên preview khớp mesh mà <see cref="Apply"/> sẽ bake (SINGLE-SOLVER INVARIANT). Side/SideRim/
        /// Center tái dùng sprite type-1; curve/curve_rim/hway_to_road2 CHƯA có art (D4/D8) → tự bỏ vẽ
        /// đúng phần đó, side/rim/filler vẫn hiện đủ.</summary>
        private void DrawRoad2Sprites(Rect canvas, List<int> edges)
        {
            if (edges.Count == 0) return;
            EnsureRoadSprites();
            if (_spTileSide == null || _spTileSideRim == null) return;

            int[] hwMasks = BuildMasks(_highwayEdges);
            int[] road2Masks = BuildMasks(edges);
            // Legacy-view mask (road2) cho ramp scan + edge-fill — CHUNG với Apply nên preview khớp
            // bake. hwMasks GIỮ DENSE (xem Apply.cs).
            int[] road2MasksLegacy = BuildLegacyMasksFromEdges(edges);

            // Ramp Highway→Road2 (D5/plan 11): CHUNG CollectRampJunctions/TryRampRoadBridge với
            // DrawHighwaySprites, chỉ đổi mask sang road2Masks. Road2 KHÔNG bắc cầu bridge tile (mesh
            // rộng 3 ô, xem P4/backlog) — chỉ lấy hiệu ứng suppress ô Road2 dưới arm ramp.
            var rampSuppress = new HashSet<int>();
            List<(int x2, int y2, int stem, int hwMask)> ramps = CollectRampJunctions(hwMasks, road2MasksLegacy);
            foreach (var (rx2, ry2, stem, _) in ramps)
                TryRampRoadBridge(rx2, ry2, stem, road2MasksLegacy, rampSuppress, out _, out _, out _);

            // Khối Station/Parking trên Road 2: CHUNG solver với Apply (im lặng, không cần Part
            // Library) — mirror khối type-1 trong DrawRoadSprites, kể cả khối ghost đang hover.
            var suppressed2 = new BlockSuppression();
            var station2Roads = new List<(RoadTilePart part, float x, float y, float yaw)>();
            var parking2Roads = new List<(RoadTilePart part, float x, float y, float yaw)>();
            var blockStrips2 = new List<BlockStrip>();
            var skin2 = new BlockRoadSkin();
            CollectStationRoad2Placements(_stations, road2Masks, suppressed2, station2Roads, blockStrips2, skin2, null, false);
            CollectParkingRoad2Kerb(_parkings, road2Masks, suppressed2, parking2Roads, blockStrips2, skin2, null, false);

            var ghost2Roads = new List<(RoadTilePart part, float x, float y, float yaw)>();
            if (TryGhostBlock(out int ghostId, out bool ghostIsStation))
            {
                if (ghostIsStation)
                    CollectStationRoad2Placements(new[] { ghostId }, road2Masks, suppressed2, ghost2Roads, blockStrips2, skin2, null, false);
                else
                    CollectParkingRoad2Kerb(new[] { ghostId }, road2Masks, suppressed2, ghost2Roads, blockStrips2, skin2, null, false);
            }

            ApplyApronPlain(station2Roads, skin2);
            ApplyApronPlain(ghost2Roads, skin2);
            ApplyApronPlain(parking2Roads, skin2);

            var blockEdgeHalves2 = new List<(float x, float y, float yaw, int sides)>();
            var blockEdgeFulls2 = new List<(float x, float y, float yaw, int sides)>();
            CollectBlockEdgeFills(road2MasksLegacy, blockStrips2, blockEdgeHalves2, blockEdgeFulls2);

            bool Blocked(int idx) => hwMasks[idx] != 0 || suppressed2.Blocked(idx) || rampSuppress.Contains(idx);
            RoadLayout layout = ResolveRoadLayout(edges, Blocked, Road2SideBranchReachSteps, true);
            int[] masks = layout.Masks;
            int lw = LatticeW, lh = LatticeH;

            // Hiệu ứng mảnh GIAO — CHUNG CollectRoad2JunctionEffects với bake (CollectRoad2Placements)
            // để preview khớp: rim bị fillet bo góc phủ, đầu cụt sát mảnh giao bị nuốt.
            var filletKerb2 = new HashSet<long>();
            var filletTurns2 = new Dictionary<long, (float x, float y, float yaw)>();
            CollectRoad2JunctionEffects(layout, Blocked, filletKerb2, filletTurns2);
            System.Func<float, float, float, bool> rimCovered = filletKerb2.Count == 0 ? null
                : (rx, ry, ryaw) => filletKerb2.Contains(KerbCellKey(rx, ry, 0f, -0.75f, ryaw));
            System.Func<float, float, float, (float x, float y, float yaw)?> filletTurn =
                filletTurns2.Count == 0 ? null
                : (rx, ry, ryaw) => filletTurns2.TryGetValue(CurveKey(rx, ry, ryaw),
                    out (float x, float y, float yaw) turn) ? turn : ((float, float, float)?)null;

            for (int y = 0; y < lh; y++)
            {
                for (int x = 0; x < lw; x++)
                {
                    int idx = y * lw + x;
                    int m = masks[idx];
                    if (m == 0 || Blocked(idx) || layout.Skip(idx)) continue;

                    if (IsStraightLikeMask(m))
                    {
                        (float ax, float ay, bool full) = StraightAnchorFor(layout, idx, m, x, y);
                        DrawRoad2StraightTiles(canvas, ax, ay, StraightYaw(m), full,
                            StraightSides(m) & ~suppressed2.Sides(idx), skin2, rimCovered);
                        continue;
                    }

                    DrawRoad2JunctionTiles(canvas, x * 0.5f, y * 0.5f, m, Road2JunctionArms(masks, x, y, m),
                        rimCovered, filletTurn, skin2, DirAll & ~suppressed2.Sides(idx));
                }
            }

            foreach ((float hx, float hy, float hyaw, int hSides) in blockEdgeHalves2)
                DrawRoad2StraightTiles(canvas, hx, hy, hyaw, false, hSides, skin2);

            foreach ((float fx, float fy, float fyaw, int fSides) in blockEdgeFulls2)
                DrawRoad2StraightTiles(canvas, fx, fy, fyaw, true, fSides, skin2);

            ForEachRoad2ApronFiller(blockStrips2,
                (fx, fy, fyaw) => DrawRoad2TilePart(canvas, Road2TilePart.Filler, fx, fy, fyaw));

            DrawStationRoadSprites(canvas, station2Roads, 1f);
            DrawStationRoadSprites(canvas, ghost2Roads, 0.5f);
            DrawStationRoadSprites(canvas, parking2Roads, 1f);

            if (_spRampHway2 == null) return;
            float s = _cellPixelSize * 4f; // ramp 4×4 ô, cùng quy ước hway_to_road (D5)
            Vector2 rectSize = _spRampHway2.rect.size;
            Vector2 piv = new Vector2(
                rectSize.x > 0f ? _spRampHway2.pivot.x / rectSize.x : 0.5f,
                rectSize.y > 0f ? _spRampHway2.pivot.y / rectSize.y : 0.5f);
            foreach ((int x2, int y2, int stem, int hwMask) in ramps)
            {
                Vector2 p = PointToPixelF(canvas, x2 * 0.5f, y2 * 0.5f);
                int turns = TurnsFromBase(DirN | DirS | DirW, hwMask | stem);
                bool flipped = RampFlipped(x2, y2);
                float a = piv.x, b = flipped ? piv.y : 1f - piv.y;
                for (int k = 0; k < (turns & 3); k++) { float na = 1f - b, nb = a; a = na; b = nb; }
                var rect = new Rect(p.x - a * s, p.y - b * s, s, s);
                GUI.DrawTexture(rect, GetRoadPieceTex(_spRampHway2, turns, flipped), ScaleMode.StretchToFill, true);
            }
        }

        /// <summary>Số nấc RotateMask90 (CW) để baseMask trùng targetMask.</summary>
        private static int TurnsFromBase(int baseMask, int targetMask)
        {
            int m = baseMask;
            for (int k = 0; k < 4; k++)
            {
                if (m == targetMask) return k;
                m = RotateMask90(m);
            }
            return 0;
        }

        /// <summary>Texture piece đã xoay sẵn turns×90° CW, cache theo (sprite, turns). Vẽ bằng GUI.DrawTexture
        /// axis-aligned nên được scroll-view clip đúng mép — thay cho GUI.matrix (nét xoay không bị clip).</summary>
        private Texture2D GetRoadPieceTex(Sprite sprite, int turns, bool mirrorY = false)
        {
            turns &= 3;
            var key = (sprite, turns, mirrorY);
            if (_roadPieceTex.TryGetValue(key, out Texture2D cached) && cached != null) return cached;

            Texture2D tex = ExtractPiece(sprite);
            if (mirrorY) // lật gương ramp: mirror trục cao tốc TRONG frame gốc (trước khi xoay turns)
            {
                Texture2D flipped = FlipY(tex);
                UnityEngine.Object.DestroyImmediate(tex);
                tex = flipped;
            }
            for (int k = 0; k < turns; k++)
            {
                Texture2D rotated = RotateCW90(tex);
                UnityEngine.Object.DestroyImmediate(tex);
                tex = rotated;
            }
            _roadPieceTex[key] = tex;
            return tex;
        }

        /// <summary>Lật Texture2D theo trục dọc (trên↔dưới). Ramp base có cao tốc DỌC nên đây = mirror
        /// trục cao tốc → đổi bên thân ramp loe, khớp mesh bake scaleMul.x = -1.</summary>
        private static Texture2D FlipY(Texture2D src)
        {
            int w = src.width, h = src.height;
            Color32[] s = src.GetPixels32();
            var d = new Color32[w * h];
            for (int y = 0; y < h; y++)
                System.Array.Copy(s, y * w, d, (h - 1 - y) * w, w);
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
                { filterMode = src.filterMode, hideFlags = HideFlags.HideAndDontSave };
            t.SetPixels32(d);
            t.Apply();
            return t;
        }

        /// <summary>Cắt region của sprite (trong atlas PSD) ra Texture2D upright. Đọc qua bản readable đầy đủ
        /// (Blit + ReadPixels) để không phụ thuộc cờ Read/Write Enabled của asset.
        /// Dùng <c>rect</c> (ô slice gốc) chứ KHÔNG dùng <c>textureRect</c>: mesh Tight cắt sát nét nên
        /// textureRect nhỏ hơn slice (Road_T 161×192, Road_turn 161×161) → cắt theo nó rồi kéo vừa khung
        /// vuông sẽ bóp méo + lệch tâm.</summary>
        private Texture2D ExtractPiece(Sprite sprite)
        {
            Rect tr = sprite.rect;
            int w = Mathf.RoundToInt(tr.width), h = Mathf.RoundToInt(tr.height);
            Texture2D full = GetReadable(sprite.texture);
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
                { filterMode = sprite.texture.filterMode, hideFlags = HideFlags.HideAndDontSave };
            t.SetPixels(full.GetPixels(Mathf.RoundToInt(tr.x), Mathf.RoundToInt(tr.y), w, h));
            t.Apply();
            return t;
        }

        /// <summary>Bản Texture2D readable (giữ màu sRGB) của atlas, cache theo texture nguồn.</summary>
        private Texture2D GetReadable(Texture src)
        {
            if (_roadReadable.TryGetValue(src, out Texture2D r) && r != null) return r;
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave };
            tex.ReadPixels(new Rect(0f, 0f, src.width, src.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            _roadReadable[src] = tex;
            return tex;
        }

        /// <summary>Xoay Texture2D 90° theo chiều kim đồng hồ (khớp GUIUtility.RotateAroundPivot dương).</summary>
        private static Texture2D RotateCW90(Texture2D src)
        {
            int w = src.width, h = src.height;
            Color32[] s = src.GetPixels32();
            var d = new Color32[w * h];
            for (int dy = 0; dy < w; dy++)
                for (int dx = 0; dx < h; dx++)
                    d[dy * h + dx] = s[dx * w + (w - 1 - dy)];
            var t = new Texture2D(h, w, TextureFormat.RGBA32, false)
                { filterMode = src.filterMode, hideFlags = HideFlags.HideAndDontSave };
            t.SetPixels32(d);
            t.Apply();
            return t;
        }

        /// <summary>Nạp sprite piece từ _road_plan.psd cho MỌI ô còn trống (override ở tab Setup được giữ).
        /// Không có art → ô đó vẫn null (lớp tương ứng bỏ vẽ, không fallback ô màu).</summary>
        private void EnsureRoadSprites()
        {
            // Required: mọi slice ĐÃ có trong psd (kể cả Road_0.5x1_center — D8). Road2 curve/curve_rim/
            // hway_to_road2, PATH path_side/path_center/path_curve/path_turn CHƯA có art nên KHÔNG đưa
            // vào đây — nếu vào, chain && không bao giờ true và hàm re-scan toàn bộ atlas mỗi lần gọi
            // (mỗi repaint), một regression hiệu năng editor thật (P7).
            if (_spTileSide != null && _spTileSideRim != null && _spTileCurve != null
                && _spTileCurveRim != null && _spTileCenter != null
                && _spTileTurn != null && _spTileTurnRim != null
                && _spTileTurn1x1 != null && _spTileTurn1x1Rim != null
                && _spTileTurn3x3 != null && _spTileTurn3x3Rim != null
                && _spHighway != null && _spHighwayRim != null && _spRampHway != null
                && _spStationArea != null && _spParkingArea != null
                && _spRoad2CenterFiller != null) return;
            string path = RoadPlanAtlasPath;
            if (string.IsNullOrEmpty(path)) return;
            foreach (UnityEngine.Object obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is not Sprite s) continue;
                switch (s.name)
                {
                    case "Road_1x1_side":     if (_spTileSide == null)     _spTileSide = s;     break;
                    case "Road_1x1_side_rim": if (_spTileSideRim == null)  _spTileSideRim = s;  break;
                    case "Road_1x1_curve":    if (_spTileCurve == null)    _spTileCurve = s;    break;
                    case "Road_1x1_curve_rim": if (_spTileCurveRim == null) _spTileCurveRim = s; break;
                    case "Road_1x1_center":   if (_spTileCenter == null)   _spTileCenter = s;   break;
                    case "Road_2x2_turn":     if (_spTileTurn == null)    _spTileTurn = s;    break;
                    case "Road_2x2_turn_rim": if (_spTileTurnRim == null) _spTileTurnRim = s; break;
                    case "Road_1x1_turn":     if (_spTileTurn1x1 == null)    _spTileTurn1x1 = s;    break;
                    case "Road_1x1_turn_rim": if (_spTileTurn1x1Rim == null) _spTileTurn1x1Rim = s; break;
                    case "Road_3x3_turn":     if (_spTileTurn3x3 == null)    _spTileTurn3x3 = s;    break;
                    case "Road_3x3_turn_rim": if (_spTileTurn3x3Rim == null) _spTileTurn3x3Rim = s; break;
                    case "Highway_1x2":     if (_spHighway == null)    _spHighway = s;    break;
                    case "Highway_1x2_rim": if (_spHighwayRim == null) _spHighwayRim = s; break;
                    case "hway_to_road": if (_spRampHway == null) _spRampHway = s; break;
                    case "station_area":   if (_spStationArea == null)   _spStationArea = s;   break;
                    case "parking_area":   if (_spParkingArea == null)   _spParkingArea = s;   break;
                    // Road 2 (D8/D9): Road_0.5x1_center đã có art (reuse); curve/curve_rim/ramp thì
                    // chưa — case vẫn wire sẵn để tự nạp ngay khi psd bổ sung slice, không đổi code.
                    case "Road_0.5x1_center": if (_spRoad2CenterFiller == null) _spRoad2CenterFiller = s; break;
                    case "road2_curve":       if (_spRoad2Curve == null)        _spRoad2Curve = s;        break;
                    case "road2_curve_rim":   if (_spRoad2CurveRim == null)     _spRoad2CurveRim = s;     break;
                    case "hway_to_road2":     if (_spRampHway2 == null)         _spRampHway2 = s;         break;
                    case "path_side":         if (_spPathSide == null)          _spPathSide = s;          break;
                    case "path_center":       if (_spPathCenter == null)        _spPathCenter = s;        break;
                    case "path_curve":        if (_spPathCurve == null)         _spPathCurve = s;         break;
                    case "path_turn":         if (_spPathTurn == null)          _spPathTurn = s;          break;
                }
            }
        }

        /// <summary>Lớp PATH (lối đi bộ, D1): vẽ icon thật đi CHUNG <see cref="ForEachPathNode"/> với
        /// bake (SSI) — thiếu slice nào thì bỏ đúng ô đó (D4), không chặn phần còn lại.</summary>
        private void DrawPathSprites(Rect canvas, List<int> edges)
        {
            if (edges.Count == 0) return;
            EnsureRoadSprites();
            if (_spPathSide == null) return;

            ForEachPathNode(edges, (part, x, y, yaw) =>
            {
                Sprite sprite = PathTilePartSprite(part);
                if (sprite == null) return;

                if (part == PathTilePart.Center)
                {
                    DrawSpriteCells(canvas, sprite, x, y, 0);
                    return;
                }

                DrawTileSprite(PointToPixelF(canvas, x, y), _cellPixelSize, sprite, yaw,
                    PathTilePartBaseTurns(part));
            });
        }

        private Sprite PathTilePartSprite(PathTilePart part) => part switch
        {
            PathTilePart.Side => _spPathSide,
            PathTilePart.Center => _spPathCenter,
            PathTilePart.Curve => _spPathCurve,
            PathTilePart.Turn => _spPathTurn,
            _ => null,
        };

        private static int PathTilePartBaseTurns(PathTilePart part) =>
            part == PathTilePart.Curve ? CurveSpriteBaseTurns : TileSpriteBaseTurns;
    }
}
#endif
