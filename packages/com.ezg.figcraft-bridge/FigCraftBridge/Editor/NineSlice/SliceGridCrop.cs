using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FigCraftBridge.Editor.FigmaApi;
using FigCraftBridge.Runtime.UI;

namespace FigCraftBridge.Editor.NineSlice
{
    /// <summary>
    ///     Border of a slice grid read off the crop each cell takes from the shared image
    ///     (<c>Paint.imageTransform</c> of a STRETCH image fill): the corner cells' crop gives the
    ///     border in texels, and crop texels over the cell's design size give the density. A grid may
    ///     leave out its middle row or column (the art then has no stretch band on that axis); the
    ///     border still comes from the first and last row or column.
    /// </summary>
    internal static class SliceGridCrop
    {
        internal readonly struct Cell
        {
            internal readonly string row, col;
            internal readonly RectTransform rect;
            internal Cell(string row, string col, RectTransform rect)
            {
                this.row = row;
                this.col = col;
                this.rect = rect;
            }
        }

        /// <summary>Border (left, bottom, right, top) in texels and texels per design unit; false when a cell has no crop to read.</summary>
        internal static bool TryMeasure(List<Cell> cells, Func<string, Node> lookup, int textureWidth, int textureHeight,
            out Vector4 border, out float density)
        {
            border = Vector4.zero;
            density = 0f;
            if (lookup == null || textureWidth <= 0 || textureHeight <= 0) return false;

            var crops = new Dictionary<Cell, Rect>();
            foreach (var cell in cells)
            {
                var marker = cell.rect.GetComponent<FigmaNodeObject>();
                var node = marker != null ? lookup(marker.NodeId) : null;
                if (!TryCrop(node, out var crop)) return false;
                crops[cell] = crop;
            }

            float Left(Cell c) => c.rect.localPosition.x + c.rect.rect.xMin;
            float Top(Cell c) => -(c.rect.localPosition.y + c.rect.rect.yMax);
            var cols = cells.Select(c => c.col).Distinct().OrderBy(col => cells.Where(c => c.col == col).Min(Left)).ToList();
            var rows = cells.Select(c => c.row).Distinct().OrderBy(row => cells.Where(c => c.row == row).Min(Top)).ToList();

            float left = 0, right = 0, top = 0, bottom = 0;
            var densities = new List<float>();
            if (cols.Count >= 2)
            {
                left = Edge(cells, crops, c => c.col == cols[0], textureWidth, true, densities);
                right = Edge(cells, crops, c => c.col == cols[^1], textureWidth, true, densities);
            }
            if (rows.Count >= 2)
            {
                top = Edge(cells, crops, c => c.row == rows[0], textureHeight, false, densities);
                bottom = Edge(cells, crops, c => c.row == rows[^1], textureHeight, false, densities);
            }
            if (densities.Count == 0) return false;

            // Unity has one pixels-per-unit for both axes: the densest edge keeps every border inside the design size
            density = densities.Max();
            border = new Vector4(Mathf.Round(left), Mathf.Round(bottom), Mathf.Round(right), Mathf.Round(top));
            return border.x + border.z < textureWidth && border.y + border.w < textureHeight;
        }

        /// <summary>Crop span in texels of the cells on one edge; adds that edge's texels per design unit.</summary>
        private static float Edge(List<Cell> cells, Dictionary<Cell, Rect> crops, Func<Cell, bool> onEdge, int textureSize,
            bool horizontal, List<float> densities)
        {
            var edge = cells.Where(onEdge).ToList();
            var cell = edge[0];
            var crop = crops[cell];
            var texels = (horizontal ? crop.width : crop.height) * textureSize;
            var design = horizontal ? cell.rect.rect.width : cell.rect.rect.height;
            if (design > 0.01f) densities.Add(texels / design);
            return texels;
        }

        /// <summary>Normalised crop of the node's single visible STRETCH image fill (imageTransform without rotation).</summary>
        private static bool TryCrop(Node node, out Rect crop)
        {
            crop = default;
            var fill = node?.fills?.FirstOrDefault(f => f != null && f.visible && f.type == Paint.PaintType.IMAGE);
            var m = fill?.imageTransform;
            if (fill == null || fill.scaleMode != Paint.ScaleMode.STRETCH || m == null || m.GetLength(0) < 2 || m.GetLength(1) < 3) return false;
            if (Mathf.Abs(m[0, 1]) > 1e-4f || Mathf.Abs(m[1, 0]) > 1e-4f || m[0, 0] <= 0f || m[1, 1] <= 0f) return false;
            crop = new Rect(m[0, 2], m[1, 2], m[0, 0], m[1, 1]);
            return true;
        }
    }
}
