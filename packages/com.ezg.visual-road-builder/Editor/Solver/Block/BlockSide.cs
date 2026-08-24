#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Block-facing direction helpers, apron plain pass, and road suppress strip.</summary>
    internal static class BlockSide
    {
        /// <summary>Nửa đường mà khối chiếm = phía THÂN khối so với dải đường trước mặt
        /// (<paramref name="rot"/> = hướng MẶT, 0 = +Z ⇒ thân nằm phía -Z). Chung cho station và
        /// parking vì cả hai đều đặt mặt quay ra đường.</summary>
        internal static int Side(int rot) =>
            rot switch { 0 => DirBits.S, 1 => DirBits.W, 2 => DirBits.N, _ => DirBits.E };

        /// <summary>Đổi ô side của apron sang ô center ở những cột mà apron BỜ ĐỐI DIỆN đã trải trơn.
        /// Chạy SAU KHI gom hết khối vì cột trơn của station này mới quyết được ô của station kia; tách
        /// khỏi collector để canvas gộp được cả khối ghost trước khi
        /// đổi. Bake và preview cùng gọi nên 2 đường vẫn ra một hình.
        /// <paramref name="plainCoreCell"/>: chuyển toạ độ ô side → tâm ô center tương ứng.</summary>
        internal static void ApplyApronPlain(
            List<(RoadTilePart part, float x, float y, float yaw)> stationRoads, BlockRoadSkin skin,
            System.Func<float, float, float, (float x, float y)> plainCoreCell)
        {
            if (skin == null) return;
            for (int i = 0; i < stationRoads.Count; i++)
            {
                (RoadTilePart part, float x, float y, float yaw) t = stationRoads[i];
                if (t.part != RoadTilePart.Side || !skin.PlainAt(t.x, t.y, t.yaw)) continue;
                (float cx, float cy) = plainCoreCell(t.x, t.y, t.yaw);
                stationRoads[i] = (RoadTilePart.Center, cx, cy, 0f);
            }
        }

        /// <summary>Bỏ part đường bị dải apron STATION phủ (parking không đi qua đây — nó giữ nguyên
        /// lòng đường), trên
        /// hàng <paramref name="line2"/> chạy từ nửa ô <paramref name="lo2"/> tới <paramref name="hi2"/>,
        /// CHỈ ở nửa mặt cắt <paramref name="side"/> — mesh khối dày đúng 1 ô tính từ tim đường ra phía
        /// nó. Mảnh road thường rộng 1 ô canh tâm điểm lưới ⇒ chồng dải ⇔ điểm nằm trong [lo2, hi2]; mép
        /// dải rơi đúng biên ô thì mảnh kề GIỮ NGUYÊN (khít, không cần bỏ). Phần lấp 2 mép KHÔNG quyết
        /// ở đây: dải chỉ được ghi lại cho <see cref="BlockEdgeFiller"/> — mép chỉ hở thật khi
        /// bên kia không phải một khối khác.</summary>
        internal static void SuppressBlockRoadStrip(
            int[] roadMasks, BlockSuppression suppressed, int line2, int lo2, int hi2, bool horizontal,
            int side, List<BlockStrip> strips, BlockRoadSkin skin,
            int latticeW, int gridWidth, int gridHeight)
        {
            int lateralMax2 = (horizontal ? gridWidth - 1 : gridHeight - 1) * 2;

            for (int q2 = Mathf.Max(0, lo2); q2 <= Mathf.Min(lateralMax2, hi2); q2++)
            {
                int idx = horizontal ? line2 * latticeW + q2 : q2 * latticeW + line2;
                if (roadMasks[idx] != 0) suppressed.Take(idx, roadMasks[idx], side);
            }

            var strip = new BlockStrip(horizontal, line2, lo2, hi2, side);
            strips?.Add(strip);
            skin?.AddBlockEdge(strip);
        }
    }
}
#endif
