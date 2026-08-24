#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Emits + bakes the filler strips that block-aprons owe Road 2.</summary>
    internal sealed class Road2ApronFiller
    {
        private readonly ToolContext _ctx;
        internal Road2ApronFiller(ToolContext ctx) => _ctx = ctx;

        /// <summary>Duyệt dải FILLER Road 2 mà mesh khối nợ lại: mỗi cột của mỗi dải khối một ô
        /// <see cref="Road2TilePart.Filler"/> ở đúng nửa mà dải đó chiếm.</summary>
        internal void ForEachRoad2ApronFiller(
            List<BlockStrip> strips, System.Action<float, float, float> place)
        {
            if (strips == null) return;
            int gw = _ctx.Doc.GridWidth, gh = _ctx.Doc.GridHeight;
            foreach (BlockStrip s in strips)
            {
                int lateralMax2 = (s.Horizontal ? gw - 1 : gh - 1) * 2;
                float line = s.Line2 * 0.5f;
                float lateral = (s.Side & (DirBits.S | DirBits.W)) != 0
                    ? -Road2Constants.Road2FillerLateralOffset
                    : Road2Constants.Road2FillerLateralOffset;
                float yaw = s.Horizontal ? 0f : 90f;
                for (int q2 = Mathf.Max(0, s.Lo2); q2 < Mathf.Min(lateralMax2, s.Hi2); q2++)
                {
                    float along = (q2 + 0.5f) * 0.5f;
                    place(s.Horizontal ? along : line + lateral,
                          s.Horizontal ? line + lateral : along, yaw);
                }
            }
        }

        /// <summary>Bake dải filler nợ lại, BỎ QUA ô đã có filler.</summary>
        internal void AddRoad2ApronFillers(
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            List<BlockStrip> strips, HashSet<string> missing)
        {
            if (strips == null || strips.Count == 0) return;
            GameObject filler = Road2TileParts.PrefabFor(Road2TilePart.Filler, _ctx.Library);
            if (filler == null)
            {
                missing?.Add("Road2 Center Filler");
                return;
            }

            var taken = new HashSet<long>();
            for (int i = 0; i < placements.Count; i++)
                if (placements[i].prefab == filler)
                    taken.Add(LatticeKeys.Road2FillerKey(placements[i].x, placements[i].y));

            ForEachRoad2ApronFiller(strips, (fx, fy, fyaw) =>
            {
                if (!taken.Add(LatticeKeys.Road2FillerKey(fx, fy))) return;
                placements.Add((fx, fy, filler, fyaw, Vector3.one));
            });
        }
    }
}
#endif
