using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.NineSlice;
using UnityFigmaBridge.Editor.Settings;
using UnityFigmaBridge.Editor.Utils;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;

namespace UnityFigmaBridge.Editor.Nodes
{
    /// <summary>
    ///     The own background of a node with children: its visible solid and gradient fills and its
    ///     stroke, clipped to its corner radii, drawn locally into a 9-sliced sprite. A server render
    ///     would also hold the children, which then draw twice. Drawn at one texel per design unit and
    ///     imported at 100 pixels per unit, like a server render. Sprites are named by content hash,
    ///     so every node with the same look shares one file.
    /// </summary>
    public static class FrameShapeSprite
    {
        private const float ReferencePixelsPerUnit = 100f;
        private const string ShapeFolderName = "Shapes";
        private const int MaxTexels = 4096;

        /// <summary>
        ///     True when a flat Image colour cannot draw the node: a corner radius, a gradient, a
        ///     stroke, or more than one visible fill. Bitmap fills keep their own sprite.
        /// </summary>
        public static bool IsNeeded(Node node)
        {
            switch (node.type)
            {
                case NodeType.FRAME:
                case NodeType.COMPONENT:
                case NodeType.INSTANCE:
                case NodeType.SECTION:
                case NodeType.RECTANGLE:
                    break;
                default:
                    return false;
            }
            if (node.fills == null) return false;
            var visibleFills = 0;
            var hasGradient = false;
            foreach (var fill in node.fills)
            {
                if (fill == null || !fill.visible) continue;
                if (fill.type == Paint.PaintType.IMAGE || fill.type == Paint.PaintType.PATTERN) return false;
                if (FigmaDataUtils.IsGradient(fill)) hasGradient = true;
                visibleFills++;
            }
            var hasStroke = VisibleStroke(node) != null;
            var hasInnerShadow = InnerShadows(node).Count > 0;
            if (visibleFills == 0 && !hasStroke && !hasInnerShadow) return false;
            return hasGradient || hasStroke || hasInnerShadow || visibleFills > 1 || FigmaDataUtils.MaxCornerRadius(node) > 0f;
        }

