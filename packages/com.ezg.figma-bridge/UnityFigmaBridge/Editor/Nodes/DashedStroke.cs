using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Settings;
using UnityFigmaBridge.Editor.Utils;
using Object = UnityEngine.Object;

namespace UnityFigmaBridge.Editor.Nodes
{
    /// <summary>
    ///     The dashed stroke of a node with children (<c>strokeDashes</c>), which <see cref="FrameShapeSprite"/> would
    ///     draw as a solid line. The node's shape sprite keeps its fills only; the whole stroke is a child Image drawn
    ///     <see cref="Image.Type.Tiled"/> from one white sprite per stroke style: the four corners are fixed, and each
    ///     edge holds exactly one dash cycle, so the dashes stay even at any size (instances resized from their master,
    ///     frames resized at run time). Dashes are spread as Figma does per segment: every edge and corner arc holds a
    ///     whole number of cycles, the cycle stretched to fit, and a dash is centred on each joint between segments.
    /// </summary>
    public static class DashedStroke
    {
        private const string ShapeFolderName = "Shapes";
        private const float ReferencePixelsPerUnit = 100f;
        private const int Samples = 3;

        /// <summary>True when the node has a dashed solid stroke this class draws.</summary>
        public static bool Has(Node node) => Pattern(node) != null && SolidStroke(node) != null;

        /// <summary>Adds or updates the stroke child. False when the node has no dashed stroke to draw.</summary>
        public static bool Apply(GameObject nodeGameObject, Node node, FigmaImportProcessData figmaImportProcessData)
        {
            var dashes = Pattern(node);
            var stroke = SolidStroke(node);
            if (dashes == null || stroke == null || node.size == null) return false;

            var weight = node.strokeWeight;
            var outset = node.strokeAlign switch
            {
                Node.StrokeAlign.OUTSIDE => weight,
                Node.StrokeAlign.CENTER => weight * 0.5f,
                _ => 0f
            };
            var sprite = Build(CentreRadii(node, outset - weight * 0.5f), weight, dashes, node.strokeCap, figmaImportProcessData.Settings);
            if (sprite == null) return false;

            var existing = nodeGameObject.transform.Find(OuterStroke.ChildName);
            var strokeObject = existing != null
                ? existing.gameObject
                : new GameObject(OuterStroke.ChildName, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            strokeObject.SetActive(true);
            var rect = (RectTransform)strokeObject.transform;
            if (existing == null) rect.SetParent(nodeGameObject.transform, false);
            rect.SetAsFirstSibling();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = -Vector2.one * outset;
            rect.offsetMax = Vector2.one * outset;

            var image = strokeObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = FigmaDataUtils.GetUnityFillColor(stroke);
            image.type = Image.Type.Tiled;
            image.pixelsPerUnitMultiplier = 1f;
            image.raycastTarget = false;
            var layoutElement = strokeObject.GetComponent<LayoutElement>();
            if (layoutElement == null) layoutElement = strokeObject.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;
            return true;
        }

        private static float[] Pattern(Node node)
        {
            var dashes = node?.strokeDashes;
            if (dashes == null || dashes.Length < 2 || node.strokeWeight <= 0f) return null;
            if (dashes.Any(d => d < 0f) || dashes.Where((d, i) => i % 2 == 0).Sum() <= 0f || dashes.Where((d, i) => i % 2 == 1).Sum() <= 0f) return null;
            // An odd list repeats twice, as in SVG
            return dashes.Length % 2 == 0 ? dashes : dashes.Concat(dashes).ToArray();
        }

        private static Paint SolidStroke(Node node)
        {
            if (node?.strokes == null) return null;
            Paint top = null;
            foreach (var stroke in node.strokes)
                if (stroke != null && stroke.visible && stroke.type == Paint.PaintType.SOLID) top = stroke;
                else if (stroke != null && stroke.visible) return null; // a gradient or image stroke keeps the solid fallback
            return top;
        }

        /// <summary>Corner radii of the stroke's centre line: the box radius moved by <paramref name="shift"/>, square corners kept.</summary>
        private static Vector4 CentreRadii(Node node, float shift)
        {
            var radii = node.rectangleCornerRadii is { Length: 4 }
                ? new Vector4(node.rectangleCornerRadii[0], node.rectangleCornerRadii[1], node.rectangleCornerRadii[2], node.rectangleCornerRadii[3])
                : Vector4.one * Mathf.Max(0f, node.cornerRadius);
            var limit = Mathf.Min(node.size.x, node.size.y) * 0.5f;
            for (var i = 0; i < 4; i++) radii[i] = radii[i] > 0f ? Mathf.Max(0f, Mathf.Min(radii[i], limit) + shift) : 0f;
            return radii;
        }

        private static Sprite Build(Vector4 radii, float weight, float[] dashes, string cap, UnityFigmaBridgeSettings settings)
        {
            var half = weight * 0.5f;
            var period = dashes.Sum();
            var tile = Mathf.Max(1, Mathf.RoundToInt(period));
            int Border(float a, float b) => Mathf.CeilToInt(Mathf.Max(half + Mathf.Max(a, b), weight + 1f));
            var left = Border(radii.x, radii.w);
            var right = Border(radii.y, radii.z);
            var top = Border(radii.x, radii.y);
            var bottom = Border(radii.w, radii.z);
            var width = left + tile + right;
            var height = top + tile + bottom;
            var path = new DashPath(new Rect(half, half, width - weight, height - weight), radii, dashes, cap, half);

            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var hits = 0;
                for (var sy = 0; sy < Samples; sy++)
                for (var sx = 0; sx < Samples; sx++)
                    if (path.Distance(new Vector2(x + (sx + 0.5f) / Samples, y + (sy + 0.5f) / Samples)) <= half) hits++;
                // Texture rows run bottom up; the path runs top down
                pixels[(height - 1 - y) * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * hits / (Samples * Samples)));
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            var png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);

            var folder = $"{FigmaPaths.FigmaImageFillFolder}/{ShapeFolderName}";
            var assetPath = $"{folder}/Dash-{Hash(png)}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (existing != null) return existing;
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(assetPath, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                var textureSettings = new TextureImporterSettings();
                importer.ReadTextureSettings(textureSettings);
                textureSettings.spriteMeshType = SpriteMeshType.FullRect;
                textureSettings.spriteBorder = new Vector4(left, bottom, right, top);
                textureSettings.spritePixelsPerUnit = ReferencePixelsPerUnit;
                importer.SetTextureSettings(textureSettings);
                SpritePlatformOverride.Apply(importer, settings);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static string Hash(byte[] png)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            return BitConverter.ToString(md5.ComputeHash(png), 0, 6).Replace("-", "").ToLowerInvariant();
        }
    }
}
