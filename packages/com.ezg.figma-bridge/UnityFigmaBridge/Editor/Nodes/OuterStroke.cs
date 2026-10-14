using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityFigmaBridge.Editor.FigmaApi;
using Color = UnityEngine.Color;

namespace UnityFigmaBridge.Editor.Nodes
{
    /// <summary>
    ///     The part of an OUTSIDE or CENTER stroke that lies past a node's box. The node's own shape
    ///     sprite stops at its rect, so this ring is a child Image that reaches past the rect on every
    ///     side, drawn first so the node's children stay on top.
    /// </summary>
    public static class OuterStroke
    {
        public const string ChildName = "[Stroke]";

        /// <summary>How far the stroke reaches past the node's box.</summary>
        public static float Outset(Node node)
        {
            if (node.strokes == null || node.strokeWeight <= 0f) return 0f;
            if (!node.strokes.Any(s => s != null && s.visible && !FigmaDataUtils.IsShaderPaint(s))) return 0f;
            return node.strokeAlign switch
            {
                Node.StrokeAlign.OUTSIDE => node.strokeWeight,
                Node.StrokeAlign.CENTER => node.strokeWeight * 0.5f,
                _ => 0f
            };
        }

        public static void Apply(GameObject nodeGameObject, Node node, FigmaImportProcessData figmaImportProcessData)
        {
            var existing = nodeGameObject.transform.Find(ChildName);
            var outset = Outset(node);
            var sprite = outset > 0f && node.size != null
                ? FrameShapeSprite.Build(Ring(node, outset), figmaImportProcessData.Settings)
                : null;
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
            rect.SetAsFirstSibling();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = -Vector2.one * outset;
            rect.offsetMax = Vector2.one * outset;

            var image = ringObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = SolidTint.TryGetShapeTint(Ring(node, outset), out var tint) ? tint : Color.white;
            image.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.pixelsPerUnitMultiplier = 1f;
            image.raycastTarget = false;
            ringObject.GetComponent<LayoutElement>().ignoreLayout = true;

            if (nodeGameObject.GetComponent<Mask>() != null)
                Debug.LogWarning($"[OuterStroke] '{node.name}' clips its content with a rounded mask, which also hides its {node.strokeAlign} stroke past the box.", nodeGameObject);
        }

        /// <summary>A stroke-only node the size of the outer edge, whose INSIDE stroke is the outer band.</summary>
        private static Node Ring(Node node, float outset)
        {
            float Grow(float radius) => radius > 0f ? radius + outset : 0f;
            return new Node
            {
                name = node.name,
                type = node.type,
                size = new Vector { x = node.size.x + 2f * outset, y = node.size.y + 2f * outset },
                fills = Array.Empty<Paint>(),
                strokes = node.strokes,
                strokeWeight = outset,
                strokeAlign = Node.StrokeAlign.INSIDE,
                cornerRadius = Grow(node.cornerRadius),
                rectangleCornerRadii = node.rectangleCornerRadii?.Select(Grow).ToArray()
            };
        }
    }
}
