#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Draws Road 2 sprites on canvas from CollectResult. D5: iterates
    /// CollectResult.Road2 — KHÔNG tự gom masks/suppress/layout/fillet effects.</summary>
    internal sealed class Road2SpriteRenderer
    {
        private readonly RoadTileDrawing _drawing;
        private readonly TilePartRegistry _reg;
        private readonly SpriteTextureCache _texCache;

        internal Road2SpriteRenderer(RoadTileDrawing drawing, TilePartRegistry reg, SpriteTextureCache texCache)
        {
            _drawing = drawing;
            _reg = reg;
            _texCache = texCache;
        }

        /// <summary>Lớp Road 2 (mặt cắt rộng x1.5, D2): vẽ icon thật đi CHUNG solver Road 2. D5: iterates
        /// CollectResult.Road2 (PlaceList). Side/SideRim/Center tái dùng sprite type-1; curve/curve_rim/
        /// hway_to_road2 CHƯA có art (D4/D8) → tự bỏ vẽ phần đó.</summary>
        internal void Draw(Rect canvas, CollectResult result, RoadPartLibrary library,
            Sprite spRampHway2, int gridHeight, float cellPixelSize)
        {
            if (result == null) return;
            Sprite tileSide = _reg.SpriteFor(RoadTilePart.Side);
            Sprite tileSideRim = _reg.SpriteFor(RoadTilePart.SideRim);
            if (tileSide == null || tileSideRim == null) return;

            DrawPlaceList(canvas, result.Road2, library, gridHeight, cellPixelSize);

            // Station/Parking road tiles on Road 2 layer — uses type-1 tile parts (RoadTilePart)
            DrawTileList(canvas, result.Station2Roads, 1f, gridHeight, cellPixelSize);
            DrawTileList(canvas, result.Parking2Roads, 1f, gridHeight, cellPixelSize);

            // Ramp hway_to_road2 — cùng quy ước hway_to_road (D5)
            if (spRampHway2 == null || library == null) return;
            DrawRoad2Ramps(canvas, result.Highway, library, spRampHway2, gridHeight, cellPixelSize);
        }

        /// <summary>Iterate PlaceList và vẽ Road 2 sprites — reverse-map prefab → Road2TilePart.</summary>
        private void DrawPlaceList(Rect canvas,
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            RoadPartLibrary library, int gridHeight, float cellPixelSize)
        {
            if (placements == null || placements.Count == 0) return;
            foreach ((float x, float y, GameObject prefab, float yaw, Vector3 _) in placements)
            {
                if (prefab == null) continue;
                if (_reg.TryReverseLookupRoad2(prefab, out Road2TilePart part))
                    _drawing.DrawRoad2TilePart(canvas, part, x, y, yaw, gridHeight, cellPixelSize);
                else if (_reg.TryReverseLookupRoad(prefab, out RoadTilePart rpart))
                    _drawing.DrawTilePart(canvas, rpart, x, y, yaw, gridHeight, cellPixelSize);
            }
        }

        /// <summary>Draw Road2 ramp entries from Highway PlaceList — nhận diện bằng prefab = hway_to_road2.</summary>
        private void DrawRoad2Ramps(Rect canvas,
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> hwPlacements,
            RoadPartLibrary library, Sprite spRampHway2, int gridHeight, float cellPixelSize)
        {
            if (spRampHway2.rect.width <= 0f || spRampHway2.rect.height <= 0f) return;
            float s = cellPixelSize * 4f; // ramp 4×4 ô, cùng quy ước hway_to_road (D5)
            Vector2 rectSize = spRampHway2.rect.size;
            Vector2 piv = new Vector2(
                rectSize.x > 0f ? spRampHway2.pivot.x / rectSize.x : 0.5f,
                rectSize.y > 0f ? spRampHway2.pivot.y / rectSize.y : 0.5f);

            foreach ((float x, float y, GameObject prefab, float yaw, Vector3 scaleMul) in hwPlacements)
            {
                if (prefab != library.hway_to_road2) continue;
                Vector2 p = RoadTileDrawing.PointToPixelF(canvas, x, y, gridHeight, cellPixelSize);
                int turns = SpriteTextureCache.TurnsFromBase(
                    DirBits.N | DirBits.S | DirBits.W,
                    Mathf.RoundToInt(yaw / 90f) switch
                    {
                        1 => DirBits.E | DirBits.W | DirBits.N,
                        2 => DirBits.N | DirBits.S | DirBits.E,
                        3 => DirBits.E | DirBits.W | DirBits.S,
                        _ => DirBits.N | DirBits.S | DirBits.W,
                    });
                bool flipped = scaleMul.x < 0f;
                float a = piv.x, b = flipped ? piv.y : 1f - piv.y;
                for (int k = 0; k < (turns & 3); k++) { float na = 1f - b, nb = a; a = na; b = nb; }
                var rect = new Rect(p.x - a * s, p.y - b * s, s, s);
                GUI.DrawTexture(rect, _texCache.GetRoadPieceTex(spRampHway2, turns, flipped),
                    ScaleMode.StretchToFill, true);
            }
        }

        /// <summary>Vẽ TileList (station/parking road tiles) tại alpha chỉ định.</summary>
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
    }
}
#endif
