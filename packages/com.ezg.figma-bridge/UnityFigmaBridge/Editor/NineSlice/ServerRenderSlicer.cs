using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Utils;
using Object = UnityEngine.Object;

namespace UnityFigmaBridge.Editor.NineSlice
{
    /// <summary>
    ///     Gives every server-rendered node a sprite border, so a render made at the component's size
    ///     keeps its corners, strokes and shadows where an instance draws it at another size.
    ///     A band of identical columns (rows) is stretched without loss, so it becomes the border
    ///     centre and is cut down to <see cref="KeptCenterPixels"/>. An axis without one (a gradient
    ///     along it) takes its border from the node geometry instead and keeps its pixels.
    /// </summary>
    public static class ServerRenderSlicer
    {
        private const int KeptCenterPixels = 2;

        /// <summary>
        ///     Largest premultiplied 8 bit channel difference two render lines may have and still count as
        ///     one. Figma dithers gradients and shadows by up to 3 levels, so exact equality finds no band.
        /// </summary>
        private const int ChannelTolerance = 4;

        /// <summary>Canvas reference pixels per unit: a sprite at this density draws one texel per UI unit.</summary>
        private const float ReferencePixelsPerUnit = 100f;

        /// <param name="serverRenderScale">Scale the renders were made at (<see cref="FigmaImportProcessData.ServerRenderScale"/>).</param>
        public static void Run(List<ServerRenderNodeData> serverRenderNodes, int serverRenderScale)
        {
            var renderScale = serverRenderScale > 0 ? serverRenderScale : 1;
            int sliced = 0, compacted = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var renderNode in serverRenderNodes)
                {
                    if (renderNode.RenderType != ServerRenderType.Substitution) continue;
                    var path = FigmaPaths.GetPathForServerRenderedImage(renderNode.SourceNode.id, serverRenderNodes);
                    if (!File.Exists(path)) continue;
                    if (SliceRender(path, renderNode.SourceNode, renderScale, out var wasCompacted)) sliced++;
                    if (wasCompacted) compacted++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            Debug.Log($"[ServerRenderSlicer] {sliced} render(s) given a border, {compacted} cut down to their corners");
        }

