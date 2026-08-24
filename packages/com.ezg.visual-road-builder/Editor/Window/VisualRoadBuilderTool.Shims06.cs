#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    // ── SHIM LAYER 06 ────────────────────────────────────────────────────────────
    // Forwarding shims for SelectMoveTool + MoveAllTool so external callers
    // (RoadInput.cs, CanvasView.cs, EraserInput.cs, OverlapDetector.cs) keep
    // compiling against the old partial-class API. Integration deletes this file
    // once those callers migrate to the standalone tool instances.
    // ─────────────────────────────────────────────────────────────────────────────
    public sealed partial class VisualRoadBuilderTool
    {
        [SerializeField] private bool _selectMode;

        private SelectMoveTool _selectTool;
        private MoveAllTool _moveAllTool;
        private ToolStyles _sharedStyles;

        private ToolStyles EnsureStyles() => _sharedStyles ??= new ToolStyles();

        private SelectMoveTool EnsureSelectTool()
        {
            if (_selectTool != null) return _selectTool;
            var ctx = new ToolContext(_doc, _library, _view, this);
            _selectTool = new SelectMoveTool(ctx,
                () => _selectMode,
                v => _selectMode = v,
                () => ResetDecorInteraction(),
                () => HasAnyOverlap(),
                EnsureStyles());
            return _selectTool;
        }

        private MoveAllTool EnsureMoveAllTool()
        {
            if (_moveAllTool != null) return _moveAllTool;
            var ctx = new ToolContext(_doc, _library, _view, this);
            _moveAllTool = new MoveAllTool(ctx);
            return _moveAllTool;
        }

        private void HandleSelectInput(Rect canvas) =>
            EnsureSelectTool().HandleInput(canvas, Event.current);

        private void HandleMoveAllInput(Rect canvas) =>
            EnsureMoveAllTool().HandleInput(canvas, Event.current);

        private void ClearSelection() => EnsureSelectTool().ClearSelection();

        private void ToggleSelectMode() => EnsureSelectTool().ToggleSelectMode();

        private void DrawSelectOverlay(Rect canvas) =>
            EnsureSelectTool().DrawOverlay(canvas);

        private void OffsetAll(Vector2Int d) =>
            EnsureMoveAllTool().OffsetAll(d);
    }
}
#endif
