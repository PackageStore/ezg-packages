#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Gom placement cho lớp PATH — single-solver walk (P9): ForEachPathNode + CollectPathPlacements.
    /// ForEachPathNode giải P9 — CollectPathPlacements + preview + debug-boundary gọi chung method này,
    /// đảm bảo kết quả LUÔN khớp nhau.</summary>
    internal static class PathCollector
    {
        /// <summary>Lattice walk GỘP: build masks, dispatch straight/junction, report toạ độ TUYỆT ĐỐI.
        /// Không gọi ResolveRoadLayout, không dùng half-straight/arm/skin.</summary>
        internal static void ForEachPathNode(
            RoadCanvasDoc doc, List<int> edges,
            System.Action<PathTilePart, float, float, float> place)
        {
            int lw = doc.LatticeW, lh = doc.LatticeH;
            int[] masks = MaskBuilder.BuildMasks(edges, lw, lh);

            for (int y2 = 0; y2 < lh; y2++)
            {
                for (int x2 = 0; x2 < lw; x2++)
                {
                    int mask = masks[y2 * lw + x2];
                    if (mask == 0) continue;

                    float nx = x2 * 0.5f, ny = y2 * 0.5f;

                    if (PathStraightWalker.IsPathStraightMask(mask))
                    {
                        PathStraightWalker.ForEachPathStraightTile(nx, ny, MaskClassifier.StraightYaw(mask),
                            (part, tx, ty, tyaw) => place(part, tx, ty, tyaw));
                        continue;
                    }

                    // Junction: cornerBlocked probe (P14) — fillet chiếm quarter ô chéo TRONG slot
                    // hàng xóm chéo; nếu hàng xóm có mask → skip fillet tránh z-fight.
                    System.Func<int, bool> blocked = (int cornerDirs) =>
                    {
                        int dx = 0, dy = 0;
                        if ((cornerDirs & DirBits.E) != 0) dx = 1; else if ((cornerDirs & DirBits.W) != 0) dx = -1;
                        if ((cornerDirs & DirBits.N) != 0) dy = 1; else if ((cornerDirs & DirBits.S) != 0) dy = -1;
                        int cx2 = x2 + dx, cy2 = y2 + dy;
                        if (cx2 < 0 || cx2 >= lw || cy2 < 0 || cy2 >= lh) return false;
                        return masks[cy2 * lw + cx2] != 0;
                    };

                    PathJunctionWalker.ForEachPathJunctionTile(mask,
                        (part, dx, dy, yaw) => place(part, nx + dx, ny + dy, yaw),
                        blocked);
                }
            }
        }

        /// <summary>Gom placement cho lớp PATH — thin wrapper trên ForEachPathNode.
        /// Thiếu prefab → ghi key missing, skip tile đó (không throw, không block Apply).</summary>
        internal static void CollectPathPlacements(
            RoadCanvasDoc doc, List<int> edges, PathTileVocabulary vocab,
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            HashSet<string> missing,
            System.Action<List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)>, int> dedupe)
        {
            int start = placements.Count;

            ForEachPathNode(doc, edges, (part, x, y, yaw) =>
            {
                GameObject prefab = vocab.PathTilePrefab(part, x, y, yaw);
                if (prefab == null)
                {
                    string key = part switch
                    {
                        PathTilePart.Side   => "Path Tile Side",
                        PathTilePart.Center => "Path Tile Center",
                        PathTilePart.Curve  => "Path Tile Curve",
                        PathTilePart.Turn   => "Path Tile Turn",
                        _ => "Path Tile Unknown",
                    };
                    missing.Add(key);
                    return;
                }
                placements.Add((x, y, prefab, yaw, Vector3.one));
            });

            dedupe(placements, start);
        }
    }
}
#endif
