#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Draws highway column sprites and ramp sprites on canvas. D5: iterates
    /// CollectResult.Highway — KHÔNG tự gom cột/ramp.</summary>
    internal sealed class HighwaySpriteRenderer
    {
        private readonly RoadTileDrawing _drawing;
        private readonly TilePartRegistry _reg;
        private readonly SpriteTextureCache _texCache;

        internal HighwaySpriteRenderer(RoadTileDrawing drawing, TilePartRegistry reg, SpriteTextureCache texCache)
        {
            _drawing = drawing;
            _reg = reg;
            _texCache = texCache;
        }

        /// <summary>Lớp Highway: vẽ ĐÚNG bộ ô modular mà bake đặt. D5: iterates CollectResult.Highway
        /// (PlaceList) — mỗi entry là một cột highway hoặc ramp placement đã có (x, y, prefab, yaw, scaleMul).
        /// Ramp sprite (4×4 ô) vẽ bằng pivot math riêng vì nó KHÔNG đi qua bộ ô modular 1×1.</summary>
        internal void Draw(Rect canvas, CollectResult result, RoadPartLibrary library,
            Sprite spRampHway, int gridHeight, float cellPixelSize)
        {
            if (result == null) return;
            Sprite hwSprite = _reg.HighwaySprite;
            Sprite hwRimSprite = _reg.HighwayRimSprite;

            foreach ((float x, float y, GameObject prefab, float yaw, Vector3 scaleMul) in result.Highway)
            {
                if (prefab == null) continue;

                // Cột highway thường: highway_1x2 / highway_1x2_rim
                if (prefab == (library != null ? library.hway1x2_side : null))
                {
                    if (hwSprite != null)
                        DrawColumnSprite(canvas, hwSprite, x, y, yaw, gridHeight, cellPixelSize);
                    continue;
                }
                if (prefab == (library != null ? library.hway1x2_side_rim : null))
                {
                    if (hwRimSprite != null)
                        DrawColumnSprite(canvas, hwRimSprite, x, y, yaw, gridHeight, cellPixelSize);
                    continue;
                }

                // Ramp hway_to_road: 4×4 ô, custom pivot math
                if (prefab == (library != null ? library.hway_to_road : null) && spRampHway != null)
                {
                    DrawRampSprite(canvas, spRampHway, x, y, yaw, scaleMul, gridHeight, cellPixelSize);
                    continue;
                }

                // Fallback: tile part reverse lookup
                if (_reg.TryReverseLookupRoad(prefab, out RoadTilePart part))
                    _drawing.DrawTilePart(canvas, part, x, y, yaw, gridHeight, cellPixelSize);
            }
        }

        /// <summary>Vẽ 1 sprite cột highway tại vị trí lưới — neo pivot như tile modular.</summary>
        private void DrawColumnSprite(Rect canvas, Sprite sprite, float x, float y, float yaw,
            int gridHeight, float cellPixelSize)
        {
            _drawing.DrawTileSprite(
                RoadTileDrawing.PointToPixelF(canvas, x, y, gridHeight, cellPixelSize),
                cellPixelSize, sprite, yaw);
        }

        /// <summary>Ramp sprite 4×4 ô: pivot neo ĐÚNG nút giao, xoay CW theo turns, lật gương khi
        /// scaleMul.x &lt; 0 (phím F). Cùng quy ước với bake nên preview khớp mesh.</summary>
        private void DrawRampSprite(Rect canvas, Sprite sprite, float x, float y, float yaw,
            Vector3 scaleMul, int gridHeight, float cellPixelSize)
        {
            if (sprite.rect.width <= 0f || sprite.rect.height <= 0f) return;
            Vector2 p = RoadTileDrawing.PointToPixelF(canvas, x, y, gridHeight, cellPixelSize);
            float s = cellPixelSize * 4f; // ramp 4×4 ô
            Vector2 rectSize = sprite.rect.size;
            Vector2 piv = new Vector2(
                rectSize.x > 0f ? sprite.pivot.x / rectSize.x : 0.5f,
                rectSize.y > 0f ? sprite.pivot.y / rectSize.y : 0.5f);
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
            // Neo pivot sprite lên ĐÚNG nút giao: (a,b) = pivot theo gốc TRÁI-TRÊN (screen y-down),
            // xoay CW `turns` nấc cho khớp texture đã xoay sẵn. Lật gương = FlipY trong frame gốc
            // ⇒ pivot.y đảo (b = piv.y thay vì 1 - piv.y) TRƯỚC khi xoay.
            float a = piv.x, b = flipped ? piv.y : 1f - piv.y;
            for (int k = 0; k < (turns & 3); k++) { float na = 1f - b, nb = a; a = na; b = nb; }
            var rect = new Rect(p.x - a * s, p.y - b * s, s, s);
            GUI.DrawTexture(rect, _texCache.GetRoadPieceTex(sprite, turns, flipped),
                ScaleMode.StretchToFill, true);
        }

        // ── Toolbar icon draw (standalone, không cần CollectResult) ──────────

        /// <summary>Vẽ MỘT cột cao tốc quanh tâm pixel — dùng CHUNG ForEachHighwayColumnTile
        /// với bake nên preview khớp mesh.</summary>
        internal void DrawHighwayColumn(Vector2 center, float cellPixels, float yaw,
            System.Action<float, float, float, System.Action<float, float, float>> forEachHighwayColumnTile)
        {
            Sprite spHw = _reg.HighwaySprite;
            Sprite spHwRim = _reg.HighwayRimSprite;
            if (spHw == null || spHwRim == null) return;
            forEachHighwayColumnTile(0f, 0f, yaw, (tx, ty, tyaw) =>
            {
                // Trục y lưới hướng LÊN màn hình nên offset ô đảo dấu theo y.
                var pivot = new Vector2(center.x + tx * cellPixels, center.y - ty * cellPixels);
                _drawing.DrawTileSprite(pivot, cellPixels, spHw, tyaw);
                _drawing.DrawTileSprite(pivot, cellPixels, spHwRim, tyaw);
            });
        }

        /// <summary>Vẽ 1 Ô cao tốc đã vẽ (2 cột lệch ±RoadTileColumnOffsetCells) — ghost của brush.</summary>
        internal void DrawHighwayCellTiles(Vector2 center, float cellPixels, float yaw,
            float columnOffsetCells,
            System.Action<float, float, float, System.Action<float, float, float>> forEachHighwayColumnTile)
        {
            bool alongX = (Mathf.RoundToInt(yaw / 90f) & 1) == 0;
            float dx = (alongX ? columnOffsetCells : 0f) * cellPixels;
            float dy = (alongX ? 0f : columnOffsetCells) * cellPixels;
            DrawHighwayColumn(new Vector2(center.x - dx, center.y + dy), cellPixels, yaw, forEachHighwayColumnTile);
            DrawHighwayColumn(new Vector2(center.x + dx, center.y - dy), cellPixels, yaw, forEachHighwayColumnTile);
        }
    }
}
#endif
