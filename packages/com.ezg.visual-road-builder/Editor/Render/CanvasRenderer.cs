#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Top-level canvas draw dispatcher — calls sub-renderers in z-order then overlays.</summary>
    internal sealed class CanvasRenderer
    {
        private readonly ToolContext _ctx;
        private readonly GridRenderer _grid;
        private readonly TileRenderer _tile;
        private readonly HoverRenderer _hover;
        private readonly BlockRenderer _block;
        private readonly DecorRenderer _decor;
        private readonly CropOverlayRenderer _crop;
        private readonly RoadPaintTool _roadPaint;

        // Sprite renderers still on the window partial — injected as delegates.
        private readonly Action<Rect, List<int>> _drawRoadSprites;
        private readonly Action<Rect, List<int>> _drawHighwaySprites;
        private readonly Action<Rect, List<int>> _drawRoad2Sprites;
        private readonly Action<Rect, List<int>> _drawPathSprites;
        // Sub-renderers on other slices' files.
        private readonly Action<Rect> _drawSelectOverlay;
        private readonly Action<Rect> _drawDebugBoundaries;
        private readonly Action<Rect> _drawEraserCursor;
        private readonly Func<bool> _anyDebugBoundary;
        private readonly Func<bool> _getSelectMode;

        internal CanvasRenderer(ToolContext ctx,
            GridRenderer grid, TileRenderer tile, HoverRenderer hover,
            BlockRenderer block, DecorRenderer decor, CropOverlayRenderer crop,
            RoadPaintTool roadPaint,
            Action<Rect, List<int>> drawRoadSprites,
            Action<Rect, List<int>> drawHighwaySprites,
            Action<Rect, List<int>> drawRoad2Sprites,
            Action<Rect, List<int>> drawPathSprites,
            Action<Rect> drawSelectOverlay,
            Action<Rect> drawDebugBoundaries,
            Action<Rect> drawEraserCursor,
            Func<bool> anyDebugBoundary,
            Func<bool> getSelectMode)
        {
            _ctx = ctx;
            _grid = grid;
            _tile = tile;
            _hover = hover;
            _block = block;
            _decor = decor;
            _crop = crop;
            _roadPaint = roadPaint;
            _drawRoadSprites = drawRoadSprites;
            _drawHighwaySprites = drawHighwaySprites;
            _drawRoad2Sprites = drawRoad2Sprites;
            _drawPathSprites = drawPathSprites;
            _drawSelectOverlay = drawSelectOverlay;
            _drawDebugBoundaries = drawDebugBoundaries;
            _drawEraserCursor = drawEraserCursor;
            _anyDebugBoundary = anyDebugBoundary;
            _getSelectMode = getSelectMode;
        }

        internal void DrawCanvasContent(Rect canvas, DecorState ds)
        {
            var view = _ctx.View;
            var doc = _ctx.Doc;

            EditorGUI.DrawRect(canvas, new Color(0.13f, 0.13f, 0.13f));
            _grid.DrawGridLines(canvas);

            _drawRoadSprites(canvas, doc.Edges);
            _drawHighwaySprites(canvas, doc.HighwayEdges);
            _drawRoad2Sprites(canvas, doc.Road2Edges);
            _drawPathSprites(canvas, doc.PathEdges);
            _tile.DrawRoadTiles(canvas, doc.HwDecorEdges, ToolStyles.TileHwDecor);
            _hover.DrawRoadHoverGhost(canvas);
            _hover.DrawShiftLineGuide(canvas, _roadPaint);

            if (view.Dragging)
            {
                Vector2 p = CoordHelper.PointToPixelF(canvas, view.DragPoint.x * 0.5f, view.DragPoint.y * 0.5f, doc, view);
                EditorGUI.DrawRect(new Rect(p.x - 5f, p.y - 5f, 10f, 10f),
                    view.Erasing ? Color.red : Color.cyan);
            }

            _block.DrawStations(canvas);
            _decor.DrawDecors(canvas, ds);
            if (_anyDebugBoundary()) _drawDebugBoundaries(canvas);

            if (view.EraserMode) _drawEraserCursor(canvas);
            if (view.CropMode) _crop.DrawCropOverlay(canvas);
            if (_getSelectMode()) _drawSelectOverlay(canvas);
            _hover.DrawHoverReadout(canvas);
        }
    }
}
#endif
