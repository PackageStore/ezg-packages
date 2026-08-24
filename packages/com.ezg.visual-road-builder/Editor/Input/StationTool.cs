#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Place/drag/erase/rotate station & parking blocks.</summary>
    internal sealed class StationTool : IPaintTool
    {
        private readonly ToolContext _ctx;

        internal StationTool(ToolContext ctx) => _ctx = ctx;

        public bool HandleInput(Rect canvas, Event e)
        {
            var view = _ctx.View;
            var doc = _ctx.Doc;
            switch (e.type)
            {
                case EventType.MouseMove:
                    if (canvas.Contains(e.mousePosition))
                    {
                        Vector2Int k = view.BlockKindSize;
                        view.HasHover = true;
                        view.HoverAnchor = BlockCodec.ClampBlockAnchor(
                            AnchorFromMouseFor(canvas, e.mousePosition, k.x, k.y, doc, view), k.x, k.y,
                            doc.GridWidth, doc.GridHeight);
                        _ctx.Host.Repaint();
                    }
                    else if (view.HasHover)
                    {
                        view.HasHover = false;
                        _ctx.Host.Repaint();
                    }
                    break;

                case EventType.MouseDown when e.button == 0 && canvas.Contains(e.mousePosition):
                {
                    Vector2 f = CoordHelper.MouseToGridF(canvas, e.mousePosition, doc, view);
                    int hitS = FindStationAt(f, doc);
                    if (hitS >= 0)
                    {
                        view.DraggingStation = hitS;
                    }
                    else
                    {
                        int hitP = FindParkingAt(f, doc);
                        if (hitP >= 0)
                        {
                            view.DraggingParking = hitP;
                        }
                        else
                        {
                            Vector2Int k = view.BlockKindSize;
                            Vector2Int anchor = BlockCodec.ClampBlockAnchor(
                                AnchorFromMouseFor(canvas, e.mousePosition, k.x, k.y, doc, view), k.x, k.y,
                                doc.GridWidth, doc.GridHeight);
                            if (view.BlockKind == 0)
                            {
                                doc.Stations.Add(BlockCodec.EncodeStation(anchor, 0));
                                view.DraggingStation = doc.Stations.Count - 1;
                            }
                            else
                            {
                                doc.Parkings.Add(BlockCodec.EncodeParking(anchor, view.BlockKind == 2 ? 1 : 0));
                                view.DraggingParking = doc.Parkings.Count - 1;
                            }
                        }
                    }
                    e.Use();
                    _ctx.Host.Repaint();
                    break;
                }

                case EventType.MouseDown when e.button == 1 && canvas.Contains(e.mousePosition):
                {
                    Vector2 f = CoordHelper.MouseToGridF(canvas, e.mousePosition, doc, view);
                    int hitS = FindStationAt(f, doc);
                    if (hitS >= 0)
                    {
                        doc.Stations.RemoveAt(hitS);
                    }
                    else
                    {
                        int hitP = FindParkingAt(f, doc);
                        if (hitP >= 0) doc.Parkings.RemoveAt(hitP);
                    }
                    e.Use();
                    _ctx.Host.Repaint();
                    break;
                }

                case EventType.MouseDrag when view.DraggingStation >= 0:
                {
                    int s = GridConst.StationSize;
                    BlockCodec.DecodeStation(doc.Stations[view.DraggingStation], out _, out _, out int rot);
                    Vector2Int a = BlockCodec.ClampBlockAnchor(
                        AnchorFromMouseFor(canvas, e.mousePosition, s, s, doc, view), s, s,
                        doc.GridWidth, doc.GridHeight);
                    doc.Stations[view.DraggingStation] = BlockCodec.EncodeStation(a, rot);
                    e.Use();
                    _ctx.Host.Repaint();
                    break;
                }

                case EventType.MouseDrag when view.DraggingParking >= 0:
                {
                    BlockCodec.DecodeParking(doc.Parkings[view.DraggingParking], out _, out _, out int orient);
                    Vector2Int k = GridConst.ParkingCells(orient);
                    doc.Parkings[view.DraggingParking] = BlockCodec.EncodeParking(BlockCodec.ClampBlockAnchor(
                        AnchorFromMouseFor(canvas, e.mousePosition, k.x, k.y, doc, view), k.x, k.y,
                        doc.GridWidth, doc.GridHeight), orient);
                    e.Use();
                    _ctx.Host.Repaint();
                    break;
                }

                case EventType.MouseUp when view.DraggingStation >= 0 || view.DraggingParking >= 0:
                    view.DraggingStation = -1;
                    view.DraggingParking = -1;
                    e.Use();
                    _ctx.Host.Repaint();
                    break;

                // R: xoay khối dưới chuột (hoặc đang kéo) — station xoay 4 hướng mặt,
                // parking đảo ngang/dọc quanh anchor.
                case EventType.KeyDown when e.keyCode == KeyCode.R:
                {
                    Vector2 f = CoordHelper.MouseToGridF(canvas, e.mousePosition, doc, view);
                    int idxS = view.DraggingStation >= 0 ? view.DraggingStation : FindStationAt(f, doc);
                    if (idxS >= 0)
                    {
                        BlockCodec.DecodeStation(doc.Stations[idxS], out int x2, out int y2, out int rot);
                        doc.Stations[idxS] = BlockCodec.EncodeStation(new Vector2Int(x2, y2), (rot + 1) & 3);
                        e.Use();
                        _ctx.Host.Repaint();
                        return true;
                    }

                    int idxP = view.DraggingParking >= 0 ? view.DraggingParking : FindParkingAt(f, doc);
                    if (idxP >= 0)
                    {
                        BlockCodec.DecodeParking(doc.Parkings[idxP], out int px2, out int py2, out int rotP);
                        int next = (rotP + 1) & 3;
                        Vector2Int k = GridConst.ParkingCells(next);
                        doc.Parkings[idxP] = BlockCodec.EncodeParking(
                            BlockCodec.ClampBlockAnchor(new Vector2Int(px2, py2), k.x, k.y,
                                doc.GridWidth, doc.GridHeight), next);
                        e.Use();
                        _ctx.Host.Repaint();
                        return true;
                    }
                    break;
                }

                default:
                    return false;
            }
            return true;
        }

        public void DrawOverlay(Rect canvas) { }

        public void Cancel()
        {
            _ctx.View.DraggingStation = -1;
            _ctx.View.DraggingParking = -1;
            _ctx.View.HasHover = false;
        }

        private static Vector2Int AnchorFromMouseFor(Rect canvas, Vector2 mouse, int w, int h,
            RoadCanvasDoc doc, ViewState view)
        {
            Vector2 f = CoordHelper.MouseToGridF(canvas, mouse, doc, view);
            return new Vector2Int(
                Mathf.RoundToInt((f.x - w * 0.5f) * 2f),
                Mathf.RoundToInt((f.y - h * 0.5f) * 2f));
        }

        private static int FindStationAt(Vector2 f, RoadCanvasDoc doc)
        {
            int size2 = GridConst.StationSize * 2;
            float px2 = f.x * 2f, py2 = f.y * 2f;
            for (int i = doc.Stations.Count - 1; i >= 0; i--)
            {
                BlockCodec.DecodeStation(doc.Stations[i], out int x2, out int y2, out _);
                if (px2 >= x2 && px2 <= x2 + size2 && py2 >= y2 && py2 <= y2 + size2) return i;
            }
            return -1;
        }

        private static int FindParkingAt(Vector2 f, RoadCanvasDoc doc)
        {
            float px2 = f.x * 2f, py2 = f.y * 2f;
            for (int i = doc.Parkings.Count - 1; i >= 0; i--)
            {
                BlockCodec.DecodeParking(doc.Parkings[i], out int x2, out int y2, out int orient);
                Vector2Int k = GridConst.ParkingCells(orient);
                if (px2 >= x2 && px2 <= x2 + k.x * 2 && py2 >= y2 && py2 <= y2 + k.y * 2) return i;
            }
            return -1;
        }
    }
}
#endif
