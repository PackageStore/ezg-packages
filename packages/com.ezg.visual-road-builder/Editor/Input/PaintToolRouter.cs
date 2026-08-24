#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Owns tool instances, dispatches HandleInput/DrawOverlay by ViewState priority;
    /// handles Ctrl+scroll zoom.</summary>
    internal sealed class PaintToolRouter
    {
        private readonly ToolContext _ctx;
        private readonly IPaintTool _pan;
        private readonly IPaintTool _crop;
        private readonly IPaintTool _eraser;
        private readonly IPaintTool _station;
        private readonly IPaintTool _decor;
        private readonly IPaintTool _roadPaint;

        // Select + MoveAll live on concurrent slice — caller injects them.
        private readonly IPaintTool _select;
        private readonly IPaintTool _moveAll;

        internal PaintToolRouter(ToolContext ctx,
            IPaintTool pan, IPaintTool crop, IPaintTool select, IPaintTool eraser,
            IPaintTool moveAll, IPaintTool station, IPaintTool decor, IPaintTool roadPaint)
        {
            _ctx = ctx;
            _pan = pan;
            _crop = crop;
            _select = select;
            _eraser = eraser;
            _moveAll = moveAll;
            _station = station;
            _decor = decor;
            _roadPaint = roadPaint;
        }

        internal void HandleInput(Rect canvas, HoverTracker hoverTracker)
        {
            hoverTracker.TrackHoverCell(canvas);

            if (_pan.HandleInput(canvas, Event.current)) return;

            Event e = Event.current;
            if (e.type == EventType.ScrollWheel && (e.control || e.command)
                && canvas.Contains(e.mousePosition))
            {
                _ctx.View.CellPixelSize = Mathf.Clamp(
                    _ctx.View.CellPixelSize - e.delta.y * 0.5f, 10f, 48f);
                e.Use();
                _ctx.Host.Repaint();
                return;
            }

            IPaintTool active = ActiveTool();
            if (active != null) active.HandleInput(canvas, e);
        }

        internal void DrawOverlays(Rect canvas)
        {
            _pan.DrawOverlay(canvas);
            IPaintTool active = ActiveTool();
            if (active != null) active.DrawOverlay(canvas);
        }

        private IPaintTool ActiveTool()
        {
            var view = _ctx.View;
            if (view.CropMode) return _crop;
            if (_select != null)
            {
                // SelectMode lives on concurrent slice's SelectMoveTool
                // Check via the tool itself — if it's active, dispatch to it
            }
            if (view.EraserMode) return _eraser;
            if (view.MoveAllMode) return _moveAll;
            if (view.Mode == PaintMode.Station) return _station;
            if (view.Mode == PaintMode.Decor) return _decor;
            return _roadPaint;
        }
    }
}
#endif
