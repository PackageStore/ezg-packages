using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;

namespace UnityFigmaBridge.Editor.Utils
{
    /// <summary>
    ///     An image fill can carry Figma's image adjustments (<see cref="ImageFilters"/>: exposure,
    ///     contrast, saturation, temperature, tint, highlights, shadows). They belong to the paint, not
    ///     the image: one imageRef is shared by every node that shows it, each with its own adjustments.
    ///     A paint with adjustments gets a filtered copy of the bitmap it shows (the fill, or its FILL
    ///     crop), <c>&lt;image&gt;_f-&lt;key&gt;.png</c> next to it, keyed by the adjustments, so equal
    ///     adjustments share one file. Figma does not publish its formulas; these follow the usual
    ///     photo-editor ones and match its renders closely for the values files use.
    /// </summary>
    public static class ImageFillFilters
    {
        /// <summary>Importer userData of a filtered copy, followed by the MD5 of its source.</summary>
        private const string FilterMarker = "figma-bridge-filter:";

        /// <summary>Writes the filtered copy of every reached image fill that has adjustments.</summary>
        public static void Run(FigmaFile file, IEnumerable<string> reachedImageRefs)
        {
            var reached = new HashSet<string>(reachedImageRefs);
            var copies = new Dictionary<string, (string source, ImageFilters filters)>();
            Collect(file.document, reached, copies);
            var written = 0;
            foreach (var copy in copies)
                if (Write(copy.Value.source, copy.Key, copy.Value.filters)) written++;
            if (copies.Count > 0) Debug.Log($"[FigmaBridge] {copies.Count} filtered image fill(s), {written} written");
        }

        /// <summary>The filtered copy of the bitmap at path for this paint when it has one on disk, otherwise path.</summary>
        public static string SpritePathFor(string path, Paint fill)
        {
            if (fill?.filters == null || !fill.filters.Any) return path;
            var filtered = FilteredPath(path, fill.filters);
            return File.Exists(filtered) ? filtered : path;
        }

        private static void Collect(Node node, HashSet<string> reached, Dictionary<string, (string, ImageFilters)> copies)
        {
            if (node == null) return;
            var fill = ImageFillCover.TopVisibleFill(node);
            if (fill != null && fill.type == Paint.PaintType.IMAGE && fill.filters != null && fill.filters.Any && reached.Contains(fill.imageRef))
            {
                var source = ImageFillCover.SpritePathFor(FigmaPaths.GetPathForImageFill(fill.imageRef), fill, node);
                copies[FilteredPath(source, fill.filters)] = (source, fill.filters);
            }
            if (node.children == null) return;
            foreach (var child in node.children) Collect(child, reached, copies);
        }

        private static string FilteredPath(string path, ImageFilters f)
        {
            string N(float v) => Mathf.RoundToInt(v * 100f).ToString(CultureInfo.InvariantCulture);
            var key = $"e{N(f.exposure)}c{N(f.contrast)}s{N(f.saturation)}t{N(f.temperature)}n{N(f.tint)}h{N(f.highlights)}d{N(f.shadows)}";
            var directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
            return $"{directory}/{Path.GetFileNameWithoutExtension(path)}_f-{key}.png";
        }

        private static bool Write(string sourcePath, string filteredPath, ImageFilters f)
        {
            if (!File.Exists(sourcePath)) return false;
            var sourceBytes = File.ReadAllBytes(sourcePath);
            var marker = FilterMarker + ImageFillCover.Md5(sourceBytes);
            if (File.Exists(filteredPath) && AssetImporter.GetAtPath(filteredPath) is TextureImporter existing && existing.userData == marker)
                return false;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(sourceBytes)) return false;
                var pixels = texture.GetPixels();
                for (var i = 0; i < pixels.Length; i++) pixels[i] = Adjust(pixels[i], f);
                texture.SetPixels(pixels);
                texture.Apply();
                File.WriteAllBytes(filteredPath, texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(filteredPath, ImportAssetOptions.ForceUpdate);
            ImageFillCover.CopyImporter(sourcePath, filteredPath, marker);
            return true;
        }

        /// <summary>
        ///     One pixel through the adjustments in Figma's panel order. Exposure scales by 2^(1.5·e)
        ///     stops; contrast stretches around mid grey; saturation mixes with Rec. 601 luma (−1 = grey);
        ///     temperature shifts red against blue and tint magenta against green by up to 10 %;
        ///     highlights and shadows lift or lower the bright and dark halves by up to 25 %.
        /// </summary>
        public static Color Adjust(Color c, ImageFilters f)
        {
            var rgb = new Vector3(c.r, c.g, c.b);
            if (f.exposure != 0f) rgb *= Mathf.Pow(2f, 1.5f * f.exposure);
            if (f.contrast != 0f) rgb = (rgb - Vector3.one * 0.5f) * (1f + f.contrast) + Vector3.one * 0.5f;
            if (f.saturation != 0f)
            {
                var grey = Luma(rgb);
                rgb = Vector3.one * grey + (rgb - Vector3.one * grey) * (1f + f.saturation);
            }
            if (f.temperature != 0f) rgb += new Vector3(0.1f, 0f, -0.1f) * f.temperature;
            if (f.tint != 0f) rgb += new Vector3(0.05f, -0.1f, 0.05f) * f.tint;
            if (f.highlights != 0f || f.shadows != 0f)
            {
                var l = Mathf.Clamp01(Luma(rgb));
                rgb += Vector3.one * (f.highlights * Mathf.Max(0f, l - 0.5f) * 0.5f + f.shadows * Mathf.Max(0f, 0.5f - l) * 0.5f);
            }
            return new Color(Mathf.Clamp01(rgb.x), Mathf.Clamp01(rgb.y), Mathf.Clamp01(rgb.z), c.a);
        }

        private static float Luma(Vector3 rgb) => rgb.x * 0.299f + rgb.y * 0.587f + rgb.z * 0.114f;
    }
}
