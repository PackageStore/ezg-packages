using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FigCraftBridge.Editor.Nodes
{
    /// <summary>
    ///     The texture side of <see cref="LinearAlphaCompensation"/>: an image whose own pixels are soft
    ///     (a glow, a soft rim, a plate at alpha 240; colour alpha 1, alpha in the texture) blends brighter in a
    ///     Linear project than FigCraft's sRGB blend, and a colour alpha cannot fix it because each texel
    ///     needs its own correction. Each soft texture the imported screens draw gets an alpha curve
    ///     <c>a → a'</c> baked into a sibling copy, <c>&lt;name&gt;_linalpha.png</c>, and every Image of the
    ///     imported prefabs that shows the texture is pointed at the copy; the bridge's own file stays as
    ///     written, so derived crops and caches never see a corrected texture.
    ///     The curve is fitted on the screens: each Image is drawn hidden, with its texture forced opaque, and
    ///     with a map of its texture alpha, every Graphic drawn over it hidden, which gives per pixel the
    ///     backdrop, the colour and the coverage; the sRGB blend is the target and the curve is the
    ///     least-squares fit in linear light over every use of the texture over an opaque backdrop.
    ///     No runtime cost: the copy is an ordinary sprite.
    /// </summary>
    public static class LinearAlphaTextures
    {
        internal const string Suffix = "_linalpha";
        private const string Marker = "figcraft-linear-alpha:";

        /// <summary>Share of the drawn texels (alpha &gt; 0) below <see cref="OpaqueAlpha"/> that makes a texture soft.</summary>
        private const float SoftShare = 0.2f;

        private const byte OpaqueAlpha = 250;
        private const int MaxSamplesPerTexturePerScreen = 3;
        private const int BandWidth = 8;
        private const double MinBandSignal = 1e-3;

        /// <summary>A curve that moves no alpha level this far leaves the texture as it is.</summary>
        private const int MinCurveChange = 2;

        private const double MinSignal = 0.05;

        private sealed class Source
        {
            public string Path;
            public byte[] Bytes;
            public Color32[] Pixels;
            public int Width, Height;
            public bool Soft;
            public bool SRGB;
            public Texture2D Opaque, AlphaMap;
            public readonly double[] Sxx = new double[256], Sxy = new double[256], Syy = new double[256];
        }

        /// <summary>
        ///     Fits and writes the corrected copies for the soft textures the screens draw, then points the
        ///     Images of every prefab at them. Returns the number of corrected textures and of Images changed.
        /// </summary>
        public static (int textures, int images) Apply(IReadOnlyList<string> screenPaths, IReadOnlyList<string> prefabPaths)
        {
            if (!LinearAlphaCompensation.IsNeeded) return (0, 0);
            var sources = new Dictionary<string, Source>();
            try
            {
                foreach (var screenPath in screenPaths) Measure(screenPath, sources);

                var copies = new Dictionary<string, Sprite>();
                var measured = new HashSet<string>();
                var written = 0;
                foreach (var source in sources.Values)
                {
                    if (!source.Soft) continue;
                    measured.Add(source.Path);
                    var curve = FitCurve(source);
                    if (curve == null) continue;
                    var copy = WriteCopy(source, curve);
                    if (copy == null) continue;
                    copies[source.Path] = copy;
                    written++;
                }

                var changed = 0;
                if (copies.Count > 0 || measured.Count > 0)
                    foreach (var prefabPath in prefabPaths) changed += Repoint(prefabPath, copies, measured);
                return (written, changed);
            }
            finally
            {
                foreach (var source in sources.Values)
                {
                    if (source.Opaque != null) Object.DestroyImmediate(source.Opaque);
                    if (source.AlphaMap != null) Object.DestroyImmediate(source.AlphaMap);
                }
            }
        }

        #region Measure

        private static void Measure(string screenPath, Dictionary<string, Source> sources)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(screenPath);
            if (prefab == null || prefab.transform is not RectTransform rootRect) return;
            var width = Mathf.RoundToInt(rootRect.rect.width);
            var height = Mathf.RoundToInt(rootRect.rect.height);
            if (width < 1 || height < 1) return;

            var scene = EditorSceneManager.NewPreviewScene();
            var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previousActive = RenderTexture.active;
            try
            {
                var camera = LinearAlphaCompensation.CreateCamera(scene, renderTexture);
                var instance = LinearAlphaCompensation.Stage(prefab, scene, camera, width, height);
                var samples = new Dictionary<string, int>();
                // Pre-order is draw order on one canvas: everything after an Image is drawn over it
                var graphics = instance.GetComponentsInChildren<Graphic>(false);
                for (var i = 0; i < graphics.Length; i++)
                {
                    if (graphics[i] is not Image image || !Drawn(image)) continue;
                    var source = SourceOf(image.sprite, sources);
                    if (source == null || !source.Soft) continue;
                    samples.TryGetValue(source.Path, out var count);
                    if (count >= MaxSamplesPerTexturePerScreen) continue;
                    if (MeasureImage(image, graphics, i + 1, source, camera, renderTexture)) samples[source.Path] = count + 1;
                }
            }
            finally
            {
                RenderTexture.active = previousActive;
                EditorSceneManager.ClosePreviewScene(scene);
                renderTexture.Release();
                Object.DestroyImmediate(renderTexture);
            }
        }

        /// <summary>Drawn with full strength: a group above it below alpha 1 would scale the alpha map too.</summary>
        private static bool Drawn(Image image)
        {
            if (!image.enabled || image.sprite == null || image.GetComponent<Mask>() != null) return false;
            if (image.canvasRenderer.cull) return false;
            for (var node = image.transform; node != null; node = node.parent)
            {
                var group = node.GetComponent<CanvasGroup>();
                if (group != null && group.enabled && group.alpha < 0.998f) return false;
            }
            return true;
        }

        /// <summary>
        ///     Draws the Image three ways with every Graphic drawn over it hidden, so a translucent layer on top
        ///     (a white plate at 0.18 over a glow) does not leak into the backdrop, the colour or the alpha map.
        /// </summary>
        private static bool MeasureImage(Image image, Graphic[] graphics, int firstOver, Source source, Camera camera,
            RenderTexture target)
        {
            var region = ScreenRegion(image.rectTransform, camera, target);
            if (region.width < 1 || region.height < 1) return false;
            var sprite = image.sprite;
            if (Mathf.RoundToInt(sprite.rect.width) != source.Width || Mathf.RoundToInt(sprite.rect.height) != source.Height) return false;

            var color = image.color;
            var overAlpha = new float[graphics.Length - firstOver];
            for (var i = firstOver; i < graphics.Length; i++)
            {
                overAlpha[i - firstOver] = graphics[i].canvasRenderer.GetAlpha();
                graphics[i].canvasRenderer.SetAlpha(0f);
            }
            var opaque = SpriteFor(source.Opaque ??= Copy(source, false), sprite);
            var alphaMap = SpriteFor(source.AlphaMap ??= Copy(source, true), sprite);
            try
            {
                image.color = new Color(color.r, color.g, color.b, 0f);
                var under = Render(camera, target, region);
                image.overrideSprite = opaque;
                image.color = new Color(color.r, color.g, color.b, 1f);
                var full = Render(camera, target, region);
                image.overrideSprite = alphaMap;
                image.color = Color.white;
                var map = Render(camera, target, region);
                Accumulate(under, full, map, source);
                return true;
            }
            finally
            {
                image.overrideSprite = null;
                image.color = color;
                for (var i = firstOver; i < graphics.Length; i++) graphics[i].canvasRenderer.SetAlpha(overAlpha[i - firstOver]);
                Object.DestroyImmediate(opaque);
                Object.DestroyImmediate(alphaMap);
            }
        }

        /// <summary>
        ///     Per channel the opaque draw <c>f</c> over the backdrop <c>u</c> at the filtered texture alpha
        ///     <c>a</c>: FigCraft shows <c>u + a (f - u)</c> in sRGB, Unity <c>U + g(a) (F - U)</c> in linear light.
        ///     Sums are binned by <c>a</c> so that the curve can be fitted later over every use of the texture.
        /// </summary>
        private static void Accumulate(Color32[] under, Color32[] full, Color32[] map, Source source)
        {
            var toLinear = LinearAlphaCompensation.s_ToLinear;
            for (var i = 0; i < under.Length; i++)
            {
                var u = under[i];
                var f = full[i];
                if (u.a < LinearAlphaCompensation.MinBackdropAlpha || (u.r == f.r && u.g == f.g && u.b == f.b)) continue;
                var alpha = toLinear[map[i].r];
                var bin = Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255);
                if (bin == 0 || bin == 255) continue;
                Add(u.r, f.r, alpha, bin, source);
                Add(u.g, f.g, alpha, bin, source);
                Add(u.b, f.b, alpha, bin, source);
            }
        }

        private static void Add(byte under, byte full, float alpha, int bin, Source source)
        {
            var toLinear = LinearAlphaCompensation.s_ToLinear;
            var u = toLinear[under];
            var x = toLinear[full] - u;
            var y = LinearAlphaCompensation.ToLinear(under / 255f + alpha * (full - under) / 255f) - u;
            source.Sxx[bin] += x * x;
            source.Sxy[bin] += x * y;
            source.Syy[bin] += y * y;
        }

        private static RectInt ScreenRegion(RectTransform rect, Camera camera, RenderTexture target)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var corner in corners)
            {
                var point = camera.WorldToScreenPoint(corner);
                minX = Mathf.Min(minX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxX = Mathf.Max(maxX, point.x);
                maxY = Mathf.Max(maxY, point.y);
            }
            var x0 = Mathf.Clamp(Mathf.FloorToInt(minX), 0, target.width);
            var y0 = Mathf.Clamp(Mathf.FloorToInt(minY), 0, target.height);
            var x1 = Mathf.Clamp(Mathf.CeilToInt(maxX), 0, target.width);
            var y1 = Mathf.Clamp(Mathf.CeilToInt(maxY), 0, target.height);
            return new RectInt(x0, y0, x1 - x0, y1 - y0);
        }

        private static Color32[] Render(Camera camera, RenderTexture target, RectInt region)
        {
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            var readback = new Texture2D(region.width, region.height, TextureFormat.RGBA32, false);
            try
            {
                readback.ReadPixels(new Rect(region.x, region.y, region.width, region.height), 0, 0, false);
                return readback.GetPixels32();
            }
            finally
            {
                Object.DestroyImmediate(readback);
            }
        }

        #endregion

        #region Sources

        /// <summary>The PNG behind a sprite (the bridge's own file for a corrected copy), loaded once.</summary>
        private static Source SourceOf(Sprite sprite, Dictionary<string, Source> sources)
        {
            var path = OriginalPath(AssetDatabase.GetAssetPath(sprite));
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return null;
            if (sources.TryGetValue(path, out var source)) return source;

            source = new Source { Path = path };
            sources[path] = source;
            if (!File.Exists(path) || AssetImporter.GetAtPath(path) is not TextureImporter importer ||
                importer.spriteImportMode != SpriteImportMode.Single) return source;

            source.Bytes = File.ReadAllBytes(path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(source.Bytes)) return source;
                source.Pixels = texture.GetPixels32();
                source.Width = texture.width;
                source.Height = texture.height;
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
            source.SRGB = importer.sRGBTexture;
            source.Soft = IsSoft(source.Pixels);
            return source;
        }

        private static bool IsSoft(Color32[] pixels)
        {
            int drawn = 0, soft = 0;
            foreach (var pixel in pixels)
            {
                if (pixel.a == 0) continue;
                drawn++;
                if (pixel.a < OpaqueAlpha) soft++;
            }
            return drawn > 0 && soft >= SoftShare * drawn;
        }

        internal static string OriginalPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var end = Suffix + ".png";
            return path.EndsWith(end, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(0, path.Length - end.Length) + ".png"
                : path;
        }

        /// <summary>The texture forced opaque (its colour), or its alpha as a grey level in a linear texture.</summary>
        private static Texture2D Copy(Source source, bool alphaMap)
        {
            var pixels = new Color32[source.Pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = source.Pixels[i];
                pixels[i] = alphaMap ? new Color32(p.a, p.a, p.a, 255) : new Color32(p.r, p.g, p.b, 255);
            }
            var texture = new Texture2D(source.Width, source.Height, TextureFormat.RGBA32, false, alphaMap || !source.SRGB)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static Sprite SpriteFor(Texture2D texture, Sprite like)
        {
            var rect = like.rect;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(like.pivot.x / rect.width, like.pivot.y / rect.height), like.pixelsPerUnit, 0,
                SpriteMeshType.FullRect, like.border);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        #endregion

        #region Fit and write

        /// <summary>
        ///     Alpha curve <c>a → a'</c> fitted per band of 8 alpha levels: the least-squares <c>a'</c> of each band,
        ///     pooled into a non-decreasing curve (pool adjacent violators, weighted), linear between bands,
        ///     0 at 0 and 255 at 255. One texture can need both ways at once: a soft light rim over a dark
        ///     backdrop wants less alpha, the near-opaque dark centre of the same plate over light art wants
        ///     more. Null when the texture changed too few pixels or the curve stays within 2 levels of itself.
        /// </summary>
        private static float[] FitCurve(Source source)
        {
            double signal = 0;
            for (var bin = 1; bin < 255; bin++) signal += source.Sxx[bin];
            if (signal < MinSignal) return null;

            var blocks = new List<(double weight, double value, double position)>();
            for (var start = 0; start < 256; start += BandWidth)
            {
                double sxx = 0, sxy = 0, position = 0;
                for (var bin = start; bin < start + BandWidth; bin++)
                {
                    sxx += source.Sxx[bin];
                    sxy += source.Sxy[bin];
                    position += bin * source.Sxx[bin];
                }
                if (sxx < MinBandSignal) continue;
                blocks.Add((sxx, Math.Max(0, Math.Min(1, sxy / sxx)), position / sxx));
                while (blocks.Count >= 2 && blocks[^2].value > blocks[^1].value)
                {
                    var (w1, v1, p1) = blocks[^2];
                    var (w2, v2, p2) = blocks[^1];
                    blocks.RemoveRange(blocks.Count - 2, 2);
                    blocks.Add((w1 + w2, (w1 * v1 + w2 * v2) / (w1 + w2), (w1 * p1 + w2 * p2) / (w1 + w2)));
                }
            }
            if (blocks.Count == 0) return null;

            var knots = new List<(double position, double value)> { (0, 0) };
            foreach (var block in blocks) knots.Add((block.position, block.value));
            knots.Add((255, 1));
            var curve = new float[256];
            var changed = false;
            var k = 0;
            for (var level = 0; level < 256; level++)
            {
                while (k < knots.Count - 2 && knots[k + 1].position < level) k++;
                var (p0, v0) = knots[k];
                var (p1, v1) = knots[k + 1];
                var t = p1 > p0 ? Math.Max(0, Math.Min(1, (level - p0) / (p1 - p0))) : 1;
                curve[level] = (float)Math.Max(0, Math.Min(255, 255 * (v0 + t * (v1 - v0))));
                changed |= Math.Abs(curve[level] - level) >= MinCurveChange;
            }
            curve[0] = 0;
            curve[255] = 255;
            return changed ? curve : null;
        }

        /// <summary>4 × 4 ordered dither thresholds in [0, 1).</summary>
        private static readonly float[] s_Bayer =
        {
            0.5f / 16, 8.5f / 16, 2.5f / 16, 10.5f / 16,
            12.5f / 16, 4.5f / 16, 14.5f / 16, 6.5f / 16,
            3.5f / 16, 11.5f / 16, 1.5f / 16, 9.5f / 16,
            15.5f / 16, 7.5f / 16, 13.5f / 16, 5.5f / 16,
        };

        /// <summary>
        ///     The copy with the curve applied. Ordered dither picks between the two nearest alpha levels: in
        ///     linear light one alpha level near 0 is about 13 sRGB levels of a white glow, so plain rounding
        ///     would band the faint edge of a glow.
        /// </summary>
        private static Sprite WriteCopy(Source source, float[] curve)
        {
            var copyPath = source.Path.Substring(0, source.Path.Length - ".png".Length) + Suffix + ".png";
            var curveBytes = new byte[curve.Length * 4];
            Buffer.BlockCopy(curve, 0, curveBytes, 0, curveBytes.Length);
            var marker = Marker + Md5(source.Bytes) + ":" + Md5(curveBytes);
            if (!(File.Exists(copyPath) && AssetImporter.GetAtPath(copyPath) is TextureImporter current && current.userData == marker))
            {
                var pixels = new Color32[source.Pixels.Length];
                for (var i = 0; i < pixels.Length; i++)
                {
                    var p = source.Pixels[i];
                    var alpha = p.a == 0 || p.a == 255 ? p.a
                        : (byte)Mathf.Clamp(Mathf.FloorToInt(curve[p.a] + s_Bayer[(i % source.Width & 3) + ((i / source.Width & 3) << 2)]), 0, 255);
                    pixels[i] = new Color32(p.r, p.g, p.b, alpha);
                }
                var texture = new Texture2D(source.Width, source.Height, TextureFormat.RGBA32, false);
                try
                {
                    texture.SetPixels32(pixels);
                    texture.Apply(false, false);
                    File.WriteAllBytes(copyPath, texture.EncodeToPNG());
                }
                finally
                {
                    Object.DestroyImmediate(texture);
                }
                AssetDatabase.ImportAsset(copyPath, ImportAssetOptions.ForceUpdate);
                CopyImporter(source.Path, copyPath, marker);
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(copyPath);
        }

        private static string Md5(byte[] bytes)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            return BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static readonly string[] s_Platforms = { "Standalone", "Android", "iPhone", "WebGL" };

        /// <summary>Same import as the bridge's own file, border and pivot included.</summary>
        private static void CopyImporter(string sourcePath, string copyPath, string marker)
        {
            if (AssetImporter.GetAtPath(copyPath) is not TextureImporter target ||
                AssetImporter.GetAtPath(sourcePath) is not TextureImporter source) return;
            var settings = new TextureImporterSettings();
            source.ReadTextureSettings(settings);
            target.SetTextureSettings(settings);
            foreach (var platform in s_Platforms)
            {
                var platformSettings = source.GetPlatformTextureSettings(platform);
                if (platformSettings.overridden) target.SetPlatformTextureSettings(platformSettings);
            }
            target.userData = marker;
            target.SaveAndReimport();
        }

        #endregion

        #region Repoint

        /// <summary>
        ///     Points every Image of one prefab that shows a corrected texture at its copy, and every Image that
        ///     shows a copy no longer needed back at the bridge's file. An Image inside a nested prefab whose
        ///     sprite is not overridden here follows its own prefab and is left alone.
        /// </summary>
        private static int Repoint(string prefabPath, Dictionary<string, Sprite> copies, HashSet<string> measured)
        {
            if (string.IsNullOrEmpty(prefabPath) || !File.Exists(prefabPath)) return 0;
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            var changed = 0;
            try
            {
                foreach (var image in contents.GetComponentsInChildren<Image>(true))
                {
                    if (image.sprite == null) continue;
                    var spritePath = AssetDatabase.GetAssetPath(image.sprite);
                    var original = OriginalPath(spritePath);
                    Sprite wanted;
                    if (copies.TryGetValue(original, out var copy)) wanted = copy;
                    else if (original != spritePath && measured.Contains(original)) wanted = AssetDatabase.LoadAssetAtPath<Sprite>(original);
                    else continue;
                    if (wanted == null || wanted == image.sprite) continue;
                    if (PrefabUtility.IsPartOfPrefabInstance(image) &&
                        PrefabUtility.GetCorrespondingObjectFromSource(image) is Image inner && inner.sprite == image.sprite)
                        continue;
                    image.sprite = wanted;
                    changed++;
                }
                if (changed > 0) PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            return changed;
        }

        #endregion
    }
}
