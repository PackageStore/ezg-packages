using System;
using System.Collections.Generic;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;
using Color = UnityEngine.Color;

namespace UnityFigmaBridge.Editor.Nodes
{
    /// <summary>
    ///     Decides whether a node paints in one RGB, and which. A white sprite times that RGB in
    ///     Image.color draws the same pixels, so every site that whitens a sprite and every site that
    ///     sets the tint colour asks this class and the two cannot drift.
    /// </summary>
    public static class SolidTint
    {
        /// <summary>
        ///     A server render of a node tints when every visible node of its subtree paints only
        ///     SOLID paints of one RGB, with no effect, mask or blend mode. The tint is the first
        ///     counted paint in pre-order document order, alpha 1.
        /// </summary>
        public static bool TryGetRenderTint(Node node, out UnityEngine.Color tint)
        {
            tint = UnityEngine.Color.white;
            if (node == null) return false;

            var hasReference = false;
            var reference = UnityEngine.Color.white;
            var stack = new Stack<Node>();
            stack.Push(node);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == null || !current.visible) continue;
                if (current.isMask || !IsPlainBlend(current.blendMode)) return false;

                if (current.effects != null)
                    foreach (var effect in current.effects)
                        if (effect != null && effect.visible) return false;

                if (current.type == NodeType.TEXT && current.characterStyleOverrides != null)
                    foreach (var style in current.characterStyleOverrides)
                        if (style != 0) return false;

                if (current.fills != null)
                    foreach (var fill in current.fills)
                        if (!TryReadPaint(fill, true, ref hasReference, ref reference)) return false;

                if (current.strokes != null && current.strokeWeight > 0f)
                    foreach (var stroke in current.strokes)
                        if (!TryReadPaint(stroke, true, ref hasReference, ref reference)) return false;

                if (current.children == null) continue;
                for (var i = current.children.Length - 1; i >= 0; i--)
                    stack.Push(current.children[i]);
            }

            if (!hasReference) return false;
            tint = new UnityEngine.Color(reference.r, reference.g, reference.b, 1f);
            return true;
        }

        /// <summary>
        ///     A shape sprite tints when what FrameShapeSprite draws for the node itself (its visible
        ///     non-shader fills and the drawable stroke) is SOLID of one RGB and every visible inner
        ///     shadow is black. Other effects are ignored.
        /// </summary>
        public static bool TryGetShapeTint(Node node, out UnityEngine.Color tint)
        {
            tint = UnityEngine.Color.white;
            if (node == null) return false;

            var hasReference = false;
            var reference = UnityEngine.Color.white;

            if (node.fills != null)
                foreach (var fill in node.fills)
                {
                    if (fill == null || FigmaDataUtils.IsShaderPaint(fill)) continue;
                    if (!TryReadPaint(fill, false, ref hasReference, ref reference)) return false;
                }

            var stroke = DrawableStroke(node);
            if (stroke != null && !TryReadPaint(stroke, false, ref hasReference, ref reference)) return false;

            if (!hasReference) return false;

            if (node.effects != null)
                foreach (var effect in node.effects)
                {
                    if (effect == null || !effect.visible || effect.type != Effect.EffectType.INNER_SHADOW) continue;
                    if (effect.color == null || !IsPlainBlend(effect.blendMode)) return false;
                    if (To8Bit(effect.color.r) != 0 || To8Bit(effect.color.g) != 0 || To8Bit(effect.color.b) != 0) return false;
                }

            tint = new UnityEngine.Color(reference.r, reference.g, reference.b, 1f);
            return true;
        }

        /// <summary>
        ///     The tint of the render queued for an id: only a Substitution entry tints, and an id whose
        ///     list also holds an Export or PatternSource entry never does.
        /// </summary>
        public static bool TryGetEntryTint(string nodeId, List<ServerRenderNodeData> renderNodes, out UnityEngine.Color tint)
        {
            tint = UnityEngine.Color.white;
            if (string.IsNullOrEmpty(nodeId) || renderNodes == null) return false;

            ServerRenderNodeData first = null;
            foreach (var entry in renderNodes)
            {
                if (entry?.SourceNode == null) continue;
                if (!string.Equals(entry.SourceNode.id, nodeId, StringComparison.Ordinal)) continue;
                if (entry.RenderType == ServerRenderType.Export || entry.RenderType == ServerRenderType.PatternSource) return false;
                first ??= entry;
            }

            if (first == null || first.RenderType != ServerRenderType.Substitution) return false;
            return TryGetRenderTint(first.SourceNode, out tint);
        }

        private static bool TryReadPaint(Paint paint, bool checkBlend, ref bool hasReference, ref UnityEngine.Color reference)
        {
            if (paint == null || !paint.visible) return true;
            var alpha = paint.color?.a ?? 1f;
            if (paint.opacity * alpha <= 0f) return true;

            if (paint.type != Paint.PaintType.SOLID) return false;
            if (checkBlend && !IsPlainBlend(paint.blendMode)) return false;

            var color = paint.color == null
                ? UnityEngine.Color.white
                : new UnityEngine.Color(paint.color.r, paint.color.g, paint.color.b, 1f);
            if (!hasReference)
            {
                hasReference = true;
                reference = color;
                return true;
            }

            return To8Bit(color.r) == To8Bit(reference.r)
                   && To8Bit(color.g) == To8Bit(reference.g)
                   && To8Bit(color.b) == To8Bit(reference.b);
        }

        private static Paint DrawableStroke(Node node)
        {
            if (node.strokes == null || node.strokeWeight <= 0f) return null;
            if (node.strokeAlign == Node.StrokeAlign.OUTSIDE) return null;
            Paint top = null;
            foreach (var stroke in node.strokes)
            {
                if (stroke == null || !stroke.visible || FigmaDataUtils.IsShaderPaint(stroke)) continue;
                if (stroke.type == Paint.PaintType.IMAGE || stroke.type == Paint.PaintType.PATTERN) continue;
                top = stroke;
            }
            return top;
        }

        private static bool IsPlainBlend(BlendMode mode)
        {
            return mode == BlendMode.NORMAL || mode == BlendMode.PASS_THROUGH;
        }

        private static int To8Bit(float channel)
        {
            return Mathf.RoundToInt(channel * 255f);
        }
    }
}
