#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Mode "Chon vung &amp; keo" (phim Q): keo CHUOT TRAI tao khung marquee de chon MOI vat the
    /// nam gon trong khung, roi hien gizmo o tam de keo ca nhom di (snap 1/2 o).
    ///
    /// Rieng lop Road: khi dich, moi diem noi giua nhom chon va phan duong DUNG YEN duoc BAC CAU tu dong.
    /// Cac lop con lai dich CUNG theo nhom, KHONG bac cau.</summary>
    internal sealed class SelectMoveTool : IPaintTool
    {
        private readonly ToolContext _ctx;
        private readonly Func<bool> _getSelectMode;
        private readonly Action<bool> _setSelectMode;
        private readonly Action _resetDecorInteraction;
        private readonly Func<bool> _hasAnyOverlap;

        private readonly SelectMoveRebuilder _rebuilder;
        private readonly SelectOverlayRenderer _overlay;

        private bool _selecting;
        private Vector2Int _selStart2, _selEnd2;
        private bool _movingSel;
        private Vector2Int _selMovePoint;

        internal bool HasSelection { get; private set; }
        internal Vector2Int Delta { get; private set; }
        internal bool OverlapHint { get; set; }

        internal readonly List<int> EdgesOrig = new();
        internal readonly List<int> Stationary = new();
        internal readonly List<Vector2Int> AnchorsOrig = new();
        internal readonly List<int> HwOrig = new(), HwStat = new();
        internal readonly List<int> HwDecOrig = new(), HwDecStat = new();
        internal readonly List<int> StationsOrig = new(), StationsStat = new();
        internal readonly List<int> ParkingsOrig = new(), ParkingsStat = new();
        internal readonly List<DecorItem> DecorsOrig = new(), DecorsStat = new();
        internal readonly List<int> RampsOrig = new(), RampsStat = new();
        internal readonly List<int> Road2Orig = new(), Road2Stat = new();
        internal readonly List<int> PathOrig = new(), PathStat = new();

        internal bool Selecting => _selecting;
        internal Vector2Int SelStart2 => _selStart2;
        internal Vector2Int SelEnd2 => _selEnd2;
        internal bool MovingSel => _movingSel;

        internal SelectMoveTool(ToolContext ctx, Func<bool> getSelectMode, Action<bool> setSelectMode,
            Action resetDecorInteraction, Func<bool> hasAnyOverlap, ToolStyles styles)
        {
            _ctx = ctx;
            _getSelectMode = getSelectMode;
            _setSelectMode = setSelectMode;
            _resetDecorInteraction = resetDecorInteraction;
            _hasAnyOverlap = hasAnyOverlap;
            _rebuilder = new SelectMoveRebuilder(ctx, this, hasAnyOverlap);
            _overlay = new SelectOverlayRenderer(ctx, this, styles);
        }

        internal bool IsActive => _getSelectMode();

        internal void ToggleSelectMode()
        {
            bool mode = !_getSelectMode();
            _setSelectMode(mode);
            if (mode)
            {
                var view = _ctx.View;
                view.EraserMode = false;
                view.MoveAllMode = false;
                view.CropMode = false;
                view.Dragging = false;
                view.DraggingStation = -1;
                view.DraggingParking = -1;
                view.MovingAll = false;
                view.HasHover = false;
                _resetDecorInteraction();
            }
            ClearSelection();
        }

        internal void ClearSelection()
        {
            _selecting = false;
            _movingSel = false;
            HasSelection = false;
            Delta = Vector2Int.zero;
            OverlapHint = false;
            EdgesOrig.Clear(); Stationary.Clear(); AnchorsOrig.Clear();
            HwOrig.Clear(); HwStat.Clear();
            HwDecOrig.Clear(); HwDecStat.Clear();
            StationsOrig.Clear(); StationsStat.Clear();
            ParkingsOrig.Clear(); ParkingsStat.Clear();
            DecorsOrig.Clear(); DecorsStat.Clear();
            RampsOrig.Clear(); RampsStat.Clear();
            Road2Orig.Clear(); Road2Stat.Clear();
            PathOrig.Clear(); PathStat.Clear();
        }

        public bool HandleInput(Rect canvas, Event e)
        {
            if (!_getSelectMode()) return false;
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && canvas.Contains(e.mousePosition):
                    if (HasSelection && SelectMoveGeometry.PointInSelection(canvas, e.mousePosition, this, _ctx.Doc, _ctx.View))
                    {
                        _movingSel = true;
                        _selMovePoint = SelectMoveGeometry.PixelToHalfPointRaw(canvas, e.mousePosition, _ctx.Doc, _ctx.View);
                    }
                    else
                    {
                        ClearSelection();
                        _selecting = true;
                        _selStart2 = SelectMoveGeometry.PixelToHalfPointRaw(canvas, e.mousePosition, _ctx.Doc, _ctx.View);
                        _selEnd2 = _selStart2;
                    }
                    e.Use();
                    _ctx.Host.Repaint();
                    return true;

                case EventType.MouseDrag when _selecting:
                    _selEnd2 = SelectMoveGeometry.PixelToHalfPointRaw(canvas, e.mousePosition, _ctx.Doc, _ctx.View);
                    e.Use();
                    _ctx.Host.Repaint();
                    return true;

                case EventType.MouseDrag when _movingSel:
                {
                    Vector2Int cur = SelectMoveGeometry.PixelToHalfPointRaw(canvas, e.mousePosition, _ctx.Doc, _ctx.View);
                    Vector2Int step = cur - _selMovePoint;
                    if (step != Vector2Int.zero)
                    {
                        Vector2Int target = _rebuilder.ClampSelDelta(Delta + step);
                        Vector2Int applied = target - Delta;
                        if (applied != Vector2Int.zero)
                        {
                            Delta = target;
                            _selMovePoint += applied;
                            _rebuilder.RebuildSelection();
                        }
                    }
                    e.Use();
                    _ctx.Host.Repaint();
                    return true;
                }

                case EventType.MouseUp when _selecting:
                    _selecting = false;
                    ComputeSelection();
                    e.Use();
                    _ctx.Host.Repaint();
                    return true;

                case EventType.MouseUp when _movingSel:
                    _movingSel = false;
                    e.Use();
                    _ctx.Host.Repaint();
                    return true;
            }
            return false;
        }

        public void DrawOverlay(Rect canvas)
        {
            _overlay.Draw(canvas);
        }

        public void Cancel()
        {
            _selecting = false;
            _movingSel = false;
        }

        private void ComputeSelection()
        {
            EdgesOrig.Clear(); Stationary.Clear(); AnchorsOrig.Clear();
            HwOrig.Clear(); HwStat.Clear();
            HwDecOrig.Clear(); HwDecStat.Clear();
            StationsOrig.Clear(); StationsStat.Clear();
            ParkingsOrig.Clear(); ParkingsStat.Clear();
            DecorsOrig.Clear(); DecorsStat.Clear();
            RampsOrig.Clear(); RampsStat.Clear();
            Road2Orig.Clear(); Road2Stat.Clear();
            PathOrig.Clear(); PathStat.Clear();
            HasSelection = false;
            Delta = Vector2Int.zero;

            int minX = Mathf.Min(_selStart2.x, _selEnd2.x);
            int maxX = Mathf.Max(_selStart2.x, _selEnd2.x);
            int minY = Mathf.Min(_selStart2.y, _selEnd2.y);
            int maxY = Mathf.Max(_selStart2.y, _selEnd2.y);
            if (maxX - minX < 1 && maxY - minY < 1) return;

            var doc = _ctx.Doc;

            var selSet = new HashSet<int>();
            var selNodes = new HashSet<Vector2Int>();
            foreach (int id in doc.Edges)
            {
                SelectMoveGeometry.EdgeEndpoints(id, out Vector2Int a, out Vector2Int b);
                if (!SelectMoveGeometry.InBox(a, minX, minY, maxX, maxY) ||
                    !SelectMoveGeometry.InBox(b, minX, minY, maxX, maxY)) continue;
                EdgesOrig.Add(id);
                selSet.Add(id);
                selNodes.Add(a);
                selNodes.Add(b);
            }

            var stationaryNodes = new HashSet<Vector2Int>();
            foreach (int id in doc.Edges)
            {
                if (selSet.Contains(id)) continue;
                Stationary.Add(id);
                SelectMoveGeometry.EdgeEndpoints(id, out Vector2Int a, out Vector2Int b);
                stationaryNodes.Add(a);
                stationaryNodes.Add(b);
            }

            foreach (Vector2Int n in selNodes)
                if (stationaryNodes.Contains(n))
                    AnchorsOrig.Add(n);

            PartitionEdges(doc.HighwayEdges, minX, minY, maxX, maxY, HwOrig, HwStat);
            PartitionEdges(doc.HwDecorEdges, minX, minY, maxX, maxY, HwDecOrig, HwDecStat);
            PartitionEdges(doc.Road2Edges, minX, minY, maxX, maxY, Road2Orig, Road2Stat);
            PartitionEdges(doc.PathEdges, minX, minY, maxX, maxY, PathOrig, PathStat);

            int st2 = GridConst.StationSize * 2;
            foreach (int id in doc.Stations)
            {
                BlockCodec.DecodeStation(id, out int x2, out int y2, out _);
                if (SelectMoveGeometry.RectInBox(x2, y2, st2, st2, minX, minY, maxX, maxY))
                    StationsOrig.Add(id);
                else StationsStat.Add(id);
            }

            foreach (int id in doc.Parkings)
            {
                BlockCodec.DecodeParking(id, out int x2, out int y2, out int orient);
                Vector2Int k = GridConst.ParkingCells(orient);
                if (SelectMoveGeometry.RectInBox(x2, y2, k.x * 2, k.y * 2, minX, minY, maxX, maxY))
                    ParkingsOrig.Add(id);
                else ParkingsStat.Add(id);
            }

            foreach (DecorItem item in doc.Decors)
            {
                if (SelectMoveGeometry.InBox(new Vector2Int(item.x2, item.y2), minX, minY, maxX, maxY))
                    DecorsOrig.Add(item);
                else DecorsStat.Add(item);
            }

            foreach (int key in doc.RampFlips)
            {
                EdgeCodec.DecodeRampAnchor(key, out int x2, out int y2);
                if (SelectMoveGeometry.InBox(new Vector2Int(x2, y2), minX, minY, maxX, maxY))
                    RampsOrig.Add(key);
                else RampsStat.Add(key);
            }

            HasSelection = EdgesOrig.Count > 0 || HwOrig.Count > 0 || HwDecOrig.Count > 0
                || Road2Orig.Count > 0 || PathOrig.Count > 0
                || StationsOrig.Count > 0 || ParkingsOrig.Count > 0 || DecorsOrig.Count > 0;
        }

        private static void PartitionEdges(List<int> src, int minX, int minY, int maxX, int maxY,
            List<int> selOut, List<int> statOut)
        {
            foreach (int id in src)
            {
                SelectMoveGeometry.EdgeEndpoints(id, out Vector2Int a, out Vector2Int b);
                if (SelectMoveGeometry.InBox(a, minX, minY, maxX, maxY) &&
                    SelectMoveGeometry.InBox(b, minX, minY, maxX, maxY))
                    selOut.Add(id);
                else statOut.Add(id);
            }
        }
    }
}
#endif
