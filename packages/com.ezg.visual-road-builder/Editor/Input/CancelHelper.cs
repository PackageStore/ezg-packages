#if UNITY_EDITOR
namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>CancelCanvasInteractions — resets every tool's drag state in one call.</summary>
    internal static class CancelHelper
    {
        /// <summary>Huỷ mọi tương tác canvas ĐANG DỞ (kéo vẽ/xoá/pan/di chuyển/marquee) — dùng khi đổi
        /// tab hoặc chuyển ngữ cảnh để không kẹt cờ khiến canvas "chết" input. GIỮ NGUYÊN mode đang chọn
        /// (eraser/crop/move-all/select) và vùng chọn (Q) đã chốt.</summary>
        internal static void CancelAll(ViewState view, DecorState decorState)
        {
            view.Dragging = false;
            view.Erasing = false;
            view.Panning = false;
            view.MovingAll = false;
            view.DraggingStation = -1;
            view.DraggingParking = -1;
            view.CropDragHandle = -1;
            view.HasHover = false;
            decorState.ResetInteraction();
        }
    }
}
#endif
