#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Tile part classification for type-1 junction modular tiles.</summary>
    internal enum RoadTilePart
    {
        Side, SideRim, Curve, CurveRim, Center, Turn2x2, Turn2x2Rim, Turn1x1, Turn1x1Rim,
    }

    /// <summary>Enumerates modular tile (part, x, y, yaw) for one type-1 junction — THE single source
    /// for bake, preview, overlap, debug boundary.</summary>
    internal static class JunctionTileEmitter
    {
        // Ô góc base (yaw 0) của mảnh giao = góc ĐÔNG-NAM. Pivot prefab curve/curve_rim nằm ở góc
        // TRONG của chính ô đó (mesh chìa về +X / -Z), nên offset pivot = (0.5, -0.5) xoay theo yaw.
        internal const float JunctionCornerPivotX = 0.5f, JunctionCornerPivotY = -0.5f;

        /// <summary>Duyệt các ô modular dựng nên một mảnh GIAO, gọi <paramref name="place"/> với
        /// (loại ô, x, y, yaw) — x/y là offset ô so với TÂM mảnh, đúng pivot prefab. Nguồn DUY NHẤT của
        /// vị trí + yaw ô giao cho bake và preview sprite; công thức đọc ngược từ prefab mẫu
        /// <c>modular_Road_Cross</c> / <c>modular_Road_T</c> / <c>modular_Road_Turn</c>:
        /// <list type="bullet">
        /// <item>mỗi nhánh MỞ = 1 cột side (yaw và yaw+180 quanh cùng pivot, cách tâm 0.75 ô);</item>
        /// <item>mỗi góc có 2 nhánh mở kề nhau = curve + curve_rim, pivot ở góc TRONG của ô góc đó;</item>
        /// <item>T + ngã tư: giữa mảnh 4 ô center (không viền), mỗi hướng ĐÓNG = 4 side_rim dọc mép;</item>
        /// <item>cua 2 nhánh: lõi là turn + turn_rim (1 ô cung, cùng pivot với curve của góc mở nhưng
        /// lệch 1 nấc CW), và mỗi nhánh chỉ 1 side_rim ở sườn NGOÀI — vỉa hè 2 mép đóng do turn_rim lo.</item>
        /// </list>
        /// <paramref name="junctionArms"/> (xem <see cref="JunctionBaker.JunctionArms"/>) = các nhánh mở chĩa vào một
        /// mảnh GIAO kề: ô side của nhánh đó bị bỏ (mảnh bên kia đã lấp bằng ô center) cùng 2 ô vỉa hè
        /// mép đóng nằm về phía đó (vỉa hè rơi vào lòng đường mảnh bên kia).
        /// <paramref name="filletTurn"/> (xem <see cref="FilletCollector"/>) = ô bo góc rơi trùng ô bo
        /// góc của mảnh giao kề ⇒ thay cả cặp curve bằng 1 ô cua nhỏ tại offset nó trả về — kể cả ở LÕI
        /// CUNG, chỗ turn 2x2 vẫn giữ và chỉ curve của nó nhường chỗ.
        /// Mảnh thẳng KHÔNG đi qua đây (xem <see cref="StraightTileEmitter.ForEachStraightTile"/>).</summary>
        internal static void ForEachJunctionTile(
            int mask, int junctionArms, System.Action<RoadTilePart, float, float, float> place,
            System.Func<float, float, float, bool> rimBlocked = null,
            System.Func<float, float, float, (float x, float y, float yaw)?> filletTurn = null)
        {
            const float arm = 0.75f, mid = 0.25f;
            // KHÔNG phân loại mảnh (cua / T / ngã tư) — mọi ô suy từ HƯỚNG MỞ tại điểm này. Đúng 2
            // hướng mở VUÔNG GÓC ⇒ lõi là cung cua thay 4 ô center, và vỉa hè chạy theo sườn NGOÀI
            // của 2 nhánh thay vì theo mép đóng.
            bool arcCore = DirBits.IsArcCoreMask(mask);

            // Vỉa hè bị ô bo góc của mảnh kề lấp chỗ thì bỏ — xem RoadLayout.RimCovered.
            void PlaceRim(RoadTilePart part, float px, float py, float yaw)
            {
                if (rimBlocked == null || !rimBlocked(px, py, yaw)) place(part, px, py, yaw);
            }

            if (!arcCore)
            {
                place(RoadTilePart.Center, -mid, -mid, 0f);
                place(RoadTilePart.Center, -mid, mid, 0f);
                place(RoadTilePart.Center, mid, -mid, 0f);
                place(RoadTilePart.Center, mid, mid, 0f);
            }

            void Arm(int dir, float px, float py, float yaw)
            {
                if ((mask & dir) == 0 || (junctionArms & dir) != 0) return;
                place(RoadTilePart.Side, px, py, yaw);
                place(RoadTilePart.Side, px, py, (yaw + 180f) % 360f);
            }

            Arm(DirBits.E, arm, 0f, 0f);
            Arm(DirBits.W, -arm, 0f, 0f);
            Arm(DirBits.N, 0f, arm, 90f);
            Arm(DirBits.S, 0f, -arm, 90f);

            void Corner(int d1, int d2, float yaw)
            {
                if ((mask & d1) == 0 || (mask & d2) == 0) return;
                (float px, float py) = DirBits.RotateCellsCW(
                    JunctionCornerPivotX, JunctionCornerPivotY, Mathf.RoundToInt(yaw / 90f));
                if (arcCore)
                {
                    // Lòng đường cung chìa về -X/-Z còn curve chìa về +X/-Z quanh CÙNG pivot ⇒ lệch 1 nấc.
                    float turnYaw = (yaw + 90f) % 360f;
                    place(RoadTilePart.Turn2x2, px, py, turnYaw);
                    place(RoadTilePart.Turn2x2Rim, px, py, turnYaw);
                }

                // Ô cua nhỏ thay curve ở CẢ lõi cung: turn 2x2 ở trên là lòng đường của cung, còn curve
                // là fillet bo góc — chỉ fillet mới chồng với fillet mảnh giao kề.
                if (filletTurn != null && filletTurn(px, py, yaw) is { } small)
                {
                    place(RoadTilePart.Turn1x1, small.x, small.y, small.yaw);
                    place(RoadTilePart.Turn1x1Rim, small.x, small.y, small.yaw);
                    return;
                }

                place(RoadTilePart.Curve, px, py, yaw);
                place(RoadTilePart.CurveRim, px, py, yaw);
            }

            Corner(DirBits.E, DirBits.S, 0f);
            Corner(DirBits.S, DirBits.W, 90f);
            Corner(DirBits.W, DirBits.N, 180f);
            Corner(DirBits.N, DirBits.E, 270f);

            if (arcCore)
            {
                // Sườn NGOÀI của nhánh mở = phía ngược nhánh mở còn lại (2 nhánh vuông góc nhau).
                void ArmRim(int dir, float px, float py)
                {
                    if ((mask & dir) == 0 || (junctionArms & dir) != 0) return;
                    PlaceRim(RoadTilePart.SideRim, px, py,
                        DirBits.RimYawFacing(DirBits.OppositeDir(mask & ~dir)));
                }

                ArmRim(DirBits.E, arm, 0f);
                ArmRim(DirBits.W, -arm, 0f);
                ArmRim(DirBits.N, 0f, arm);
                ArmRim(DirBits.S, 0f, -arm);
                return;
            }

            // Hướng đóng: mép thẳng, 4 ô vỉa hè chạy suốt 2 ô bề ngang (base yaw 0 = vỉa hè quay NAM).
            void EdgeRim(int dir, float yaw)
            {
                if ((mask & dir) != 0) return;
                int turns = Mathf.RoundToInt(yaw / 90f) & 3;
                int plus = DirBits.RimRunPlusDir(dir), minus = DirBits.OppositeDir(plus);
                for (int k = 0; k < 4; k++)
                {
                    float t = k switch { 0 => -arm, 1 => -mid, 2 => mid, _ => arm };
                    if ((junctionArms & (t > 0f ? plus : minus)) != 0) continue;
                    (float rx, float ry) = DirBits.RotateCellsCW(t, 0f, turns);
                    PlaceRim(RoadTilePart.SideRim, rx, ry, yaw);
                }
            }

            EdgeRim(DirBits.S, 0f);
            EdgeRim(DirBits.W, 90f);
            EdgeRim(DirBits.N, 180f);
            EdgeRim(DirBits.E, 270f);
        }
    }
}
#endif
