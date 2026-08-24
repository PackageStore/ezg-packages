#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Collects <see cref="DebugBoundaryItem"/> lists from <see cref="CollectResult"/> for
    /// road + highway + Road 2 + Path + block layers. D5: consumes the same (prefab, x, y, yaw)
    /// tuples the sprite pass built — never re-derives masks or collect order.</summary>
    internal sealed class DebugBoundaryCollector
    {
        internal static readonly Color DebugRoadColor      = new(0.2f,  0.85f, 1f,   0.85f);
        // Ô vỉa hè nằm CHỒNG lên ô lòng đường cùng pivot (side vs side_rim) — cùng hệ màu lớp Đường
        // nhưng nhạt hơn để phân biệt được 2 box lồng nhau.
        internal static readonly Color DebugRoadRimColor   = new(0.55f, 0.92f, 1f,   0.55f);
        internal static readonly Color DebugHighwayColor   = new(1f,    0.65f, 0.2f, 0.85f);
        internal static readonly Color DebugHighwayRimColor= new(1f,    0.82f, 0.55f,0.55f);
        // Tím, cùng hệ với TileRoad2 trên canvas — tách Road 2 khỏi lớp Đường (cyan) và Highway (cam).
        internal static readonly Color DebugRoad2Color     = new(0.78f, 0.50f, 1f,   0.85f);
        internal static readonly Color DebugRoad2RimColor  = new(0.88f, 0.72f, 1f,   0.55f);
        // Xanh ngọc — tách khỏi Đường (cyan), Highway (cam), Road 2 (tím), cùng họ TilePath trên canvas
        internal static readonly Color DebugPathColor      = new(0.25f, 0.82f, 0.72f,0.85f);
        internal static readonly Color DebugStationColor   = new(0.45f, 0.65f, 1f,   0.9f);
        internal static readonly Color DebugParkingColor   = new(0.35f, 0.95f, 0.4f, 0.9f);

        private readonly TilePartRegistry _reg;

        internal DebugBoundaryCollector(TilePartRegistry reg) => _reg = reg;

        internal List<DebugBoundaryItem> CollectAll(Rect canvas, CollectResult result,
            RoadCanvasDoc doc, RoadPartLibrary library,
            Sprite spRampHway, Sprite spRampHway2,
            int gridHeight, float cellPixelSize,
            bool showBoundary, bool showBlockBoundary)
        {
            var items = new List<DebugBoundaryItem>();
            if (result == null) return items;

            if (showBoundary)
            {
                CollectRoadBoundary(canvas, items, result, gridHeight, cellPixelSize);
                CollectHighwayBoundary(canvas, items, result, library, spRampHway, gridHeight, cellPixelSize);
                CollectRoad2Boundary(canvas, items, result, library, spRampHway2, gridHeight, cellPixelSize);
                CollectPathBoundary(canvas, items, result, library, gridHeight, cellPixelSize);
            }
            if (showBlockBoundary)
                CollectBlockBoundary(canvas, items, doc, library, gridHeight, cellPixelSize);
            return items;
        }

        // ── Road (type-1) ─────────────────────────────────────────────────────

        private void CollectRoadBoundary(Rect canvas, List<DebugBoundaryItem> items,
            CollectResult result, int gridHeight, float cellPixelSize)
        {
            foreach ((float x, float y, GameObject prefab, float yaw, Vector3 _) in result.Road)
            {
                if (prefab == null) continue;
                if (_reg.TryReverseLookupRoad(prefab, out RoadTilePart part))
                    AddTileBoundary(canvas, items, part, x, y, yaw, gridHeight, cellPixelSize);
            }
        }

        // ── Highway ───────────────────────────────────────────────────────────

        private void CollectHighwayBoundary(Rect canvas, List<DebugBoundaryItem> items,
            CollectResult result, RoadPartLibrary library, Sprite spRampHway,
            int gridHeight, float cellPixelSize)
        {
            if (library == null) return;
            Sprite hwSprite = _reg.HighwaySprite;
            Sprite hwRimSprite = _reg.HighwayRimSprite;

            foreach ((float x, float y, GameObject prefab, float yaw, Vector3 scaleMul) in result.Highway)
            {
                if (prefab == null) continue;

                if (prefab == library.hway1x2_side)
                {
                    AddPartItem(canvas, items, hwSprite, false, TilePartRegistry.TileSpriteBaseTurns,
                        hwSprite != null ? hwSprite.name : "highway", DebugHighwayColor,
                        x, y, yaw, gridHeight, cellPixelSize);
                    continue;
                }
                if (prefab == library.hway1x2_side_rim)
                {
                    AddPartItem(canvas, items, hwRimSprite, false, TilePartRegistry.TileSpriteBaseTurns,
                        hwRimSprite != null ? hwRimSprite.name : "highway_rim", DebugHighwayRimColor,
                        x, y, yaw, gridHeight, cellPixelSize);
                    continue;
                }

                if (prefab == library.hway_to_road && spRampHway != null)
                {
                    AddRampBoundary(canvas, items, spRampHway, x, y, yaw, scaleMul,
                        library.hway_to_road.name, DebugHighwayColor, gridHeight, cellPixelSize);
                    continue;
                }

                // hway_to_road2 handled in Road2 boundary pass
                if (prefab == library.hway_to_road2) continue;

                if (_reg.TryReverseLookupRoad(prefab, out RoadTilePart roadPart))
                    AddTileBoundary(canvas, items, roadPart, x, y, yaw, gridHeight, cellPixelSize);
            }
        }

        // ── Road 2 ───────────────────────────────────────────────────────────

        private void CollectRoad2Boundary(Rect canvas, List<DebugBoundaryItem> items,
            CollectResult result, RoadPartLibrary library, Sprite spRampHway2,
            int gridHeight, float cellPixelSize)
        {
            foreach ((float x, float y, GameObject prefab, float yaw, Vector3 _) in result.Road2)
            {
                if (prefab == null) continue;
                if (_reg.TryReverseLookupRoad2(prefab, out Road2TilePart part))
                    AddRoad2TileBoundary(canvas, items, part, x, y, yaw, gridHeight, cellPixelSize);
                else if (_reg.TryReverseLookupRoad(prefab, out RoadTilePart rpart))
                    AddTileBoundary(canvas, items, rpart, x, y, yaw, gridHeight, cellPixelSize);
            }

            if (spRampHway2 != null && library != null)
            {
                string name = library.hway_to_road2 != null ? library.hway_to_road2.name : spRampHway2.name;
                foreach ((float x, float y, GameObject prefab, float yaw, Vector3 scaleMul) in result.Highway)
                {
                    if (prefab != library.hway_to_road2) continue;
                    AddRampBoundary(canvas, items, spRampHway2, x, y, yaw, scaleMul,
                        name, DebugHighwayColor, gridHeight, cellPixelSize);
                }
            }
        }

        // ── Path ──────────────────────────────────────────────────────────────

        private void CollectPathBoundary(Rect canvas, List<DebugBoundaryItem> items,
            CollectResult result, RoadPartLibrary library, int gridHeight, float cellPixelSize)
        {
            foreach ((float x, float y, GameObject prefab, float yaw, Vector3 _) in result.Path)
            {
                if (prefab == null || library == null) continue;
                PathTilePart? part = ReversePathPrefab(prefab, library);
                if (part == null) continue;

                Sprite sprite = _reg.SpriteFor(part.Value);
                if (sprite == null || sprite.rect.width <= 0f || sprite.rect.height <= 0f) continue;

                Rect rect = part.Value == PathTilePart.Center
                    ? RoadTileDrawing.SpriteCellsRect(canvas, sprite, x, y, 0, gridHeight, cellPixelSize)
                    : RoadTileDrawing.TileSpriteRect(
                        RoadTileDrawing.PointToPixelF(canvas, x, y, gridHeight, cellPixelSize),
                        cellPixelSize, sprite,
                        Mathf.RoundToInt(yaw / 90f) + TilePartRegistry.BaseTurns(part.Value));

                items.Add(new DebugBoundaryItem
                {
                    Rect = rect,
                    Name = _reg.DisplayName(part.Value),
                    Color = DebugPathColor,
                });
            }
        }

        // ── Block (station/parking footprint) ─────────────────────────────────

        private void CollectBlockBoundary(Rect canvas, List<DebugBoundaryItem> items,
            RoadCanvasDoc doc, RoadPartLibrary library, int gridHeight, float cellPixelSize)
        {
            string stationName = library != null && library.stationPrefab != null
                ? library.stationPrefab.name : "station";
            int s = GridConst.StationSize;
            foreach (int id in doc.Stations)
            {
                BlockCodec.DecodeStation(id, out int x2, out int y2, out _);
                items.Add(new DebugBoundaryItem
                {
                    Rect = BlockBoundaryRect(canvas, new Vector2Int(x2, y2), s, s, gridHeight, cellPixelSize),
                    Name = stationName,
                    Color = DebugStationColor,
                });
            }

            string parkingName = library != null && library.parkingPrefab != null
                ? library.parkingPrefab.name : "parking";
            foreach (int id in doc.Parkings)
            {
                BlockCodec.DecodeParking(id, out int x2, out int y2, out int rot);
                items.Add(new DebugBoundaryItem
                {
                    Rect = ParkingSlabBoundaryRect(canvas, new Vector2Int(x2, y2), rot, gridHeight, cellPixelSize),
                    Name = parkingName,
                    Color = DebugParkingColor,
                });
            }
        }

        // ── Add helpers ───────────────────────────────────────────────────────

        private void AddTileBoundary(Rect canvas, List<DebugBoundaryItem> items,
            RoadTilePart part, float x, float y, float yaw, int gh, float cps) =>
            AddPartItem(canvas, items, _reg.SpriteFor(part), part == RoadTilePart.Center,
                TilePartRegistry.BaseTurns(part), _reg.DisplayName(part),
                TilePartRegistry.IsRim(part) ? DebugRoadRimColor : DebugRoadColor,
                x, y, yaw, gh, cps);

        private void AddRoad2TileBoundary(Rect canvas, List<DebugBoundaryItem> items,
            Road2TilePart part, float x, float y, float yaw, int gh, float cps) =>
            AddPartItem(canvas, items, _reg.SpriteFor(part), part == Road2TilePart.Center,
                TilePartRegistry.BaseTurns(part), _reg.DisplayName(part),
                TilePartRegistry.IsRim(part) ? DebugRoad2RimColor : DebugRoad2Color,
                x, y, yaw, gh, cps);

        private static void AddPartItem(Rect canvas, List<DebugBoundaryItem> items,
            Sprite sprite, bool isCenter, int baseTurns, string name, Color color,
            float x, float y, float yaw, int gh, float cps)
        {
            if (sprite == null || sprite.rect.width <= 0f || sprite.rect.height <= 0f) return;
            Rect rect = isCenter
                ? RoadTileDrawing.SpriteCellsRect(canvas, sprite, x, y, 0, gh, cps)
                : RoadTileDrawing.TileSpriteRect(
                    RoadTileDrawing.PointToPixelF(canvas, x, y, gh, cps), cps, sprite,
                    Mathf.RoundToInt(yaw / 90f) + baseTurns);
            items.Add(new DebugBoundaryItem { Rect = rect, Name = name, Color = color });
        }

        /// <summary>Box ramp 4×4 ô: pivot math cùng quy ước với sprite renderer.</summary>
        private static void AddRampBoundary(Rect canvas, List<DebugBoundaryItem> items,
            Sprite sprite, float x, float y, float yaw, Vector3 scaleMul,
            string name, Color color, int gridHeight, float cellPixelSize)
        {
            if (sprite.rect.width <= 0f || sprite.rect.height <= 0f) return;
            Vector2 p = RoadTileDrawing.PointToPixelF(canvas, x, y, gridHeight, cellPixelSize);
            float s = cellPixelSize * 4f;
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
            float a = piv.x, b = flipped ? piv.y : 1f - piv.y;
            for (int k = 0; k < (turns & 3); k++) { float na = 1f - b, nb = a; a = na; b = nb; }
            items.Add(new DebugBoundaryItem
            {
                Rect = new Rect(p.x - a * s, p.y - b * s, s, s),
                Name = name,
                Color = color,
            });
        }

        // ── Block rect helpers ────────────────────────────────────────────────

        private static Rect BlockBoundaryRect(Rect canvas, Vector2Int anchor2, int w, int h,
            int gridHeight, float cellPixelSize)
        {
            float ax = anchor2.x * 0.5f, ay = anchor2.y * 0.5f;
            Vector2 bl = RoadTileDrawing.PointToPixelF(canvas, ax, ay, gridHeight, cellPixelSize);
            Vector2 tr = RoadTileDrawing.PointToPixelF(canvas, ax + w, ay + h, gridHeight, cellPixelSize);
            return new Rect(bl.x, tr.y, w * cellPixelSize, h * cellPixelSize);
        }

        private static Rect ParkingSlabBoundaryRect(Rect canvas, Vector2Int a2, int rot,
            int gridHeight, float cellPixelSize)
        {
            Vector2Int k = GridConst.ParkingCells(rot);
            return rot switch
            {
                1 => BlockBoundaryRect(canvas, new Vector2Int(a2.x + k.x * 2 - 3, a2.y), 1, k.y, gridHeight, cellPixelSize),
                2 => BlockBoundaryRect(canvas, new Vector2Int(a2.x, a2.y + 1), k.x, 1, gridHeight, cellPixelSize),
                3 => BlockBoundaryRect(canvas, new Vector2Int(a2.x + 1, a2.y), 1, k.y, gridHeight, cellPixelSize),
                _ => BlockBoundaryRect(canvas, new Vector2Int(a2.x, a2.y + k.y * 2 - 3), k.x, 1, gridHeight, cellPixelSize),
            };
        }

        // ── Reverse lookup ────────────────────────────────────────────────────

        private static PathTilePart? ReversePathPrefab(GameObject prefab, RoadPartLibrary lib)
        {
            if (lib == null) return null;
            if (prefab == lib.path_side)   return PathTilePart.Side;
            if (prefab == lib.path_center) return PathTilePart.Center;
            if (prefab == lib.path_curve)  return PathTilePart.Curve;
            if (prefab == lib.path_turn)   return PathTilePart.Turn;
            return null;
        }
    }
}
#endif