        public static Sprite Build(Node node, UnityFigmaBridgeSettings settings)
        {
            if (node.size == null) return null;
            var width = Mathf.RoundToInt(node.size.x);
            var height = Mathf.RoundToInt(node.size.y);
            if (width < 1 || height < 1 || width > MaxTexels || height > MaxTexels) return null;

            var radii = CornerRadii(node);
            var stroke = VisibleStroke(node);
            var strokeWidth = stroke == null ? 0f : node.strokeAlign switch
            {
                Node.StrokeAlign.INSIDE => node.strokeWeight,
                Node.StrokeAlign.CENTER => node.strokeWeight * 0.5f,
                _ => 0f
            };
            if (stroke != null && node.strokeAlign != Node.StrokeAlign.INSIDE)
                Debug.LogWarning($"[FrameShapeSprite] '{node.name}' has a {node.strokeAlign} stroke: only the part inside its box is drawn.");

            var pixels = Draw(node, width, height, radii, stroke, strokeWidth);

            // One guard texel past the widest corner or stroke, for the anti-aliased edge
            var left = Mathf.CeilToInt(Mathf.Max(radii.x, radii.w, strokeWidth)) + 1;
            var right = Mathf.CeilToInt(Mathf.Max(radii.y, radii.z, strokeWidth)) + 1;
            var top = Mathf.CeilToInt(Mathf.Max(radii.x, radii.y, strokeWidth)) + 1;
            var bottom = Mathf.CeilToInt(Mathf.Max(radii.w, radii.z, strokeWidth)) + 1;
            if (left + right >= width) left = right = 0;
            if (top + bottom >= height) top = bottom = 0;
            var slice = ServerRenderSlicer.Slice(pixels, width, height, new Vector4(left, bottom, right, top));

            var png = ServerRenderSlicer.EncodePng(slice);
            var folder = $"{FigmaPaths.FigmaImageFillFolder}/{ShapeFolderName}";
            var path = $"{folder}/Shape-{Hash(png, slice.Border)}.png";

            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            Directory.CreateDirectory(folder);
            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                var textureSettings = new TextureImporterSettings();
                importer.ReadTextureSettings(textureSettings);
                textureSettings.spriteMeshType = SpriteMeshType.FullRect;
                textureSettings.spriteBorder = slice.Border;
                textureSettings.spritePixelsPerUnit = ReferencePixelsPerUnit;
                importer.SetTextureSettings(textureSettings);
                SpritePlatformOverride.Apply(importer, settings);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static List<Effect> InnerShadows(Node node)
        {
            var shadows = new List<Effect>();
            if (node.effects == null) return shadows;
            foreach (var effect in node.effects)
                if (effect.visible && effect.type == Effect.EffectType.INNER_SHADOW) shadows.Add(effect);
            return shadows;
        }

        private static Paint VisibleStroke(Node node)
        {
            if (node.strokes == null || node.strokeWeight <= 0f) return null;
            Paint top = null;
            foreach (var stroke in node.strokes)
            {
                if (stroke == null || !stroke.visible) continue;
                if (stroke.type == Paint.PaintType.IMAGE || stroke.type == Paint.PaintType.PATTERN) continue;
                top = stroke;
            }
            return top;
        }

        /// <summary>Radii as (top left, top right, bottom right, bottom left), clamped to half the short side as Figma does.</summary>
        private static Vector4 CornerRadii(Node node)
        {
            var radii = node.rectangleCornerRadii is { Length: 4 }
                ? new Vector4(node.rectangleCornerRadii[0], node.rectangleCornerRadii[1], node.rectangleCornerRadii[2], node.rectangleCornerRadii[3])
                : Vector4.one * Mathf.Max(0f, node.cornerRadius);
            var limit = Mathf.Min(node.size.x, node.size.y) * 0.5f;
            for (var i = 0; i < 4; i++) radii[i] = Mathf.Clamp(radii[i], 0f, limit);
            return radii;
        }

        /// <summary>
        ///     Fills are composited bottom to top in straight alpha, the stroke over them, and the result
        ///     is clipped to the rounded box. Texture rows run bottom up; Figma space runs top down.
        /// </summary>
        private static Color32[] Draw(Node node, int width, int height, Vector4 radii, Paint stroke, float strokeWidth)
        {
            var pixels = new Color32[width * height];
            var size = new Vector2(width, height);
            var innerShadows = InnerShadowLayers(node, width, height, radii);
            var innerRadii = new Vector4(
                Mathf.Max(0f, radii.x - strokeWidth), Mathf.Max(0f, radii.y - strokeWidth),
                Mathf.Max(0f, radii.z - strokeWidth), Mathf.Max(0f, radii.w - strokeWidth));
            var innerSize = size - Vector2.one * (2f * strokeWidth);

            for (var row = 0; row < height; row++)
            for (var x = 0; x < width; x++)
            {
                var figmaPoint = new Vector2(x + 0.5f, height - row - 0.5f);
                var normalised = new Vector2(figmaPoint.x / width, figmaPoint.y / height);

                var colour = new Color(0f, 0f, 0f, 0f);
                foreach (var fill in node.fills)
                {
                    if (fill == null || !fill.visible) continue;
                    colour = Over(PaintColour(fill, normalised), colour);
                }

                foreach (var layer in innerShadows)
                {
                    var shadowColour = layer.Colour;
                    shadowColour.a *= layer.Alpha[row * width + x];
                    colour = Over(shadowColour, colour);
                }

                var coverage = Coverage(figmaPoint - size * 0.5f, size * 0.5f, radii);
                if (stroke != null && strokeWidth > 0f)
                {
                    var inner = innerSize.x > 0f && innerSize.y > 0f
                        ? Coverage(figmaPoint - size * 0.5f, innerSize * 0.5f, innerRadii)
                        : 0f;
                    var strokeColour = PaintColour(stroke, normalised);
                    // Share of the covered area that is stroke; the coverage itself is applied once, below
                    strokeColour.a *= coverage > 0f ? Mathf.Clamp01((coverage - inner) / coverage) : 0f;
                    colour = Over(strokeColour, colour);
                }
                colour.a *= coverage;
                pixels[row * width + x] = colour;
            }
            return pixels;
        }

        private struct ShadowLayer
        {
            public Color Colour;
            /// <summary>Per texel, texture rows bottom up.</summary>
            public float[] Alpha;
        }

        /// <summary>
        ///     Figma casts an inner shadow from outside the shape inward: the outside is offset, spread
        ///     and blurred, and only its part inside the shape shows. The outside reaches past the
        ///     texture, so the work area is padded with outside on every side.
        /// </summary>
        private static List<ShadowLayer> InnerShadowLayers(Node node, int width, int height, Vector4 radii)
        {
            var layers = new List<ShadowLayer>();
            foreach (var effect in InnerShadows(node))
            {
                var offset = effect.offset != null ? new Vector2(effect.offset.x, -effect.offset.y) : Vector2.zero;
                var pad = Mathf.CeilToInt(effect.radius * 1.5f + Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y)) + Mathf.Abs(effect.spread)) + 1;
                var paddedWidth = width + 2 * pad;
                var paddedHeight = height + 2 * pad;
                var halfSize = new Vector2(width, height) * 0.5f;
                var outside = new float[paddedWidth * paddedHeight];
                for (var row = 0; row < paddedHeight; row++)
                for (var x = 0; x < paddedWidth; x++)
                {
                    var figmaPoint = new Vector2(x - pad + 0.5f, height - (row - pad) - 0.5f);
                    outside[row * paddedWidth + x] = 1f - Coverage(figmaPoint - halfSize, halfSize, radii);
                }
                var shadow = Blur(
                    Spread(Shift(outside, paddedWidth, paddedHeight, offset, 1f), paddedWidth, paddedHeight, effect.spread),
                    paddedWidth, paddedHeight, effect.radius * 0.5f);
                var alpha = new float[width * height];
                for (var row = 0; row < height; row++)
                for (var x = 0; x < width; x++)
                    alpha[row * width + x] = shadow[(row + pad) * paddedWidth + x + pad];
                layers.Add(new ShadowLayer { Colour = FigmaDataUtils.ToUnityColor(effect.color), Alpha = alpha });
            }
            return layers;
        }

