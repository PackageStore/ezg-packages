#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Collects fillet kerb quarter-cells and fillet-turn merge points for overlapping
    /// curves of adjacent junctions.</summary>
    internal sealed class FilletCollector
    {
        private readonly ToolContext _ctx;
        private readonly JunctionBaker _junctionBaker;

        internal FilletCollector(ToolContext ctx)
        {
            _ctx = ctx;
            _junctionBaker = new JunctionBaker(ctx);
        }

        /// <summary>Gom quarter-cell mà ô bo góc của MỌI mảnh giao đã trải vỉa hè. Chạy TRƯỚC khi đặt ô
        /// vì vỉa hè mép đóng / vỉa hè arm của mảnh giao KHÁC có thể rơi đúng vào đó.</summary>
        internal void CollectFilletKerb(RoadLayout layout, System.Func<int, bool> blocked)
        {
            int lw = _ctx.Doc.LatticeW, lh = _ctx.Doc.LatticeH;
            int[] masks = layout.Masks;

            for (int y2 = 0; y2 < lh; y2++)
            {
                for (int x2 = 0; x2 < lw; x2++)
                {
                    int i = y2 * lw + x2;
                    int mask = masks[i];
                    if (mask == 0 || !DirBits.IsJunctionMask(mask)) continue;
                    if (blocked != null && blocked(i)) continue;
                    if (layout.Skip(i)) continue;

                    float nx = x2 * 0.5f, ny = y2 * 0.5f;
                    JunctionTileEmitter.ForEachJunctionTile(mask,
                        _junctionBaker.JunctionArms(masks, x2, y2, mask), (part, dx, dy, yaw) =>
                    {
                        if (part != RoadTilePart.Curve) return;
                        layout.FilletKerb.Add(LatticeKeys.KerbCellKey(nx + dx, ny + dy, 0.25f, -0.25f, yaw));
                    });
                }
            }
        }

        /// <summary>Gom các quarter-cell có ĐÚNG 2 ô bo góc (từ 2 mảnh giao khác nhau) rơi trùng nhau —
        /// chỉ xảy ra khi 2 mảnh giao lệch 1.5 ô, tức phải có junction nửa ô. Hai curve đó là ảnh gương
        /// của nhau và phủ y một chỗ, nên cả hai nhường cho MỘT ô cua nhỏ: pivot = trung điểm 2 pivot
        /// curve, yaw hướng mesh từ cạnh đó vào trong ô.</summary>
        internal void CollectFilletTurns(RoadLayout layout, System.Func<int, bool> blocked)
        {
            int lw = _ctx.Doc.LatticeW, lh = _ctx.Doc.LatticeH;
            int[] masks = layout.Masks;
            var byCell = new Dictionary<long, List<(float x, float y, float yaw)>>();

            for (int y2 = 0; y2 < lh; y2++)
            {
                for (int x2 = 0; x2 < lw; x2++)
                {
                    int i = y2 * lw + x2;
                    int mask = masks[i];
                    if (mask == 0 || !DirBits.IsJunctionMask(mask)) continue;
                    if (blocked != null && blocked(i)) continue;
                    if (layout.Skip(i)) continue;

                    float nx = x2 * 0.5f, ny = y2 * 0.5f;
                    JunctionTileEmitter.ForEachJunctionTile(mask,
                        _junctionBaker.JunctionArms(masks, x2, y2, mask), (part, dx, dy, yaw) =>
                    {
                        if (part != RoadTilePart.Curve) return;
                        long cell = LatticeKeys.KerbCellKey(nx + dx, ny + dy, 0.25f, -0.25f, yaw);
                        if (!byCell.TryGetValue(cell, out List<(float x, float y, float yaw)> list))
                            byCell[cell] = list = new List<(float x, float y, float yaw)>();
                        list.Add((nx + dx, ny + dy, yaw));
                    });
                }
            }

            foreach (KeyValuePair<long, List<(float x, float y, float yaw)>> kv in byCell)
            {
                List<(float x, float y, float yaw)> curves = kv.Value;
                if (curves.Count != 2) continue;

                (float x, float y, float yaw) a = curves[0], b = curves[1];
                bool sameX = Mathf.Approximately(a.x, b.x), sameY = Mathf.Approximately(a.y, b.y);
                if (sameX == sameY) continue;

                float tx = (a.x + b.x) * 0.5f, ty = (a.y + b.y) * 0.5f;
                (float cx, float cy) = LatticeKeys.QuarterCellCenter(a);
                float tyaw = DirBits.RimYawFacing(
                    sameY ? (cy > ty ? DirBits.N : DirBits.S) : (cx > tx ? DirBits.E : DirBits.W));
                layout.FilletTurn[LatticeKeys.CurveKey(a.x, a.y, a.yaw)] = (tx, ty, tyaw);
                layout.FilletTurn[LatticeKeys.CurveKey(b.x, b.y, b.yaw)] = (tx, ty, tyaw);
            }
        }
    }
}
#endif
