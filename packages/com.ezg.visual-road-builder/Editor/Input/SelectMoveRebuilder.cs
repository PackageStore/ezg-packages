#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Rebuild live data from frozen selection snapshot after each drag step.</summary>
    internal sealed class SelectMoveRebuilder
    {
        private readonly ToolContext _ctx;
        private readonly SelectMoveTool _sel;
        private readonly Func<bool> _hasAnyOverlap;

        internal SelectMoveRebuilder(ToolContext ctx, SelectMoveTool sel, Func<bool> hasAnyOverlap)
        {
            _ctx = ctx;
            _sel = sel;
            _hasAnyOverlap = hasAnyOverlap;
        }

        /// <summary>Dựng lại MỌI lớp dữ liệu = phần đứng yên + phần chọn đã dịch <see cref="SelectMoveTool.Delta"/>.
        /// Riêng Road thêm các đoạn cầu nối (từ anchor GỐC tới anchor đã dịch). Chạy mỗi bước kéo nên
        /// idempotent (luôn dựng lại từ ảnh chụp gốc).</summary>
        internal void RebuildSelection()
        {
            var doc = _ctx.Doc;
            Vector2Int d = _sel.Delta;

            // --- Road: stationary + shifted + bridge.
            doc.Edges.Clear();
            var set = new HashSet<int>();
            foreach (int id in _sel.Stationary)
                if (set.Add(id)) doc.Edges.Add(id);
            foreach (int id in _sel.EdgesOrig)
            {
                int shifted = SelectMoveGeometry.ShiftEdgeId(id, d);
                if (set.Add(shifted)) doc.Edges.Add(shifted);
            }
            foreach (Vector2Int anchor in _sel.AnchorsOrig)
                AddBridgePath(anchor, anchor + d, set, doc);

            // --- Highway + hw-decor + Road 2 + PATH: dịch cứng.
            RebuildEdgeLayer(doc.HighwayEdges, _sel.HwStat, _sel.HwOrig, d);
            RebuildEdgeLayer(doc.HwDecorEdges, _sel.HwDecStat, _sel.HwDecOrig, d);
            RebuildEdgeLayer(doc.Road2Edges, _sel.Road2Stat, _sel.Road2Orig, d);
            RebuildEdgeLayer(doc.PathEdges, _sel.PathStat, _sel.PathOrig, d);

            // --- Station.
            doc.Stations.Clear();
            doc.Stations.AddRange(_sel.StationsStat);
            foreach (int id in _sel.StationsOrig)
            {
                BlockCodec.DecodeStation(id, out int x2, out int y2, out int rot);
                doc.Stations.Add(BlockCodec.EncodeStation(new Vector2Int(x2 + d.x, y2 + d.y), rot));
            }

            // --- Parking.
            doc.Parkings.Clear();
            doc.Parkings.AddRange(_sel.ParkingsStat);
            foreach (int id in _sel.ParkingsOrig)
            {
                BlockCodec.DecodeParking(id, out int x2, out int y2, out int orient);
                doc.Parkings.Add(BlockCodec.EncodeParking(new Vector2Int(x2 + d.x, y2 + d.y), orient));
            }

            // --- Decor.
            doc.Decors.Clear();
            doc.Decors.AddRange(_sel.DecorsStat);
            foreach (DecorItem item in _sel.DecorsOrig)
            {
                DecorItem moved = item;
                moved.x2 += d.x;
                moved.y2 += d.y;
                doc.Decors.Add(moved);
            }

            // --- Cờ lật ramp (giữ SẮP XẾP để signature undo ổn định).
            doc.RampFlips.Clear();
            doc.RampFlips.AddRange(_sel.RampsStat);
            foreach (int key in _sel.RampsOrig)
            {
                EdgeCodec.DecodeRampAnchor(key, out int x2, out int y2);
                doc.RampFlips.Add(EdgeCodec.RampAnchorKey(x2 + d.x, y2 + d.y));
            }
            doc.RampFlips.Sort();

            _sel.OverlapHint = doc.Edges.Count > 0 && _hasAnyOverlap();
        }

        /// <summary>Dựng lại 1 list edge = phần đứng yên + phần chọn dịch cứng.</summary>
        private static void RebuildEdgeLayer(List<int> target, List<int> stationary,
            List<int> selected, Vector2Int d)
        {
            target.Clear();
            var set = new HashSet<int>();
            foreach (int id in stationary)
                if (set.Add(id)) target.Add(id);
            foreach (int id in selected)
            {
                int shifted = SelectMoveGeometry.ShiftEdgeId(id, d);
                if (set.Add(shifted)) target.Add(shifted);
            }
        }

        // PATH không tham gia AddBridgePath — không có cross-layer contract, connector tổng hợp sẽ tạo
        // hình học mà user chưa bao giờ vẽ.
        /// <summary>Bắc cầu 2 điểm lattice bằng đoạn thẳng bẻ góc chữ L (ưu tiên trục X trước), mỗi bước
        /// 1 nấc lattice = edge dài nửa ô. Thêm edge chưa có vào <see cref="RoadCanvasDoc.Edges"/>.</summary>
        private static void AddBridgePath(Vector2Int from2, Vector2Int to2, HashSet<int> set, RoadCanvasDoc doc)
        {
            Vector2Int cur = from2;
            int guard = 0;
            while (cur != to2 && guard++ < 4096)
            {
                Vector2Int next = cur;
                if (cur.x != to2.x) next.x += Math.Sign(to2.x - cur.x);
                else next.y += Math.Sign(to2.y - cur.y);
                int id = EdgeCodec.EncodeEdge(cur, next);
                if (set.Add(id)) doc.Edges.Add(id);
                cur = next;
            }
        }

        /// <summary>Kẹp delta (nửa ô) sao cho MỌI vật thể đã chọn sau khi dịch vẫn nằm trong lưới.</summary>
        internal Vector2Int ClampSelDelta(Vector2Int d)
        {
            var doc = _ctx.Doc;
            int gx2Max = (doc.GridWidth - 1) * 2, gy2Max = (doc.GridHeight - 1) * 2;
            int dxMin = int.MinValue, dxMax = int.MaxValue, dyMin = int.MinValue, dyMax = int.MaxValue;
            bool any = false;

            // Điểm bao [lo,hi] theo mỗi trục của 1 vật thể → thu hẹp khoảng delta hợp lệ.
            void Fit(int loX, int hiX, int loY, int hiY)
            {
                any = true;
                dxMin = Mathf.Max(dxMin, -loX);
                dxMax = Mathf.Min(dxMax, gx2Max - hiX);
                dyMin = Mathf.Max(dyMin, -loY);
                dyMax = Mathf.Min(dyMax, gy2Max - hiY);
            }

            void FitEdges(List<int> edges)
            {
                foreach (int id in edges)
                {
                    SelectMoveGeometry.EdgeEndpoints(id, out Vector2Int a, out Vector2Int b);
                    Fit(Mathf.Min(a.x, b.x), Mathf.Max(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.y, b.y));
                }
            }

            FitEdges(_sel.EdgesOrig);
            FitEdges(_sel.HwOrig);
            FitEdges(_sel.HwDecOrig);
            FitEdges(_sel.Road2Orig);
            FitEdges(_sel.PathOrig);

            int st2 = GridConst.StationSize * 2;
            foreach (int id in _sel.StationsOrig)
            {
                BlockCodec.DecodeStation(id, out int x2, out int y2, out _);
                Fit(x2, x2 + st2, y2, y2 + st2);
            }
            foreach (int id in _sel.ParkingsOrig)
            {
                BlockCodec.DecodeParking(id, out int x2, out int y2, out int orient);
                Vector2Int k = GridConst.ParkingCells(orient);
                Fit(x2, x2 + k.x * 2, y2, y2 + k.y * 2);
            }
            foreach (DecorItem item in _sel.DecorsOrig)
                Fit(item.x2, item.x2, item.y2, item.y2);

            if (!any) return Vector2Int.zero;
            return new Vector2Int(
                Mathf.Clamp(d.x, dxMin, dxMax),
                Mathf.Clamp(d.y, dyMin, dyMax));
        }
    }
}
#endif