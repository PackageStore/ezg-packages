#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Middle-mouse canvas pan — always runs first in the dispatch chain,
    /// returns early if not panning.</summary>
    internal sealed class PanTool : IPaintTool
    {
        private readonly ToolContext _ctx;

        internal PanTool(ToolContext ctx) => _ctx = ctx;

        public bool HandleInput(Rect canvas, Event e)
        {
            var view = _ctx.View;
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 2 && canvas.Contains(e.mousePosition):
                    view.Panning = true;
                    e.Use();
                    return true;
                case EventType.MouseDrag when view.Panning:
                    view.Scroll -= e.delta;
                    e.Use();
                    _ctx.Host.Repaint();
                    return true;
                case EventType.MouseUp when view.Panning && e.button == 2:
                    view.Panning = false;
                    e.Use();
                    return true;
            }
            return false;
        }

        public void DrawOverlay(Rect canvas) { }
        public void Cancel() => _ctx.View.Panning = false;
    }
}
#endif
