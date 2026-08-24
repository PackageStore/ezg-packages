#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Một biến thể của ô PATH: prefab + trọng số random. Tổng trọng số của mọi biến thể
    /// trong CÙNG một slot luôn bằng 1 — <c>RoadPartLibraryEditor</c> chuẩn hoá lại mỗi lần user
    /// kéo slider, thêm hoặc xoá biến thể.</summary>
    [System.Serializable]
    public struct PathPartVariant
    {
        public GameObject prefab;
        [Range(0f, 1f)] public float weight;
    }

    /// <summary>Thư viện mảnh đường cho tool <see cref="VisualRoadBuilderTool"/> (gom field theo tab qua <c>RoadPartLibraryEditor</c>).</summary>
    [CreateAssetMenu(menuName = "EZG Technical Art/Road Part Library", fileName = "RoadPartLibrary")]
    public sealed class RoadPartLibrary : ScriptableObject
    {
        // ── Atlas ──
        [Tooltip("Sprite atlas (_road_plan.psd) chứa mọi slice preview đường trên canvas 2D. "
                 + "Tên slice phải khớp với switch trong EnsureRoadSprites (Road_1x1_side, "
                 + "Highway_1x2, station_area…). Để trống thì canvas không vẽ icon đường.")]
        public Texture2D roadPlanAtlas;

        // ── Road: ô modular 0.5x0.5 ô, ghép nên mọi mảnh thẳng ──
        [FormerlySerializedAs("road1x1_core")]
        [Tooltip("Ô rìa đường 0.5x0.5 — chiếm ĐÚNG 1 ô logic. Pivot ở giữa cạnh +Z của ô, mesh nằm phía -Z " +
                 "(X ∈ [-0.25, 0.25], Z ∈ [-0.5, 0]). Cặp yaw / yaw+180 quanh CÙNG pivot phủ 0.5x1 ô; " +
                 "2 cặp lệch ±0.25 ô theo trục X (đã xoay) phủ 1x1 ô.")]
        public GameObject road1x1_side;
        [FormerlySerializedAs("road1x1_rim")]
        [Tooltip("Vỉa hè của ô side — chìa RA NGOÀI ô logic (tiếp ngay sau mép -Z của side). Luôn đặt " +
                 "kèm side: cùng vị trí, cùng yaw.")]
        public GameObject road1x1_side_rim;
        [Tooltip("Ô bo góc của mảnh giao — pivot ở góc TRONG của ô, mesh chìa về +X / -Z (yaw 0 = ô góc " +
                 "ĐÔNG-NAM). Luôn đặt kèm curve_rim: cùng vị trí, cùng yaw.")]
        public GameObject road1x1_curve;
        [Tooltip("Vỉa hè của ô góc — cùng quy ước pivot/yaw với curve.")]
        public GameObject road1x1_curve_rim;
        [Tooltip("Ô lòng đường trơn (KHÔNG có rim) — lấp 4 ô giữa mảnh giao, pivot ở tâm ô.")]
        public GameObject road1x1_center;
        [Tooltip("Lòng đường cung 1x1 ô của mảnh CUA — lấp lõi thay 4 ô center. Pivot ở góc TRONG của " +
                 "ô góc có 2 nhánh mở, mesh chìa về -X / -Z (yaw 0 = lõi nằm phía TÂY-NAM pivot). " +
                 "Luôn đặt kèm turn_rim: cùng vị trí, cùng yaw.")]
        public GameObject road2x2_turn;
        [Tooltip("Vỉa hè cung của mảnh cua — cùng quy ước pivot/yaw với turn, chìa thêm ra ngoài " +
                 "2 mép ĐÓNG của mảnh cua.")]
        public GameObject road2x2_turn_rim;
        [Tooltip("Lòng đường cung của mảnh CUA LỚN Road 2 (mặt cắt x1.5) — cùng quy ước pivot/yaw với " +
                 "road2x2_turn, lõi arc-core (2 nhánh mở vuông góc) của Road2Solver. Luôn đặt kèm " +
                 "turn_rim: cùng vị trí, cùng yaw.")]
        public GameObject road3x3_turn;
        [Tooltip("Vỉa hè cung của mảnh cua lớn Road 2 — cùng quy ước pivot/yaw với road3x3_turn.")]
        public GameObject road3x3_turn_rim;
        [Tooltip("Ô cua NHỎ lấp quarter-cell nằm giữa 2 mảnh giao lệch 1.5 ô — thay CẢ HAI ô bo góc " +
                 "chĩa vào đó (2 curve chồng khít nhau). Quy ước pivot y như side: pivot ở giữa cạnh " +
                 "+Z, mesh nằm phía -Z (chỉ sâu 0.25 ô). Luôn đặt kèm turn_rim: cùng vị trí, cùng yaw.")]
        public GameObject road1x1_turn;
        [Tooltip("Vỉa hè của ô cua nhỏ — cùng quy ước pivot/yaw với road1x1_turn, phủ trọn quarter-cell.")]
        public GameObject road1x1_turn_rim;
        [Tooltip("Prefab nối đường cao tốc với đường thường.")]
        public GameObject hway_to_road;

        // ── Highway: ô modular 0.5x1 ô, ghép nên mảnh thẳng (không có part cua/giao) ──
        [FormerlySerializedAs("highwayStraightPrefab")]
        [Tooltip("Ô lòng cao tốc 0.5x1 — quy ước pivot y hệt road1x1_side nhưng SÂU GẤP ĐÔI: pivot ở " +
                 "giữa cạnh +Z, mesh nằm phía -Z (X ∈ [-0.25, 0.25], Z ∈ [-1, 0]) = nửa bề ngang cao " +
                 "tốc. Cặp yaw / yaw+180 quanh CÙNG pivot phủ 0.5x2 ô = trọn bề ngang.")]
        public GameObject hway1x2_side;
        [Tooltip("Vỉa hè của ô cao tốc — chìa RA NGOÀI mép -Z của side. Luôn đặt kèm side: cùng vị " +
                 "trí, cùng yaw.")]
        public GameObject hway1x2_side_rim;

        // ── Building ──
        // Station: khối vuông 4x4 ô, pivot ở tâm khối.
        public GameObject stationPrefab;
        // Parking slot: khối 4x2 ô, pivot tâm khối, cạnh DÀI dọc trục X, MẶT quay về +Z.
        public GameObject parkingPrefab;

        // ── Road 2: mặt cắt rộng x1.5 (3 ô) — rim | center_filler | road1x1_side | road1x1_side | center_filler | rim ──
        [Tooltip("Ô lấp nửa ô giữa rim và side của mặt cắt Road 2 — TÁI DÙNG mesh Road_0.5x1_center " +
                 "(road1x1_center dùng cho lõi giao lộ type-1). 2 ô side ở giữa mặt cắt dùng lại " +
                 "road1x1_side/road1x1_side_rim đã có ở trên, KHÔNG cần field riêng.")]
        public GameObject road2_center_filler;
        [Tooltip("Ô bo góc của mảnh giao Road 2 — chưa có art, để trống (missing-part warning, không chặn Apply).")]
        public GameObject road2_curve;
        [Tooltip("Vỉa hè của ô bo góc Road 2 — chưa có art, để trống.")]
        public GameObject road2_curve_rim;
        [Tooltip("Prefab nối đường cao tốc với Road 2 — chưa có art, để trống.")]
        public GameObject hway_to_road2;

        // ── Path: lối đi bộ, mặt cắt 0.5 ô (nửa type-1), KHÔNG có rim (D3) ──
        // Mỗi slot là DANH SÁCH biến thể + trọng số: solver bốc 1 biến thể cho TỪNG ô theo trọng số.
        // Tổng trọng số mỗi slot luôn = 1 (RoadPartLibraryEditor chuẩn hoá khi kéo slider / thêm / xoá).
        [Tooltip("Biến thể ô rìa path 0.25 rộng × 0.5 sâu — pivot giữa cạnh +Z, mesh về -Z. " +
                 "Cặp yaw/yaw+180 quanh CÙNG pivot phủ 0.25 dọc trục × 1 ô ngang trục; slot 0.5 của " +
                 "node cần 2 cột lệch ±0.125. Luôn KHÔNG có rim (D3).")]
        public List<PathPartVariant> path_side_variants = new List<PathPartVariant>();
        [Tooltip("Biến thể lõi giao path 0.5×0.5 — pivot TÂM ô. Dùng lấp giữa mảnh giao path. " +
                 "Luôn KHÔNG có rim (D3).")]
        public List<PathPartVariant> path_center_variants = new List<PathPartVariant>();
        [Tooltip("Biến thể bo góc path 0.25×0.25 — pivot góc TRONG, mesh về +X/-Z (yaw 0 = ô ĐÔNG-NAM). " +
                 "Luôn KHÔNG có rim (D3).")]
        public List<PathPartVariant> path_curve_variants = new List<PathPartVariant>();
        [Tooltip("Biến thể lõi cung path 0.5×0.5 — pivot TÂM ô (KHÔNG lệch 1 nấc CW như road2x2_turn " +
                 "của type-1). Luôn KHÔNG có rim (D3).")]
        public List<PathPartVariant> path_turn_variants = new List<PathPartVariant>();

        // Field 1-prefab ĐỜI CŨ — chỉ còn để migrate: inspector chuyển vào *_variants rồi set null.
        // Solver vẫn đọc làm fallback nếu list rỗng (asset chưa mở inspector lần nào).
        [HideInInspector] public GameObject path_side;
        [HideInInspector] public GameObject path_center;
        [HideInInspector] public GameObject path_curve;
        [HideInInspector] public GameObject path_turn;
    }
}
#endif
