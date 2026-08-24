#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>All view, interaction, and tool-mode state.</summary>
    [System.Serializable]
    internal sealed class ViewState
    {
        [SerializeField] internal float CellPixelSize = 22f;
        [SerializeField] internal PaintMode Mode = PaintMode.Road;
        [SerializeField] internal int BlockKind; // mode Station đặt khối gì: 0 station, 1 parking ngang, 2 parking dọc
        [SerializeField] internal int EdgeLayer; // 0 = đường, 1 = highway, 2 = highway decor, 3 = road2, 4 = path
        [SerializeField] internal Vector2 Scroll;
        [SerializeField] internal Vector2 ControlScroll;
        [SerializeField] internal bool FoldTarget = true, FoldTools = true, FoldDecor = true;
        [SerializeField] internal bool ShowDebugBoundary = true;
        [SerializeField] internal bool ShowDebugBlockBoundary = true;
        [SerializeField] internal bool DebugBoundaryDefaultApplied;
        // Độ mờ chung của MỌI box boundary (road/highway/station/parking). Cờ riêng vì window cũ đã
        // chạy DebugBoundaryDefaultApplied rồi, không thể nhờ nó set hộ.
        // Nhớ qua EditorPrefs (xem LoadDebugBoundaryAlpha) — [SerializeField] chỉ để giữ giữa các lượt OnGUI.
        [SerializeField] internal float DebugBoundaryAlpha = 1f;
        internal bool HoverCellValid;
        internal Vector2 HoverCell;   // toạ độ lưới (ô) dưới con trỏ
        internal Vector2 HoverPixel;  // vị trí pixel con trỏ (cho ô readout)
        internal bool Panning;        // giữ chuột GIỮA để pan canvas

        internal bool Dragging;
        internal bool Erasing;
        internal Vector2Int DragPoint;
        internal int DraggingStation = -1; // index trong _stations, -1 = không kéo
        internal int DraggingParking = -1; // index trong _parkings, -1 = không kéo
        internal bool HasHover;
        internal Vector2Int HoverAnchor;
        internal bool MovingAll;
        internal Vector2Int MovePoint;
        [SerializeField] internal bool MoveAllMode;
        [SerializeField] internal bool CropMode;
        [SerializeField] internal bool EraserMode;
        internal int CropDragHandle = -1; // -1 = none, 0..7 = TL, T, TR, R, BR, B, BL, L
        internal Vector2 CropDragStartMouse;
        internal int CropDeltaLeft, CropDeltaDown, CropDeltaRight, CropDeltaUp;

        /// <summary>Kích thước (ô) của loại khối đang chọn đặt trong mode Station.</summary>
        internal Vector2Int BlockKindSize => BlockKind switch
        {
            1 => new Vector2Int(GridConst.ParkingLong, GridConst.ParkingShort),
            2 => new Vector2Int(GridConst.ParkingShort, GridConst.ParkingLong),
            _ => new Vector2Int(GridConst.StationSize, GridConst.StationSize),
        };
    }
}
#endif
