#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Chup / khoi phuc trang thai canvas cho undo history va tinh signature (hash) de phat
    /// hien thay doi. 14-field set identical to CanvasState and CanvasSaveIO round-trip.</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        private CanvasState CaptureState() => new()
        {
            width = _doc.GridWidth,
            height = _doc.GridHeight,
            cellWorldSize = GridConst.CellWorldSize,
            originCell = _doc.OriginCell,
            edges = new List<int>(_doc.Edges),
            highwayEdges = new List<int>(_doc.HighwayEdges),
            hwDecorEdges = new List<int>(_doc.HwDecorEdges),
            road2Edges = new List<int>(_doc.Road2Edges),
            pathEdges = new List<int>(_doc.PathEdges),
            stations = new List<int>(_doc.Stations),
            stations2 = new List<int>(_doc.Stations2),
            parkings = new List<int>(_doc.Parkings),
            decors = new List<DecorItem>(_doc.Decors),
            rampFlips = new List<int>(_doc.RampFlips),
        };

        private void RestoreState(CanvasState s)
        {
            _doc.GridWidth = s.width;
            _doc.GridHeight = s.height;
            _doc.OriginCell = s.originCell;
            _doc.Edges = new List<int>(s.edges);
            _doc.HighwayEdges = new List<int>(s.highwayEdges);
            _doc.HwDecorEdges = new List<int>(s.hwDecorEdges);
            _doc.Road2Edges = new List<int>(s.road2Edges ?? new List<int>());
            _doc.PathEdges = new List<int>(s.pathEdges ?? new List<int>());
            _doc.Stations = new List<int>(s.stations);
            _doc.Stations2 = new List<int>(s.stations2 ?? new List<int>());
            _doc.Parkings = new List<int>(s.parkings);
            _doc.Decors = new List<DecorItem>(s.decors);
            _doc.RampFlips = new List<int>(s.rampFlips ?? new List<int>());
        }

        private int ComputeCanvasSignature()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + _doc.GridWidth;
                h = h * 31 + _doc.GridHeight;
                h = h * 31 + GridConst.CellWorldSize.GetHashCode();
                h = h * 31 + _doc.OriginCell.GetHashCode();
                h = HashList(h, _doc.Edges);
                h = HashList(h, _doc.HighwayEdges);
                h = HashList(h, _doc.HwDecorEdges);
                h = HashList(h, _doc.Road2Edges);
                h = HashList(h, _doc.PathEdges);
                h = HashList(h, _doc.Stations);
                h = HashList(h, _doc.Stations2);
                h = HashList(h, _doc.Parkings);
                h = HashList(h, _doc.RampFlips);
                h = h * 31 + _doc.Decors.Count;
                foreach (DecorItem d in _doc.Decors)
                {
                    h = h * 31 + d.entry;
                    h = h * 31 + d.x2;
                    h = h * 31 + d.y2;
                    h = h * 31 + d.rot;
                    h = h * 31 + d.scale.GetHashCode();
                }
                return h;
            }
        }

        private static int HashList(int h, List<int> list)
        {
            unchecked
            {
                h = h * 31 + list.Count;
                foreach (int v in list) h = h * 31 + v;
                return h;
            }
        }
    }
}
#endif
