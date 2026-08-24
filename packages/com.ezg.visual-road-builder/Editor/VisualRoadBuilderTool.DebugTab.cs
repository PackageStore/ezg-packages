#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Tab Debug: các tùy chọn kiểm tra / gỡ lỗi cho road builder (boundary lớp đường
    /// road/highway và boundary khối station/parking, bật tắt riêng).</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        private const string DebugBoundaryAlphaPrefKey = "VisualRoadBuilder.DebugBoundaryAlpha";

        private static readonly Color DebugRoadColor = new(0.2f, 0.85f, 1f, 0.85f);
        // Ô vỉa hè nằm CHỒNG lên ô lòng đường cùng pivot (side vs side_rim) — cùng hệ màu lớp Đường
        // nhưng nhạt hơn để phân biệt được 2 box lồng nhau.
        private static readonly Color DebugRoadRimColor = new(0.55f, 0.92f, 1f, 0.55f);
        private static readonly Color DebugHighwayColor = new(1f, 0.65f, 0.2f, 0.85f);
        private static readonly Color DebugHighwayRimColor = new(1f, 0.82f, 0.55f, 0.55f);
        // Tím, cùng hệ với TileRoad2 trên canvas — tách Road 2 khỏi lớp Đường (cyan) và Highway (cam).
        private static readonly Color DebugRoad2Color = new(0.78f, 0.50f, 1f, 0.85f);
        private static readonly Color DebugRoad2RimColor = new(0.88f, 0.72f, 1f, 0.55f);
        // Xanh ngọc — tách khỏi Đường (cyan), Highway (cam), Road 2 (tím), cùng họ TilePath trên canvas
        private static readonly Color DebugPathColor = new(0.25f, 0.82f, 0.72f, 0.85f);
        private static readonly Color DebugStationColor = new(0.45f, 0.65f, 1f, 0.9f);
        private static readonly Color DebugParkingColor = new(0.35f, 0.95f, 0.4f, 0.9f);

        private struct DebugBoundaryItem
        {
            public Rect Rect;
            public string Name;
            public Color Color;
        }

        private bool AnyDebugBoundary => _showDebugBoundary || _showDebugBlockBoundary;

        /// <summary>Boundary mặc định BẬT. Field initializer chỉ ăn với window tạo mới, nên window đã
        /// serialize từ bản cũ (2 cờ = false) được bật đúng MỘT lần ở đây — tắt tay sau đó vẫn giữ.</summary>
        private void ApplyDebugBoundaryDefault()
        {
            if (_debugBoundaryDefaultApplied) return;
            _debugBoundaryDefaultApplied = true;
            _showDebugBoundary = true;
            _showDebugBlockBoundary = true;
        }

        /// <summary>Alpha nhớ qua EditorPrefs (mặc định 100% khi chưa có pref). [SerializeField] KHÔNG đủ:
        /// đóng/mở lại window hay restart Editor đều dựng instance mới với mọi field về default nên slider
        /// luôn nhảy về 100% — pref sống ngoài window nên giữ được mức người dùng đã chọn.</summary>
        private void LoadDebugBoundaryAlpha()
        {
            _debugBoundaryAlpha = Mathf.Clamp01(EditorPrefs.GetFloat(DebugBoundaryAlphaPrefKey, 1f));
        }

        private void DrawDebugTab()
        {
            EditorGUILayout.LabelField("Debug Controls", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUI.BeginChangeCheck();
            _showDebugBoundary = EditorGUILayout.ToggleLeft("Show boundary (road, highway, road 2)", _showDebugBoundary);
            _showDebugBlockBoundary = EditorGUILayout.ToggleLeft("Show boundary (station, parking)", _showDebugBlockBoundary);

            using (new EditorGUI.DisabledScope(!AnyDebugBoundary))
            {
                int pct = Mathf.RoundToInt(_debugBoundaryAlpha * 100f);
                pct = EditorGUILayout.IntSlider(
                    new GUIContent("Boundary alpha (%)",
                        "Độ mờ của MỌI box boundary (road, highway, station, parking). 0% = chỉ còn box " +
                        "dưới con trỏ (highlight + tooltip luôn giữ nguyên độ đậm)."),
                    pct, 0, 100);
                float alpha = pct / 100f;
                if (!Mathf.Approximately(alpha, _debugBoundaryAlpha))
                {
                    _debugBoundaryAlpha = alpha;
                    EditorPrefs.SetFloat(DebugBoundaryAlphaPrefKey, alpha);
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                Repaint();
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>Vẽ boundary (bounding box) cho các element của những lớp đang bật debug boundary.</summary>
        private void DrawDebugBoundaries(Rect canvas)
        {
            EnsureRoadSprites();
            var items = new List<DebugBoundaryItem>();
            if (_showDebugBoundary)
            {
                CollectDebugBoundaryItems(canvas, items);
                CollectDebugRoad2BoundaryItems(canvas, items);
                CollectDebugPathBoundaryItems(canvas, items);
            }
            if (_showDebugBlockBoundary) CollectDebugBlockBoundaryItems(canvas, items);

            // Box NHỎ NHẤT chứa con trỏ mới được highlight + tooltip: khối station/parking phủ trọn
            // các piece đường bên trong nên chọn theo thứ tự vẽ sẽ nhặt sai cái to.
            int hovered = -1;
            if (_hoverCellValid)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (!items[i].Rect.Contains(_hoverPixel)) continue;
                    if (hovered < 0 || RectArea(items[i].Rect) < RectArea(items[hovered].Rect)) hovered = i;
                }
            }

            // Alpha 0% chỉ tắt các box NỀN — box dưới con trỏ vẫn đậm, thành chế độ "chỉ xem cái đang trỏ".
            if (_debugBoundaryAlpha > 0f)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (i != hovered) DrawRectOutline(items[i].Rect, FadeDebug(items[i].Color), 1.2f);
                }
            }

            if (hovered >= 0)
            {
                var item = items[hovered];
                EditorGUI.DrawRect(item.Rect, new Color(1f, 1f, 0f, 0.25f));
                DrawRectOutline(item.Rect, Color.yellow, 2f);

                Vector2 mousePos = _hoverPixel;
                string text = item.Name;
                Vector2 size = PillStyle.CalcSize(new GUIContent(text));
                float labelW = Mathf.Max(60f, size.x + 16f);
                float labelH = Mathf.Max(20f, size.y + 4f);

                var tooltipRect = new Rect(mousePos.x + 12f, mousePos.y - 24f, labelW, labelH);
                if (tooltipRect.xMax > canvas.xMax) tooltipRect.x = mousePos.x - labelW - 8f;
                if (tooltipRect.y < canvas.y) tooltipRect.y = mousePos.y + 16f;

                EditorGUI.DrawRect(tooltipRect, new Color(0.08f, 0.08f, 0.08f, 0.9f));
                DrawRectOutline(tooltipRect, Color.yellow, 1f);
                GUI.Label(tooltipRect, text, PillStyle);
            }
        }

        private static float RectArea(Rect r) => r.width * r.height;

        /// <summary>Nhân alpha gốc của box với slider — giữ tương quan đậm/nhạt giữa lòng đường và
        /// vỉa hè (<see cref="DebugRoadRimColor"/> vốn đã nhạt hơn) ở mọi mức slider.</summary>
        private Color FadeDebug(Color color) =>
            new(color.r, color.g, color.b, color.a * _debugBoundaryAlpha);

        /// <summary>Boundary khối Station / Parking = ĐÚNG rect art thật đang vẽ trong
        /// <see cref="DrawStations"/>: station_area phủ kín khối s×s ô, parking_area chỉ là slab dày
        /// 1 ô (512×128 = 4×1 ô) chứ không phải cả khối placement. Cả hai đặt pivot NGOÀI box theo
        /// hướng mặt — station cách 1 ô, parking cách nửa ô (mesh ăn sát dải đường).</summary>
        private void CollectDebugBlockBoundaryItems(Rect canvas, List<DebugBoundaryItem> items)
        {
            string stationName = _library != null && _library.stationPrefab != null
                ? _library.stationPrefab.name
                : "station";
            int s = StationSize;
            foreach (int id in _stations)
            {
                DecodeStation(id, out int x2, out int y2, out _);
                items.Add(new DebugBoundaryItem
                {
                    Rect = BlockRect(canvas, new Vector2Int(x2, y2), s, s),
                    Name = stationName,
                    Color = DebugStationColor,
                });
            }

            string parkingName = _library != null && _library.parkingPrefab != null
                ? _library.parkingPrefab.name
                : "parking";
            foreach (int id in _parkings)
            {
                DecodeParking(id, out int x2, out int y2, out int rot);
                items.Add(new DebugBoundaryItem
                {
                    Rect = ParkingSlabRect(canvas, new Vector2Int(x2, y2), rot),
                    Name = parkingName,
                    Color = DebugParkingColor,
                });
            }
        }

        private static void DrawRectOutline(Rect rect, Color color, float thickness = 1.2f)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private void CollectDebugBoundaryItems(Rect canvas, List<DebugBoundaryItem> items)
        {
            int lw = LatticeW, lh = LatticeH;
            float cell = _cellPixelSize;
            Color hwColor = DebugHighwayColor;

            int[] hwMasks = BuildMasks(_highwayEdges);
            int[] roadMasks = BuildMasks(_edges);
            // Legacy-view mask (road) cho ramp scan + edge-fill — CHUNG với Apply nên box khớp bake.
            // hwMasks GIỮ DENSE (xem Apply.cs).
            int[] roadMasksLegacy = BuildLegacyMasksFromEdges(_edges);
            var suppressed = new BlockSuppression();
            var rampSuppressed = new HashSet<int>(); // dưới arm ramp — chặn part, KHÔNG kích half junction
            var stationRoads = new List<(RoadTilePart part, float x, float y, float yaw)>();
            var blockStrips = new List<BlockStrip>();
            var blockEdgeHalves = new List<(float x, float y, float yaw, int sides)>();
            var blockEdgeFulls = new List<(float x, float y, float yaw, int sides)>();

            // Box boundary chỉ quan tâm FOOTPRINT: ô center thay ô side phủ đúng quarter-cell đó và ô
            // vỉa hè bỏ đi không nới khung, nên skin không đổi kết quả — vẫn truyền để đi chung đường
            // với bake/preview.
            var parkingRoads = new List<(RoadTilePart part, float x, float y, float yaw)>();
            var skin = new BlockRoadSkin();
            CollectStationRoadPlacements(_stations, roadMasks, suppressed, stationRoads, blockStrips, skin, null, false);
            CollectParkingRoadKerb(_parkings, roadMasks, suppressed, parkingRoads, blockStrips, skin, null, false);
            CollectBlockEdgeFills(roadMasksLegacy, blockStrips, blockEdgeHalves, blockEdgeFulls);
            ApplyApronPlain(stationRoads, skin);
            ApplyApronPlain(parkingRoads, skin);

            // 1. Ramp Highway->Road + Ramp bridges
            List<(int x2, int y2, int stem, int hwMask)> ramps = CollectRampJunctions(hwMasks, roadMasksLegacy);
            string rampName = _library != null && _library.hway_to_road != null ? _library.hway_to_road.name : (_spRampHway != null ? _spRampHway.name : "hway_to_road");

            foreach (var (x2, y2, stem, hwMask) in ramps)
            {
                Vector2 p = PointToPixelF(canvas, x2 * 0.5f, y2 * 0.5f);
                int turns = TurnsFromBase(DirN | DirS | DirW, hwMask | stem);
                float s = cell * 4f;
                Vector2 piv = _spRampHway != null && _spRampHway.rect.width > 0f
                    ? new Vector2(_spRampHway.pivot.x / _spRampHway.rect.width, _spRampHway.pivot.y / _spRampHway.rect.height)
                    : new Vector2(0.5f, 0.5f);
                float a = piv.x, b = 1f - piv.y;
                for (int k = 0; k < (turns & 3); k++) { float na = 1f - b, nb = a; a = na; b = nb; }
                Rect rampRect = new Rect(p.x - a * s, p.y - b * s, s, s);
                items.Add(new DebugBoundaryItem { Rect = rampRect, Name = rampName, Color = hwColor });

                if (TryRampRoadBridge(x2, y2, stem, roadMasksLegacy, rampSuppressed, out float bx, out float by, out float bYaw))
                    AddStraightTileBoundary(canvas, items, bx, by, bYaw, false);
            }

            // 2. Cột thẳng cao tốc — mỗi Ô MODULAR một box, đi CÙNG đường với DrawHighwaySprites nên
            // box khớp 1:1 sprite đang vẽ.
            foreach (var (cx, cy, horiz) in CollectHighwayColumns(ramps))
                ForEachHighwayColumnTile(cx, cy, horiz ? 0f : 90f,
                    (tx, ty, tyaw) => AddHighwayTileBoundary(canvas, items, tx, ty, tyaw));

            // 3. Mảnh đường trước mặt station/parking — mỗi ô modular một box, đi CÙNG đường với
            // DrawStationRoadSprites nên box khớp 1:1 sprite đang vẽ.
            foreach (var (part, rx, ry, ryaw) in stationRoads)
                AddTileBoundary(canvas, items, part, rx, ry, ryaw);
            foreach (var (part, rx, ry, ryaw) in parkingRoads)
                AddTileBoundary(canvas, items, part, rx, ry, ryaw);

            // 4. Road Elements — mỗi Ô MODULAR một box (side, side_rim, curve, … tách rời), đi CÙNG
            // đường với DrawRoadSprites nên box khớp 1:1 sprite đang vẽ.
            bool Blocked(int idx) => hwMasks[idx] != 0 || suppressed.Blocked(idx) || rampSuppressed.Contains(idx);

            RoadLayout layout = ResolveRoadLayout(_edges, Blocked);
            roadMasks = layout.Masks;
            HashSet<int> replacedByHalf = layout.ReplacedByHalf;

            for (int y2 = 0; y2 < lh; y2++)
            {
                for (int x2 = 0; x2 < lw; x2++)
                {
                    int i = y2 * lw + x2;
                    int mask = roadMasks[i];
                    if (mask == 0 || Blocked(i) || layout.Skip(i)) continue;

                    if (IsStraightLikeMask(mask))
                    {
                        (float ax, float ay, bool full) = StraightAnchorFor(layout, i, mask, x2, y2);
                        AddStraightTileBoundary(canvas, items, ax, ay, StraightYaw(mask), full,
                            StraightSides(mask) & ~suppressed.Sides(i));
                        continue;
                    }

                    float nx = x2 * 0.5f, ny = y2 * 0.5f;
                    ForEachJunctionTile(mask, JunctionArms(roadMasks, x2, y2, mask),
                        (part, dx, dy, yaw) => AddTileBoundary(canvas, items, part, nx + dx, ny + dy, yaw),
                        (dx, dy, yaw) => layout.RimCovered(nx + dx, ny + dy, yaw),
                        FilletTurnProbe(layout, nx, ny));

                    ForEachHalfStraight(mask, x2, y2, roadMasks, replacedByHalf, suppressed, (hx, hy, hyaw, hsides) =>
                        AddStraightTileBoundary(canvas, items, hx, hy, hyaw, false, hsides));
                }
            }

            foreach ((float hx, float hy, float hyaw, int hsides) in blockEdgeHalves)
                AddStraightTileBoundary(canvas, items, hx, hy, hyaw, false, hsides);

            foreach ((float fx, float fy, float fyaw, int fsides) in blockEdgeFulls)
                AddStraightTileBoundary(canvas, items, fx, fy, fyaw, true, fsides);
        }

        /// <summary>Boundary lớp Road 2 — mirror của <see cref="DrawRoad2Sprites"/>: đi CHUNG
        /// <see cref="ForEachRoad2StraightPart"/> / <see cref="ForEachRoad2JunctionTile"/> + cùng bộ
        /// suppress (highway, khối station/parking, arm ramp) nên box khớp 1:1 sprite đang vẽ và mesh sẽ
        /// bake. Không tính khối GHOST đang hover (giống collector lớp Đường) — ghost chưa phải dữ liệu.</summary>
        private void CollectDebugRoad2BoundaryItems(Rect canvas, List<DebugBoundaryItem> items)
        {
            if (_road2Edges.Count == 0) return;

            int[] hwMasks = BuildMasks(_highwayEdges);
            int[] road2Masks = BuildMasks(_road2Edges);
            // Legacy-view mask cho ramp scan + edge-fill — CHUNG với Apply. hwMasks GIỮ DENSE (xem Apply.cs).
            int[] road2MasksLegacy = BuildLegacyMasksFromEdges(_road2Edges);

            var rampSuppress = new HashSet<int>();
            List<(int x2, int y2, int stem, int hwMask)> ramps = CollectRampJunctions(hwMasks, road2MasksLegacy);
            foreach ((int rx2, int ry2, int stem, int _) in ramps)
                TryRampRoadBridge(rx2, ry2, stem, road2MasksLegacy, rampSuppress, out _, out _, out _);

            var suppressed2 = new BlockSuppression();
            var station2Roads = new List<(RoadTilePart part, float x, float y, float yaw)>();
            var blockStrips2 = new List<BlockStrip>();
            var skin2 = new BlockRoadSkin();
            var parking2Roads = new List<(RoadTilePart part, float x, float y, float yaw)>();
            CollectStationRoad2Placements(_stations, road2Masks, suppressed2, station2Roads, blockStrips2, skin2, null, false);
            CollectParkingRoad2Kerb(_parkings, road2Masks, suppressed2, parking2Roads, blockStrips2, skin2, null, false);
            ApplyApronPlain(station2Roads, skin2);
            ApplyApronPlain(parking2Roads, skin2);

            var blockEdgeHalves2 = new List<(float x, float y, float yaw, int sides)>();
            var blockEdgeFulls2 = new List<(float x, float y, float yaw, int sides)>();
            CollectBlockEdgeFills(road2MasksLegacy, blockStrips2, blockEdgeHalves2, blockEdgeFulls2);

            bool Blocked(int idx) => hwMasks[idx] != 0 || suppressed2.Blocked(idx) || rampSuppress.Contains(idx);
            RoadLayout layout = ResolveRoadLayout(_road2Edges, Blocked, Road2SideBranchReachSteps, true);
            int[] masks = layout.Masks;

            var filletKerb2 = new HashSet<long>();
            var filletTurns2 = new Dictionary<long, (float x, float y, float yaw)>();
            CollectRoad2JunctionEffects(layout, Blocked, filletKerb2, filletTurns2);
            System.Func<float, float, float, bool> rimCovered = filletKerb2.Count == 0 ? null
                : (rx, ry, ryaw) => filletKerb2.Contains(KerbCellKey(rx, ry, 0f, -0.75f, ryaw));
            System.Func<float, float, float, (float x, float y, float yaw)?> filletTurn =
                filletTurns2.Count == 0 ? null
                : (rx, ry, ryaw) => filletTurns2.TryGetValue(CurveKey(rx, ry, ryaw),
                    out (float x, float y, float yaw) turn) ? turn : ((float, float, float)?)null;

            int lw = LatticeW, lh = LatticeH;
            for (int y = 0; y < lh; y++)
            {
                for (int x = 0; x < lw; x++)
                {
                    int idx = y * lw + x;
                    int m = masks[idx];
                    if (m == 0 || Blocked(idx) || layout.Skip(idx)) continue;

                    if (IsStraightLikeMask(m))
                    {
                        (float ax, float ay, bool full) = StraightAnchorFor(layout, idx, m, x, y);
                        AddRoad2StraightTileBoundary(canvas, items, ax, ay, StraightYaw(m), full,
                            StraightSides(m) & ~suppressed2.Sides(idx), skin2, rimCovered);
                        continue;
                    }

                    float nx = x * 0.5f, ny = y * 0.5f;
                    ForEachRoad2JunctionTile(m, Road2JunctionArms(masks, x, y, m),
                        (part, dx, dy, yaw) => AddRoad2TileBoundary(canvas, items, part, nx + dx, ny + dy, yaw),
                        rimCovered == null ? null : (rx, ry, ryaw) => rimCovered(nx + rx, ny + ry, ryaw),
                        filletTurn == null ? null : (rx, ry, ryaw) =>
                        {
                            (float x, float y, float yaw)? turn = filletTurn(nx + rx, ny + ry, ryaw);
                            return turn == null ? null : (turn.Value.x - nx, turn.Value.y - ny, turn.Value.yaw);
                        },
                        skin2, nx, ny, DirAll & ~suppressed2.Sides(idx));
                }
            }

            foreach ((float hx, float hy, float hyaw, int hSides) in blockEdgeHalves2)
                AddRoad2StraightTileBoundary(canvas, items, hx, hy, hyaw, false, hSides, skin2, null);

            foreach ((float fx, float fy, float fyaw, int fSides) in blockEdgeFulls2)
                AddRoad2StraightTileBoundary(canvas, items, fx, fy, fyaw, true, fSides, skin2, null);

            ForEachRoad2ApronFiller(blockStrips2,
                (fx, fy, fyaw) => AddRoad2TileBoundary(canvas, items, Road2TilePart.Filler, fx, fy, fyaw));

            // Mảnh đường trước mặt station/parking trên lớp Road 2 = prefab road1x1_* (RoadTilePart) → dùng
            // chung AddTileBoundary, giữ màu lớp Đường như box cùng loại ở collector type-1.
            foreach ((RoadTilePart part, float rx, float ry, float ryaw) in station2Roads)
                AddTileBoundary(canvas, items, part, rx, ry, ryaw);
            foreach ((RoadTilePart part, float rx, float ry, float ryaw) in parking2Roads)
                AddTileBoundary(canvas, items, part, rx, ry, ryaw);

            AddRamp2Boundary(canvas, items, ramps);
        }

        /// <summary>Box ramp hway_to_road2 (4×4 ô) — cùng quy ước pivot + lật gương (F) với sprite trong
        /// <see cref="DrawRoad2Sprites"/>. Màu highway: ramp là mảnh nối cao tốc, cùng họ với hway_to_road.</summary>
        private void AddRamp2Boundary(
            Rect canvas, List<DebugBoundaryItem> items, List<(int x2, int y2, int stem, int hwMask)> ramps)
        {
            if (_spRampHway2 == null || _spRampHway2.rect.width <= 0f || _spRampHway2.rect.height <= 0f) return;
            string name = _library != null && _library.hway_to_road2 != null
                ? _library.hway_to_road2.name
                : _spRampHway2.name;
            float s = _cellPixelSize * 4f;
            Vector2 rectSize = _spRampHway2.rect.size;
            var piv = new Vector2(_spRampHway2.pivot.x / rectSize.x, _spRampHway2.pivot.y / rectSize.y);

            foreach ((int x2, int y2, int stem, int hwMask) in ramps)
            {
                Vector2 p = PointToPixelF(canvas, x2 * 0.5f, y2 * 0.5f);
                int turns = TurnsFromBase(DirN | DirS | DirW, hwMask | stem);
                bool flipped = RampFlipped(x2, y2);
                float a = piv.x, b = flipped ? piv.y : 1f - piv.y;
                for (int k = 0; k < (turns & 3); k++) { float na = 1f - b, nb = a; a = na; b = nb; }
                items.Add(new DebugBoundaryItem
                {
                    Rect = new Rect(p.x - a * s, p.y - b * s, s, s),
                    Name = name,
                    Color = DebugHighwayColor,
                });
            }
        }

        /// <summary>Road 2 mirror của <see cref="AddStraightTileBoundary"/>: box từng ô modular của MỘT
        /// mảnh thẳng Road 2, đi CHUNG <see cref="ForEachRoad2StraightPart"/> với bake và preview.</summary>
        private void AddRoad2StraightTileBoundary(
            Rect canvas, List<DebugBoundaryItem> items, float x, float y, float yaw, bool fullCell,
            int sides, BlockRoadSkin skin, System.Func<float, float, float, bool> rimCovered) =>
            ForEachRoad2StraightPart(x, y, yaw, fullCell, sides, skin,
                (part, px, py, pyaw) => AddRoad2TileBoundary(canvas, items, part, px, py, pyaw), rimCovered);

        /// <summary>Road 2 mirror của <see cref="AddTileBoundary"/>: box = ĐÚNG rect sprite mà
        /// <see cref="DrawRoad2TilePart"/> vẽ ra. Thiếu slice nào thì bỏ đúng ô đó (D4).</summary>
        private void AddRoad2TileBoundary(
            Rect canvas, List<DebugBoundaryItem> items, Road2TilePart part, float x, float y, float yaw)
        {
            Sprite sprite = Road2TilePartSprite(part);
            if (sprite == null || sprite.rect.width <= 0f || sprite.rect.height <= 0f) return;

            Rect rect = part == Road2TilePart.Center
                ? SpriteCellsRect(canvas, sprite, x, y, 0)
                : TileSpriteRect(PointToPixelF(canvas, x, y), _cellPixelSize, sprite,
                    Mathf.RoundToInt(yaw / 90f) + Road2TilePartBaseTurns(part));

            GameObject prefab = _library != null ? Road2JunctionTilePrefab(part) : null;
            items.Add(new DebugBoundaryItem
            {
                Rect = rect,
                Name = prefab != null ? prefab.name : sprite.name,
                Color = IsRoad2RimPart(part) ? DebugRoad2RimColor : DebugRoad2Color,
            });
        }

        private static bool IsRoad2RimPart(Road2TilePart part) =>
            part is Road2TilePart.SideRim or Road2TilePart.CurveRim or Road2TilePart.Turn3x3Rim
                or Road2TilePart.Turn1x1Rim;

        /// <summary>Box từng ô modular của MỘT mảnh thẳng (0.5×1 ô, hoặc 1×1 khi
        /// <paramref name="fullCell"/>) — cùng bộ ô mà <see cref="DrawStraightTiles"/> vẽ, mỗi vị trí
        /// ra 2 box: lòng đường (side) và vỉa hè (side_rim).</summary>
        private void AddStraightTileBoundary(
            Rect canvas, List<DebugBoundaryItem> items, float x, float y, float yaw, bool fullCell,
            int sides = DirAll) =>
            ForEachStraightTile(x, y, yaw, fullCell, (tx, ty, tyaw) =>
            {
                AddTileBoundary(canvas, items, RoadTilePart.Side, tx, ty, tyaw);
                AddTileBoundary(canvas, items, RoadTilePart.SideRim, tx, ty, tyaw);
            }, sides);

        /// <summary>Box của 2 ô (core + rim) tại một pivot cột cao tốc — đi CÙNG đường
        /// <see cref="TileSpriteRect"/> với <see cref="DrawHighwayColumn"/> nên khớp 1:1 sprite.</summary>
        private void AddHighwayTileBoundary(
            Rect canvas, List<DebugBoundaryItem> items, float x, float y, float yaw)
        {
            Vector2 pivot = PointToPixelF(canvas, x, y);
            int turns = Mathf.RoundToInt(yaw / 90f) + TileSpriteBaseTurns;
            void Add(Sprite sprite, Color color)
            {
                if (sprite == null || sprite.rect.width <= 0f || sprite.rect.height <= 0f) return;
                items.Add(new DebugBoundaryItem
                {
                    Rect = TileSpriteRect(pivot, _cellPixelSize, sprite, turns),
                    Name = sprite.name,
                    Color = color,
                });
            }
            Add(_spHighway, DebugHighwayColor);
            Add(_spHighwayRim, DebugHighwayRimColor);
        }

        /// <summary>Box của MỘT ô modular = ĐÚNG rect sprite mà <see cref="DrawTileSprite"/> /
        /// <see cref="DrawSpriteCells"/> vẽ ra, nên nó ôm cả phần vỉa hè chìa NGOÀI ô logic (khung logic
        /// <see cref="TryTileLocalRect"/> bỏ rim nên không dùng được ở đây).</summary>
        private void AddTileBoundary(
            Rect canvas, List<DebugBoundaryItem> items, RoadTilePart part, float x, float y, float yaw)
        {
            Sprite sprite = TilePartSprite(part);
            if (sprite == null || sprite.rect.width <= 0f || sprite.rect.height <= 0f) return;

            // Center canh TÂM ô (DrawJunctionTiles đi qua DrawSpriteCells), các ô còn lại neo pivot.
            Rect rect = part == RoadTilePart.Center
                ? SpriteCellsRect(canvas, sprite, x, y, 0)
                : TileSpriteRect(PointToPixelF(canvas, x, y), _cellPixelSize, sprite,
                    Mathf.RoundToInt(yaw / 90f) + TilePartBaseTurns(part));

            items.Add(new DebugBoundaryItem
            {
                Rect = rect,
                Name = TilePartName(part, sprite),
                Color = IsRimPart(part) ? DebugRoadRimColor : DebugRoadColor,
            });
        }

        private Sprite TilePartSprite(RoadTilePart part) => part switch
        {
            RoadTilePart.Side => _spTileSide,
            RoadTilePart.SideRim => _spTileSideRim,
            RoadTilePart.Curve => _spTileCurve,
            RoadTilePart.CurveRim => _spTileCurveRim,
            RoadTilePart.Turn2x2 => _spTileTurn,
            RoadTilePart.Turn2x2Rim => _spTileTurnRim,
            RoadTilePart.Turn1x1 => _spTileTurn1x1,
            RoadTilePart.Turn1x1Rim => _spTileTurn1x1Rim,
            _ => _spTileCenter,
        };

        private static int TilePartBaseTurns(RoadTilePart part) =>
            part is RoadTilePart.Curve or RoadTilePart.CurveRim
                ? CurveSpriteBaseTurns
                : TileSpriteBaseTurns;

        private static bool IsRimPart(RoadTilePart part) =>
            part is RoadTilePart.SideRim or RoadTilePart.CurveRim or RoadTilePart.Turn2x2Rim
                or RoadTilePart.Turn1x1Rim;

        /// <summary>Tên prefab ô modular cho tooltip; chưa gán Part Library thì lấy tên slice sprite.</summary>
        private string TilePartName(RoadTilePart part, Sprite sprite)
        {
            GameObject prefab = _library != null ? JunctionTilePrefab(part) : null;
            return prefab != null ? prefab.name : sprite.name;
        }

        /// <summary>Boundary lớp PATH — đi CHUNG <see cref="ForEachPathNode"/> với bake và preview (SSI)
        /// nên box khớp 1:1 sprite. Không có block/ramp/skin (D6).</summary>
        private void CollectDebugPathBoundaryItems(Rect canvas, List<DebugBoundaryItem> items)
        {
            if (_pathEdges.Count == 0) return;

            ForEachPathNode(_pathEdges, (part, x, y, yaw) =>
                AddPathTileBoundary(canvas, items, part, x, y, yaw));
        }

        /// <summary>Box = ĐÚNG rect sprite mà <see cref="DrawPathSprites"/> vẽ ra — thiếu slice thì bỏ
        /// đúng ô đó (D4). Name = tên slot để tooltip trùng library field user cần fill.</summary>
        private void AddPathTileBoundary(
            Rect canvas, List<DebugBoundaryItem> items,
            PathTilePart part, float x, float y, float yaw)
        {
            Sprite sprite = PathTilePartSprite(part);
            if (sprite == null || sprite.rect.width <= 0f || sprite.rect.height <= 0f) return;

            Rect rect = part == PathTilePart.Center
                ? SpriteCellsRect(canvas, sprite, x, y, 0)
                : TileSpriteRect(PointToPixelF(canvas, x, y), _cellPixelSize, sprite,
                    Mathf.RoundToInt(yaw / 90f) + PathTilePartBaseTurns(part));

            string name = part switch
            {
                PathTilePart.Side => "path_side",
                PathTilePart.Center => "path_center",
                PathTilePart.Curve => "path_curve",
                PathTilePart.Turn => "path_turn",
                _ => "path",
            };

            items.Add(new DebugBoundaryItem
            {
                Rect = rect,
                Name = name,
                Color = DebugPathColor,
            });
        }
    }
}
#endif
