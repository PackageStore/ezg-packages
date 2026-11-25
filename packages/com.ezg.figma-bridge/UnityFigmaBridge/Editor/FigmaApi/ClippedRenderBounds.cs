using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityFigmaBridge.Editor.Utils;

namespace UnityFigmaBridge.Editor.FigmaApi
{
    /// <summary>
    ///     Figma clips a node's <c>absoluteRenderBounds</c> by the masks and clipping frames above it,
    ///     but a server render of the node is not clipped: a stripe fill 748 px wide under a 40 %
    ///     progress mask reports 299 px of render bounds and renders 748 px, and the render was squeezed
    ///     into 299 px (a cloud cut by its card's clip frame drew about 8 % squashed). UGUI clips the
    ///     same way (the mask's or frame's Mask), so a render takes its unclipped box: on an axis where
    ///     the PNG is wider than the bounds, the bounds grow to the PNG's size from the side they were
    ///     not cut on (the placement whose centre is nearer the node's own box). The node's bounds are
    ///     corrected in memory before the renders are processed, so the slicer, the placement and the
    ///     visual check all read the same box.
    /// </summary>
    public static class ClippedRenderBounds
    {
        /// <summary>A PNG is rounded to whole pixels; less than this past the bounds is no clip.</summary>
        private const float Tolerance = 1f;

        public static void Run(FigmaFile file, List<ServerRenderNodeData> renderNodes, int serverRenderScale)
        {
            var scale = serverRenderScale > 0 ? serverRenderScale : 1;
            var corrected = 0;
            foreach (var renderNode in renderNodes)
            {
                if (renderNode?.SourceNode == null || renderNode.RenderType != ServerRenderType.Substitution) continue;
                var node = renderNode.SourceNode;
                var bounds = node.absoluteRenderBounds;
                var box = node.absoluteBoundingBox;
                if (bounds == null || box == null) continue;
                var path = FigmaPaths.GetPathForServerRenderedImage(node.id, renderNodes);
                if (!TryReadPngSize(path, out var pixelWidth, out var pixelHeight)) continue;

                var width = pixelWidth / (float)scale;
                var height = pixelHeight / (float)scale;
                var grown = false;
                var x = Unclip(bounds.x, bounds.width, width, box.x, box.width, ref grown);
                var y = Unclip(bounds.y, bounds.height, height, box.y, box.height, ref grown);
                if (!grown) continue;
                node.absoluteRenderBounds = new Rectangle
                {
                    x = x,
                    y = y,
                    width = Mathf.Max(bounds.width, width),
                    height = Mathf.Max(bounds.height, height)
                };
                corrected++;
            }
            var kept = file?.document != null ? GrowKeptRenders(file.document, renderNodes) : 0;
            if (corrected + kept > 0)
                Debug.Log($"[FigmaBridge] {corrected} clipped render bound(s) grown to their unclipped render, {kept} instance layer(s) showing one");
        }

        /// <summary>
        ///     A layer inside an instance without a render of its own shows its component's render, placed
        ///     by the layer's own render bounds, which a clip above the instance cuts too (a card cloud
        ///     cut by the card's frame on every screen card). Such a layer takes the box the render has
        ///     around its node: its own box grown by the render's outsets. Never shrinks the bounds.
        /// </summary>
        private static int GrowKeptRenders(Node root, List<ServerRenderNodeData> renderNodes)
        {
            var renderById = new Dictionary<string, Node>();
            foreach (var renderNode in renderNodes)
                if (renderNode?.SourceNode != null && renderNode.RenderType == ServerRenderType.Substitution)
                    renderById[renderNode.SourceNode.id] = renderNode.SourceNode;
            var grown = 0;
            var stack = new Stack<Node>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node.children != null) foreach (var child in node.children) if (child != null) stack.Push(child);
                if (node.absoluteRenderBounds == null || node.absoluteBoundingBox == null) continue;
                if (node.id.IndexOf(';') < 0 || renderById.ContainsKey(node.id)) continue;
                var source = KeptRenderNode(node.id, renderById);
                if (source?.absoluteRenderBounds == null || source.absoluteBoundingBox == null) continue;
                var box = node.absoluteBoundingBox;
                var sourceBox = source.absoluteBoundingBox;
                var sourceBounds = source.absoluteRenderBounds;
                var left = sourceBox.x - sourceBounds.x;
                var top = sourceBox.y - sourceBounds.y;
                var right = sourceBounds.x + sourceBounds.width - (sourceBox.x + sourceBox.width);
                var bottom = sourceBounds.y + sourceBounds.height - (sourceBox.y + sourceBox.height);
                var bounds = node.absoluteRenderBounds;
                var x0 = Mathf.Min(bounds.x, box.x - left);
                var y0 = Mathf.Min(bounds.y, box.y - top);
                var x1 = Mathf.Max(bounds.x + bounds.width, box.x + box.width + right);
                var y1 = Mathf.Max(bounds.y + bounds.height, box.y + box.height + bottom);
                if (x1 - x0 <= bounds.width + Tolerance && y1 - y0 <= bounds.height + Tolerance) continue;
                node.absoluteRenderBounds = new Rectangle { x = x0, y = y0, width = x1 - x0, height = y1 - y0 };
                grown++;
            }
            return grown;
        }

        /// <summary>The render a layer inside an instance keeps: the nearest component's, else deeper ones.</summary>
        private static Node KeptRenderNode(string nodeId, Dictionary<string, Node> renderById)
        {
            for (var id = FigmaDataUtils.NearestComponentSideId(nodeId); id != null; id = FigmaDataUtils.NearestComponentSideId(id))
                if (renderById.TryGetValue(id, out var render)) return render;
            var separator = nodeId.LastIndexOf(';');
            return renderById.TryGetValue(nodeId.Substring(separator + 1), out var innermost) ? innermost : null;
        }

        /// <summary>The start of one axis of the unclipped box.</summary>
        private static float Unclip(float start, float length, float renderLength, float boxStart, float boxLength, ref bool grown)
        {
            if (renderLength <= length + Tolerance) return start;
            grown = true;
            var boxCentre = boxStart + boxLength * 0.5f;
            var fromStart = start + renderLength * 0.5f;
            var fromEnd = start + length - renderLength * 0.5f;
            return Mathf.Abs(fromStart - boxCentre) <= Mathf.Abs(fromEnd - boxCentre) ? start : start + length - renderLength;
        }

        /// <summary>Width and height from the PNG header, without importing the texture.</summary>
        private static bool TryReadPngSize(string path, out int width, out int height)
        {
            width = height = 0;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            var header = new byte[24];
            using (var stream = File.OpenRead(path))
                if (stream.Read(header, 0, header.Length) < header.Length) return false;
            if (header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47) return false;
            width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
            return width > 0 && height > 0;
        }
    }
}
