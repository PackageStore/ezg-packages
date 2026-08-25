#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    // ── SHIM LAYER ──────────────────────────────────────────────────────────────
    // Delegating forwarders so the 36+ not-yet-migrated partial files keep compiling
    // against the old field names. Integration deletes this file once every slice has
    // migrated its files to use RoadCanvasDoc / ViewState / ApplyTarget / GridConst
    // directly. Expression-bodied for reference types; full get/set for assigned fields.
    // ─────────────────────────────────────────────────────────────────────────────
    public sealed partial class VisualRoadBuilderTool
    {
        // ── GridConst shims ─────────────────────────────────────────────────────
        private const int MaxGridSize = GridConst.MaxGridSize;
        private const float GutterLeft = GridConst.GutterLeft;
        private const float GutterTop = GridConst.GutterTop;
        private const float GutterRight = GridConst.GutterRight;
        private const float GutterBottom = GridConst.GutterBottom;
        private const float OuterMargin = GridConst.OuterMargin;
        private const float ControlColumnWidth = GridConst.ControlColumnWidth;
        private const int StationSize = GridConst.StationSize;
        private const int ParkingLong = GridConst.ParkingLong;
        private const int ParkingShort = GridConst.ParkingShort;
        private const float CellWorldSize = GridConst.CellWorldSize;

        // ── DirBits shims ───────────────────────────────────────────────────────
        private const int DirE = DirBits.E, DirN = DirBits.N, DirW = DirBits.W, DirS = DirBits.S;
        private const int DirAll = DirBits.All;

        // ── ApplyTarget shims ───────────────────────────────────────────────────
        private const string DefaultRoadParentName = ApplyTarget.DefaultRoadParentName;
        private GameObject _levelPrefab { get => _applyTarget.LevelPrefab; set => _applyTarget.LevelPrefab = value; }
        private string _roadParentName { get => _applyTarget.RoadParentName; set => _applyTarget.RoadParentName = value; }
        private DefaultAsset _saveFolder { get => _applyTarget.SaveFolder; set => _applyTarget.SaveFolder = value; }

        // ── RoadCanvasDoc shims ─────────────────────────────────────────────────
        private int _gridWidth { get => _doc.GridWidth; set => _doc.GridWidth = value; }
        private int _gridHeight { get => _doc.GridHeight; set => _doc.GridHeight = value; }
        private Vector2Int _originCell { get => _doc.OriginCell; set => _doc.OriginCell = value; }
        private int _dataVersion { get => _doc.DataVersion; set => _doc.DataVersion = value; }
        private List<int> _edges => _doc.Edges;
        private List<int> _highwayEdges => _doc.HighwayEdges;
        private List<int> _hwDecorEdges => _doc.HwDecorEdges;
        private List<int> _road2Edges => _doc.Road2Edges;
        private List<int> _pathEdges => _doc.PathEdges;
        private List<int> _rampFlips => _doc.RampFlips;
        private List<int> _stations => _doc.Stations;
        private List<int> _stations2 => _doc.Stations2;
        private List<int> _parkings => _doc.Parkings;
        private List<DecorItem> _decors => _doc.Decors;
        private List<int> ActiveEdges => _doc.EdgesFor(_edgeLayer);
        private int LatticeW => _doc.LatticeW;
        private int LatticeH => _doc.LatticeH;

        // ── ViewState shims ─────────────────────────────────────────────────────
        private float _cellPixelSize { get => _view.CellPixelSize; set => _view.CellPixelSize = value; }
        private PaintMode _mode { get => _view.Mode; set => _view.Mode = value; }
        private int _blockKind { get => _view.BlockKind; set => _view.BlockKind = value; }
        private int _edgeLayer { get => _view.EdgeLayer; set => _view.EdgeLayer = value; }
        private Vector2 _scroll { get => _view.Scroll; set => _view.Scroll = value; }
        private Vector2 _controlScroll { get => _view.ControlScroll; set => _view.ControlScroll = value; }
        private bool _foldTarget { get => _view.FoldTarget; set => _view.FoldTarget = value; }
        private bool _foldTools { get => _view.FoldTools; set => _view.FoldTools = value; }
        private bool _foldDecor { get => _view.FoldDecor; set => _view.FoldDecor = value; }
        private bool _showDebugBoundary { get => _view.ShowDebugBoundary; set => _view.ShowDebugBoundary = value; }
        private bool _showDebugBlockBoundary { get => _view.ShowDebugBlockBoundary; set => _view.ShowDebugBlockBoundary = value; }
        private bool _debugBoundaryDefaultApplied { get => _view.DebugBoundaryDefaultApplied; set => _view.DebugBoundaryDefaultApplied = value; }
        private float _debugBoundaryAlpha { get => _view.DebugBoundaryAlpha; set => _view.DebugBoundaryAlpha = value; }
        private bool _hoverCellValid { get => _view.HoverCellValid; set => _view.HoverCellValid = value; }
        private Vector2 _hoverCell { get => _view.HoverCell; set => _view.HoverCell = value; }
        private Vector2 _hoverPixel { get => _view.HoverPixel; set => _view.HoverPixel = value; }
        private bool _panning { get => _view.Panning; set => _view.Panning = value; }
        private bool _dragging { get => _view.Dragging; set => _view.Dragging = value; }
        private bool _erasing { get => _view.Erasing; set => _view.Erasing = value; }
        private Vector2Int _dragPoint { get => _view.DragPoint; set => _view.DragPoint = value; }
        private int _draggingStation { get => _view.DraggingStation; set => _view.DraggingStation = value; }
        private int _draggingStation2 { get => _view.DraggingStation2; set => _view.DraggingStation2 = value; }
        private int _draggingParking { get => _view.DraggingParking; set => _view.DraggingParking = value; }
        private bool _hasHover { get => _view.HasHover; set => _view.HasHover = value; }
        private Vector2Int _hoverAnchor { get => _view.HoverAnchor; set => _view.HoverAnchor = value; }
        private bool _movingAll { get => _view.MovingAll; set => _view.MovingAll = value; }
        private Vector2Int _movePoint { get => _view.MovePoint; set => _view.MovePoint = value; }
        private bool _moveAllMode { get => _view.MoveAllMode; set => _view.MoveAllMode = value; }
        private bool _cropMode { get => _view.CropMode; set => _view.CropMode = value; }
        private bool _eraserMode { get => _view.EraserMode; set => _view.EraserMode = value; }
        private int _cropDragHandle { get => _view.CropDragHandle; set => _view.CropDragHandle = value; }
        private Vector2 _cropDragStartMouse { get => _view.CropDragStartMouse; set => _view.CropDragStartMouse = value; }
        private int _cropDeltaLeft { get => _view.CropDeltaLeft; set => _view.CropDeltaLeft = value; }
        private int _cropDeltaDown { get => _view.CropDeltaDown; set => _view.CropDeltaDown = value; }
        private int _cropDeltaRight { get => _view.CropDeltaRight; set => _view.CropDeltaRight = value; }
        private int _cropDeltaUp { get => _view.CropDeltaUp; set => _view.CropDeltaUp = value; }
        private Vector2Int BlockKindSize => _view.BlockKindSize;

        // ── ParkingCells shim ───────────────────────────────────────────────────
        private Vector2Int ParkingCells(int rot) => GridConst.ParkingCells(rot);

        // ── EdgeCodec shims (static methods, called by name from other partials) ─
        private static void DoubleEdgeCoords(List<int> e) => EdgeCodec.DoubleEdgeCoords(e);
        private static void SplitEdgeSpan(List<int> e) => EdgeCodec.SplitEdgeSpan(e);
        private static int EncodeEdge(Vector2Int a, Vector2Int b) => EdgeCodec.EncodeEdge(a, b);
        private static void DecodeEdge(int id, out int x, out int y, out int orient) => EdgeCodec.DecodeEdge(id, out x, out y, out orient);
        private static int EncodeEdgeRaw(int x2, int y2, int orient) => EdgeCodec.EncodeEdgeRaw(x2, y2, orient);
        private static List<int> PairHalfEdges(List<int> e) => EdgeCodec.PairHalfEdges(e);
        // DecodeRampAnchor / RampAnchorKey: still defined in HighwaySolver.cs (not our file);
        // EdgeCodec has the canonical copy for post-migration.

        // ── MaskBuilder shims ───────────────────────────────────────────────────
        private int[] BuildMasks(List<int> edges) => MaskBuilder.BuildMasks(edges, LatticeW, LatticeH);
        private int[] BuildLegacyMasks(List<int> anchors) => MaskBuilder.BuildLegacyMasks(anchors, LatticeW, LatticeH);
        private int[] BuildLegacyMasksFromEdges(List<int> edges) => MaskBuilder.BuildLegacyMasksFromEdges(edges, LatticeW, LatticeH);

        // ── DirBits method shims ────────────────────────────────────────────────
        private static float SolveYaw(int baseMask, int targetMask) => DirBits.SolveYaw(baseMask, targetMask);
        private static int RotateMask90(int mask) => DirBits.RotateMask90(mask);
        private static int CountBits(int mask) => DirBits.CountBits(mask);

        // ── MaskClassifier shims ────────────────────────────────────────────────
        private static float StraightYaw(int mask) => MaskClassifier.StraightYaw(mask);
        private static int StraightSides(int mask) => MaskClassifier.StraightSides(mask);
        private static int SideAtRimYaw(float yaw) => MaskClassifier.SideAtRimYaw(yaw);
        // TryTileLocalRect promoted to MaskClassifier (slice 02)
        private static bool TryTileLocalRect(RoadTilePart part, out float lx, out float ly, out float half) =>
            MaskClassifier.TryTileLocalRect(part, out lx, out ly, out half);
        private static int CountPieces(int[] masks) => MaskClassifier.CountPieces(masks);

        // ── BlockCodec shims ────────────────────────────────────────────────────
        private static int EncodeStation(Vector2Int a2, int rot) => BlockCodec.EncodeStation(a2, rot);
        private static void DecodeStation(int id, out int x2, out int y2, out int rot) => BlockCodec.DecodeStation(id, out x2, out y2, out rot);
        private static int EncodeParking(Vector2Int a2, int rot) => BlockCodec.EncodeParking(a2, rot);
        private static void DecodeParking(int id, out int x2, out int y2, out int rot) => BlockCodec.DecodeParking(id, out x2, out y2, out rot);
        private static Vector2 StationPivotCell(int sx2, int sy2, int s, int rot) => BlockCodec.StationPivotCell(sx2, sy2, s, rot);
    }
}
#endif
