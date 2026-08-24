#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    // ── SHIM LAYER 2 ────────────────────────────────────────────────────────────
    // Instance method forwarders for BlockModel, GridOps, and DecorModel methods
    // that depend on window state. Integration deletes this file.
    // ─────────────────────────────────────────────────────────────────────────────
    public sealed partial class VisualRoadBuilderTool
    {
        // ── GridOps shims ───────────────────────────────────────────────────────
        private GridOps _gridOps;
        private GridOps EnsureGridOps() => _gridOps ??= new GridOps(_doc, Repaint);

        private void MigrateWindowData() => EnsureGridOps().MigrateWindowData();
        private void ExpandGrid(int left, int down, int right, int up) =>
            EnsureGridOps().ExpandGrid(left, down, right, up, OffsetAll);
        private void PruneOutOfRangeEdges() => EnsureGridOps().PruneOutOfRangeEdges();

        // ── BlockModel instance method shims ────────────────────────────────────
        private Vector2Int AnchorFromMouseFor(Rect canvas, Vector2 mouse, int w, int h)
        {
            Vector2 f = MouseToGridF(canvas, mouse);
            return new Vector2Int(
                Mathf.RoundToInt((f.x - w * 0.5f) * 2f),
                Mathf.RoundToInt((f.y - h * 0.5f) * 2f));
        }

        private Vector2Int ClampBlockAnchor(Vector2Int a2, int w, int h) =>
            BlockCodec.ClampBlockAnchor(a2, w, h, _gridWidth, _gridHeight);

        private Vector2Int ClampStationAnchor(Vector2Int a2) =>
            ClampBlockAnchor(a2, StationSize, StationSize);

        private int FindStationAt(Vector2 f)
        {
            int size2 = StationSize * 2;
            float px2 = f.x * 2f, py2 = f.y * 2f;
            for (int i = _stations.Count - 1; i >= 0; i--)
            {
                DecodeStation(_stations[i], out int x2, out int y2, out _);
                if (px2 >= x2 && px2 <= x2 + size2 && py2 >= y2 && py2 <= y2 + size2) return i;
            }
            return -1;
        }

        private static Vector2 BlockFacingStep(int rot)
        {
            (int fx, int fy) = DirStep(OppositeDir(BlockSide(rot)));
            return new Vector2(fx, fy);
        }

        private bool TryGhostBlock(out int id, out bool isStation)
        {
            id = 0;
            isStation = _blockKind == 0;
            if (_mode != PaintMode.Station || _eraserMode || !_hasHover
                || _draggingStation >= 0 || _draggingParking >= 0) return false;
            id = isStation
                ? EncodeStation(_hoverAnchor, 0)
                : EncodeParking(_hoverAnchor, _blockKind == 2 ? 1 : 0);
            return true;
        }

        private Vector2 ParkingPivotCell(int ax2, int ay2, int rot) =>
            BlockCodec.ParkingPivotCell(ax2, ay2, rot, ParkingCells(rot));

        private static Vector2 StationHookCell(int sx2, int sy2, int s, int rot, bool road2)
        {
            float half = s * 0.5f;
            float cx = sx2 * 0.5f + half;
            float cy = sy2 * 0.5f + half;
            float fwd = half + BlockClearanceCells(road2, StationApronPlazaCells);
            return rot switch
            {
                1 => new Vector2(cx + fwd, cy),
                2 => new Vector2(cx, cy - fwd),
                3 => new Vector2(cx - fwd, cy),
                _ => new Vector2(cx, cy + fwd),
            };
        }

        private Vector2 ParkingHookCell(int ax2, int ay2, int rot) => ParkingPivotCell(ax2, ay2, rot);

        private int FindParkingAt(Vector2 f)
        {
            float px2 = f.x * 2f, py2 = f.y * 2f;
            for (int i = _parkings.Count - 1; i >= 0; i--)
            {
                DecodeParking(_parkings[i], out int x2, out int y2, out int orient);
                Vector2Int k = ParkingCells(orient);
                if (px2 >= x2 && px2 <= x2 + k.x * 2 && py2 >= y2 && py2 <= y2 + k.y * 2) return i;
            }
            return -1;
        }
    }
}
#endif
