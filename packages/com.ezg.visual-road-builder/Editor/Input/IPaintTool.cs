#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Contract for canvas mouse/key tools — each tool handles one
    /// interaction mode (road paint, eraser, station, decor, select, etc.).</summary>
    internal interface IPaintTool
    {
        /// <summary>Handle mouse/key events on the canvas. Return true if the
        /// event was consumed (caller skips remaining tools).</summary>
        bool HandleInput(Rect canvas, Event e);

        /// <summary>Draw overlays during Repaint (cursor, ghost, selection box,
        /// gizmo). Called AFTER the main canvas content is drawn.</summary>
        void DrawOverlay(Rect canvas);

        /// <summary>Reset all drag/interaction state (e.g. when switching tool
        /// or pressing Esc at window level).</summary>
        void Cancel();
    }
}
#endif
