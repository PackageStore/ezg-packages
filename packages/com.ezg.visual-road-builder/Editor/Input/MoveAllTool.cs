#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Mode "Di chuyen tat ca": keo chuot trai de dich TOAN BO layout (edge + station +
    /// parking + decor) theo buoc 1/2 o, kep trong bien luoi.
    /// BUG PRESERVED: OffsetAll never shifts _road2Edges — no Road 2 handling.</summary>
    internal sealed class MoveAllTool : IPaintTool
    {
        private readonly ToolContext _ctx;

        internal MoveAllTool(ToolContext ctx) { _ctx = ctx; }

        public bool HandleInput(Rect canvas, Event e)
        {
            var view = _ctx.View;
            if (!view.MoveAllMode) return false;

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && canvas.Contains(e.mousePosition):
                    view.MovingAll = true;
                    view.MovePoint = SelectMoveGeometry.PixelToHalfPointRaw(canvas, e.mousePosition, _ctx.Doc, _ctx.View);
                    e.Use();
                    return true;

                case EventType.MouseDrag when view.MovingAll:
                {
                    Vector2Int cur = SelectMoveGeometry.PixelToHalfPointRaw(canvas, e.mousePosition, _ctx.Doc, _ctx.View);
                    Vector2Int delta = ClampMoveDelta(cur - view.MovePoint);
                    if (delta != Vector2Int.zero)
                    {
                        OffsetAll(delta);
                        view.MovePoint += delta;
                    }
                    e.Use();
                    _ctx.Host.Repaint();
                    return true;
                }

                case EventType.MouseUp when view.MovingAll:
                    view.MovingAll = false;
                    e.Use();
                    _ctx.Host.Repaint();
                    return true;
            }
            return false;
        }

        public void DrawOverlay(Rect canvas) { }
        public void Cancel() { _ctx.View.MovingAll = false; }

        private Vector2Int ClampMoveDelta(Vector2Int d)
        {
            var doc = _ctx.Doc;
            int dxMin = int.MinValue, dxMax = int.MaxValue;
            int dyMin = int.MinValue, dyMax = int.MaxValue;
            bool any = false;

            void FitEdges(List<int> edges)
            {
                int gx2Max = (doc.GridWidth - 1) * 2, gy2Max = (doc.GridHeight - 1) * 2;
                foreach (int id in edges)
                {
                    EdgeCodec.DecodeEdge(id, out int x2, out int y2, out int orient);
                    int xe2 = orient == 0 ? x2 + 1 : x2;
                    int ye2 = orient == 1 ? y2 + 1 : y2;
                    any = true;
                    dxMin = Math.Max(dxMin, -x2);
                    dxMax = Math.Min(dxMax, gx2Max - xe2);
                    dyMin = Math.Max(dyMin, -y2);
                    dyMax = Math.Min(dyMax, gy2Max - ye2);
                }
            }

            FitEdges(doc.Edges);
            FitEdges(doc.HighwayEdges);
            FitEdges(doc.HwDecorEdges);
            FitEdges(doc.PathEdges);

            int size = GridConst.StationSize;
            int maxX2 = (doc.GridWidth - 1 - size) * 2;
            int maxY2 = (doc.GridHeight - 1 - size) * 2;
            foreach (int id in doc.Stations)
            {
                BlockCodec.DecodeStation(id, out int ax2, out int ay2, out _);
                any = true;
                dxMin = Math.Max(dxMin, -ax2);
                dxMax = Math.Min(dxMax, maxX2 - ax2);
                dyMin = Math.Max(dyMin, -ay2);
                dyMax = Math.Min(dyMax, maxY2 - ay2);
            }
            foreach (int id in doc.Stations2)
            {
                BlockCodec.DecodeStation(id, out int ax2, out int ay2, out _);
                any = true;
                dxMin = Math.Max(dxMin, -ax2);
                dxMax = Math.Min(dxMax, maxX2 - ax2);
                dyMin = Math.Max(dyMin, -ay2);
                dyMax = Math.Min(dyMax, maxY2 - ay2);
            }

            foreach (int id in doc.Parkings)
            {
                BlockCodec.DecodeParking(id, out int ax2, out int ay2, out int orient);
                Vector2Int k = GridConst.ParkingCells(orient);
                any = true;
                int maxPX2 = (doc.GridWidth - 1 - k.x) * 2;
                int maxPY2 = (doc.GridHeight - 1 - k.y) * 2;
                dxMin = Math.Max(dxMin, -ax2);
                dxMax = Math.Min(dxMax, maxPX2 - ax2);
                dyMin = Math.Max(dyMin, -ay2);
                dyMax = Math.Min(dyMax, maxPY2 - ay2);
            }

            int maxDX2 = (doc.GridWidth - 1) * 2, maxDY2 = (doc.GridHeight - 1) * 2;
            foreach (DecorItem item in doc.Decors)
            {
                any = true;
                dxMin = Math.Max(dxMin, -item.x2);
                dxMax = Math.Min(dxMax, maxDX2 - item.x2);
                dyMin = Math.Max(dyMin, -item.y2);
                dyMax = Math.Min(dyMax, maxDY2 - item.y2);
            }

            if (!any) return Vector2Int.zero;
            return new Vector2Int(Mathf.Clamp(d.x, dxMin, dxMax), Mathf.Clamp(d.y, dyMin, dyMax));
        }

        // BUG PRESERVED: OffsetAll never shifts doc.Road2Edges — no Road 2 handling.
        internal void OffsetAll(Vector2Int d)
        {
            var doc = _ctx.Doc;

            static void ShiftEdges(List<int> edges, Vector2Int delta)
            {
                for (int i = 0; i < edges.Count; i++)
                    edges[i] = SelectMoveGeometry.ShiftEdgeId(edges[i], delta);
            }

            ShiftEdges(doc.Edges, d);
            ShiftEdges(doc.HighwayEdges, d);
            ShiftEdges(doc.HwDecorEdges, d);
            ShiftEdges(doc.PathEdges, d);

            for (int i = 0; i < doc.Stations.Count; i++)
            {
                BlockCodec.DecodeStation(doc.Stations[i], out int x2, out int y2, out int rot);
                doc.Stations[i] = BlockCodec.EncodeStation(new Vector2Int(x2 + d.x, y2 + d.y), rot);
            }
            for (int i = 0; i < doc.Stations2.Count; i++)
            {
                BlockCodec.DecodeStation(doc.Stations2[i], out int x2, out int y2, out int rot);
                doc.Stations2[i] = BlockCodec.EncodeStation(new Vector2Int(x2 + d.x, y2 + d.y), rot);
            }

            for (int i = 0; i < doc.Parkings.Count; i++)
            {
                BlockCodec.DecodeParking(doc.Parkings[i], out int x2, out int y2, out int orient);
                doc.Parkings[i] = BlockCodec.EncodeParking(new Vector2Int(x2 + d.x, y2 + d.y), orient);
            }

            for (int i = 0; i < doc.Decors.Count; i++)
            {
                DecorItem item = doc.Decors[i];
                item.x2 += d.x;
                item.y2 += d.y;
                doc.Decors[i] = item;
            }

            for (int i = 0; i < doc.RampFlips.Count; i++)
            {
                EdgeCodec.DecodeRampAnchor(doc.RampFlips[i], out int x2, out int y2);
                doc.RampFlips[i] = EdgeCodec.RampAnchorKey(x2 + d.x, y2 + d.y);
            }
            doc.RampFlips.Sort();
        }
    }
}
#endif
