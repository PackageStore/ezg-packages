#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Single source of truth for part → sprite / baseTurns / isRim / displayName / prefab
    /// across all three tile layers. Replaces the per-layer copies.</summary>
    internal sealed class TilePartRegistry
    {
        internal const int TileSpriteBaseTurns = 3;
        internal const int CurveSpriteBaseTurns = 2;
        internal const float SpritePixelsPerCell = 128f;

        private readonly RoadPartLibrary _library;

        // Sprite fields — kept on the EditorWindow as [SerializeField] for override persistence;
        // registry reads them through the window reference.
        private readonly Sprite[] _roadSprites;   // indexed by RoadTilePart
        private readonly Sprite[] _road2Sprites;   // indexed by Road2TilePart
        private readonly Sprite[] _pathSprites;    // indexed by PathTilePart
        private readonly Sprite _spHighway, _spHighwayRim;

        internal TilePartRegistry(RoadPartLibrary library,
            Sprite spTileSide, Sprite spTileSideRim, Sprite spTileCurve, Sprite spTileCurveRim,
            Sprite spTileCenter, Sprite spTileTurn, Sprite spTileTurnRim,
            Sprite spTileTurn1x1, Sprite spTileTurn1x1Rim,
            Sprite spTileTurn3x3, Sprite spTileTurn3x3Rim,
            Sprite spHighway, Sprite spHighwayRim,
            Sprite spRoad2Curve, Sprite spRoad2CurveRim, Sprite spRoad2CenterFiller,
            Sprite spPathSide, Sprite spPathCenter, Sprite spPathCurve, Sprite spPathTurn)
        {
            _library = library;
            _spHighway = spHighway;
            _spHighwayRim = spHighwayRim;

            _roadSprites = new Sprite[(int)RoadTilePart.Turn1x1Rim + 1];
            _roadSprites[(int)RoadTilePart.Side] = spTileSide;
            _roadSprites[(int)RoadTilePart.SideRim] = spTileSideRim;
            _roadSprites[(int)RoadTilePart.Curve] = spTileCurve;
            _roadSprites[(int)RoadTilePart.CurveRim] = spTileCurveRim;
            _roadSprites[(int)RoadTilePart.Center] = spTileCenter;
            _roadSprites[(int)RoadTilePart.Turn2x2] = spTileTurn;
            _roadSprites[(int)RoadTilePart.Turn2x2Rim] = spTileTurnRim;
            _roadSprites[(int)RoadTilePart.Turn1x1] = spTileTurn1x1;
            _roadSprites[(int)RoadTilePart.Turn1x1Rim] = spTileTurn1x1Rim;

            _road2Sprites = new Sprite[(int)Road2TilePart.Turn1x1Rim + 1];
            _road2Sprites[(int)Road2TilePart.Side] = spTileSide;
            _road2Sprites[(int)Road2TilePart.SideRim] = spTileSideRim;
            // Fallback sprite type-1 mirror fallback prefab của Road2JunctionTilePrefab.
            _road2Sprites[(int)Road2TilePart.Curve] = spRoad2Curve != null ? spRoad2Curve : spTileCurve;
            _road2Sprites[(int)Road2TilePart.CurveRim] = spRoad2CurveRim != null ? spRoad2CurveRim : spTileCurveRim;
            _road2Sprites[(int)Road2TilePart.Center] = spTileCenter;
            _road2Sprites[(int)Road2TilePart.Filler] = spRoad2CenterFiller;
            _road2Sprites[(int)Road2TilePart.Turn3x3] = spTileTurn3x3;
            _road2Sprites[(int)Road2TilePart.Turn3x3Rim] = spTileTurn3x3Rim;
            _road2Sprites[(int)Road2TilePart.Turn1x1] = spTileTurn1x1;
            _road2Sprites[(int)Road2TilePart.Turn1x1Rim] = spTileTurn1x1Rim;

            _pathSprites = new Sprite[(int)PathTilePart.Turn + 1];
            _pathSprites[(int)PathTilePart.Side] = spPathSide;
            _pathSprites[(int)PathTilePart.Center] = spPathCenter;
            _pathSprites[(int)PathTilePart.Curve] = spPathCurve;
            _pathSprites[(int)PathTilePart.Turn] = spPathTurn;
        }

        // ── Road (type-1) ─────────────────────────────────────────────────────

        internal Sprite SpriteFor(RoadTilePart part)
        {
            int i = (int)part;
            return i >= 0 && i < _roadSprites.Length ? _roadSprites[i] : _roadSprites[(int)RoadTilePart.Center];
        }

        internal static int BaseTurns(RoadTilePart part) =>
            part is RoadTilePart.Curve or RoadTilePart.CurveRim
                ? CurveSpriteBaseTurns : TileSpriteBaseTurns;

        internal static bool IsRim(RoadTilePart part) =>
            part is RoadTilePart.SideRim or RoadTilePart.CurveRim or RoadTilePart.Turn2x2Rim
                or RoadTilePart.Turn1x1Rim;

        internal string DisplayName(RoadTilePart part)
        {
            GameObject prefab = _library != null ? Prefab(part) : null;
            Sprite sprite = SpriteFor(part);
            return prefab != null ? prefab.name : (sprite != null ? sprite.name : part.ToString());
        }

        internal GameObject Prefab(RoadTilePart part) => _library == null ? null : part switch
        {
            RoadTilePart.Side => _library.road1x1_side,
            RoadTilePart.SideRim => _library.road1x1_side_rim,
            RoadTilePart.Curve => _library.road1x1_curve,
            RoadTilePart.CurveRim => _library.road1x1_curve_rim,
            RoadTilePart.Turn2x2 => _library.road2x2_turn,
            RoadTilePart.Turn2x2Rim => _library.road2x2_turn_rim,
            RoadTilePart.Turn1x1 => _library.road1x1_turn,
            RoadTilePart.Turn1x1Rim => _library.road1x1_turn_rim,
            _ => _library.road1x1_center,
        };

        // ── Road 2 ────────────────────────────────────────────────────────────

        internal Sprite SpriteFor(Road2TilePart part)
        {
            int i = (int)part;
            return i >= 0 && i < _road2Sprites.Length ? _road2Sprites[i] : _road2Sprites[(int)Road2TilePart.Center];
        }

        internal static int BaseTurns(Road2TilePart part) =>
            part is Road2TilePart.Curve or Road2TilePart.CurveRim
                ? CurveSpriteBaseTurns : TileSpriteBaseTurns;

        internal static bool IsRim(Road2TilePart part) =>
            part is Road2TilePart.SideRim or Road2TilePart.CurveRim or Road2TilePart.Turn3x3Rim
                or Road2TilePart.Turn1x1Rim;

        internal string DisplayName(Road2TilePart part)
        {
            GameObject prefab = _library != null ? Prefab(part) : null;
            Sprite sprite = SpriteFor(part);
            return prefab != null ? prefab.name : (sprite != null ? sprite.name : part.ToString());
        }

        internal GameObject Prefab(Road2TilePart part) => _library == null ? null : part switch
        {
            Road2TilePart.Side => _library.road1x1_side,
            Road2TilePart.SideRim => _library.road1x1_side_rim,
            Road2TilePart.Curve => _library.road2_curve != null ? _library.road2_curve : _library.road1x1_curve,
            Road2TilePart.CurveRim => _library.road2_curve_rim != null ? _library.road2_curve_rim : _library.road1x1_curve_rim,
            Road2TilePart.Filler => _library.road2_center_filler,
            Road2TilePart.Turn3x3 => _library.road3x3_turn,
            Road2TilePart.Turn3x3Rim => _library.road3x3_turn_rim,
            Road2TilePart.Turn1x1 => _library.road1x1_turn,
            Road2TilePart.Turn1x1Rim => _library.road1x1_turn_rim,
            _ => _library.road1x1_center,
        };

        // ── Path ──────────────────────────────────────────────────────────────

        internal Sprite SpriteFor(PathTilePart part)
        {
            int i = (int)part;
            return i >= 0 && i < _pathSprites.Length ? _pathSprites[i] : null;
        }

        internal static int BaseTurns(PathTilePart part) =>
            part == PathTilePart.Curve ? CurveSpriteBaseTurns : TileSpriteBaseTurns;

        internal string DisplayName(PathTilePart part) => part switch
        {
            PathTilePart.Side => "path_side",
            PathTilePart.Center => "path_center",
            PathTilePart.Curve => "path_curve",
            PathTilePart.Turn => "path_turn",
            _ => "path",
        };

        // ── Highway sprites (no per-part enum — just core + rim) ─────────────

        internal Sprite HighwaySprite => _spHighway;
        internal Sprite HighwayRimSprite => _spHighwayRim;

        // ── Reverse lookup (prefab → part) — D5: renderers identify parts from PlaceList entries ──

        internal bool TryReverseLookupRoad(GameObject prefab, out RoadTilePart part)
        {
            if (_library != null && prefab != null)
            {
                if (prefab == _library.road1x1_side)     { part = RoadTilePart.Side;       return true; }
                if (prefab == _library.road1x1_side_rim) { part = RoadTilePart.SideRim;    return true; }
                if (prefab == _library.road1x1_curve)    { part = RoadTilePart.Curve;      return true; }
                if (prefab == _library.road1x1_curve_rim){ part = RoadTilePart.CurveRim;   return true; }
                if (prefab == _library.road1x1_center)   { part = RoadTilePart.Center;     return true; }
                if (prefab == _library.road2x2_turn)     { part = RoadTilePart.Turn2x2;    return true; }
                if (prefab == _library.road2x2_turn_rim) { part = RoadTilePart.Turn2x2Rim; return true; }
                if (prefab == _library.road1x1_turn)     { part = RoadTilePart.Turn1x1;    return true; }
                if (prefab == _library.road1x1_turn_rim) { part = RoadTilePart.Turn1x1Rim; return true; }
            }
            part = default;
            return false;
        }

        internal bool TryReverseLookupRoad2(GameObject prefab, out Road2TilePart part)
        {
            if (_library != null && prefab != null)
            {
                if (prefab == _library.road1x1_side)     { part = Road2TilePart.Side;       return true; }
                if (prefab == _library.road1x1_side_rim) { part = Road2TilePart.SideRim;    return true; }
                if (prefab == _library.road1x1_center)   { part = Road2TilePart.Center;     return true; }
                if (prefab == _library.road2_center_filler) { part = Road2TilePart.Filler;  return true; }
                if (prefab == _library.road3x3_turn)     { part = Road2TilePart.Turn3x3;    return true; }
                if (prefab == _library.road3x3_turn_rim) { part = Road2TilePart.Turn3x3Rim; return true; }
                if (prefab == _library.road1x1_turn)     { part = Road2TilePart.Turn1x1;    return true; }
                if (prefab == _library.road1x1_turn_rim) { part = Road2TilePart.Turn1x1Rim; return true; }
                // Curve with fallback (Road2 may share Road prefab)
                GameObject r2Curve = _library.road2_curve != null ? _library.road2_curve : _library.road1x1_curve;
                GameObject r2CurveRim = _library.road2_curve_rim != null ? _library.road2_curve_rim : _library.road1x1_curve_rim;
                if (prefab == r2Curve)    { part = Road2TilePart.Curve;    return true; }
                if (prefab == r2CurveRim) { part = Road2TilePart.CurveRim; return true; }
            }
            part = default;
            return false;
        }
    }
}
#endif