        /// <summary>Moves the values by a whole texel offset; texels shifted in from outside take <paramref name="outside"/>.</summary>
        private static float[] Shift(float[] values, int width, int height, Vector2 offset, float outside)
        {
            var dx = Mathf.RoundToInt(offset.x);
            var dy = Mathf.RoundToInt(offset.y);
            var result = new float[values.Length];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var sx = x - dx;
                var sy = y - dy;
                result[y * width + x] = sx < 0 || sy < 0 || sx >= width || sy >= height ? outside : values[sy * width + sx];
            }
            return result;
        }

        /// <summary>Grows (positive) or shrinks (negative) the covered area by a square max or min filter.</summary>
        private static float[] Spread(float[] values, int width, int height, float spread)
        {
            var reach = Mathf.RoundToInt(Mathf.Abs(spread));
            if (reach == 0) return values;
            var grow = spread > 0f;
            var horizontal = new float[values.Length];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var value = grow ? 0f : 1f;
                for (var k = Mathf.Max(0, x - reach); k <= Mathf.Min(width - 1, x + reach); k++)
                    value = grow ? Mathf.Max(value, values[y * width + k]) : Mathf.Min(value, values[y * width + k]);
                horizontal[y * width + x] = value;
            }
            var result = new float[values.Length];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var value = grow ? 0f : 1f;
                for (var k = Mathf.Max(0, y - reach); k <= Mathf.Min(height - 1, y + reach); k++)
                    value = grow ? Mathf.Max(value, horizontal[k * width + x]) : Mathf.Min(value, horizontal[k * width + x]);
                result[y * width + x] = value;
            }
            return result;
        }

        /// <summary>Separable Gaussian blur; texels past the edge repeat the edge value.</summary>
        private static float[] Blur(float[] values, int width, int height, float sigma)
        {
            if (sigma < 0.3f) return values;
            var reach = Mathf.CeilToInt(sigma * 3f);
            var kernel = new float[reach * 2 + 1];
            var sum = 0f;
            for (var i = -reach; i <= reach; i++) sum += kernel[i + reach] = Mathf.Exp(-(i * i) / (2f * sigma * sigma));
            for (var i = 0; i < kernel.Length; i++) kernel[i] /= sum;

            var horizontal = new float[values.Length];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var value = 0f;
                for (var k = -reach; k <= reach; k++)
                    value += kernel[k + reach] * values[y * width + Mathf.Clamp(x + k, 0, width - 1)];
                horizontal[y * width + x] = value;
            }
            var result = new float[values.Length];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var value = 0f;
                for (var k = -reach; k <= reach; k++)
                    value += kernel[k + reach] * horizontal[Mathf.Clamp(y + k, 0, height - 1) * width + x];
                result[y * width + x] = value;
            }
            return result;
        }

        /// <summary>Anti-aliased coverage of a rounded box centred on the origin, from its signed distance.</summary>
        private static float Coverage(Vector2 point, Vector2 halfSize, Vector4 radii)
        {
            // Figma space runs top down: negative y is the top edge
            var radius = point.y < 0f ? (point.x < 0f ? radii.x : radii.y) : (point.x < 0f ? radii.w : radii.z);
            var q = new Vector2(Mathf.Abs(point.x), Mathf.Abs(point.y)) - halfSize + Vector2.one * radius;
            var distance = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude +
                           Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
            return Mathf.Clamp01(0.5f - distance);
        }

        private static Color PaintColour(Paint paint, Vector2 normalisedPoint)
        {
            if (!FigmaDataUtils.IsGradient(paint)) return FigmaDataUtils.GetUnityFillColor(paint);
            var colour = SampleGradient(paint, GradientPosition(paint, normalisedPoint));
            colour.a *= paint.opacity;
            return colour;
        }

        /// <summary>
        ///     Position along the gradient: handle 0 is the start, handle 1 the end and handle 2 the
        ///     width, all in normalised node space (see Figma's <c>gradientHandlePositions</c>).
        /// </summary>
        public static float GradientPosition(Paint paint, Vector2 point)
        {
            var handles = paint.gradientHandlePositions;
            if (handles == null || handles.Length < 2) return 0f;
            var start = FigmaDataUtils.ToUnityVector(handles[0]);
            var end = FigmaDataUtils.ToUnityVector(handles[1]);
            var axis = end - start;

            if (paint.type == Paint.PaintType.GRADIENT_LINEAR || handles.Length < 3)
            {
                var lengthSquared = axis.sqrMagnitude;
                return lengthSquared > 0f ? Vector2.Dot(point - start, axis) / lengthSquared : 0f;
            }

            // Radial, diamond and angular gradients live in the frame spanned by the two handle axes
            var widthAxis = FigmaDataUtils.ToUnityVector(handles[2]) - start;
            var determinant = axis.x * widthAxis.y - axis.y * widthAxis.x;
            if (Mathf.Approximately(determinant, 0f)) return 0f;
            var offset = point - start;
            var u = (offset.x * widthAxis.y - offset.y * widthAxis.x) / determinant;
            var v = (axis.x * offset.y - axis.y * offset.x) / determinant;
            return paint.type switch
            {
                Paint.PaintType.GRADIENT_RADIAL => Mathf.Sqrt(u * u + v * v),
                Paint.PaintType.GRADIENT_DIAMOND => Mathf.Abs(u) + Mathf.Abs(v),
                _ => Mathf.Repeat(Mathf.Atan2(v, u) / (2f * Mathf.PI), 1f)
            };
        }

        public static Color SampleGradient(Paint paint, float position)
        {
            var stops = paint.gradientStops;
            if (stops == null || stops.Length == 0) return new Color(0f, 0f, 0f, 0f);
            if (position <= stops[0].position) return FigmaDataUtils.ToUnityColor(stops[0].color);
            for (var i = 1; i < stops.Length; i++)
            {
                if (position > stops[i].position) continue;
                var span = stops[i].position - stops[i - 1].position;
                var t = span > 0f ? (position - stops[i - 1].position) / span : 1f;
                return Color.Lerp(FigmaDataUtils.ToUnityColor(stops[i - 1].color), FigmaDataUtils.ToUnityColor(stops[i].color), t);
            }
            return FigmaDataUtils.ToUnityColor(stops[stops.Length - 1].color);
        }

        private static Color Over(Color source, Color destination)
        {
            var alpha = source.a + destination.a * (1f - source.a);
            if (alpha <= 0f) return new Color(0f, 0f, 0f, 0f);
            var rgb = (new Vector3(source.r, source.g, source.b) * source.a +
                       new Vector3(destination.r, destination.g, destination.b) * destination.a * (1f - source.a)) / alpha;
            return new Color(rgb.x, rgb.y, rgb.z, alpha);
        }

        private static string Hash(byte[] png, Vector4 border)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            var borderBytes = System.Text.Encoding.UTF8.GetBytes($"{border.x},{border.y},{border.z},{border.w}");
            md5.TransformBlock(png, 0, png.Length, null, 0);
            md5.TransformFinalBlock(borderBytes, 0, borderBytes.Length);
            return BitConverter.ToString(md5.Hash, 0, 6).Replace("-", "").ToLowerInvariant();
        }
    }
}
