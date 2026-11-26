using System.Collections.Generic;
using UnityFigmaBridge.Editor.FigmaApi;

namespace UnityFigmaBridge.Editor.Nodes
{
    /// <summary>
    ///     A childless rounded (or stroked, or gradient) shape collapsed to under one unit along one
    ///     axis, such as the "Use as mask" value layer of a progress bar's 0 % variant (0.01 x 28).
    ///     Figma has nothing to render there and returns a 1 px transparent strip, so code that widens
    ///     the layer at runtime gets a mask that writes no alpha and the masked fill never shows.
    ///     Such a shape is drawn locally by <see cref="FrameShapeSprite"/> instead, at the size of the
    ///     box it sits in along the collapsed axis (Figma clamps the corners against that size when
    ///     the layer grows to fill it), 9-sliced, so any size set later draws the shape's own corners
    ///     and the collapsed size itself still draws nothing.
    /// </summary>
    public static class CollapsedShape
    {
        private const float CollapsedBelow = 1f;

        /// <summary>True for a node <see cref="FrameShapeSprite"/> draws that is collapsed along exactly one axis.</summary>
        public static bool Applies(Node node) =>
            IsCollapsed(node) && !FigmaDataUtils.NeedsShaderRender(node) && FrameShapeSprite.IsNeeded(node);

        /// <summary>
        ///     The node to draw for <paramref name="node"/>: when it is collapsed, a copy grown along the
        ///     collapsed axis to the size of <paramref name="parentNode"/> on that axis (or to its other
        ///     side when the parent has no size there). Figma reports the corners of a layer inside a
        ///     scaled instance clamped to its own size, so a collapsed one reports none; its radii then
        ///     come from the component-side node in <paramref name="nodeLookup"/>. Returns the node
        ///     itself when none of this applies.
        /// </summary>
        public static Node Expanded(Node node, Node parentNode, Dictionary<string, Node> nodeLookup = null)
        {
            if (!IsCollapsed(node) || FigmaDataUtils.NeedsShaderRender(node)) return node;
            var copy = node.ShallowCopy();
            var componentSide = ComponentSideNode(node, nodeLookup);
            if (componentSide != null && FigmaDataUtils.MaxCornerRadius(node) < CollapsedBelow * 0.5f &&
                FigmaDataUtils.MaxCornerRadius(componentSide) > FigmaDataUtils.MaxCornerRadius(node))
            {
                copy.cornerRadius = componentSide.cornerRadius;
                copy.rectangleCornerRadii = componentSide.rectangleCornerRadii;
            }
            if (!FrameShapeSprite.IsNeeded(copy)) return node;

            var collapsedX = node.size.x < CollapsedBelow;
            var parentSize = parentNode?.size == null ? 0f : collapsedX ? parentNode.size.x : parentNode.size.y;
            var other = collapsedX ? node.size.y : node.size.x;
            var grown = parentSize >= CollapsedBelow ? parentSize : other;
            copy.size = collapsedX ? new Vector { x = grown, y = node.size.y } : new Vector { x = node.size.x, y = grown };
            return copy;
        }

        private static bool IsCollapsed(Node node)
        {
            if (node?.size == null || (node.children != null && node.children.Length > 0)) return false;
            if (node.type is not (NodeType.RECTANGLE or NodeType.FRAME or NodeType.COMPONENT)) return false;
            return (node.size.x < CollapsedBelow) != (node.size.y < CollapsedBelow);
        }

        /// <summary>The node <c>&lt;c&gt;</c> of the component an instance-side id <c>I&lt;a&gt;;…;&lt;c&gt;</c> comes from.</summary>
        private static Node ComponentSideNode(Node node, Dictionary<string, Node> nodeLookup)
        {
            if (nodeLookup == null || string.IsNullOrEmpty(node.id) || !node.id.StartsWith("I")) return null;
            var separator = node.id.LastIndexOf(';');
            if (separator < 0) return null;
            return nodeLookup.TryGetValue(node.id.Substring(separator + 1), out var componentNode) ? componentNode : null;
        }
    }
}
