using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using FigCraftBridge.Editor.FigmaApi;
using Color = UnityEngine.Color;

namespace FigCraftBridge.Editor.Nodes
{
    /// <summary>
    ///     A node's stroke as a child Image. FigCraft draws a frame's stroke above its children, so a
    ///     node with children gets its whole stroke band here, drawn last, and its own shape sprite
    ///     keeps only the fills. A node without children keeps its INSIDE band in its own sprite;
    ///     this ring then holds only the OUTSIDE or CENTER part past the box, drawn first.
    /// </summary>
    public static class OuterStroke
    {
        public const string ChildName = "[Stroke]";

        /// <summary>How far the stroke reaches past the node's box on its widest side.</summary>
        public static float Outset(Node node)
        {
            var outsets = Outsets(node);
            return Mathf.Max(outsets.x, outsets.y, outsets.z, outsets.w);
        }

        /// <summary>How far the stroke reaches past the node's box per side, as (left, top, right, bottom).</summary>
        public static Vector4 Outsets(Node node)
        {
            if (node.strokes == null) return Vector4.zero;
            if (!node.strokes.Any(s => s != null && s.visible && !FigmaDataUtils.IsShaderPaint(s))) return Vector4.zero;
            return node.strokeAlign switch
            {
                Node.StrokeAlign.OUTSIDE => FrameShapeSprite.StrokeSides(node),
                Node.StrokeAlign.CENTER => FrameShapeSprite.StrokeSides(node) * 0.5f,
                _ => Vector4.zero
            };
        }

        /// <summary>True when the stroke goes in its own child, drawn above the node's children.</summary>
        public static bool DrawsAboveChildren(Node node) =>
            node.children is { Length: > 0 } && node.size != null && FrameShapeSprite.HasStroke(node);

        /// <summary>The node without its strokes: what its own Image draws when the stroke is on top.</summary>
        public static Node FillsOnly(Node node)
        {
            var copy = node.ShallowCopy();
            copy.strokes = Array.Empty<Paint>();
            copy.strokeWeight = 0f;
            return copy;
        }

        public static void Apply(GameObject nodeGameObject, Node node, FigmaImportProcessData figmaImportProcessData)
        {
            var existing = nodeGameObject.transform.Find(ChildName);
            var onTop = DrawsAboveChildren(node);
            var outsets = Outsets(node);
            var band = onTop ? FrameShapeSprite.StrokeSides(node) : outsets;
            var ring = band != Vector4.zero && node.size != null ? Ring(node, outsets, band) : null;
            var sprite = ring != null ? FrameShapeSprite.Build(ring, figmaImportProcessData.Settings) : null;
            if (sprite == null)
            {
                // A child of a prefab instance cannot be destroyed, so an unused ring is hidden
                if (existing != null) existing.gameObject.SetActive(false);
                return;
            }

            var ringObject = existing != null
                ? existing.gameObject
                : new GameObject(ChildName, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            ringObject.SetActive(true);
            var rect = (RectTransform)ringObject.transform;
            if (existing == null) rect.SetParent(nodeGameObject.transform, false);
            if (onTop) rect.SetAsLastSibling();
            else rect.SetAsFirstSibling();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(-outsets.x, -outsets.w);
            rect.offsetMax = new Vector2(outsets.z, outsets.y);

            var image = ringObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = SolidTint.TryGetShapeTint(ring, out var tint) ? tint : Color.white;
            image.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.pixelsPerUnitMultiplier = 1f;
            image.raycastTarget = false;
            ringObject.GetComponent<LayoutElement>().ignoreLayout = true;

            if (outsets != Vector4.zero && nodeGameObject.GetComponent<Mask>() != null)
                Debug.LogWarning($"[OuterStroke] '{node.name}' clips its content with a rounded mask, which also hides its {node.strokeAlign} stroke past the box.", nodeGameObject);
        }

        /// <summary>
        ///     Children are built after the node's own Image, so the ring that draws above them is
        ///     moved behind the last child once they exist.
        /// </summary>
        public static void KeepAboveChildren(GameObject nodeGameObject, Node node)
        {
            if (!DrawsAboveChildren(node)) return;
            var ring = nodeGameObject.transform.Find(ChildName);
            if (ring != null) ring.SetAsLastSibling();
        }

        /// <summary>
        ///     A stroke-only node the size of the stroke's outer edge, whose INSIDE stroke of widths
        ///     <paramref name="band"/> is the part of the stroke this ring draws. Both are per side,
        ///     as (left, top, right, bottom).
        /// </summary>
        private static Node Ring(Node node, Vector4 outsets, Vector4 band)
        {
            var outset = Mathf.Max(outsets.x, outsets.y, outsets.z, outsets.w);
            float Grow(float radius) => radius > 0f ? radius + outset : 0f;
            return new Node
            {
                name = node.name,
                type = node.type,
                size = new Vector { x = node.size.x + outsets.x + outsets.z, y = node.size.y + outsets.y + outsets.w },
                fills = Array.Empty<Paint>(),
                strokes = node.strokes,
                strokeWeight = Mathf.Max(band.x, band.y, band.z, band.w),
                individualStrokeWeights = new IndividualStrokeWeights { left = band.x, top = band.y, right = band.z, bottom = band.w },
                strokeAlign = Node.StrokeAlign.INSIDE,
                cornerRadius = Grow(node.cornerRadius),
                rectangleCornerRadii = node.rectangleCornerRadii?.Select(Grow).ToArray()
            };
        }
    }
}
