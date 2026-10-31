using System.Linq;
using UnityEngine;

namespace UnityFigmaBridge.Editor.FigmaApi
{
    /// <summary>
    ///     The box a node's render covers: the layout box plus outside strokes, drop shadows and layer blur.
    ///     Figma leaves absoluteRenderBounds null on a node that does not render (hidden, or under a hidden
    ///     parent), so for those the box is grown from the node's own strokes and effects. Without that a
    ///     hidden state of a button would import without the outside stroke its shown state has.
    /// </summary>
    public static class NodeRenderBounds
    {
        public static Rectangle Of(Node node)
        {
            if (node.absoluteRenderBounds != null) return node.absoluteRenderBounds;
            var box = node.absoluteBoundingBox;
            if (box == null) return null;

            StrokeOutsets(node, out var left, out var top, out var right, out var bottom);
            foreach (var effect in node.effects ?? new Effect[0])
            {
                if (effect == null || !effect.visible) continue;
                if (effect.type == Effect.EffectType.LAYER_BLUR)
                {
                    left += effect.radius;
                    top += effect.radius;
                    right += effect.radius;
                    bottom += effect.radius;
                }
                else if (effect.type == Effect.EffectType.DROP_SHADOW)
                {
                    var reach = effect.radius + effect.spread;
                    var dx = effect.offset != null ? effect.offset.x : 0f;
                    var dy = effect.offset != null ? effect.offset.y : 0f;
                    left = Mathf.Max(left, reach - dx);
                    top = Mathf.Max(top, reach - dy);
                    right = Mathf.Max(right, reach + dx);
                    bottom = Mathf.Max(bottom, reach + dy);
                }
            }

            return new Rectangle
            {
                x = box.x - left,
                y = box.y - top,
                width = box.width + left + right,
                height = box.height + top + bottom
            };
        }

        private static void StrokeOutsets(Node node, out float left, out float top, out float right, out float bottom)
        {
            left = top = right = bottom = 0f;
            if (node.strokes == null || !node.strokes.Any(p => p != null && p.visible)) return;
            var share = node.strokeAlign switch
            {
                Node.StrokeAlign.OUTSIDE => 1f,
                Node.StrokeAlign.CENTER => 0.5f,
                _ => 0f
            };
            if (share == 0f) return;
            var sides = node.individualStrokeWeights;
            left = share * (sides != null ? sides.left : node.strokeWeight);
            top = share * (sides != null ? sides.top : node.strokeWeight);
            right = share * (sides != null ? sides.right : node.strokeWeight);
            bottom = share * (sides != null ? sides.bottom : node.strokeWeight);
        }
    }
}
