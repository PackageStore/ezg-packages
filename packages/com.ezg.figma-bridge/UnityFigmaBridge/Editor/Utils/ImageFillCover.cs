using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;
using Object = UnityEngine.Object;

namespace UnityFigmaBridge.Editor.Utils
{
    /// <summary>
    ///     An image fill in FILL mode covers its box: Figma scales the image until it fills the box and
    ///     crops the overflow, centred. A plain <c>Image</c> stretches the whole bitmap instead, so a fill
    ///     whose aspect differs from its box gets a cropped copy, <c>&lt;fill&gt;_cover-&lt;w&gt;x&lt;h&gt;.png</c>
    ///     next to the fill, holding exactly the part Figma shows.
    ///     An image fill in STRETCH mode that was cropped in Figma (<c>Paint.imageTransform</c> picks a
    ///     part of the bitmap, which is stretched over the box) gets the same treatment,
    ///     <c>&lt;fill&gt;_crop-&lt;x&gt;-&lt;y&gt;-&lt;w&gt;x&lt;h&gt;.png</c>: an icon zoomed in on its art keeps
    ///     the size Figma draws instead of shrinking to the whole bitmap with its margins.
    /// </summary>
    public static class ImageFillCover
    {
        /// <summary>Importer userData of a crop, followed by the MD5 of the fill it was cut from.</summary>
        private const string CoverMarker = "figma-bridge-cover:";

        private static readonly string[] s_Platforms = { "Standalone", "Android", "iPhone", "WebGL" };
        private static readonly Dictionary<string, Vector2Int> s_SizeByPath = new();

        /// <summary>Writes the crop of every reached FILL image fill that does not show its whole bitmap.</summary>
        public static void Run(FigmaFile file, IEnumerable<string> reachedImageRefs)
        {
            s_SizeByPath.Clear();
            var reached = new HashSet<string>(reachedImageRefs);
            var crops = new Dictionary<string, (string fillPath, RectInt rect)>();
            Collect(file.document, reached, crops);

            var written = 0;
            foreach (var crop in crops)
                if (WriteCrop(crop.Value.fillPath, crop.Key, crop.Value.rect)) written++;
            if (crops.Count > 0)
                Debug.Log($"[FigmaBridge] {crops.Count} FILL / cropped STRETCH image fill crop(s), {written} written");
        }

        /// <summary>The cropped sprite for this fill when it has one on disk, otherwise the fill itself.</summary>
        public static string SpritePathFor(string fillPath, Paint fill, Node node)
        {
            return TryGetCrop(fillPath, fill, node, out var cropPath, out _) && File.Exists(cropPath) ? cropPath : fillPath;
        }

        private static void Collect(Node node, HashSet<string> reached, Dictionary<string, (string, RectInt)> crops)
        {
            if (node == null) return;
            var fill = TopVisibleFill(node);
            if (fill != null && fill.type == Paint.PaintType.IMAGE && reached.Contains(fill.imageRef))
            {
                var fillPath = FigmaPaths.GetPathForImageFill(fill.imageRef);
                if (TryGetCrop(fillPath, fill, node, out var cropPath, out var rect)) crops[cropPath] = (fillPath, rect);
            }
            if (node.children == null) return;
            foreach (var child in node.children) Collect(child, reached, crops);
        }

        private static bool TryGetCrop(string fillPath, Paint fill, Node node, out string cropPath, out RectInt rect)
        {
            cropPath = null;
            rect = default;
            // A slice grid shares one plate across its cells; the 9-slice pass collapses them
            if (node.name != null && NineSlice.FigmaNineSlice.SliceName.IsMatch(node.name)) return false;
            if (fill.scaleMode == Paint.ScaleMode.STRETCH) return TryGetStretchCrop(fillPath, fill, out cropPath, out rect);
            if (fill.scaleMode != Paint.ScaleMode.FILL || !Mathf.Approximately(fill.rotation, 0f)) return false;
            if (node.size == null || node.size.x <= 0f || node.size.y <= 0f) return false;
            if (!TryGetImageSize(fillPath, out var image)) return false;

            var scale = Mathf.Max(node.size.x / image.x, node.size.y / image.y);
            var width = Mathf.Clamp(Mathf.RoundToInt(node.size.x / scale), 1, image.x);
            var height = Mathf.Clamp(Mathf.RoundToInt(node.size.y / scale), 1, image.y);
            if (width >= image.x - 1 && height >= image.y - 1) return false;

            // Texture rows run bottom up; the crop is centred, so the flip leaves it in place
            rect = new RectInt((image.x - width) / 2, (image.y - height) / 2, width, height);
            var directory = Path.GetDirectoryName(fillPath)?.Replace('\\', '/');
            cropPath = $"{directory}/{Path.GetFileNameWithoutExtension(fillPath)}_cover-{width}x{height}.png";
            return true;
        }

