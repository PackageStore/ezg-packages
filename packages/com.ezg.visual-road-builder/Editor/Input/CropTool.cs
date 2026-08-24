#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Drag 8 handles to resize canvas.</summary>
    internal sealed class CropTool : IPaintTool
    {
        private readonly ToolContext _ctx;
        private readonly CropOverlayRenderer _overlay;

        internal CropTool(ToolContext ctx, CropOverlayRenderer overlay)
        {
            _ctx = ctx;
            _overlay = overlay;
        }

        public bool HandleInput(Rect canvas, Event e)
        {
            var view = _ctx.View;
            var doc = _ctx.Doc;
            Rect[] handles = _overlay.GetCropHandleRects(canvas, out Rect _);

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:
                    for (int i = 0; i < handles.Length; i++)
                    {
                        if (handles[i].Contains(e.mousePosition))
                        {
                            view.CropDragHandle = i;
                            view.CropDragStartMouse = e.mousePosition;
                            e.Use();
                            _ctx.Host.Repaint();
                            return true;
                        }
                    }
                    break;

                case EventType.MouseDrag when view.CropDragHandle >= 0:
                    Vector2 diff = e.mousePosition - view.CropDragStartMouse;
                    int cellDx = Mathf.RoundToInt(diff.x / view.CellPixelSize);
                    int cellDy = Mathf.RoundToInt(diff.y / view.CellPixelSize);

                    int dL = 0, dR = 0, dU = 0, dD = 0;

                    if (view.CropDragHandle == 0 || view.CropDragHandle == 6 || view.CropDragHandle == 7)
                        dL = -cellDx;
                    else if (view.CropDragHandle == 2 || view.CropDragHandle == 3 || view.CropDragHandle == 4)
                        dR = cellDx;

                    if (view.CropDragHandle == 0 || view.CropDragHandle == 1 || view.CropDragHandle == 2)
                        dU = -cellDy;
                    else if (view.CropDragHandle == 4 || view.CropDragHandle == 5 || view.CropDragHandle == 6)
                        dD = cellDy;

                    dL = Mathf.Clamp(dL, 2 - doc.GridWidth, GridConst.MaxGridSize - doc.GridWidth);
                    dR = Mathf.Clamp(dR, 2 - doc.GridWidth, GridConst.MaxGridSize - doc.GridWidth);
                    dD = Mathf.Clamp(dD, 2 - doc.GridHeight, GridConst.MaxGridSize - doc.GridHeight);
                    dU = Mathf.Clamp(dU, 2 - doc.GridHeight, GridConst.MaxGridSize - doc.GridHeight);

                    view.CropDeltaLeft = dL;
                    view.CropDeltaRight = dR;
                    view.CropDeltaDown = dD;
                    view.CropDeltaUp = dU;

                    e.Use();
                    _ctx.Host.Repaint();
                    return true;

                case EventType.MouseUp when view.CropDragHandle >= 0 && e.button == 0:
                    CropApplied = view.CropDeltaLeft != 0 || view.CropDeltaDown != 0
                                  || view.CropDeltaRight != 0 || view.CropDeltaUp != 0;
                    view.CropDragHandle = -1;
                    e.Use();
                    _ctx.Host.Repaint();
                    return true;
            }
            return false;
        }

        /// <summary>Set after MouseUp when crop deltas are non-zero — caller reads and applies ExpandGrid.</summary>
        internal bool CropApplied { get; set; }

        public void DrawOverlay(Rect canvas) { }

        public void Cancel()
        {
            _ctx.View.CropDragHandle = -1;
            _ctx.View.CropDeltaLeft = _ctx.View.CropDeltaDown = _ctx.View.CropDeltaRight = _ctx.View.CropDeltaUp = 0;
        }
    }
}
#endif
