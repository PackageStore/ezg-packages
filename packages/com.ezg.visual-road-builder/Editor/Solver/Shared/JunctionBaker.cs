#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Resolves part→prefab, emits Placements for one type-1 junction, builds
    /// rimBlocked/filletTurn delegates.</summary>
    internal sealed class JunctionBaker
    {
        private readonly ToolContext _ctx;
        internal JunctionBaker(ToolContext ctx) => _ctx = ctx;

        /// <summary>Đủ ô modular để ghép mảnh giao chưa (mảnh cua dùng turn thay 4 ô center, nên
        /// đòi CẢ hai bộ — thiếu bộ nào cũng chặn Apply để không bake ra mảnh khuyết).</summary>
        internal bool JunctionTilesReady
        {
            get
            {
                var lib = _ctx.Library;
                return lib != null && lib.road1x1_side != null && lib.road1x1_side_rim != null
                    && lib.road1x1_curve != null && lib.road1x1_curve_rim != null
                    && lib.road1x1_center != null
                    && lib.road2x2_turn != null && lib.road2x2_turn_rim != null
                    && lib.road1x1_turn != null && lib.road1x1_turn_rim != null;
            }
        }

        /// <summary>Các nhánh MỞ của mảnh giao tại (x2, y2) mà điểm kề 1 ô CŨNG là mảnh giao — ví dụ 2
        /// chữ T rẽ về 2 phía ĐỐI DIỆN trên cùng con đường. Hai mảnh giao kề nhau trùm lên nhau đúng 1 ô
        /// và KHÔNG đầu nào bị bỏ như mảnh thẳng, nên phần trùm phải chia đôi: mỗi bên nhường ô đó cho ô
        /// center của bên kia (bỏ ô side của nhánh + 2 ô vỉa hè mép đóng nằm về phía đó).</summary>
        internal int JunctionArms(int[] masks, int x2, int y2, int mask)
        {
            int lw = _ctx.Doc.LatticeW, lh = _ctx.Doc.LatticeH;
            int arms = 0;

            void Probe(int dir, int jx2, int jy2)
            {
                if ((mask & dir) == 0) return;
                if (jx2 < 0 || jx2 >= lw || jy2 < 0 || jy2 >= lh) return;
                if (DirBits.IsJunctionMask(masks[jy2 * lw + jx2])) arms |= dir;
            }

            Probe(DirBits.E, x2 + 2, y2);
            Probe(DirBits.W, x2 - 2, y2);
            Probe(DirBits.N, x2, y2 + 2);
            Probe(DirBits.S, x2, y2 - 2);

            // Mảnh giao cách NỬA Ô (1 nấc lattice) — chỉ có được khi junction nửa ô mọc sát một mảnh
            // giao thật: 2 ô center của nó phủ ĐÚNG ô side của arm bên này nên arm cũng phải nhường.
            Probe(DirBits.E, x2 + 1, y2);
            Probe(DirBits.W, x2 - 1, y2);
            Probe(DirBits.N, x2, y2 + 1);
            Probe(DirBits.S, x2, y2 - 1);
            return arms;
        }

        /// <summary>Đặt một mảnh GIAO bằng ô modular. Trả về false + ghi tên ô thiếu khi library chưa
        /// đủ part (caller bỏ luôn part nửa ô quanh mảnh này).</summary>
        internal bool AddJunctionTiles(
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            float x, float y, int mask, int junctionArms, HashSet<string> missing,
            RoadLayout layout = null, BlockRoadSkin skin = null)
        {
            var lib = _ctx.Library;
            if (!JunctionTilesReady)
            {
                if (lib == null || lib.road1x1_side == null) missing?.Add("Road Tile Side");
                if (lib == null || lib.road1x1_side_rim == null) missing?.Add("Road Tile Side Rim");
                if (lib == null || lib.road1x1_curve == null) missing?.Add("Road Tile Curve");
                if (lib == null || lib.road1x1_curve_rim == null) missing?.Add("Road Tile Curve Rim");
                if (lib == null || lib.road1x1_center == null) missing?.Add("Road Tile Center");
                if (lib == null || lib.road2x2_turn == null) missing?.Add("Road Tile Turn");
                if (lib == null || lib.road2x2_turn_rim == null) missing?.Add("Road Tile Turn Rim");
                if (lib == null || lib.road1x1_turn == null) missing?.Add("Road Tile Turn 1x1");
                if (lib == null || lib.road1x1_turn_rim == null) missing?.Add("Road Tile Turn 1x1 Rim");
                return false;
            }

            JunctionTileEmitter.ForEachJunctionTile(mask, junctionArms,
                (part, dx, dy, yaw) =>
                    placements.Add((x + dx, y + dy, JunctionTilePrefab(part), yaw, Vector3.one)),
                RimBlockedProbe(layout, skin, x, y),
                FilletTurnProbe(layout, x, y));
            return true;
        }

        /// <summary>Delegate <c>rimBlocked</c> của <see cref="JunctionTileEmitter.ForEachJunctionTile"/>: vỉa hè bị ô bo góc
        /// mảnh kề lấp (<see cref="RoadLayout.RimCovered"/>) HOẶC nằm trong dải thân khối
        /// (<see cref="BlockRoadSkin"/>) — cả 2 nguồn gộp ở đây để bake, preview và debug dùng chung.</summary>
        internal static System.Func<float, float, float, bool> RimBlockedProbe(
            RoadLayout layout, BlockRoadSkin skin, float nx, float ny)
        {
            if (layout == null && skin == null) return null;
            return (dx, dy, yaw) =>
                (layout != null && layout.RimCovered(nx + dx, ny + dy, yaw))
                || (skin != null && skin.KerbFreeAt(nx + dx, ny + dy, yaw));
        }

        /// <summary>Delegate <c>filletTurn</c> của <see cref="JunctionTileEmitter.ForEachJunctionTile"/> cho mảnh giao tại
        /// (<paramref name="nx"/>, <paramref name="ny"/>): layout lưu toạ độ TUYỆT ĐỐI còn
        /// <c>place</c> nhận offset so với tâm mảnh, nên quy đổi ở đây một lần cho cả 4 đường.</summary>
        internal static System.Func<float, float, float, (float x, float y, float yaw)?> FilletTurnProbe(
            RoadLayout layout, float nx, float ny)
        {
            if (layout == null) return null;
            return (dx, dy, yaw) =>
            {
                (float x, float y, float yaw)? turn = layout.FilletTurnAt(nx + dx, ny + dy, yaw);
                return turn == null ? null : (turn.Value.x - nx, turn.Value.y - ny, turn.Value.yaw);
            };
        }

        internal GameObject JunctionTilePrefab(RoadTilePart part)
        {
            var lib = _ctx.Library;
            return part switch
            {
                RoadTilePart.Side => lib.road1x1_side,
                RoadTilePart.SideRim => lib.road1x1_side_rim,
                RoadTilePart.Curve => lib.road1x1_curve,
                RoadTilePart.CurveRim => lib.road1x1_curve_rim,
                RoadTilePart.Turn2x2 => lib.road2x2_turn,
                RoadTilePart.Turn2x2Rim => lib.road2x2_turn_rim,
                RoadTilePart.Turn1x1 => lib.road1x1_turn,
                RoadTilePart.Turn1x1Rim => lib.road1x1_turn_rim,
                _ => lib.road1x1_center,
            };
        }
    }
}
#endif
