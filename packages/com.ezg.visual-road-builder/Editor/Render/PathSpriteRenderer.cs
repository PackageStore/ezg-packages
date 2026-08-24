#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Draws Path sprites on canvas from CollectResult. D5: iterates
    /// CollectResult.Path — KHÔNG tự gom ForEachPathNode.</summary>
    internal sealed class PathSpriteRenderer
    {
        private readonly RoadTileDrawing _drawing;
        private readonly TilePartRegistry _reg;

        internal PathSpriteRenderer(RoadTileDrawing drawing, TilePartRegistry reg)
        {
            _drawing = drawing;
            _reg = reg;
        }

        /// <summary>Lớp PATH (lối đi bộ, D1): vẽ icon thật đi CHUNG solver Path (SSI) —
        /// thiếu slice nào thì bỏ đúng ô đó (D4), không chặn phần còn lại.</summary>
        internal void Draw(Rect canvas, CollectResult result, RoadPartLibrary library,
            int gridHeight, float cellPixelSize)
        {
            if (result == null) return;
            Sprite pathSide = _reg.SpriteFor(PathTilePart.Side);
            if (pathSide == null) return;

            foreach ((float x, float y, GameObject prefab, float yaw, Vector3 _) in result.Path)
            {
                if (prefab == null) continue;
                // Path không có rim (D3). Reverse-map qua tên slot.
                PathTilePart? part = ReversePathPrefab(prefab, library);
                if (part == null) continue;

                Sprite sprite = _reg.SpriteFor(part.Value);
                if (sprite == null) continue;

                if (part.Value == PathTilePart.Center)
                {
                    _drawing.DrawSpriteCells(canvas, sprite, x, y, 0, gridHeight, cellPixelSize);
                    continue;
                }

                _drawing.DrawTileSprite(
                    RoadTileDrawing.PointToPixelF(canvas, x, y, gridHeight, cellPixelSize),
                    cellPixelSize, sprite, yaw, TilePartRegistry.BaseTurns(part.Value));
            }
        }

        private static PathTilePart? ReversePathPrefab(GameObject prefab, RoadPartLibrary lib)
        {
            if (lib == null) return null;
            if (prefab == lib.path_side) return PathTilePart.Side;
            if (prefab == lib.path_center) return PathTilePart.Center;
            if (prefab == lib.path_curve) return PathTilePart.Curve;
            if (prefab == lib.path_turn) return PathTilePart.Turn;
            return null;
        }
    }
}
#endif
