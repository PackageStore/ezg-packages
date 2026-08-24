#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Track hover cell from mouse-move and ramp-flip under cursor.</summary>
    internal sealed class HoverTracker
    {
        private readonly ToolContext _ctx;
        private readonly Func<bool> _hoverWouldOverlap;
        private readonly Action<bool> _setOverlapHint;

        internal HoverTracker(ToolContext ctx, Func<bool> hoverWouldOverlap, Action<bool> setOverlapHint)
        {
            _ctx = ctx;
            _hoverWouldOverlap = hoverWouldOverlap;
            _setOverlapHint = setOverlapHint;
        }

        /// <summary>Cập nhật ô lưới dưới con trỏ (mọi mode) cho ô readout; repaint khi rê.</summary>
        internal void TrackHoverCell(Rect canvas)
        {
            Event e = Event.current;
            var view = _ctx.View;
            var doc = _ctx.Doc;
            if (e.type != EventType.MouseMove && e.type != EventType.MouseDrag) return;
            if (canvas.Contains(e.mousePosition))
            {
                view.HoverCell = CoordHelper.MouseToGridF(canvas, e.mousePosition, doc, view);
                view.HoverPixel = e.mousePosition;
                view.HoverCellValid = true;
                if (!view.Dragging) _setOverlapHint(_hoverWouldOverlap());
                _ctx.Host.Repaint();
            }
            else if (view.HoverCellValid)
            {
                view.HoverCellValid = false;
                _setOverlapHint(false);
                _ctx.Host.Repaint();
            }
        }

        /// <summary>Phím F: lật hướng ramp Highway→Road GẦN con trỏ nhất (toggle, nhớ theo từng ramp trong
        /// RampFlips). Ramp neo ở đường tâm highway; nhận ramp có anchor trong bán kính ~ nửa
        /// mesh (2 ô) quanh hover. Trả true khi có ramp để lật.</summary>
        internal bool TryToggleRampFlipUnderCursor(
            Func<int[]> buildHwMasks, Func<int[]> buildRoadLegacyMasks,
            Func<int[], int[], List<(int x2, int y2, int stem, int hwMask)>> collectRampJunctions,
            float rampHalfWidthCells, Func<int, int, int> rampAnchorKey)
        {
            var view = _ctx.View;
            var doc = _ctx.Doc;
            if (!view.HoverCellValid || doc.HighwayEdges.Count == 0 || doc.Edges.Count == 0) return false;

            int[] hwMasks = buildHwMasks();
            int[] roadMasks = buildRoadLegacyMasks();
            var ramps = collectRampJunctions(hwMasks, roadMasks);
            if (ramps.Count == 0) return false;

            float hx2 = view.HoverCell.x * 2f, hy2 = view.HoverCell.y * 2f;
            int best = -1;
            float bestSq = float.MaxValue;
            for (int i = 0; i < ramps.Count; i++)
            {
                float dx = ramps[i].x2 - hx2, dy = ramps[i].y2 - hy2;
                float sq = dx * dx + dy * dy;
                if (sq < bestSq) { bestSq = sq; best = i; }
            }

            float reach = rampHalfWidthCells * 2f + 2f;
            if (best < 0 || bestSq > reach * reach) return false;

            int key = rampAnchorKey(ramps[best].x2, ramps[best].y2);
            if (!doc.RampFlips.Remove(key)) doc.RampFlips.Add(key);
            doc.RampFlips.Sort();
            return true;
        }
    }
}
#endif
