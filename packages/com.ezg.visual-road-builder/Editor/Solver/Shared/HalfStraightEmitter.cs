#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Enumerates and bakes half-straight pieces adjacent to junctions — THE single source
    /// for bake, preview, debug.</summary>
    internal sealed class HalfStraightEmitter
    {
        private readonly ToolContext _ctx;
        internal HalfStraightEmitter(ToolContext ctx) => _ctx = ctx;

        /// <summary>Phân loại mảnh straight nằm sát junction — dùng CHUNG cho bake, preview sprite và
        /// debug boundary nên cả 3 luôn khớp. Đếm số junction kề mỗi mảnh straight: đúng 1 → mảnh bị
        /// THAY bằng part half (junction kề đặt hộ); 2 junction kẹp 2 bên (vd 2 chữ T gần nhau) → BỎ
        /// HẲN, không đặt gì (2 part half từ 2 phía sẽ chồng/xuyên nhau; mesh 2 junction phủ ~1 ô mỗi
        /// bên với offset 1.25 nên tự khít).</summary>
        internal void CollectHalfStraightSets(
            int[] masks, System.Func<int, bool> blocked,
            HashSet<int> replacedByHalf, HashSet<int> droppedBetween, HashSet<int> covered = null)
        {
            int lw = _ctx.Doc.LatticeW, lh = _ctx.Doc.LatticeH;
            var markCount = new Dictionary<int, int>();
            for (int y2 = 0; y2 < lh; y2++)
            {
                for (int x2 = 0; x2 < lw; x2++)
                {
                    int i = y2 * lw + x2;
                    int mask = masks[i];
                    if (mask == 0 || blocked(i) || !DirBits.IsJunctionMask(mask)) continue;

                    void TryReplace(int dir, int sx2, int sy2)
                    {
                        if ((mask & dir) == 0) return;
                        int jx2 = x2 + sx2 * 2, jy2 = y2 + sy2 * 2;
                        if (jx2 < 0 || jx2 >= lw || jy2 < 0 || jy2 >= lh) return;
                        int j = jy2 * lw + jx2;
                        // Node đã bị junction nửa ô phủ trọn thì bỏ hẳn, KHÔNG kích part half từ phía
                        // bên kia nữa (nếu không mảnh giao ở xa sẽ chìa half vào đúng chỗ đã có centers).
                        if (covered != null && covered.Contains(j)) return;
                        if (masks[j] == 0 || blocked(j) || !DirBits.IsStraightLikeMask(masks[j])) return;

                        // Mảnh giao đối diện cách 3 nấc (1.5 ô): part half của mảnh NÀY nằm ở 1.25 ô nên
                        // lọt vào khối 1 ô của nó (khối phủ tâm ±1 nấc). markCount không bắt được vì 2 bên
                        // kẹp 2 node KHÁC NHAU — mỗi bên chỉ mark node ở +2 của chính nó, lệch nhau 1 nấc
                        // — nên phải ép bỏ tại đây.
                        int fx2 = x2 + sx2 * 3, fy2 = y2 + sy2 * 3;
                        if (fx2 >= 0 && fx2 < lw && fy2 >= 0 && fy2 < lh)
                        {
                            int f = fy2 * lw + fx2;
                            if (masks[f] != 0 && !blocked(f) && DirBits.IsJunctionMask(masks[f]))
                            {
                                droppedBetween.Add(j);
                                return;
                            }
                        }

                        markCount[j] = markCount.TryGetValue(j, out int c) ? c + 1 : 1;
                    }

                    TryReplace(DirBits.E, 1, 0);
                    TryReplace(DirBits.W, -1, 0);
                    TryReplace(DirBits.N, 0, 1);
                    TryReplace(DirBits.S, 0, -1);
                }
            }

            foreach (KeyValuePair<int, int> kv in markCount)
            {
                if (droppedBetween.Contains(kv.Key)) continue;
                if (kv.Value == 1) replacedByHalf.Add(kv.Key);
                else droppedBetween.Add(kv.Key);
            }
        }

        /// <summary>Duyệt các part nửa ô mà junction tại (x2, y2) phải đặt, gọi
        /// <paramref name="place"/> với (x, y, yaw) đơn vị ô — nguồn DUY NHẤT của vị trí + yaw part
        /// half cho bake, preview sprite và debug boundary. Bỏ qua nhánh mà điểm kề
        /// (<paramref name="masks"/>) là đầu cụt (mask 1 hướng): mảnh cụt đó đã tự neo khít mép nút
        /// (xem <see cref="StraightTileEmitter.StraightAnchor"/>), thêm half ở đây sẽ tràn quá đầu nét vẽ.</summary>
        internal void ForEachHalfStraight(
            int mask, int x2, int y2, int[] masks, HashSet<int> replacedByHalf, BlockSuppression suppressed,
            System.Action<float, float, float, int> place)
        {
            int lw = _ctx.Doc.LatticeW, lh = _ctx.Doc.LatticeH;
            float px = x2 * 0.5f, py = y2 * 0.5f;
            // Nhánh mở của mảnh giao vươn đúng 1 ô tính từ tâm; điểm kề (1 ô) bị bỏ hẳn, mảnh nửa ô lấp
            // đúng khúc [1.0, 1.5] nên khít mép junction lẫn mảnh straight kế tiếp.
            const float off = 1.25f;

            void Place(int dir, int jx2, int jy2, float dx, float dy, float yaw)
            {
                if ((mask & dir) == 0) return;
                if (jx2 < 0 || jx2 >= lw || jy2 < 0 || jy2 >= lh) return;
                int j = jy2 * lw + jx2;
                if (!replacedByHalf.Contains(j)) return;
                if (DirBits.CountBits(masks[j]) == 1) return;

                int sides = ((dir & (DirBits.E | DirBits.W)) != 0 ? DirBits.N | DirBits.S : DirBits.E | DirBits.W)
                            & ~(suppressed?.Sides(j) ?? 0);
                if (sides == 0) return;

                place(px + dx, py + dy, yaw, sides);
            }

            Place(DirBits.E, x2 + 2, y2, off, 0f, 0f);
            Place(DirBits.S, x2, y2 - 2, 0f, -off, 90f);
            Place(DirBits.W, x2 - 2, y2, -off, 0f, 180f);
            Place(DirBits.N, x2, y2 + 2, 0f, off, 270f);
        }

        /// <summary>Đặt part nửa ô (0.5×1 ô, ghép từ 2 ô modular) về phía các nhánh mà mảnh thẳng
        /// kề bên đã bị thay (mảnh thẳng sát junction).</summary>
        internal void AddHalfStraights(
            int mask, int x2, int y2, int[] masks, HashSet<int> replacedByHalf, BlockSuppression suppressed,
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            HashSet<string> missing, StraightBaker straightBaker, BlockRoadSkin skin = null)
        {
            ForEachHalfStraight(mask, x2, y2, masks, replacedByHalf, suppressed,
                (x, y, yaw, sides) => straightBaker.AddStraightTiles(placements, x, y, yaw, false, missing, sides, skin));
        }
    }
}
#endif
