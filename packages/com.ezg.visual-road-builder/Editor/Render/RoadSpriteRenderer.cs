#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Draws type-1 road sprites on canvas from CollectResult — pixel mapping + sprite choice only,
    /// KHÔNG re-derive tile position hay yaw (D5). Toolbar icon draw giữ phiên bản standalone.</summary>
    internal sealed class RoadSpriteRenderer
    {
        private readonly RoadTileDrawing _drawing;
        private readonly TilePartRegistry _reg;
        private readonly SpriteTextureCache _texCache;

        internal RoadSpriteRenderer(RoadTileDrawing drawing, TilePartRegistry reg, SpriteTextureCache texCache)
        {
            _drawing = drawing;
            _reg = reg;
            _texCache = texCache;
        }

        /// <summary>Lớp Đường: vẽ icon thật từ _road_plan.psd — mỗi điểm chọn piece theo số nhánh
        /// (thẳng/cua/ngã ba/ngã tư) rồi xoay khớp mask, đúng như lúc bake. Thiếu art → fallback ô cam.
        /// D5: iterates CollectResult.Road (PlaceList) — KHÔNG tự gom masks/suppress/layout.</summary>
        internal void Draw(Rect canvas, CollectResult result, int gridHeight, float cellPixelSize)
        {
            if (result == null) return;
            Sprite tileSide = _reg.SpriteFor(RoadTilePart.Side);
            Sprite tileSideRim = _reg.SpriteFor(RoadTilePart.SideRim);
            if (tileSide == null || tileSideRim == null) return;

            // Road placements (side, sideRim, curve, curveRim, center, turn, ...) — ĐÚNG bộ ô
            // mà CollectAll.Run → CollectRoadPlacements + AddStraightTiles gom ra.
            DrawPlaceList(canvas, result.Road, gridHeight, cellPixelSize);

            // Station/Parking road tiles — đã ở dạng TileList (part, x, y, yaw), vẽ trực tiếp.
            DrawTileList(canvas, result.StationRoads, 1f, gridHeight, cellPixelSize);
            DrawTileList(canvas, result.ParkingRoads, 1f, gridHeight, cellPixelSize);
        }

        /// <summary>Mảnh đường trước mặt station bằng ĐÚNG bộ ô modular mà bake đặt. Khối ghost
        /// vẽ mờ theo <paramref name="alpha"/>.</summary>
        internal void DrawStationRoadSprites(
            Rect canvas, List<(RoadTilePart part, float x, float y, float yaw)> parts, float alpha,
            int gridHeight, float cellPixelSize)
        {
            DrawTileList(canvas, parts, alpha, gridHeight, cellPixelSize);
        }

        /// <summary>Vẽ danh sách TileList (part, x, y, yaw) tại alpha chỉ định.</summary>
        private void DrawTileList(
            Rect canvas, List<(RoadTilePart part, float x, float y, float yaw)> tiles, float alpha,
            int gridHeight, float cellPixelSize)
        {
            if (tiles == null || tiles.Count == 0) return;
            Color prev = GUI.color;
            if (alpha < 1f) GUI.color = new Color(1f, 1f, 1f, alpha);
            foreach ((RoadTilePart part, float px, float py, float yaw) in tiles)
                _drawing.DrawTilePart(canvas, part, px, py, yaw, gridHeight, cellPixelSize);
            GUI.color = prev;
        }

        /// <summary>Iterate PlaceList (bake format) và vẽ sprite — reverse-map prefab → RoadTilePart
        /// qua TilePartRegistry.Prefab(part).</summary>
        private void DrawPlaceList(Rect canvas, List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            int gridHeight, float cellPixelSize)
        {
            if (placements == null || placements.Count == 0) return;
            foreach ((float x, float y, GameObject prefab, float yaw, Vector3 _) in placements)
            {
                if (prefab == null) continue;
                if (_reg.TryReverseLookupRoad(prefab, out RoadTilePart part))
                    _drawing.DrawTilePart(canvas, part, x, y, yaw, gridHeight, cellPixelSize);
            }
        }

        // ── Toolbar icon draw (standalone, không cần CollectResult) ──────────

        /// <summary>Vẽ mảnh thẳng quanh tâm pixel — icon toolbar thu theo cỡ vừa nút.</summary>
        internal void DrawStraightTiles(Vector2 center, float cellPixels, float yaw, bool fullCell,
            int sides, System.Action<float, float, float, System.Action<float, float, float>> forEachStraightTile)
        {
            Sprite tileSide = _reg.SpriteFor(RoadTilePart.Side);
            Sprite tileSideRim = _reg.SpriteFor(RoadTilePart.SideRim);
            if (tileSide == null || tileSideRim == null) return;
            forEachStraightTile(0f, 0f, yaw, (tx, ty, tyaw) =>
            {
                // Trục y lưới hướng LÊN màn hình nên offset ô đảo dấu theo y.
                var pivot = new Vector2(center.x + tx * cellPixels, center.y - ty * cellPixels);
                _drawing.DrawTileSprite(pivot, cellPixels, tileSide, tyaw);
                _drawing.DrawTileSprite(pivot, cellPixels, tileSideRim, tyaw);
            });
        }
    }
}
#endif
