using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        private const int MaxSamplesPerTexturePerScreen = 24;
        private const int BandWidth = 8;
        private const double MinBandSignal = 1e-3;

        /// <summary>A curve that moves no alpha level this far leaves the texture as it is.</summary>
        private const int MinCurveChange = 2;

        private const double MinSignal = 0.05;

        /// <summary>
        ///     A use gets a copy of its own when its curve stays this many alpha levels (RMS, weighted by what the
        ///     use draws at each alpha) from the texture's shared curve: a near-opaque dark plate over bright icons
        ///     wants more alpha than the same plate over the dark panels most uses sit on.
        /// </summary>
        private const float UseCurveRms = 4f;

        /// <summary>
        ///     ... and when its own curve removes at least this much squared error (alpha levels², weighted by
        ///     signal) against the shared one, so a small or faint use, where the shared curve is close enough
        ///     to see, keeps the shared copy instead of adding a file.
        /// </summary>
        private const double UseMinGain = 1e5;

        /// <summary>Uses of one texture whose own curves stay this close share one copy.</summary>
        private const int UseCurveShare = 8;

        /// <summary>The shared curve of each soft texture of the last <see cref="Apply"/>, null when it kept the texture.</summary>
        private static readonly Dictionary<string, float[]> s_SharedCurves = new Dictionary<string, float[]>();

        /// <summary>At or below this alpha level a lowered alpha is written as a darker colour only.</summary>
        private const int ColourOnlyBelow = 16;

        /// <summary>From this alpha level up the curve is written into the alpha only.</summary>
        private const int AlphaOnlyFrom = 48;

        /// <summary>Changes when the way a copy is written changes, so older copies are written again.</summary>
        private const string CopyVersion = "3:";

        private static int s_Draws;

        /// <summary>Time split of the last <see cref="Apply"/>, for the import report.</summary>
        public static string LastTiming { get; private set; } = "";

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
            public readonly Dictionary<string, Use> Uses = new Dictionary<string, Use>();
        }

        /// <summary>One Image of one screen that draws the texture, with its own sums.</summary>
        private sealed class Use
        {
            public string Screen;
            public string ImagePath;
            public readonly double[] Sxx = new double[256], Sxy = new double[256];
        }

        /// <summary>
        ///     Fits and writes the corrected copies for the soft textures the screens draw, then points the
        ///     Images of every prefab at them. Returns the number of corrected textures and of Images changed.
        /// </summary>
        public static (int textures, int images) Apply(IReadOnlyList<string> screenPaths, IReadOnlyList<string> prefabPaths)
        {
            if (!LinearAlphaCompensation.IsNeeded) return (0, 0);
            var sources = new Dictionary<string, Source>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            s_Draws = 0;
            s_SharedCurves.Clear();
            try
            {
                foreach (var screenPath in screenPaths) Measure(screenPath, sources, false);
                var measureTime = clock.Elapsed.TotalSeconds;

                var measured = new HashSet<string>();
                var copyPaths = new Dictionary<string, string>();
                var imports = new List<string>();
                foreach (var source in sources.Values)
                {
                    if (!source.Soft) continue;
                    measured.Add(source.Path);
                    var curve = FitCurve(source.Sxx, source.Sxy);
                    s_SharedCurves[source.Path] = curve;
                    if (curve != null)
                    {
                        var copyPath = WriteCopy(source, curve, CopyPath(source.Path, null), out var import);
                        if (copyPath != null)
                        {
                            copyPaths[source.Path] = copyPath;
                            if (import) imports.Add(copyPath);
                        }
                    }
                }
                // One batch: the copies import together, each once, with the importer settings its .meta already holds
                if (imports.Count > 0)
                {
                    AssetDatabase.StartAssetEditing();
                    try
                    {
                        foreach (var path in imports) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    }
                    finally
                    {
                        AssetDatabase.StopAssetEditing();
                    }
                }
                var copies = new Dictionary<string, Sprite>();
                foreach (var (sourcePath, copyPath) in copyPaths)
                {
                    var copy = AssetDatabase.LoadAssetAtPath<Sprite>(copyPath);
                    if (copy != null) copies[sourcePath] = copy;
                }
                var written = copies.Count;
                var writeTime = clock.Elapsed.TotalSeconds - measureTime;

                var changed = Repoint(prefabPaths, SpriteSwaps(copies, measured));
                LastTiming = $"measure {measureTime:0.0} s ({s_Draws} draws), write {writeTime:0.0} s, repoint {clock.Elapsed.TotalSeconds - measureTime - writeTime:0.0} s";
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

        /// <summary>
        ///     Runs after <see cref="LinearAlphaCompensation"/>, so each Image is measured over the backdrop the
        ///     player sees, with every translucent layer under it already fitted. A
        ///     use whose own curve stays far from the shared one (<see cref="NeedsOwnCurve"/>) gets a copy for it
        ///     (uses with near curves share one), set on that Image as an override. Returns the copies used and
        ///     the Images changed.
        /// </summary>
        public static (int copies, int images) ApplyPerUse(IReadOnlyList<string> screenPaths)
        {
            if (!LinearAlphaCompensation.IsNeeded) return (0, 0);
            var sources = new Dictionary<string, Source>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            s_Draws = 0;
            try
            {
                foreach (var screenPath in screenPaths) Measure(screenPath, sources, true);
                var measureTime = clock.Elapsed.TotalSeconds;

                var byScreen = new Dictionary<string, Dictionary<string, string>>();
                var imports = new List<string>();
                var used = new HashSet<string>();
                foreach (var source in sources.Values)
                {
                    if (!source.Soft) continue;
                    s_SharedCurves.TryGetValue(source.Path, out var shared);
                    var own = new List<(float[] curve, string path)>();
                    foreach (var use in source.Uses.Values.OrderBy(use => use.Screen + "|" + use.ImagePath, StringComparer.Ordinal))
                    {
                        if (!NeedsOwnCurve(use, shared, out var curve)) continue;
                        string path = null;
                        if (curve == null) path = source.Path;
                        else
                        {
                            foreach (var (other, otherPath) in own)
                                if (MaxGap(curve, other) <= UseCurveShare)
                                {
                                    path = otherPath;
                                    break;
                                }
                            if (path == null)
                            {
                                path = WriteCopy(source, curve, CopyPath(source.Path, curve), out var import);
                                if (path == null) continue;
                                if (import) imports.Add(path);
                                own.Add((curve, path));
                            }
                            used.Add(path);
                        }
                        if (!byScreen.TryGetValue(use.Screen, out var byImage)) byScreen[use.Screen] = byImage = new Dictionary<string, string>();
                        byImage[use.ImagePath] = path;
                    }
                }
                if (imports.Count > 0)
                {
                    AssetDatabase.StartAssetEditing();
                    try
                    {
                        foreach (var path in imports) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    }
                    finally
                    {
                        AssetDatabase.StopAssetEditing();
                    }
                }
                var writeTime = clock.Elapsed.TotalSeconds - measureTime;

                var changed = 0;
                foreach (var (screen, byImage) in byScreen)
                {
                    var contents = PrefabUtility.LoadPrefabContents(screen);
                    var screenChanged = 0;
                    try
                    {
                        foreach (var image in contents.GetComponentsInChildren<Image>(true))
                        {
                            if (!byImage.TryGetValue(PathKey(contents.transform, image.transform), out var path)) continue;
                            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                            if (sprite == null || sprite == image.sprite) continue;
                            image.sprite = sprite;
                            screenChanged++;
                        }
                        if (screenChanged > 0) PrefabUtility.SaveAsPrefabAsset(contents, screen);
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(contents);
                    }
                    changed += screenChanged;
                }
                LastTiming = $"{used.Count} copies for single uses; measure {measureTime:0.0} s ({s_Draws} draws), write {writeTime:0.0} s, " +
                             $"repoint {clock.Elapsed.TotalSeconds - measureTime - writeTime:0.0} s";
                return (used.Count, changed);
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

        private static float MaxGap(float[] a, float[] b)
        {
            var gap = 0f;
            for (var level = 0; level < 256; level++) gap = Mathf.Max(gap, Mathf.Abs(a[level] - b[level]));
            return gap;
        }

        /// <summary>
        ///     True when the use's own curve is <see cref="UseCurveRms"/> levels from the shared one and removes
        ///     <see cref="UseMinGain"/> of error, both weighted per band of alpha by the use's own signal.
        ///     <paramref name="own"/> is its curve, null when it wants the texture as it is.
        /// </summary>
        private static bool NeedsOwnCurve(Use use, float[] shared, out float[] own)
        {
            own = FitCurve(use.Sxx, use.Sxy);
            double weight = 0, gapSquares = 0, gain = 0;
            for (var start = 0; start < 256; start += BandWidth)
            {
                double sxx = 0, sxy = 0;
                for (var bin = start; bin < start + BandWidth; bin++)
                {
                    sxx += use.Sxx[bin];
                    sxy += use.Sxy[bin];
                }
                if (sxx < MinBandSignal) continue;
                var mid = start + BandWidth / 2;
                var best = 255 * sxy / sxx;
                double mine = own != null ? own[mid] : mid, theirs = shared != null ? shared[mid] : mid;
                weight += sxx;
                gapSquares += sxx * (mine - theirs) * (mine - theirs);
                gain += sxx * ((best - theirs) * (best - theirs) - (best - mine) * (best - mine));
            }
            return weight > 0 && gain >= UseMinGain && Math.Sqrt(gapSquares / weight) >= UseCurveRms;
        }

        #region Measure

        /// <summary>
        ///     <paramref name="perUse"/>: every Image is measured (no cap per texture) and its sums are kept per use too.
        /// </summary>
        private static void Measure(string screenPath, Dictionary<string, Source> sources, bool perUse)
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
                    if (!perUse && count >= MaxSamplesPerTexturePerScreen) continue;
                    var use = perUse ? UseOf(source, screenPath, PathKey(instance.transform, image.transform)) : null;
                    if (MeasureImage(image, graphics, i + 1, source, use, camera, renderTexture)) samples[source.Path] = count + 1;
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
        private static bool MeasureImage(Image image, Graphic[] graphics, int firstOver, Source source, Use use, Camera camera,
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
                Accumulate(under, full, map, source, use);
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
        private static void Accumulate(Color32[] under, Color32[] full, Color32[] map, Source source, Use use)
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
                Add(u.r, f.r, alpha, bin, source, use);
                Add(u.g, f.g, alpha, bin, source, use);
                Add(u.b, f.b, alpha, bin, source, use);
            }
        }

        private static void Add(byte under, byte full, float alpha, int bin, Source source, Use use)
        {
            var toLinear = LinearAlphaCompensation.s_ToLinear;
            var u = toLinear[under];
            var x = toLinear[full] - u;
            var y = LinearAlphaCompensation.ToLinear(under / 255f + alpha * (full - under) / 255f) - u;
            source.Sxx[bin] += x * x;
            source.Sxy[bin] += x * y;
            source.Syy[bin] += y * y;
            if (use == null) return;
            use.Sxx[bin] += x * x;
            use.Sxy[bin] += x * y;
        }

        private static Use UseOf(Source source, string screen, string imagePath)
        {
            var key = screen + "|" + imagePath;
            if (!source.Uses.TryGetValue(key, out var use))
                source.Uses[key] = use = new Use { Screen = screen, ImagePath = imagePath };
            return use;
        }

        /// <summary>Sibling indices from the prefab root, the same in a staged instance and in the prefab's contents.</summary>
        private static string PathKey(Transform root, Transform node)
        {
            var parts = new List<string>();
            for (var current = node; current != null && current != root; current = current.parent)
                parts.Add(current.GetSiblingIndex().ToString(System.Globalization.CultureInfo.InvariantCulture));
            parts.Reverse();
            return string.Join("/", parts);
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
            s_Draws++;
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

        /// <summary>The bridge's file behind a shared copy (<c>_linalpha</c>) or a copy for some uses (<c>_linalpha_&lt;8 hex&gt;</c>).</summary>
        internal static string OriginalPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return path;
            var stem = path.Substring(0, path.Length - ".png".Length);
            if (stem.EndsWith(Suffix, StringComparison.Ordinal)) return stem.Substring(0, stem.Length - Suffix.Length) + ".png";
            var at = stem.Length - Suffix.Length - 9;
            if (at > 0 && string.CompareOrdinal(stem, at, Suffix + "_", 0, Suffix.Length + 1) == 0) return stem.Substring(0, at) + ".png";
            return path;
        }

        /// <summary>The shared copy of a texture, or a copy for the uses that want <paramref name="curve"/>.</summary>
        private static string CopyPath(string sourcePath, float[] curve)
        {
            var stem = sourcePath.Substring(0, sourcePath.Length - ".png".Length) + Suffix;
            if (curve == null) return stem + ".png";
            var bytes = new byte[curve.Length * 4];
            Buffer.BlockCopy(curve, 0, bytes, 0, bytes.Length);
            return stem + "_" + Md5(bytes).Substring(0, 8) + ".png";
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
        private static float[] FitCurve(double[] sxxBins, double[] sxyBins)
        {
            double signal = 0;
            for (var bin = 1; bin < 255; bin++) signal += sxxBins[bin];
            if (signal < MinSignal) return null;

            var blocks = new List<(double weight, double value, double position)>();
            for (var start = 0; start < 256; start += BandWidth)
            {
                double sxx = 0, sxy = 0, position = 0;
                for (var bin = start; bin < start + BandWidth; bin++)
                {
                    sxx += sxxBins[bin];
                    sxy += sxyBins[bin];
                    position += bin * sxxBins[bin];
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
        ///     The copy with the curve applied. Where the curve lowers a faint alpha, the texel keeps its own
        ///     alpha and its colour is darkened in linear light by <c>curve / alpha</c> instead: one alpha level
        ///     near 0 is about 13 sRGB levels of a white glow over black, so a lowered alpha of 0 or 1 showed as
        ///     a speckled band and a hard box at the edge of a glow, while a darker colour at the same alpha
        ///     gives the same light over a dark backdrop to a fraction of a level (over a light backdrop the
        ///     error is at most the alpha itself). From <see cref="ColourOnlyBelow"/> to
        ///     <see cref="AlphaOnlyFrom"/> the two ways blend; above, ordered dither picks between the two
        ///     nearest alpha levels so the curve does not band. <paramref name="import"/> is true when the copy
        ///     was written and waits for the batch import.
        /// </summary>
        private static string WriteCopy(Source source, float[] curve, string copyPath, out bool import)
        {
            import = false;
            var curveBytes = new byte[curve.Length * 4];
            Buffer.BlockCopy(curve, 0, curveBytes, 0, curveBytes.Length);
            var marker = Marker + CopyVersion + Md5(source.Bytes) + ":" + Md5(curveBytes);
            if (!(File.Exists(copyPath) && AssetImporter.GetAtPath(copyPath) is TextureImporter current && current.userData == marker))
            {
                var target = new float[256];
                for (var level = 0; level < 256; level++) target[level] = TargetAlpha(level, curve[level]);
                var pixels = new Color32[source.Pixels.Length];
                for (var i = 0; i < pixels.Length; i++)
                {
                    var p = source.Pixels[i];
                    if (p.a == 0 || p.a == 255)
                    {
                        pixels[i] = p;
                        continue;
                    }
                    var alpha = (byte)Mathf.Clamp(Mathf.FloorToInt(target[p.a] + s_Bayer[(i % source.Width & 3) + ((i / source.Width & 3) << 2)]), 0, 255);
                    var scale = alpha == 0 ? 0f : Mathf.Min(1f, curve[p.a] / alpha);
                    pixels[i] = scale >= 0.999f
                        ? new Color32(p.r, p.g, p.b, alpha)
                        : new Color32(Scale(p.r, scale, source.SRGB), Scale(p.g, scale, source.SRGB), Scale(p.b, scale, source.SRGB), alpha);
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
                if (WriteMeta(source.Path, copyPath, marker)) import = true;
                else
                {
                    AssetDatabase.ImportAsset(copyPath, ImportAssetOptions.ForceUpdate);
                    CopyImporter(source.Path, copyPath, marker);
                }
            }
            return copyPath;
        }

        private static readonly Regex s_GuidLine = new Regex(@"^guid: \w+", RegexOptions.Multiline);
        private static readonly Regex s_UserDataLine = new Regex(@"^  userData:.*$", RegexOptions.Multiline);

        /// <summary>
        ///     The bridge file's .meta with the copy's own GUID (kept when the copy exists, so prefabs stay
        ///     linked) and the marker, so the copy imports once with the same settings, border and pivot.
        /// </summary>
        private static bool WriteMeta(string sourcePath, string copyPath, string marker)
        {
            var sourceMeta = sourcePath + ".meta";
            if (!File.Exists(sourceMeta)) return false;
            var text = File.ReadAllText(sourceMeta);
            if (!s_GuidLine.IsMatch(text) || !s_UserDataLine.IsMatch(text)) return false;
            var guid = AssetDatabase.AssetPathToGUID(copyPath, AssetPathToGUIDOptions.OnlyExistingAssets);
            if (string.IsNullOrEmpty(guid)) guid = GUID.Generate().ToString();
            text = s_GuidLine.Replace(text, "guid: " + guid, 1);
            text = s_UserDataLine.Replace(text, "  userData: " + marker, 1);
            File.WriteAllText(copyPath + ".meta", text);
            return true;
        }

        /// <summary>The alpha a texel at <paramref name="level"/> is written with, before dither.</summary>
        private static float TargetAlpha(int level, float curved)
        {
            if (curved >= level || level >= AlphaOnlyFrom) return curved;
            if (level <= ColourOnlyBelow) return level;
            var t = (level - ColourOnlyBelow) / (float)(AlphaOnlyFrom - ColourOnlyBelow);
            return level + t * (curved - level);
        }

        private static byte Scale(byte channel, float scale, bool srgb)
        {
            if (!srgb) return (byte)Mathf.Clamp(Mathf.RoundToInt(channel * scale), 0, 255);
            var linear = LinearAlphaCompensation.s_ToLinear[channel] * scale;
            return (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.LinearToGammaSpace(linear) * 255f), 0, 255);
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
        ///     Sprite reference swaps: the bridge's file to its copy for every corrected texture, and a copy no
        ///     longer needed back to the bridge's file. Keyed by the whole YAML reference of a single sprite.
        /// </summary>
        private static Dictionary<string, string> SpriteSwaps(Dictionary<string, Sprite> copies, HashSet<string> measured)
        {
            var swaps = new Dictionary<string, string>();
            foreach (var path in measured)
            {
                var sourceGuid = AssetDatabase.AssetPathToGUID(path);
                var copyPath = path.Substring(0, path.Length - ".png".Length) + Suffix + ".png";
                var copyGuid = AssetDatabase.AssetPathToGUID(copyPath, AssetPathToGUIDOptions.OnlyExistingAssets);
                if (string.IsNullOrEmpty(sourceGuid) || string.IsNullOrEmpty(copyGuid)) continue;
                if (copies.ContainsKey(path)) swaps[SpriteRef(sourceGuid)] = SpriteRef(copyGuid);
                else swaps[SpriteRef(copyGuid)] = SpriteRef(sourceGuid);
            }
            return swaps;
        }

        private static string SpriteRef(string guid) => "{fileID: " + SingleSpriteFileId + ", guid: " + guid + ", type: 3}";

        /// <summary>The file id of the one sprite of a texture imported as a single sprite.</summary>
        private const int SingleSpriteFileId = 21300000;

        /// <summary>
        ///     Rewrites the sprite references in the prefabs' YAML and imports the changed prefabs in one batch.
        ///     An Image, and an override of a nested prefab's Image, that shows a corrected texture then shows its
        ///     copy; an Image inside a nested prefab that is not overridden follows its own prefab, which is in
        ///     the list too. Returns the number of references changed.
        /// </summary>
        private static int Repoint(IReadOnlyList<string> prefabPaths, Dictionary<string, string> swaps)
        {
            if (swaps.Count == 0) return 0;
            var pattern = new Regex(@"\{fileID: " + SingleSpriteFileId + @", guid: [0-9a-f]{32}, type: 3\}");
            var changed = 0;
            var written = new List<string>();
            foreach (var prefabPath in prefabPaths)
            {
                if (string.IsNullOrEmpty(prefabPath) || !File.Exists(prefabPath)) continue;
                var yaml = File.ReadAllText(prefabPath);
                var count = 0;
                var result = pattern.Replace(yaml, match =>
                {
                    if (!swaps.TryGetValue(match.Value, out var swap)) return match.Value;
                    count++;
                    return swap;
                });
                if (count == 0) continue;
                File.WriteAllText(prefabPath, result);
                written.Add(prefabPath);
                changed += count;
            }
            if (written.Count == 0) return 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in written) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            return changed;
        }

        #endregion
    }
}