        /// <summary>
        ///     The part of the bitmap a STRETCH fill shows: <c>imageTransform</c> maps the node's unit box
        ///     into the image's unit box. Only an axis-aligned crop that stays inside the image is cut;
        ///     a fill zoomed out past the image edge or rotated keeps the whole bitmap.
        /// </summary>
        private static bool TryGetStretchCrop(string fillPath, Paint fill, out string cropPath, out RectInt rect)
        {
            cropPath = null;
            rect = default;
            var m = fill.imageTransform;
            if (m == null || m.GetLength(0) < 2 || m.GetLength(1) < 3) return false;
            if (Mathf.Abs(m[0, 1]) > 1e-4f || Mathf.Abs(m[1, 0]) > 1e-4f || m[0, 0] <= 0f || m[1, 1] <= 0f) return false;
            const float edge = 1e-3f;
            if (m[0, 2] < -edge || m[1, 2] < -edge || m[0, 2] + m[0, 0] > 1f + edge || m[1, 2] + m[1, 1] > 1f + edge) return false;
            if (!TryGetImageSize(fillPath, out var image)) return false;

            var x = Mathf.Clamp(Mathf.RoundToInt(m[0, 2] * image.x), 0, image.x - 1);
            var top = Mathf.Clamp(Mathf.RoundToInt(m[1, 2] * image.y), 0, image.y - 1);
            var width = Mathf.Clamp(Mathf.RoundToInt(m[0, 0] * image.x), 1, image.x - x);
            var height = Mathf.Clamp(Mathf.RoundToInt(m[1, 1] * image.y), 1, image.y - top);
            if (width >= image.x - 1 && height >= image.y - 1) return false;

            // Texture rows run bottom up; imageTransform measures from the top
            rect = new RectInt(x, image.y - top - height, width, height);
            var directory = Path.GetDirectoryName(fillPath)?.Replace('\\', '/');
            cropPath = $"{directory}/{Path.GetFileNameWithoutExtension(fillPath)}_crop-{x}-{top}-{width}x{height}.png";
            return true;
        }

        private static bool WriteCrop(string fillPath, string cropPath, RectInt rect)
        {
            var sourceBytes = File.ReadAllBytes(fillPath);
            var marker = CoverMarker + Md5(sourceBytes);
            if (File.Exists(cropPath) && AssetImporter.GetAtPath(cropPath) is TextureImporter existing && existing.userData == marker)
                return false;

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var crop = new Texture2D(rect.width, rect.height, TextureFormat.RGBA32, false);
            try
            {
                if (!source.LoadImage(sourceBytes)) return false;
                crop.SetPixels32(Region(source.GetPixels32(), source.width, rect));
                crop.Apply();
                File.WriteAllBytes(cropPath, crop.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(crop);
            }

            AssetDatabase.ImportAsset(cropPath, ImportAssetOptions.ForceUpdate);
            CopyImporter(fillPath, cropPath, marker);
            return true;
        }

        private static Color32[] Region(Color32[] pixels, int sourceWidth, RectInt rect)
        {
            var region = new Color32[rect.width * rect.height];
            for (var row = 0; row < rect.height; row++)
                Array.Copy(pixels, (rect.y + row) * sourceWidth + rect.x, region, row * rect.width, rect.width);
            return region;
        }

        internal static void CopyImporter(string fillPath, string cropPath, string marker)
        {
            if (AssetImporter.GetAtPath(cropPath) is not TextureImporter target) return;
            if (AssetImporter.GetAtPath(fillPath) is TextureImporter source)
            {
                var settings = new TextureImporterSettings();
                source.ReadTextureSettings(settings);
                target.SetTextureSettings(settings);
                foreach (var platform in s_Platforms)
                {
                    var platformSettings = source.GetPlatformTextureSettings(platform);
                    if (platformSettings.overridden) target.SetPlatformTextureSettings(platformSettings);
                }
            }
            else
            {
                target.textureType = TextureImporterType.Sprite;
                target.spriteImportMode = SpriteImportMode.Single;
                target.sRGBTexture = true;
            }
            target.spriteBorder = Vector4.zero;
            target.userData = marker;
            target.SaveAndReimport();
        }

        /// <summary>Width and height from the PNG header, so no fill has to be decoded to size it.</summary>
        private static bool TryGetImageSize(string path, out Vector2Int size)
        {
            if (s_SizeByPath.TryGetValue(path, out size)) return true;
            size = default;
            if (!File.Exists(path)) return false;
            var header = new byte[24];
            using (var stream = File.OpenRead(path))
                if (stream.Read(header, 0, header.Length) < header.Length) return false;
            // 8 byte signature, IHDR length and type, then big-endian width and height
            if (header[12] != 'I' || header[13] != 'H' || header[14] != 'D' || header[15] != 'R') return false;
            size = new Vector2Int(BigEndian(header, 16), BigEndian(header, 20));
            s_SizeByPath[path] = size;
            return size.x > 0 && size.y > 0;
        }

        private static int BigEndian(byte[] bytes, int offset) =>
            (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

        internal static Paint TopVisibleFill(Node node)
        {
            if (node.fills == null) return null;
            for (var i = node.fills.Length - 1; i >= 0; i--)
                if (node.fills[i] != null && node.fills[i].visible && !FigmaDataUtils.IsShaderPaint(node.fills[i])) return node.fills[i];
            return null;
        }

        internal static string Md5(byte[] bytes)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            return BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
