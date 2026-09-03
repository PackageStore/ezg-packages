using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityFigmaBridge.Runtime.UI;

namespace UnityFigmaBridge.Editor.NineSlice
{
    /// <summary>
    ///     Collapses a 3x3 (or 1x3 / 3x1) grid of <c>slice_ROW_COL</c> children into a single
    ///     <c>Image.Type.Sliced</c>. This is an optimisation, not a correctness fix: the bridge
    ///     already renders each cell correctly via <c>FigmaImage.ImageTransform</c> and constraint-
    ///     derived anchors. Collapsing replaces nine <c>FigmaImage</c> components (each with its own
    ///     dynamic material) with one plain <c>Image</c> and a shared sprite.
    /// </summary>
    internal static class FigmaNineSlice
    {
        private static readonly Regex SliceName = new(@"^slice_(.+)_(.+)$", RegexOptions.Compiled);

        internal readonly struct Result
        {
            internal readonly int collapsed;
            internal readonly int spritesWritten;
            internal Result(int collapsed, int spritesWritten)
            {
                this.collapsed = collapsed;
                this.spritesWritten = spritesWritten;
            }
        }

        /// <summary>Collapse every slice grid under <paramref name="root"/>. Depth-first, so nested grids resolve too.</summary>
        internal static Result Apply(GameObject root, string spriteDir)
        {
            var collapsed = 0;
            var written = 0;
            var stack = new Stack<Transform>();
            stack.Push(root.transform);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (TryCollapse(node, spriteDir, ref written)) collapsed++;
                else
                    foreach (Transform child in node)
                        stack.Push(child);
            }

            return new Result(collapsed, written);
        }

        private static bool TryCollapse(Transform node, string spriteDir, ref int written)
        {
            if (node.childCount < 2) return false;

            var slices = new List<(Transform t, string row, string col, Image img)>();
            foreach (Transform child in node)
            {
                var m = SliceName.Match(child.name);
                var img = child.GetComponent<Image>();
                if (!m.Success || img == null || img.sprite == null) return false;
                slices.Add((child, m.Groups[1].Value, m.Groups[2].Value, img));
            }

            // A plain Image cannot reproduce FigmaImage's stroke or corner-radius shader features.
            foreach (var s in slices)
            {
                if (s.img is FigmaImage fi && (fi.StrokeWidth > 0 || fi.CornerRadius != Vector4.zero))
                {
                    Debug.Log($"[FigmaNineSlice] Skipping {node.name}: cell {s.t.name} has stroke or corner radius");
                    return false;
                }
            }

            // All cells of a slice grid share one image fill; under the bridge each cell's sprite
            // points at the same asset (keyed by imageRef). Size equality is the safety check.
            var texture = slices[0].img.sprite.texture;
            if (slices.Any(s => s.img.sprite.texture.width != texture.width ||
                                s.img.sprite.texture.height != texture.height)) return false;

            var border = MeasureBorder(node, slices);
            var sprite = EnsureSlicedSprite(slices[0].img.sprite, border, spriteDir, ref written);
            if (sprite == null) return false;

            foreach (var s in slices) Object.DestroyImmediate(s.t.gameObject);

            // The collapsed result must be a plain Image, not a FigmaImage.
            var target = node.GetComponent<Image>();
            if (target is FigmaImage)
            {
                Object.DestroyImmediate(target);
                target = null;
            }
            if (target == null)
            {
                if (node.GetComponent<CanvasRenderer>() == null) node.gameObject.AddComponent<CanvasRenderer>();
                target = node.gameObject.AddComponent<Image>();
            }

            target.sprite = sprite;
            target.type = border == Vector4.zero ? Image.Type.Simple : Image.Type.Sliced;
            target.enabled = true;
            return true;
        }

        /// <summary>
        ///     Border in Unity's order (left, bottom, right, top), read off the corner slices.
        ///     A grid with a single column contributes no horizontal border, likewise a single row.
        /// </summary>
        private static Vector4 MeasureBorder(Transform node, List<(Transform t, string row, string col, Image img)> slices)
        {
            // Position in the PARENT's space. anchoredPosition is measured from each slice's own
            // anchor, so it cannot order slices that do not share one.
            float LeftOf(Transform t) => t.localPosition.x + ((RectTransform)t).rect.xMin;
            float TopOf(Transform t) => -(t.localPosition.y + ((RectTransform)t).rect.yMax);

            var rowOrder = slices.Select(s => s.row).Distinct()
                .OrderBy(r => slices.Where(s => s.row == r).Min(s => TopOf(s.t))).ToList();
            var colOrder = slices.Select(s => s.col).Distinct()
                .OrderBy(c => slices.Where(s => s.col == c).Min(s => LeftOf(s.t))).ToList();

            float Width(string col) => slices.Where(s => s.col == col).Max(s => ((RectTransform)s.t).rect.width);
            float Height(string row) => slices.Where(s => s.row == row).Max(s => ((RectTransform)s.t).rect.height);

            var left = colOrder.Count >= 3 ? Width(colOrder[0]) : 0f;
            var right = colOrder.Count >= 3 ? Width(colOrder[^1]) : 0f;
            var top = rowOrder.Count >= 3 ? Height(rowOrder[0]) : 0f;
            var bottom = rowOrder.Count >= 3 ? Height(rowOrder[^1]) : 0f;

            // A border wider than the art itself cannot be drawn; Unity would squash the corners.
            var size = ((RectTransform)node).rect.size;
            if (left + right >= size.x) { left = 0f; right = 0f; }
            if (top + bottom >= size.y) { top = 0f; bottom = 0f; }

            return new Vector4(Mathf.Round(left), Mathf.Round(bottom), Mathf.Round(right), Mathf.Round(top));
        }

        /// <summary>
        ///     Use the existing image fill asset when available, setting the sprite border on its
        ///     importer. Falls back to a RenderTexture read-back when the sprite has no asset path.
        /// </summary>
        private static Sprite EnsureSlicedSprite(Sprite cellSprite, Vector4 border, string spriteDir, ref int written)
        {
            var assetPath = AssetDatabase.GetAssetPath(cellSprite);
            if (!string.IsNullOrEmpty(assetPath))
            {
                ConfigureBorder(assetPath, border);
                return cellSprite;
            }
            return BakeSprite(cellSprite.texture, border, spriteDir, ref written);
        }

        private static void ConfigureBorder(string path, Vector4 border)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            if (importer.textureType == TextureImporterType.Sprite &&
                importer.spriteImportMode == SpriteImportMode.Single &&
                settings.spriteBorder == border) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.ReadTextureSettings(settings);
            settings.spriteBorder = border;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        /// <summary>
        ///     Fallback: write the texture out as a project sprite when the cell sprite has no
        ///     asset path. Content hash as the name so the same art becomes one sprite.
        /// </summary>
        private static Sprite BakeSprite(Texture2D texture, Vector4 border, string spriteDir, ref int written)
        {
            var png = ReadablePng(texture);
            if (png == null) return null;

            using var md5 = System.Security.Cryptography.MD5.Create();
            var name = System.BitConverter.ToString(md5.ComputeHash(png)).Replace("-", "").ToLowerInvariant();

            Directory.CreateDirectory(spriteDir);
            var path = $"{spriteDir}/{name}.png";
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, png);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                written++;
            }

            ConfigureBorder(path, border);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static byte[] ReadablePng(Texture2D texture)
        {
            var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                var readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                readable.Apply();
                var png = readable.EncodeToPNG();
                Object.DestroyImmediate(readable);
                return png;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
