using UnityEngine;
using UnityEngine.UI;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Settings;
using Color = UnityEngine.Color;

namespace UnityFigmaBridge.Editor.Nodes
{
    /// <summary>
    ///     The drop shadow of a frame that has no fill of its own, only a stroke, while a child fills
    ///     the box inside the stroke (a card: a brown ring around a gradient panel). Figma casts the
    ///     shadow from the whole card. A UI.Shadow on the frame's Image only copies the ring, which is
    ///     hollow, so the shadow showed as a ring with a gap under the stroke. This draws the shadow of
    ///     the node's rounded box, cut by the box itself (it is not seen through an opaque card), into
    ///     a child Image that reaches past the rect and is drawn before the children.
    /// </summary>
    public static class ContentShadow
    {
        public const string ChildName = "[Shadow]";

        /// <summary>Levels of frames below the node searched for the fill that covers it.</summary>
        private const int SearchDepth = 3;

        /// <summary>Design units a covering fill may fall short of the box inside the stroke.</summary>
        private const float Tolerance = 1f;

        /// <summary>
        ///     True when the node's own Image is only its stroke and a descendant fill covers the rest
        ///     of the box. A node that clips its content keeps UI.Shadow: its mask would hide a child
        ///     drawn past the rect.
        /// </summary>
        public static bool Applies(Node node)
        {
            if (node.size == null || node.absoluteBoundingBox == null || node.clipsContent) return false;
            if (node.children is not { Length: > 0 } || DrawsFill(node) || !FrameShapeSprite.HasStroke(node)) return false;
            var share = node.strokeAlign switch
            {
                Node.StrokeAlign.INSIDE => 1f,
                Node.StrokeAlign.CENTER => 0.5f,
                _ => 0f
            };
            var sides = FrameShapeSprite.StrokeSides(node) * share;
            var box = node.absoluteBoundingBox;
            var inner = Rect.MinMaxRect(box.x + sides.x, box.y + sides.y, box.x + box.width - sides.z, box.y + box.height - sides.w);
            return CoveredBy(node.children, inner, SearchDepth);
        }

        /// <summary>
        ///     Draws the shadow child of drop shadow number <paramref name="index"/>. False when there
        ///     is nothing to draw past the box (no offset, spread or blur).
        /// </summary>
        public static bool Apply(GameObject nodeGameObject, Node node, Effect effect, int index, FigmaImportProcessData figmaImportProcessData)
        {
            var name = index == 0 ? ChildName : $"[Shadow {index + 1}]";
            var existing = nodeGameObject.transform.Find(name);
            var sprite = Build(node, effect, figmaImportProcessData.Settings, out var outsets);
            if (sprite == null)
            {
                // A child of a prefab instance cannot be destroyed, so an unused shadow is hidden
                if (existing != null) existing.gameObject.SetActive(false);
                return false;
            }

            var shadowObject = existing != null
                ? existing.gameObject
                : new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            shadowObject.SetActive(true);
            var rect = (RectTransform)shadowObject.transform;
            if (existing == null) rect.SetParent(nodeGameObject.transform, false);
            rect.SetAsFirstSibling();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(-outsets.x, -outsets.w);
            rect.offsetMax = new Vector2(outsets.z, outsets.y);

            var image = shadowObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = Color.white;
            image.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.pixelsPerUnitMultiplier = 1f;
            image.raycastTarget = false;
            shadowObject.GetComponent<LayoutElement>().ignoreLayout = true;
            return true;
        }

