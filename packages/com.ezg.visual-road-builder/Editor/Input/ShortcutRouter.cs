#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Global keyboard handler: C/G/Q/E/F, Esc/Enter in crop/select.
    /// Called BEFORE the canvas layout in OnGUI.</summary>
    internal sealed class ShortcutRouter
    {
        private readonly ToolContext _ctx;
        private readonly Action _toggleSelectMode;
        private readonly Action _clearSelection;
        private readonly Action _toggleEraser;
        private readonly Func<bool> _tryToggleRampFlipUnderCursor;
        private readonly Action<int, int, int, int> _expandGrid;
        // SelectMode lives on SelectMove.cs (concurrent slice) — access via getter during migration.
        private readonly Func<bool> _getSelectMode;
        private readonly Action<bool> _setSelectMode;

        internal bool ShiftHeld { get; private set; }

        internal ShortcutRouter(ToolContext ctx,
            Action toggleSelectMode, Action clearSelection, Action toggleEraser,
            Func<bool> tryToggleRampFlipUnderCursor, Action<int, int, int, int> expandGrid,
            Func<bool> getSelectMode, Action<bool> setSelectMode)
        {
            _ctx = ctx;
            _toggleSelectMode = toggleSelectMode;
            _clearSelection = clearSelection;
            _toggleEraser = toggleEraser;
            _tryToggleRampFlipUnderCursor = tryToggleRampFlipUnderCursor;
            _expandGrid = expandGrid;
            _getSelectMode = getSelectMode;
            _setSelectMode = setSelectMode;
        }

        internal void HandleShortcuts()
        {
            Event e = Event.current;
            var view = _ctx.View;
            if (e.type == EventType.KeyDown && !EditorGUIUtility.editingTextField)
            {
                if (e.keyCode == KeyCode.C)
                {
                    view.CropMode = !view.CropMode;
                    view.EraserMode = false;
                    _setSelectMode(false);
                    _clearSelection();
                    view.CropDragHandle = -1;
                    view.CropDeltaLeft = view.CropDeltaDown = view.CropDeltaRight = view.CropDeltaUp = 0;
                    e.Use();
                    _ctx.Host.Repaint();
                }
                else if (e.keyCode == KeyCode.G)
                {
                    view.MoveAllMode = !view.MoveAllMode;
                    view.EraserMode = false;
                    _setSelectMode(false);
                    _clearSelection();
                    view.Dragging = false;
                    view.DraggingStation = -1;
                    view.DraggingParking = -1;
                    view.HasHover = false;
                    view.MovingAll = false;
                    e.Use();
                    _ctx.Host.Repaint();
                }
                else if (e.keyCode == KeyCode.Q)
                {
                    _toggleSelectMode();
                    e.Use();
                    _ctx.Host.Repaint();
                }
                else if (e.keyCode == KeyCode.E)
                {
                    _toggleEraser();
                    e.Use();
                    _ctx.Host.Repaint();
                }
                else if (e.keyCode == KeyCode.F)
                {
                    // Lật hướng ramp Highway→Road dưới con trỏ (toggle). Chỉ tiêu thụ phím khi trúng ramp.
                    if (_tryToggleRampFlipUnderCursor()) { e.Use(); _ctx.Host.Repaint(); }
                }
                else if (_getSelectMode() && e.keyCode == KeyCode.Escape)
                {
                    _clearSelection();
                    e.Use();
                    _ctx.Host.Repaint();
                }
                else if (view.CropMode && e.keyCode == KeyCode.Escape)
                {
                    view.CropMode = false;
                    view.CropDragHandle = -1;
                    view.CropDeltaLeft = view.CropDeltaDown = view.CropDeltaRight = view.CropDeltaUp = 0;
                    e.Use();
                    _ctx.Host.Repaint();
                }
                else if (view.CropMode && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
                {
                    if (view.CropDeltaLeft != 0 || view.CropDeltaDown != 0
                        || view.CropDeltaRight != 0 || view.CropDeltaUp != 0)
                    {
                        _expandGrid(view.CropDeltaLeft, view.CropDeltaDown, view.CropDeltaRight, view.CropDeltaUp);
                    }
                    view.CropMode = false;
                    view.CropDragHandle = -1;
                    view.CropDeltaLeft = view.CropDeltaDown = view.CropDeltaRight = view.CropDeltaUp = 0;
                    e.Use();
                    _ctx.Host.Repaint();
                }
            }
        }

        /// <summary>Chốt cờ giữ SHIFT để canvas hiện/ẩn chỉ báo vẽ thẳng ngay lúc bấm/nhả phím. Phải
        /// cache thành cờ vì lượt Repaint (nơi vẽ chỉ báo) không mang modifier đáng tin; đổi trạng thái
        /// thì tự Repaint để chỉ báo hiện/tắt mà không cần rê chuột.</summary>
        internal void TrackShiftHeld()
        {
            Event e = Event.current;
            if (e.type == EventType.Repaint || e.type == EventType.Layout) return;
            if (e.shift == ShiftHeld) return;
            ShiftHeld = e.shift;
            _ctx.Host.Repaint();
        }
    }
}
#endif
