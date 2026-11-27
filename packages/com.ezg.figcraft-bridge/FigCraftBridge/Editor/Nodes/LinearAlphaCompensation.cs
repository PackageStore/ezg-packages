using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FigCraftBridge.Editor.Nodes
{
    /// <summary>
    ///     FigCraft blends a translucent layer in sRGB; a Linear colour space project blends UI in linear
    ///     light, so a light layer at low alpha over a dark one comes out much brighter (white at 0.18 over
    ///     #14100D: sRGB 59 in FigCraft, about 118 in Unity). After a screen prefab is saved, every visible
    ///     Graphic whose colour alpha is below 1 and every CanvasGroup below 1 gets the alpha whose linear
    ///     blend comes closest to FigCraft's sRGB blend, measured on the screen itself: the screen is drawn
    ///     once with the layer hidden (what lies under it) and once at alpha 1, the sRGB blend of the two at
    ///     the design alpha is the target, and the alpha is fitted by least squares in linear light over the
    ///     pixels the layer changes. Parents are fitted before their children, so a child is measured over
    ///     its already corrected parent. Only pixels over an opaque backdrop (or a popup's dim) count: a layer over a
    ///     see-through part of the frame (a HUD drawn over the game view) keeps its design alpha, as do
    ///     component prefabs, which have no backdrop; a screen writes its corrected alpha as an override on
    ///     the component instance it holds.
    /// </summary>
    public static class LinearAlphaCompensation
    {
        private const float MinAlpha = 0.002f;
        private const float MaxAlpha = 0.998f;
        private const double MinSignal = 1e-6;

        /// <summary>
        ///     A backdrop at least this opaque counts as known: a popup's dim layer (black at 0.86) is, a HUD over
        ///     the game view (alpha 0) is not. Its colour is taken as drawn over black, as the frame renders it.
        /// </summary>
        internal const byte MinBackdropAlpha = 200;

        internal static readonly float[] s_ToLinear = BuildToLinear();

        /// <summary>True when the project blends in linear light, the only case that needs the pass.</summary>
        public static bool IsNeeded => PlayerSettings.colorSpace == ColorSpace.Linear;

        /// <summary>Corrects the translucent layers of one screen prefab in place; returns the layers changed.</summary>
        public static int Apply(string screenPrefabPath)
        {
            if (!IsNeeded || string.IsNullOrEmpty(screenPrefabPath)) return 0;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(screenPrefabPath);
            if (prefab == null) return 0;
            var rootRect = prefab.transform as RectTransform;
            if (rootRect == null) return 0;
            var width = Mathf.RoundToInt(rootRect.rect.width);
            var height = Mathf.RoundToInt(rootRect.rect.height);
            if (width < 1 || height < 1) return 0;

            var fitted = Fit(prefab, width, height);
            if (fitted.Count == 0) return 0;

            var contents = PrefabUtility.LoadPrefabContents(screenPrefabPath);
            try
            {
                foreach (var (path, isGroup, color) in fitted)
                {
                    var target = Find(contents.transform, path);
                    if (target == null) continue;
                    if (isGroup)
                    {
                        var group = target.GetComponent<CanvasGroup>();
                        if (group != null) group.alpha = color.a;
                    }
                    else
                    {
                        var graphic = target.GetComponent<Graphic>();
                        if (graphic != null) graphic.color = color;
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(contents, screenPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            return fitted.Count;
        }

        /// <summary>Per layer: its path, whether it is a CanvasGroup, and its fitted colour (a group uses only alpha).</summary>
        private static List<(int[] path, bool isGroup, Color color)> Fit(GameObject prefab, int width, int height)
        {
            var result = new List<(int[], bool, Color)>();
            var scene = EditorSceneManager.NewPreviewScene();
            var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previousActive = RenderTexture.active;
            try
            {
                var camera = CreateCamera(scene, renderTexture);
                var instance = Stage(prefab, scene, camera, width, height);

                foreach (var target in Targets(instance.transform))
                {
                    // Only the pixels under the layer can change: read back its box, not the whole frame
                    var region = Region(target.Transform, camera, renderTexture);
                    if (region.width < 1 || region.height < 1) continue;
                    var designAlpha = target.Alpha;
                    // What is drawn over the layer is hidden, so a translucent plate or rim on top does not
                    // count as the layer's backdrop or colour; each of those layers is fitted on its own
                    var over = DrawnOver(instance.transform, target.Transform);
                    var overAlpha = new float[over.Count];
                    for (var i = 0; i < over.Count; i++)
                    {
                        overAlpha[i] = over[i].canvasRenderer.GetAlpha();
                        over[i].canvasRenderer.SetAlpha(0f);
                    }
                    target.Alpha = 0f;
                    var under = Render(camera, renderTexture, region);
                    target.Alpha = 1f;
                    var full = Render(camera, renderTexture, region);
                    for (var i = 0; i < over.Count; i++) over[i].canvasRenderer.SetAlpha(overAlpha[i]);

                    var alpha = FitAlpha(under, full, designAlpha);
                    if (float.IsNaN(alpha))
                    {
                        target.Alpha = designAlpha;
                        continue;
                    }
                    var drawn = target.IsGroup ? target.Transform.GetComponentsInChildren<Graphic>(false) : null;
                    var flat = (!target.IsGroup || drawn.Length == 1) && Solid(under, full);
                    // A flat layer takes its colour down per channel, which only darkens: its alpha must reach the
                    // channel that needs the most, or that channel stays short (white over brown came out grey)
                    if (flat) alpha = Mathf.Max(alpha, FitChannelAlpha(under, full, designAlpha));
                    target.Alpha = alpha;
                    if (target.IsGroup)
                    {
                        if (Mathf.Abs(alpha - designAlpha) >= 0.5f / 255f)
                            result.Add((PathOf(instance.transform, target.Transform), true, new Color(1f, 1f, 1f, alpha)));
                        // A frame at opacity 0.18 holding one flat fill is a flat layer too: its fill takes the colour
                        if (!flat) continue;
                        var fill = drawn[0];
                        var fillDesign = fill.color;
                        var fillColor = FitColour(under, full, designAlpha, alpha, fillDesign);
                        fillColor.a = fillDesign.a;
                        if (Mathf.Abs(fillColor.r - fillDesign.r) < 0.5f / 255f && Mathf.Abs(fillColor.g - fillDesign.g) < 0.5f / 255f &&
                            Mathf.Abs(fillColor.b - fillDesign.b) < 0.5f / 255f) continue;
                        fill.color = fillColor;
                        result.Add((PathOf(instance.transform, fill.transform), false, fillColor));
                        continue;
                    }
                    var design = target.Graphic.color;
                    var color = flat ? FitColour(under, full, designAlpha, alpha, design) : design;
                    color.a = alpha;
                    target.Graphic.color = color;
                    if (Mathf.Abs(alpha - designAlpha) < 0.5f / 255f && Mathf.Abs(color.r - design.r) < 0.5f / 255f &&
                        Mathf.Abs(color.g - design.g) < 0.5f / 255f && Mathf.Abs(color.b - design.b) < 0.5f / 255f) continue;
                    result.Add((PathOf(instance.transform, target.Transform), false, color));
                }
            }
            finally
            {
                RenderTexture.active = previousActive;
                EditorSceneManager.ClosePreviewScene(scene);
                renderTexture.Release();
                Object.DestroyImmediate(renderTexture);
            }
            return result;
        }

        /// <summary>Graphics drawn after <paramref name="node"/>'s subtree: pre-order is draw order on one canvas.</summary>
        private static List<Graphic> DrawnOver(Transform root, Transform node)
        {
            var all = root.GetComponentsInChildren<Graphic>(false);
            var over = new List<Graphic>();
            var past = false;
            foreach (var graphic in all)
            {
                var inside = graphic.transform == node || graphic.transform.IsChildOf(node);
                if (inside)
                {
                    past = true;
                    continue;
                }
                if (past) over.Add(graphic);
            }
            return over;
        }

        /// <summary>Pixel box of every Graphic under <paramref name="node"/>, itself included.</summary>
        private static RectInt Region(Transform node, Camera camera, RenderTexture target)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            var corners = new Vector3[4];
            foreach (var graphic in node.GetComponentsInChildren<Graphic>(false))
            {
                graphic.rectTransform.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var point = camera.WorldToScreenPoint(corner);
                    minX = Mathf.Min(minX, point.x);
                    minY = Mathf.Min(minY, point.y);
                    maxX = Mathf.Max(maxX, point.x);
                    maxY = Mathf.Max(maxY, point.y);
                }
            }
            if (minX > maxX) return default;
            var x0 = Mathf.Clamp(Mathf.FloorToInt(minX) - 1, 0, target.width);
            var y0 = Mathf.Clamp(Mathf.FloorToInt(minY) - 1, 0, target.height);
            var x1 = Mathf.Clamp(Mathf.CeilToInt(maxX) + 1, 0, target.width);
            var y1 = Mathf.Clamp(Mathf.CeilToInt(maxY) + 1, 0, target.height);
            return new RectInt(x0, y0, x1 - x0, y1 - y0);
        }

        /// <summary>
        ///     Least-squares alpha k for O(k) = U + k (F - U) in linear light against the sRGB blend
        ///     T = u + a (f - u), over every channel the layer changes. NaN when the layer changes nothing.
        /// </summary>
        private static float FitAlpha(Color32[] under, Color32[] full, float designAlpha)
        {
            double sxy = 0, sxx = 0;
            for (var i = 0; i < under.Length; i++)
            {
                var u = under[i];
                var f = full[i];
                // Over a see-through part of the frame (a HUD over the game view) the backdrop is unknown
                if (u.a < MinBackdropAlpha || (u.r == f.r && u.g == f.g && u.b == f.b)) continue;
                Accumulate(u.r, f.r, designAlpha, ref sxy, ref sxx);
                Accumulate(u.g, f.g, designAlpha, ref sxy, ref sxx);
                Accumulate(u.b, f.b, designAlpha, ref sxy, ref sxx);
            }
            if (sxx < MinSignal) return float.NaN;
            return Mathf.Clamp((float)(sxy / sxx), MinAlpha, MaxAlpha);
        }

        /// <summary>
        ///     A layer of one flat colour (a solid rectangle, a text fill): its opaque draw has the same colour on
        ///     every pixel it fully covers. Its colour can then be changed as well as its alpha; a textured layer
        ///     (art, an icon) keeps its colour, since a tint would change the art.
        /// </summary>
        private static bool Solid(Color32[] under, Color32[] full)
        {
            var counts = new Dictionary<int, int>();
            var n = 0;
            for (var i = 0; i < under.Length; i++)
            {
                var u = under[i];
                var f = full[i];
                if (u.a < MinBackdropAlpha || (u.r == f.r && u.g == f.g && u.b == f.b)) continue;
                n++;
                var key = (f.r >> 2) << 12 | (f.g >> 2) << 6 | (f.b >> 2);
                counts.TryGetValue(key, out var count);
                counts[key] = count + 1;
            }
            if (n < 16) return false;
            var top = 0;
            foreach (var count in counts.Values) top = Mathf.Max(top, count);
            // Anti-aliased edges and glyph outlines are the rest
            return top >= SolidShare * n;
        }

        /// <summary>Share of a layer's changed pixels that must draw one colour (within 4 levels) for it to count as flat.</summary>
        private const float SolidShare = 0.6f;

        /// <summary>
        ///     With the alpha <paramref name="alpha"/> fixed, each channel's colour is scaled in linear light by the
        ///     least-squares <c>s</c> of <c>U (1 - a) + a s F</c> against the sRGB blend at the design alpha: white at
        ///     0.18 over brown keeps the warm cast FigCraft shows, instead of the greyer linear blend. Never
        ///     brighter than 1.
        /// </summary>
        private static Color FitColour(Color32[] under, Color32[] full, float designAlpha, float alpha, Color design)
        {
            double fr = 0, fg = 0, fb = 0, tr = 0, tg = 0, tb = 0;
            for (var i = 0; i < under.Length; i++)
            {
                var u = under[i];
                var f = full[i];
                if (u.a < MinBackdropAlpha || (u.r == f.r && u.g == f.g && u.b == f.b)) continue;
                AccumulateColour(u.r, f.r, designAlpha, alpha, ref fr, ref tr);
                AccumulateColour(u.g, f.g, designAlpha, alpha, ref fg, ref tg);
                AccumulateColour(u.b, f.b, designAlpha, alpha, ref fb, ref tb);
            }
            float Channel(float value, double sff, double sft)
            {
                if (sff < MinSignal) return value;
                var scale = (float)(sft / sff);
                return Mathf.Clamp01(Mathf.LinearToGammaSpace(Mathf.GammaToLinearSpace(value) * scale));
            }
            return new Color(Channel(design.r, fr, tr), Channel(design.g, fg, tg), Channel(design.b, fb, tb), alpha);
        }

        private static void AccumulateColour(byte under, byte full, float designAlpha, float alpha, ref double sff, ref double sft)
        {
            var u = s_ToLinear[under];
            var f = alpha * s_ToLinear[full];
            var target = ToLinear(under / 255f + designAlpha * (full - under) / 255f) - u * (1f - alpha);
            sff += f * f;
            sft += f * target;
        }

        /// <summary>The largest of the least-squares alphas fitted on each channel alone.</summary>
        private static float FitChannelAlpha(Color32[] under, Color32[] full, float designAlpha)
        {
            double xr = 0, yr = 0, xg = 0, yg = 0, xb = 0, yb = 0;
            for (var i = 0; i < under.Length; i++)
            {
                var u = under[i];
                var f = full[i];
                if (u.a < MinBackdropAlpha || (u.r == f.r && u.g == f.g && u.b == f.b)) continue;
                Accumulate(u.r, f.r, designAlpha, ref yr, ref xr);
                Accumulate(u.g, f.g, designAlpha, ref yg, ref xg);
                Accumulate(u.b, f.b, designAlpha, ref yb, ref xb);
            }
            var best = 0f;
            if (xr >= MinSignal) best = Mathf.Max(best, (float)(yr / xr));
            if (xg >= MinSignal) best = Mathf.Max(best, (float)(yg / xg));
            if (xb >= MinSignal) best = Mathf.Max(best, (float)(yb / xb));
            return Mathf.Clamp(best, MinAlpha, MaxAlpha);
        }

        private static void Accumulate(byte under, byte full, float designAlpha, ref double sxy, ref double sxx)
        {
            var u = s_ToLinear[under];
            var x = s_ToLinear[full] - u;
            var target = under / 255f + designAlpha * (full - under) / 255f;
            var y = ToLinear(target) - u;
            sxy += x * y;
            sxx += x * x;
        }

        private sealed class Target
        {
            public Transform Transform;
            public bool IsGroup;
            public CanvasGroup Group;
            public Graphic Graphic;

            public float Alpha
            {
                get => IsGroup ? Group.alpha : Graphic.color.a;
                set
                {
                    if (IsGroup)
                    {
                        Group.alpha = value;
                        return;
                    }
                    var color = Graphic.color;
                    color.a = value;
                    Graphic.color = color;
                }
            }
        }

        /// <summary>Translucent groups and graphics that draw, parents before children.</summary>
        private static IEnumerable<Target> Targets(Transform node)
        {
            if (!node.gameObject.activeInHierarchy) yield break;
            var group = node.GetComponent<CanvasGroup>();
            if (group != null && group.enabled && group.alpha > MinAlpha && group.alpha < MaxAlpha)
                yield return new Target { Transform = node, IsGroup = true, Group = group };
            var graphic = node.GetComponent<Graphic>();
            // A Mask's graphic only feeds the stencil
            if (graphic != null && graphic.enabled && graphic.color.a > MinAlpha && graphic.color.a < MaxAlpha &&
                graphic.GetComponent<Mask>() == null)
                yield return new Target { Transform = node, Graphic = graphic };
            for (var i = 0; i < node.childCount; i++)
                foreach (var target in Targets(node.GetChild(i)))
                    yield return target;
        }

        internal static Camera CreateCamera(Scene scene, RenderTexture target)
        {
            var cameraObject = new GameObject("LinearAlphaCamera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.orthographic = true;
            camera.targetTexture = target;
            return camera;
        }

        internal static GameObject Stage(GameObject prefab, Scene scene, Camera camera, int width, int height)
        {
            var canvasObject = new GameObject("LinearAlphaCanvas", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10f;
            GammaVertexColors.Configure(canvas);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var rect = (RectTransform)instance.transform;
            rect.SetParent(canvasObject.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height);
            instance.SetActive(true);
            Canvas.ForceUpdateCanvases();
            foreach (var text in instance.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true, true);
            return instance;
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

        private static int[] PathOf(Transform root, Transform node)
        {
            var path = new List<int>();
            for (var current = node; current != null && current != root; current = current.parent)
                path.Add(current.GetSiblingIndex());
            path.Reverse();
            return path.ToArray();
        }

        private static Transform Find(Transform root, int[] path)
        {
            var current = root;
            foreach (var index in path)
            {
                if (index >= current.childCount) return null;
                current = current.GetChild(index);
            }
            return current;
        }

        internal static float ToLinear(float srgb) =>
            srgb <= 0.04045f ? srgb / 12.92f : Mathf.Pow((srgb + 0.055f) / 1.055f, 2.4f);

        private static float[] BuildToLinear()
        {
            var table = new float[256];
            for (var i = 0; i < 256; i++) table[i] = ToLinear(i / 255f);
            return table;
        }
    }
}
