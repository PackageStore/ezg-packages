#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Flat-colour tile draw for hw-decor and debug.</summary>
    internal sealed class TileRenderer
    {
        private readonly ToolContext _ctx;

        internal TileRenderer(ToolContext ctx) => _ctx = ctx;

        /// <summary>Mỗi điểm lattice có nét (mask != 0) tô 1 ô đặc — đúng chỗ Apply đặt 1 mảnh prefab;
        /// điểm kề nhau cách đúng 1 ô nên các ô khít lại thành dải đường liền.</summary>
        internal void DrawRoadTiles(Rect canvas, List<int> edges, Color color)
        {
            if (edges.Count == 0) return;
            var doc = _ctx.Doc;
            var view = _ctx.View;
            int[] masks = MaskBuilder.BuildMasks(edges, doc.LatticeW, doc.LatticeH);
            int lw = doc.LatticeW, lh = doc.LatticeH;
            float cell = view.CellPixelSize;
            for (int y = 0; y < lh; y++)
            {
                for (int x = 0; x < lw; x++)
                {
                    if (masks[y * lw + x] == 0) continue;
                    Vector2 p = CoordHelper.PointToPixelF(canvas, x * 0.5f, y * 0.5f, doc, view);
                    EditorGUI.DrawRect(new Rect(p.x - cell * 0.5f, p.y - cell * 0.5f, cell, cell), color);
                }
            }
        }
    }
}
#endif