        /// <summary>
        ///     The shadow of the rounded box, offset, spread and blurred as Figma does, times what the
        ///     box leaves uncovered. <paramref name="outsets"/> is how far the sprite reaches past the
        ///     box, as (left, top, right, bottom).
        /// </summary>
        private static Sprite Build(Node node, Effect effect, UnityFigmaBridgeSettings settings, out Vector4 outsets)
        {
            outsets = Vector4.zero;
            var width = Mathf.RoundToInt(node.size.x);
            var height = Mathf.RoundToInt(node.size.y);
            if (width < 1 || height < 1) return null;

            var offset = effect.offset != null ? new Vector2(effect.offset.x, effect.offset.y) : Vector2.zero;
            var spread = effect.spread;
            var blurReach = Mathf.CeilToInt(Mathf.Max(0f, effect.radius) * 1.5f);
            outsets = new Vector4(
                Mathf.Max(0, Mathf.CeilToInt(spread - offset.x) + blurReach),
                Mathf.Max(0, Mathf.CeilToInt(spread - offset.y) + blurReach),
                Mathf.Max(0, Mathf.CeilToInt(spread + offset.x) + blurReach),
                Mathf.Max(0, Mathf.CeilToInt(spread + offset.y) + blurReach));
            if (outsets == Vector4.zero) return null;

            var canvasWidth = width + (int)outsets.x + (int)outsets.z;
            var canvasHeight = height + (int)outsets.y + (int)outsets.w;
            if (canvasWidth > 4096 || canvasHeight > 4096) return null;

            var size = new Vector2(width, height);
            var radii = FrameShapeSprite.CornerRadii(node);
            var shadowRadii = new Vector4(Grow(radii.x, spread), Grow(radii.y, spread), Grow(radii.z, spread), Grow(radii.w, spread));
            var shadowHalf = Vector2.Max(Vector2.zero, size * 0.5f + Vector2.one * spread);
            // Figma space, top-left origin of the canvas
            var boxCentre = new Vector2(outsets.x, outsets.y) + size * 0.5f;
            var shadowCentre = boxCentre + offset;

            var cast = new float[canvasWidth * canvasHeight];
            for (var row = 0; row < canvasHeight; row++)
            for (var x = 0; x < canvasWidth; x++)
            {
                var figmaPoint = new Vector2(x + 0.5f, canvasHeight - row - 0.5f);
                cast[row * canvasWidth + x] = FrameShapeSprite.Coverage(figmaPoint - shadowCentre, shadowHalf, shadowRadii);
            }
            cast = FrameShapeSprite.Blur(cast, canvasWidth, canvasHeight, Mathf.Max(0f, effect.radius) * 0.5f);

            var colour = effect.color != null ? FigmaDataUtils.ToUnityColor(effect.color) : new Color(0f, 0f, 0f, 0.25f);
            var pixels = new Color32[cast.Length];
            var drawn = false;
            for (var row = 0; row < canvasHeight; row++)
            for (var x = 0; x < canvasWidth; x++)
            {
                var figmaPoint = new Vector2(x + 0.5f, canvasHeight - row - 0.5f);
                var uncovered = 1f - FrameShapeSprite.Coverage(figmaPoint - boxCentre, size * 0.5f, radii);
                var alpha = colour.a * cast[row * canvasWidth + x] * uncovered;
                if (alpha > 0.002f) drawn = true;
                pixels[row * canvasWidth + x] = new Color(colour.r, colour.g, colour.b, alpha);
            }
            if (!drawn) return null;

            // Guard past the widest corner and the shadow's own reach on each side
            var reach = Mathf.CeilToInt(Mathf.Abs(spread)) + blurReach + 1;
            var guard = new Vector4(
                outsets.x + Mathf.CeilToInt(Mathf.Max(radii.x, radii.w) + Mathf.Abs(offset.x)) + reach,
                outsets.w + Mathf.CeilToInt(Mathf.Max(radii.w, radii.z) + Mathf.Abs(offset.y)) + reach,
                outsets.z + Mathf.CeilToInt(Mathf.Max(radii.y, radii.z) + Mathf.Abs(offset.x)) + reach,
                outsets.y + Mathf.CeilToInt(Mathf.Max(radii.x, radii.y) + Mathf.Abs(offset.y)) + reach);
            return FrameShapeSprite.Save(pixels, canvasWidth, canvasHeight, guard, settings);
        }

        private static float Grow(float radius, float spread) => radius > 0f ? Mathf.Max(0f, radius + spread) : 0f;

        private static bool CoveredBy(Node[] children, Rect inner, int depth)
        {
            if (children == null || depth <= 0) return false;
            foreach (var child in children)
            {
                if (child == null || !child.visible || child.opacity < 0.99f || child.absoluteBoundingBox == null) continue;
                var box = child.absoluteBoundingBox;
                var covers = box.x <= inner.xMin + Tolerance && box.y <= inner.yMin + Tolerance &&
                             box.x + box.width >= inner.xMax - Tolerance && box.y + box.height >= inner.yMax - Tolerance;
                if (!covers) continue;
                if (DrawsFill(child, true)) return true;
                if (CoveredBy(child.children, inner, depth - 1)) return true;
            }
            return false;
        }

        /// <summary>A visible fill that draws; <paramref name="opaque"/> also asks for a solid one to be opaque.</summary>
        private static bool DrawsFill(Node node, bool opaque = false)
        {
            if (node.fills == null) return false;
            foreach (var fill in node.fills)
            {
                if (fill == null || !fill.visible || FigmaDataUtils.IsShaderPaint(fill)) continue;
                var alpha = fill.opacity * (fill.type == Paint.PaintType.SOLID && fill.color != null ? fill.color.a : 1f);
                if (alpha >= (opaque ? 0.99f : 0.001f)) return true;
            }
            return false;
        }
    }
}
