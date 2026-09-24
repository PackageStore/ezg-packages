using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Settings;
using UnityFigmaBridge.Editor.Utils;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;

namespace UnityFigmaBridge.Editor.Verify
{
    /// <summary>
    ///     Scores a generated screen prefab against Figma's own render of its frame. Each container
    ///     (a visible node with children) gets the SSIM of its render bounds, so a small wrong part
    ///     cannot hide behind a large correct background. Both captures, a difference map, the
    ///     report and a side-by-side crop (Figma | Unity | difference) of every container below its
    ///     pass score (<c>low/</c>) are written to <see cref="OutputRoot"/>/&lt;prefab name&gt;.
    /// </summary>
    public static class FigmaVisualCheck
    {
        public const float DefaultPassScore = 0.9f;

        /// <summary>TextMeshPro rasterises and outlines glyphs its own way, so text alone passes lower.</summary>
        public const float DefaultTextPassScore = 0.85f;
        public const string OutputRoot = "Library/FigmaVisualCheck";

        private const int MinContainerSide = 16;
        private const int WindowSize = 8;
        private const int WindowStride = 4;
        private const double RequestTimeoutSeconds = 120;

        // SSIM stabilisers for 8 bit channels: (0.01 * 255)^2 and (0.03 * 255)^2
        private const double C1 = 6.5025;
        private const double C2 = 58.5225;

        [Serializable]
        public class ContainerScore
        {
            public string path;
            public string nodeId;
            public int x, y, width, height;
            public bool textOnly;
            public float passScore;
            public float score;
        }

        [Serializable]
        public class Report
        {
            public string prefab;
            public string frameNodeId;
            public float passScore;
            public float textPassScore;
            public float minScore;
            public bool passed;
            public string folder;
            public string checkedAtUtc;
            public List<ContainerScore> containers = new List<ContainerScore>();

            public override string ToString()
            {
                var lines = containers.OrderBy(c => c.score - c.passScore)
                    .Select(c => $"{(c.score >= c.passScore ? "  ok " : "  LOW")} {c.score:0.000}{(c.textOnly ? " text" : "     ")}  {c.path}");
                return $"[FigmaVisualCheck] {prefab}: {(passed ? "PASS" : "FAIL")} min {minScore:0.000} " +
                       $"(pass {passScore:0.00}, text {textPassScore:0.00}, {containers.Count} containers) -> {folder}\n" +
                       string.Join("\n", lines);
            }
        }

        /// <summary>Asset path of the prefab selected in the Project window, or null.</summary>
        public static string SelectedPrefabPath()
        {
            var path = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : null;
            return !string.IsNullOrEmpty(path) && path.EndsWith(".prefab") ? path : null;
        }

        /// <param name="screenPrefabPath">A screen prefab the bridge generated.</param>
        /// <param name="passScore">Lowest container SSIM that passes.</param>
        /// <param name="textPassScore">Lowest SSIM that passes for a container that draws nothing but text.</param>
        /// <param name="refreshReference">Fetch Figma's render again instead of the one saved by an earlier check.</param>
        public static Report Run(string screenPrefabPath, float passScore = DefaultPassScore,
            float textPassScore = DefaultTextPassScore, bool refreshReference = false)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(screenPrefabPath);
            if (prefab == null) throw new ArgumentException($"No prefab at '{screenPrefabPath}'");
            var frame = FindFrame(screenPrefabPath);

            var width = Mathf.RoundToInt(frame.absoluteBoundingBox.width);
            var height = Mathf.RoundToInt(frame.absoluteBoundingBox.height);
            var folder = $"{OutputRoot}/{Path.GetFileNameWithoutExtension(screenPrefabPath)}";
            Directory.CreateDirectory(folder);

            var referencePath = $"{folder}/figma.png";
            if (refreshReference || !File.Exists(referencePath))
                File.WriteAllBytes(referencePath, FetchFigmaRender(frame.id));
            var reference = LoadPixels(File.ReadAllBytes(referencePath), out var referenceWidth, out var referenceHeight);
            if (referenceWidth != width || referenceHeight != height)
                throw new InvalidOperationException($"Figma render is {referenceWidth}x{referenceHeight}, frame is {width}x{height}");

            var capture = CapturePrefab(prefab, width, height);
            var difference = DifferenceMap(reference, capture);
            File.WriteAllBytes($"{folder}/unity.png", EncodePng(capture, width, height));
            File.WriteAllBytes($"{folder}/diff.png", EncodePng(difference, width, height));

