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
                foreach (var (path, isGroup, alpha) in fitted)
                {
                    var target = Find(contents.transform, path);
                    if (target == null) continue;
                    if (isGroup)
                    {
                        var group = target.GetComponent<CanvasGroup>();
                        if (group != null) group.alpha = alpha;
                    }
                    else
                    {
                        var graphic = target.GetComponent<Graphic>();
                        if (graphic == null) continue;
                        var color = graphic.color;
                        color.a = alpha;
                        graphic.color = color;
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

        private static List<(int[] path, bool isGroup, float alpha)> Fit(GameObject prefab, int width, int height)
        {
            var result = new List<(int[], bool, float)>();
            var scene = EditorSceneManager.NewPreviewScene();
            var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var readback = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var previousActive = RenderTexture.active;
            try
            {
                var camera = CreateCamera(scene, renderTexture);
                var instance = Stage(prefab, scene, camera, width, height);

                foreach (var target in Targets(instance.transform))
                {
                    var designAlpha = target.Alpha;
                    target.Alpha = 0f;
                    var under = Render(instance, camera, renderTexture, readback);
                    target.Alpha = 1f;
                    var full = Render(instance, camera, renderTexture, readback);

                    var alpha = FitAlpha(under, full, designAlpha);
                    if (float.IsNaN(alpha))
                    {
                        target.Alpha = designAlpha;
                        continue;
                    }
                    target.Alpha = alpha;
                    if (Mathf.Abs(alpha - designAlpha) < 0.5f / 255f) continue;
                    result.Add((PathOf(instance.transform, target.Transform), target.IsGroup, alpha));
                }
            }
            finally
            {
                RenderTexture.active = previousActive;
                EditorSceneManager.ClosePreviewScene(scene);
                renderTexture.Release();
                Object.DestroyImmediate(renderTexture);
                Object.DestroyImmediate(readback);
            }
            return result;
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

        private static Color32[] Render(GameObject instance, Camera camera, RenderTexture target, Texture2D readback)
        {
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            readback.Apply();
            return readback.GetPixels32();
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
