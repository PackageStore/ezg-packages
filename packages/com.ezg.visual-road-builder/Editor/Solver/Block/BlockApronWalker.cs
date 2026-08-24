#if UNITY_EDITOR
namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Shared apron tile walker used by both <see cref="StationRoadCollector"/> and
    /// <see cref="ParkingKerbCollector"/>.</summary>
    internal static class BlockApronWalker
    {
        /// <summary>Duyệt các ô modular dựng nên mảnh đường TRƯỚC MẶT KHỐI (station/parking), gọi
        /// <paramref name="place"/> với (loại ô, x, y, yaw) — x/y là toạ độ ô TUYỆT ĐỐI. Nguồn DUY NHẤT
        /// của vị trí + yaw cho bake, preview sprite và debug boundary, CHUNG cho type-1 lẫn Road 2 và
        /// CHUNG cho station lẫn parking (chỉ khác <paramref name="spanCells"/> bề ngang mặt khối,
        /// <paramref name="apronDepths"/> profile độ sâu, <paramref name="filletDepth"/> và
        /// <paramref name="midFillets"/>). <paramref name="filletDepth"/> 0 = không có bậc vỉa hè nào
        /// để bo (parking trên Road 2) ⇒ bỏ hẳn ô bo góc. <paramref name="midFillets"/> false = mặt khối là MỘT dải
        /// liền không có đảo phân cách (parking) nên 2 nửa ô giữa lát ô apron trơn như phần còn lại,
        /// thay vì ô side + bo góc của station.
        /// (u, v) = (dọc dải đường tính từ mép mặt khối, vuông góc — DƯƠNG về phía khối).</summary>
        internal static void ForEachStationFrontTile(
            int line2, int p02, int rot, int spanCells, float[] apronDepths, float filletDepth,
            bool midFillets, System.Action<RoadTilePart, float, float, float> place)
        {
            int s = spanCells;
            bool horizontal = rot == 0 || rot == 2;
            float line = line2 * 0.5f, p0 = p02 * 0.5f;
            // Mặt station quay theo rot (0 = +Z) nên phía station nằm NGƯỢC hướng mặt so với dải đường.
            int toStation = BlockSide.Side(rot);
            int uDir = horizontal ? DirBits.E : DirBits.N;
            float vSign = rot == 0 || rot == 1 ? -1f : 1f;

            (float x, float y) At(float u, float v) => horizontal
                ? (p0 + u, line + v * vSign)
                : (line + v * vSign, p0 + u);

            float innerYaw = DirBits.RimYawFacing(toStation);

            int steps = s * 2 + 2;
            for (int k = 0; k < steps; k++)
            {
                float u = k * 0.5f - 0.25f;

                // 2 nửa ô ở 2 đầu mảnh + 2 nửa ô giữa mặt station = chỗ ô bo góc apron chiếm.
                if (k == 0 || k == steps - 1 || (midFillets && (k == s || k == s + 1)))
                {
                    (float rx, float ry) = At(u, 0f);
                    place(RoadTilePart.Side, rx, ry, innerYaw);
                    continue;
                }

                foreach (float v in apronDepths)
                {
                    (float cx, float cy) = At(u, v);
                    place(RoadTilePart.Center, cx, cy, 0f);
                }
            }

            // Pivot ô bo góc ở mép giáp dải apron trơn, cung lượn RA NGOÀI dải đó.
            void Fillet(float fu, int growDir, float fv)
            {
                (float fx, float fy) = At(fu, fv);
                float yaw = DirBits.SolveYaw(DirBits.E | DirBits.S, toStation | growDir);
                place(RoadTilePart.Curve, fx, fy, yaw);
                place(RoadTilePart.CurveRim, fx, fy, yaw);
            }

            if (filletDepth <= 0f) return;

            float half = s * 0.5f;
            int back = DirBits.OppositeDir(uDir);
            Fillet(0f, back, filletDepth);
            Fillet(s, uDir, filletDepth);
            if (!midFillets) return;
            Fillet(half - 0.5f, uDir, filletDepth);
            Fillet(half + 0.5f, back, filletDepth);
        }
    }
}
#endif