            var ssim = WindowSsim(reference, capture, width, height, out var windowsX, out var windowsY);
            var report = new Report
            {
                prefab = screenPrefabPath, frameNodeId = frame.id, passScore = passScore, textPassScore = textPassScore,
                folder = folder, checkedAtUtc = DateTime.UtcNow.ToString("o")
            };
            foreach (var (node, path) in Containers(frame, frame.name))
            {
                var box = PixelBox(node, frame, width, height);
                if (box.width < MinContainerSide || box.height < MinContainerSide) continue;
                var textOnly = DrawsOnlyText(node);
                report.containers.Add(new ContainerScore
                {
                    path = path, nodeId = node.id, x = box.x, y = box.y, width = box.width, height = box.height,
                    textOnly = textOnly, passScore = textOnly ? textPassScore : passScore,
                    score = MeanInside(ssim, windowsX, windowsY, box, height)
                });
            }

            report.minScore = report.containers.Count > 0 ? report.containers.Min(c => c.score) : 1f;
            report.passed = report.containers.All(c => c.score >= c.passScore);
            WriteLowCrops(report, reference, capture, difference, width, height);
            File.WriteAllText($"{folder}/report.json", JsonUtility.ToJson(report, true));
            return report;
        }

        /// <summary>One PNG per failing container, worst margin first: Figma, Unity and difference side by side.</summary>
        private static void WriteLowCrops(Report report, Color32[] reference, Color32[] capture, Color32[] difference,
            int width, int height)
        {
            const int gap = 4;
            var folder = $"{report.folder}/low";
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            var low = report.containers.Where(c => c.score < c.passScore).OrderBy(c => c.score - c.passScore).ToList();
            if (low.Count == 0) return;
            Directory.CreateDirectory(folder);
            var gapColor = new Color32(255, 0, 255, 255);
            for (var i = 0; i < low.Count; i++)
            {
                var box = low[i];
                var cropWidth = box.width * 3 + gap * 2;
                var pixels = Enumerable.Repeat(gapColor, cropWidth * box.height).ToArray();
                // Texture rows count from the bottom
                var sourceBottom = height - (box.y + box.height);
                for (var row = 0; row < box.height; row++)
                for (var column = 0; column < box.width; column++)
                {
                    var source = (sourceBottom + row) * width + box.x + column;
                    var target = row * cropWidth + column;
                    pixels[target] = reference[source];
                    pixels[target + box.width + gap] = capture[source];
                    pixels[target + 2 * (box.width + gap)] = difference[source];
                }
                var name = string.Join("_", box.path.Split('/').Skip(1));
                name = new string(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch == ' ' ? '_' : ch).ToArray());
                if (name.Length > 80) name = name.Substring(name.Length - 80);
                File.WriteAllBytes($"{folder}/{i:00}-{name}.png", EncodePng(pixels, cropWidth, box.height));
            }
        }

        /// <summary>The frame of the cached document that the bridge writes to this prefab path.</summary>
        private static Node FindFrame(string screenPrefabPath)
        {
            var document = FigmaApiUtils.LoadCachedDocument();
            if (document == null) throw new InvalidOperationException("No cached Figma document - run Sync Document once");
            FigmaPaths.Configure(UnityFigmaBridgeSettingsProvider.FindUnityBridgeSettingsAsset(), document.name);
            foreach (var pageNode in FigmaDataUtils.GetPageNodes(document))
            foreach (var screenNode in FigmaDataUtils.GetScreenNodes(pageNode))
                if (FigmaPaths.GetPathForScreenPrefab(screenNode, 0) == screenPrefabPath) return screenNode;
            throw new InvalidOperationException($"No frame of the cached document writes '{screenPrefabPath}'");
        }

        private static IEnumerable<(Node node, string path)> Containers(Node node, string path)
        {
            if (node.children == null) yield break;
            foreach (var child in node.children)
            {
                if (!child.visible || child.children == null || child.children.Length == 0) continue;
                var childPath = $"{path}/{child.name}";
                yield return (child, childPath);
                foreach (var nested in Containers(child, childPath)) yield return nested;
            }
        }

        /// <summary>True when every visible part of the subtree that draws anything is a text node.</summary>
        private static bool DrawsOnlyText(Node node)
        {
            if (!node.visible) return true;
            if (node.type == NodeType.TEXT) return true;
            var drawsFill = node.fills != null && node.fills.Any(fill => fill != null && fill.visible &&
                (fill.type != Paint.PaintType.SOLID || fill.color == null || fill.color.a * fill.opacity > 0f));
            var drawsStroke = node.strokes != null && node.strokeWeight > 0 && node.strokes.Any(stroke => stroke != null && stroke.visible);
            var drawsEffect = node.effects != null && node.effects.Any(effect => effect.visible);
            if (drawsFill || drawsStroke || drawsEffect) return false;
            return node.children == null || node.children.All(DrawsOnlyText);
        }

        /// <summary>Render bounds (outside strokes and shadows included) in frame pixels, top-left origin.</summary>
        private static RectInt PixelBox(Node node, Node frame, int width, int height)
        {
            var box = node.absoluteBoundingBox;
            var bounds = node.absoluteRenderBounds ?? box;
            var xMin = Mathf.Min(box.x, bounds.x) - frame.absoluteBoundingBox.x;
            var yMin = Mathf.Min(box.y, bounds.y) - frame.absoluteBoundingBox.y;
            var xMax = Mathf.Max(box.x + box.width, bounds.x + bounds.width) - frame.absoluteBoundingBox.x;
            var yMax = Mathf.Max(box.y + box.height, bounds.y + bounds.height) - frame.absoluteBoundingBox.y;
            var x0 = Mathf.Clamp(Mathf.FloorToInt(xMin), 0, width);
            var y0 = Mathf.Clamp(Mathf.FloorToInt(yMin), 0, height);
            var x1 = Mathf.Clamp(Mathf.CeilToInt(xMax), 0, width);
            var y1 = Mathf.Clamp(Mathf.CeilToInt(yMax), 0, height);
            return new RectInt(x0, y0, x1 - x0, y1 - y0);
        }

        private static byte[] FetchFigmaRender(string nodeId)
        {
            var settings = UnityFigmaBridgeSettingsProvider.FindUnityBridgeSettingsAsset();
            var token = FigmaAccessToken.Read();
            if (settings == null || string.IsNullOrEmpty(token))
                throw new InvalidOperationException("Bridge settings or Figma token missing");

            var url = $"https://api.figma.com/v1/images/{settings.FileId}?ids={Uri.EscapeDataString(nodeId)}" +
                      "&scale=1&format=png&use_absolute_bounds=true";
            var renderData = JsonConvert.DeserializeObject<FigmaServerRenderData>(
                System.Text.Encoding.UTF8.GetString(GetBlocking(url, token)));
            if (renderData?.images == null || !renderData.images.TryGetValue(nodeId, out var imageUrl) || string.IsNullOrEmpty(imageUrl))
                throw new InvalidOperationException($"Figma returned no render for {nodeId}");
            return GetBlocking(imageUrl, null);
        }

        private static byte[] GetBlocking(string url, string token)
        {
            using var request = UnityWebRequest.Get(url);
            if (token != null) request.SetRequestHeader("X-Figma-Token", token);
            var operation = request.SendWebRequest();
            var deadline = EditorApplication.timeSinceStartup + RequestTimeoutSeconds;
            while (!operation.isDone)
            {
                if (EditorApplication.timeSinceStartup > deadline)
                {
                    request.Abort();
                    throw new TimeoutException($"No answer from {url}");
                }
                Thread.Sleep(20);
            }
            if (request.result != UnityWebRequest.Result.Success)
                throw new InvalidOperationException($"HTTP {request.responseCode} {request.error} for {url}");
            return request.downloadHandler.data;
        }

        /// <summary>
        ///     Draws the prefab at its frame size through a camera in a preview scene, so the open
        ///     scene is not touched. One canvas unit is one pixel, as in Figma's 1x render.
        /// </summary>
        private static Color32[] CapturePrefab(GameObject prefab, int width, int height)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previousActive = RenderTexture.active;
            try
            {
                var cameraObject = new GameObject("VisualCheckCamera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.AddComponent<Camera>();
                camera.scene = scene;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.orthographic = true;
                camera.targetTexture = renderTexture;

                var canvasObject = new GameObject("VisualCheckCanvas", typeof(RectTransform));
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
                Canvas.ForceUpdateCanvases();
                camera.Render();

                RenderTexture.active = renderTexture;
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                var pixels = texture.GetPixels32();
                Object.DestroyImmediate(texture);
                return pixels;
            }
            finally
            {
                RenderTexture.active = previousActive;
                // The camera must go first: releasing its target texture while it holds it logs a warning
                EditorSceneManager.ClosePreviewScene(scene);
                renderTexture.Release();
                Object.DestroyImmediate(renderTexture);
            }
        }

        private static Color32[] LoadPixels(byte[] png, out int width, out int height)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(png)) throw new InvalidOperationException("Figma render is not a PNG");
                width = texture.width;
                height = texture.height;
                return texture.GetPixels32();
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        private static byte[] EncodePng(Color32[] pixels, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply();
                return texture.EncodeToPNG();
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>Colour composited over black, so a transparent pixel compares as black on both sides.</summary>
        private static double Channel(Color32 pixel, int channel)
        {
            var value = channel == 0 ? pixel.r : channel == 1 ? pixel.g : pixel.b;
            return value * (pixel.a / 255.0);
        }

        private static Color32[] DifferenceMap(Color32[] reference, Color32[] capture)
        {
            var map = new Color32[reference.Length];
            for (var i = 0; i < reference.Length; i++)
            {
                var difference = 0.0;
                for (var channel = 0; channel < 3; channel++)
                    difference = Math.Max(difference, Math.Abs(Channel(reference[i], channel) - Channel(capture[i], channel)));
                var level = (byte)Math.Min(255, difference * 2);
                map[i] = new Color32(level, (byte)(level / 4), (byte)(level / 4), 255);
            }
            return map;
        }

        /// <summary>SSIM of each window (texture rows, bottom-up), averaged over the three colour channels.</summary>
        private static float[] WindowSsim(Color32[] a, Color32[] b, int width, int height, out int windowsX, out int windowsY)
        {
            windowsX = (width - WindowSize) / WindowStride + 1;
            windowsY = (height - WindowSize) / WindowStride + 1;
            var result = new float[windowsX * windowsY];
            const int count = WindowSize * WindowSize;
            for (var wy = 0; wy < windowsY; wy++)
            for (var wx = 0; wx < windowsX; wx++)
            {
                var total = 0.0;
                for (var channel = 0; channel < 3; channel++)
                {
                    double sumA = 0, sumB = 0, sumAA = 0, sumBB = 0, sumAB = 0;
                    for (var y = 0; y < WindowSize; y++)
                    {
                        var row = (wy * WindowStride + y) * width + wx * WindowStride;
                        for (var x = 0; x < WindowSize; x++)
                        {
                            var va = Channel(a[row + x], channel);
                            var vb = Channel(b[row + x], channel);
                            sumA += va; sumB += vb; sumAA += va * va; sumBB += vb * vb; sumAB += va * vb;
                        }
                    }
                    var meanA = sumA / count;
                    var meanB = sumB / count;
                    var varA = sumAA / count - meanA * meanA;
                    var varB = sumBB / count - meanB * meanB;
                    var covariance = sumAB / count - meanA * meanB;
                    total += (2 * meanA * meanB + C1) * (2 * covariance + C2) /
                             ((meanA * meanA + meanB * meanB + C1) * (varA + varB + C2));
                }
                result[wy * windowsX + wx] = (float)(total / 3);
            }
            return result;
        }

        /// <summary>Mean SSIM of the windows that lie inside a top-left-origin pixel box.</summary>
        private static float MeanInside(float[] ssim, int windowsX, int windowsY, RectInt box, int height)
        {
            // Texture rows count from the bottom
            var bottom = height - (box.y + box.height);
            var x0 = Mathf.CeilToInt(box.x / (float)WindowStride);
            var x1 = (box.x + box.width - WindowSize) / WindowStride;
            var y0 = Mathf.CeilToInt(bottom / (float)WindowStride);
            var y1 = (bottom + box.height - WindowSize) / WindowStride;
            double sum = 0;
            var count = 0;
            for (var wy = Mathf.Max(0, y0); wy <= Mathf.Min(windowsY - 1, y1); wy++)
            for (var wx = Mathf.Max(0, x0); wx <= Mathf.Min(windowsX - 1, x1); wx++)
            {
                sum += ssim[wy * windowsX + wx];
                count++;
            }
            return count > 0 ? (float)(sum / count) : 1f;
        }
    }
}
