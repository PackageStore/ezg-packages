#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Low-level tile sprite drawing: DrawTilePart, DrawTileSprite, DrawSpriteCells,
    /// TileSpriteRect, SpriteCellsRect. Pure pixel mapping — no mask/collect logic.</summary>
    internal sealed class RoadTileDrawing
    {
        private readonly TilePartRegistry _reg;
        private readonly SpriteTextureCache _texCache;

        internal RoadTileDrawing(TilePartRegistry registry, SpriteTextureCache texCache)
        {
            _reg = registry;
            _texCache = texCache;
        }

        /// <summary>Như PointToPixelF trên window nhưng nhận params trực tiếp — nguồn DUY NHẤT
        /// pixel mapping cho toàn bộ renderer stack.</summary>
        internal static Vector2 PointToPixelF(Rect canvas, float x, float y,
            int gridHeight, float cellPixelSize)
        {
            return new Vector2(
                canvas.x + GridConst.GutterLeft + GridConst.OuterMargin + x * cellPixelSize,
                canvas.y + GridConst.GutterTop + GridConst.OuterMargin + (gridHeight - 1 - y) * cellPixelSize);
        }

        /// <summary>Vẽ MỘT ô modular tại điểm lưới (x, y) — dùng chung cho mảnh giao và mảnh trước mặt
        /// station nên 2 chỗ luôn ra cùng hình. Thiếu slice nào thì bỏ ô đó.</summary>
        internal void DrawTilePart(Rect canvas, RoadTilePart part, float x, float y, float yaw,
            int gridHeight, float cellPixelSize)
        {
            if (part == RoadTilePart.Center)
            {
                Sprite center = _reg.SpriteFor(RoadTilePart.Center);
                if (center != null) DrawSpriteCells(canvas, center, x, y, 0, gridHeight, cellPixelSize);
                return;
            }

            DrawTileSprite(PointToPixelF(canvas, x, y, gridHeight, cellPixelSize), cellPixelSize,
                _reg.SpriteFor(part), yaw, TilePartRegistry.BaseTurns(part));
        }

        /// <summary>Road 2 mirror của DrawTilePart: Side/SideRim/Center TÁI DÙNG sprite type-1;
        /// Curve/CurveRim/Filler đi qua 3 slice riêng. Thiếu slice nào thì bỏ đúng ô đó (D4).</summary>
        internal void DrawRoad2TilePart(Rect canvas, Road2TilePart part, float x, float y, float yaw,
            int gridHeight, float cellPixelSize)
        {
            if (part == Road2TilePart.Center)
            {
                Sprite center = _reg.SpriteFor(Road2TilePart.Center);
                if (center != null) DrawSpriteCells(canvas, center, x, y, 0, gridHeight, cellPixelSize);
                return;
            }

            DrawTileSprite(PointToPixelF(canvas, x, y, gridHeight, cellPixelSize), cellPixelSize,
                _reg.SpriteFor(part), yaw, TilePartRegistry.BaseTurns(part));
        }

        /// <summary>Vẽ 1 ô modular NEO THEO PIVOT của slice — cùng quy ước với mesh (prefab cũng đặt
        /// pivot tại điểm này rồi xoay quanh nó), nên hình trên canvas bám đúng chỗ mesh sẽ nằm.
        /// Kích thước suy từ rect slice ở SpritePixelsPerCell px/ô; xoay lẻ nấc thì hoán đổi rộng/cao
        /// và pivot đi theo (a, b) → (1 - b, a) mỗi nấc CW.
        /// <paramref name="baseTurns"/> = lệch hướng vẽ của slice so với mesh yaw 0.</summary>
        internal void DrawTileSprite(Vector2 pivotPixel, float cellPixels, Sprite sprite, float yaw,
            int baseTurns = TilePartRegistry.TileSpriteBaseTurns)
        {
            if (sprite == null || sprite.rect.width <= 0f || sprite.rect.height <= 0f) return;
            int turns = (Mathf.RoundToInt(yaw / 90f) + baseTurns) & 3;
            GUI.DrawTexture(TileSpriteRect(pivotPixel, cellPixels, sprite, turns),
                _texCache.GetRoadPieceTex(sprite, turns), ScaleMode.StretchToFill, true);
        }

        /// <summary>Rect pixel của 1 ô modular đã xoay <paramref name="turns"/> nấc CW, neo theo pivot
        /// slice — nguồn DUY NHẤT hình học ô cho cả vẽ sprite và debug boundary từng ô.</summary>
        internal static Rect TileSpriteRect(Vector2 pivotPixel, float cellPixels, Sprite sprite, int turns)
        {
            float w = sprite.rect.width / TilePartRegistry.SpritePixelsPerCell * cellPixels;
            float h = sprite.rect.height / TilePartRegistry.SpritePixelsPerCell * cellPixels;
            float a = sprite.pivot.x / sprite.rect.width;
            float b = 1f - sprite.pivot.y / sprite.rect.height;
            for (int k = 0; k < (turns & 3); k++) (a, b, w, h) = (1f - b, a, h, w);
            return new Rect(pivotPixel.x - a * w, pivotPixel.y - b * h, w, h);
        }

        /// <summary>Vẽ sprite (xoay sẵn turns nấc CW) canh TÂM tại điểm lưới (cx, cy).</summary>
        internal void DrawSpriteCells(Rect canvas, Sprite sprite, float cx, float cy, int turns,
            int gridHeight, float cellPixelSize)
        {
            turns &= 3;
            GUI.DrawTexture(SpriteCellsRect(canvas, sprite, cx, cy, turns, gridHeight, cellPixelSize),
                _texCache.GetRoadPieceTex(sprite, turns), ScaleMode.StretchToFill, true);
        }

        /// <summary>Rect pixel của sprite canh TÂM tại điểm lưới (cx, cy). Kích thước đọc từ slice ở
        /// SpritePixelsPerCell px/ô; xoay lẻ nấc → hoán đổi rộng/cao.</summary>
        internal static Rect SpriteCellsRect(Rect canvas, Sprite sprite, float cx, float cy, int turns,
            int gridHeight, float cellPixelSize)
        {
            float w = sprite.rect.width / TilePartRegistry.SpritePixelsPerCell * cellPixelSize;
            float h = sprite.rect.height / TilePartRegistry.SpritePixelsPerCell * cellPixelSize;
            if ((turns & 1) != 0) (w, h) = (h, w);
            Vector2 p = PointToPixelF(canvas, cx, cy, gridHeight, cellPixelSize);
            return new Rect(p.x - w * 0.5f, p.y - h * 0.5f, w, h);
        }
    }
}
#endif
