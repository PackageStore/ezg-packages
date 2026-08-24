#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Tile part classification for PATH layer.</summary>
    internal enum PathTilePart { Side, Center, Curve, Turn }

    /// <summary>Prefab variant picker + readiness check for PATH tiles.</summary>
    internal sealed class PathTileVocabulary
    {
        // Mặt cắt PATH: bề rộng bề mặt = 0.5 ô, mỗi nửa = 0.25 ô.
        internal const float PathSurfaceHalfWidth = 0.25f;
        // Ô path_side chỉ dài 0.25 dọc trục (nửa ô side type-1) nên slot 0.5 của node cần 2 cột
        // lệch ±0.125 — bước 0.25, KHÁC bước 0.25→±0.25 của type-1/Road 2.
        internal const float PathTileColumnOffsetCells = 0.125f;
        // Pivot ô bo góc PATH (yaw 0 = góc ĐÔNG-NAM): offset so với tâm node.
        internal const float PathCornerPivotX =  PathSurfaceHalfWidth;
        internal const float PathCornerPivotY = -PathSurfaceHalfWidth;

        private readonly RoadPartLibrary _library;

        internal PathTileVocabulary(RoadPartLibrary library) { _library = library; }

        internal List<PathPartVariant> PathTileVariants(PathTilePart part) => _library == null ? null : part switch
        {
            PathTilePart.Side   => _library.path_side_variants,
            PathTilePart.Center => _library.path_center_variants,
            PathTilePart.Curve  => _library.path_curve_variants,
            PathTilePart.Turn   => _library.path_turn_variants,
            _ => null,
        };

        /// <summary>Prefab đời cũ 1-biến-thể — fallback khi list biến thể còn rỗng (asset chưa migrate).</summary>
        internal GameObject LegacyPathPrefab(PathTilePart part) => _library == null ? null : part switch
        {
            PathTilePart.Side   => _library.path_side,
            PathTilePart.Center => _library.path_center,
            PathTilePart.Curve  => _library.path_curve,
            PathTilePart.Turn   => _library.path_turn,
            _ => null,
        };

        /// <summary>Bốc biến thể cho MỘT ô theo trọng số. Random là hàm THUẦN của (part, vị trí, yaw)
        /// nên: (a) bake lại cùng canvas ra cùng kết quả — prefab không nhiễu diff; (b) 2 placement
        /// trùng (part, pos, yaw) luôn bốc cùng prefab nên dedupe vẫn gộp được.</summary>
        internal GameObject PathTilePrefab(PathTilePart part, float x, float y, float yaw)
        {
            List<PathPartVariant> variants = PathTileVariants(part);
            if (variants == null || variants.Count == 0) return LegacyPathPrefab(part);

            float total = 0f;
            for (int i = 0; i < variants.Count; i++)
                if (variants[i].prefab != null) total += Mathf.Max(0f, variants[i].weight);
            bool uniform = total <= 0f;
            if (uniform)
            {
                total = 0f;
                for (int i = 0; i < variants.Count; i++) if (variants[i].prefab != null) total += 1f;
            }
            if (total <= 0f) return null;

            float pick = PathVariantNoise(part, x, y, yaw) * total;
            GameObject last = null;
            for (int i = 0; i < variants.Count; i++)
            {
                GameObject prefab = variants[i].prefab;
                if (prefab == null) continue;
                last = prefab;
                pick -= uniform ? 1f : Mathf.Max(0f, variants[i].weight);
                if (pick < 0f) return prefab;
            }
            return last;
        }

        /// <summary>Nhiễu [0,1) tất định từ (part, vị trí ¼ ô, yaw) — FNV-1a + trộn bit cuối.</summary>
        internal static float PathVariantNoise(PathTilePart part, float x, float y, float yaw)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)Mathf.RoundToInt(x * 8f)) * 16777619u;
                h = (h ^ (uint)Mathf.RoundToInt(y * 8f)) * 16777619u;
                h = (h ^ (uint)(Mathf.RoundToInt(yaw / 90f) & 3)) * 16777619u;
                h = (h ^ (uint)part) * 16777619u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
                return (h & 0xFFFFFFu) / 16777216f;
            }
        }

        /// <summary>Side + center đủ để bake mảnh thẳng + lõi giao — curve/turn thiếu chỉ warning.</summary>
        internal bool PathTilesReady => HasPathTile(PathTilePart.Side) && HasPathTile(PathTilePart.Center);

        internal bool HasPathTile(PathTilePart part)
        {
            List<PathPartVariant> variants = PathTileVariants(part);
            if (variants != null)
                for (int i = 0; i < variants.Count; i++)
                    if (variants[i].prefab != null) return true;
            return LegacyPathPrefab(part) != null;
        }
    }
}
#endif