        private static bool SliceRender(string path, Node node, int renderScale, out bool wasCompacted)
        {
            wasCompacted = false;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path))) return false;
                var width = texture.width;
                var height = texture.height;
                var pixels = texture.GetPixels32();

                var geometry = GeometryBorder(node);
                var columns = SliceAxis(pixels, width, height, true,
                    ToPixels(geometry.x, renderScale), ToPixels(geometry.z, renderScale));
                // Texture rows count from the bottom, so the near side of the row axis is the bottom border
                var rows = SliceAxis(pixels, width, height, false,
                    ToPixels(geometry.y, renderScale), ToPixels(geometry.w, renderScale));

                if (columns.removed > 0 || rows.removed > 0)
                {
                    AverageBand(pixels, width, height, true, columns);
                    AverageBand(pixels, width, height, false, rows);
                    var compactPixels = Compact(pixels, width, height, columns, rows, out var compactWidth, out var compactHeight);
                    var compactTexture = new Texture2D(compactWidth, compactHeight, TextureFormat.RGBA32, false);
                    compactTexture.SetPixels32(compactPixels);
                    compactTexture.Apply();
                    File.WriteAllBytes(path, compactTexture.EncodeToPNG());
                    Object.DestroyImmediate(compactTexture);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    wasCompacted = true;
                }

                var border = new Vector4(columns.near, rows.near, columns.far, rows.far);
                ConfigureImporter(path, border, ReferencePixelsPerUnit * renderScale);
                return border != Vector4.zero;
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        private struct AxisSlice
        {
            public int near;
            public int far;
            /// <summary>First line of the uniform band that is dropped, and how many lines are dropped.</summary>
            public int removedStart;
            public int removed;
        }

        private static AxisSlice SliceAxis(Color32[] pixels, int width, int height, bool columns, int geometryNear, int geometryFar)
        {
            var lineCount = columns ? width : height;
            var (start, length) = UniformBand(pixels, width, height, columns);
            // A gradient along the axis still repeats a few lines per 8 bit step. Such a short band
            // would stretch as one flat stripe, so the geometry border (the whole centre stretches) wins.
            var geometryCenter = lineCount - geometryNear - geometryFar;
            var bandIsCenter = geometryNear + geometryFar == 0 || length * 2 >= geometryCenter;
            if (length >= KeptCenterPixels && bandIsCenter)
            {
                return new AxisSlice
                {
                    near = start,
                    far = lineCount - start - length,
                    removedStart = start + KeptCenterPixels,
                    removed = length - KeptCenterPixels
                };
            }

            if (geometryNear + geometryFar >= lineCount - 1) return new AxisSlice();
            return new AxisSlice { near = geometryNear, far = geometryFar };
        }

        /// <summary>
        ///     The band of lines identical to its first line, away from the image edges. The band that
        ///     holds the centre line wins; otherwise the longest. A transparent margin touching an edge
        ///     is not a band: stretching it would move the art.
        /// </summary>
        private static (int start, int length) UniformBand(Color32[] pixels, int width, int height, bool columns)
        {
            var lineCount = columns ? width : height;
            var center = lineCount / 2;
            (int start, int length) best = (0, 0);
            var bestHoldsCenter = false;

            var start = 1;
            while (start < lineCount - 1)
            {
                var end = start + 1;
                while (end < lineCount - 1 && LinesMatch(pixels, width, height, columns, start, end)) end++;
                var length = end - start;
                var holdsCenter = start <= center && center < end;
                if (length >= KeptCenterPixels &&
                    (holdsCenter && !bestHoldsCenter || holdsCenter == bestHoldsCenter && length > best.length))
                {
                    best = (start, length);
                    bestHoldsCenter = holdsCenter;
                }
                start = end;
            }
            return best;
        }

        private static bool LinesMatch(Color32[] pixels, int width, int height, bool columns, int a, int b)
        {
            var span = columns ? height : width;
            for (var i = 0; i < span; i++)
            {
                var pa = columns ? pixels[i * width + a] : pixels[a * width + i];
                var pb = columns ? pixels[i * width + b] : pixels[b * width + i];
                // Premultiplied: a nearly transparent texel carries arbitrary colour that draws as nothing
                if (Mathf.Abs(pa.a - pb.a) > ChannelTolerance ||
                    Mathf.Abs(pa.r * pa.a - pb.r * pb.a) > ChannelTolerance * 255 ||
                    Mathf.Abs(pa.g * pa.a - pb.g * pb.a) > ChannelTolerance * 255 ||
                    Mathf.Abs(pa.b * pa.a - pb.b * pb.a) > ChannelTolerance * 255)
                    return false;
            }
            return true;
        }

        /// <summary>
        ///     Write the band's alpha-weighted mean into the lines that are kept, so the stretched centre
        ///     carries no dither pattern of its own.
        /// </summary>
        private static void AverageBand(Color32[] pixels, int width, int height, bool columns, AxisSlice slice)
        {
            if (slice.removed <= 0) return;
            var bandStart = slice.near;
            var bandLength = slice.removed + KeptCenterPixels;
            var span = columns ? height : width;
            for (var i = 0; i < span; i++)
            {
                double r = 0, g = 0, b = 0, alpha = 0;
                for (var line = bandStart; line < bandStart + bandLength; line++)
                {
                    var pixel = columns ? pixels[i * width + line] : pixels[line * width + i];
                    r += pixel.r * pixel.a; g += pixel.g * pixel.a; b += pixel.b * pixel.a; alpha += pixel.a;
                }
                var mean = alpha > 0
                    ? new Color32((byte)Math.Round(r / alpha), (byte)Math.Round(g / alpha), (byte)Math.Round(b / alpha),
                        (byte)Math.Round(alpha / bandLength))
                    : new Color32(0, 0, 0, 0);
                for (var line = bandStart; line < bandStart + KeptCenterPixels; line++)
                {
                    if (columns) pixels[i * width + line] = mean;
                    else pixels[line * width + i] = mean;
                }
            }
        }

        private static Color32[] Compact(Color32[] pixels, int width, int height, AxisSlice columns, AxisSlice rows,
            out int compactWidth, out int compactHeight)
        {
            compactWidth = width - columns.removed;
            compactHeight = height - rows.removed;
            var result = new Color32[compactWidth * compactHeight];
            var targetRow = 0;
            for (var y = 0; y < height; y++)
            {
                if (y >= rows.removedStart && y < rows.removedStart + rows.removed) continue;
                var targetColumn = 0;
                for (var x = 0; x < width; x++)
                {
                    if (x >= columns.removedStart && x < columns.removedStart + columns.removed) continue;
                    result[targetRow * compactWidth + targetColumn++] = pixels[y * width + x];
                }
                targetRow++;
            }
            return result;
        }

        /// <summary>
        ///     Border in design units (left, bottom, right, top) measured on the render bounds: what
        ///     draws outside the layout box, plus the widest of corner radius, inside stroke and shadow
        ///     reach on that side. Only rectangle-like nodes have one; any other shape returns zero.
        /// </summary>
        private static Vector4 GeometryBorder(Node node)
        {
            switch (node.type)
            {
                case NodeType.RECTANGLE:
                case NodeType.FRAME:
                case NodeType.COMPONENT:
                case NodeType.INSTANCE:
                    break;
                default:
                    return Vector4.zero;
            }

            var box = node.absoluteBoundingBox;
            if (box == null) return Vector4.zero;
            var bounds = node.absoluteRenderBounds ?? box;
            var outsideLeft = Mathf.Max(0f, box.x - bounds.x);
            var outsideTop = Mathf.Max(0f, box.y - bounds.y);
            var outsideRight = Mathf.Max(0f, bounds.x + bounds.width - (box.x + box.width));
            var outsideBottom = Mathf.Max(0f, bounds.y + bounds.height - (box.y + box.height));

            // Figma order: top-left, top-right, bottom-right, bottom-left
            var radii = node.rectangleCornerRadii is { Length: 4 }
                ? node.rectangleCornerRadii
                : new[] { node.cornerRadius, node.cornerRadius, node.cornerRadius, node.cornerRadius };

            var hasStroke = node.strokes != null && node.strokes.Length > 0 && node.strokeWeight > 0;
            var insideStroke = !hasStroke ? 0f : node.strokeAlign switch
            {
                Node.StrokeAlign.INSIDE => node.strokeWeight,
                Node.StrokeAlign.CENTER => node.strokeWeight * 0.5f,
                _ => 0f
            };

            float reachX = 0f, reachY = 0f;
            if (node.effects != null)
            {
                foreach (var effect in node.effects)
                {
                    if (!effect.visible) continue;
                    if (effect.type != Effect.EffectType.DROP_SHADOW && effect.type != Effect.EffectType.INNER_SHADOW) continue;
                    var offsetX = effect.offset != null ? Mathf.Abs(effect.offset.x) : 0f;
                    var offsetY = effect.offset != null ? Mathf.Abs(effect.offset.y) : 0f;
                    reachX = Mathf.Max(reachX, effect.radius + effect.spread + offsetX);
                    reachY = Mathf.Max(reachY, effect.radius + effect.spread + offsetY);
                }
            }

            var left = outsideLeft + Mathf.Max(radii[0], radii[3], insideStroke, reachX);
            var right = outsideRight + Mathf.Max(radii[1], radii[2], insideStroke, reachX);
            var top = outsideTop + Mathf.Max(radii[0], radii[1], insideStroke, reachY);
            var bottom = outsideBottom + Mathf.Max(radii[2], radii[3], insideStroke, reachY);
            return new Vector4(left, bottom, right, top);
        }

        /// <summary>Design units to render pixels, with one pixel for the anti-aliased edge.</summary>
        private static int ToPixels(float designUnits, int renderScale)
        {
            return designUnits <= 0f ? 0 : Mathf.CeilToInt(designUnits * renderScale) + 1;
        }

        private static void ConfigureImporter(string path, Vector4 border, float pixelsPerUnit)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            var meshType = border != Vector4.zero ? SpriteMeshType.FullRect : settings.spriteMeshType;
            if (importer.textureType == TextureImporterType.Sprite &&
                importer.spriteImportMode == SpriteImportMode.Single &&
                settings.spriteBorder == border &&
                Mathf.Approximately(settings.spritePixelsPerUnit, pixelsPerUnit) &&
                settings.spriteMeshType == meshType) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.ReadTextureSettings(settings);
            settings.spriteBorder = border;
            settings.spritePixelsPerUnit = pixelsPerUnit;
            settings.spriteMeshType = meshType;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }
    }
}
