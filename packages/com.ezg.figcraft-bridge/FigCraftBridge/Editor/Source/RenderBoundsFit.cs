using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FigCraftBridge.Editor.Source
{
    /// <summary>
    ///     FigCraft reports no render bounds, and its unclipped export pads the canvas beyond the ink. When a
    ///     render's size differs from the box the importer computes, the layout-box export is found inside the
    ///     full render by its pixels, the transparent margin is trimmed (never inside the layout box), and the
    ///     result is kept as outsets of the layout box: left, top, right, bottom in design units.
    /// </summary>
    public static class RenderBoundsFit
    {
        public const string StorePath = "Library/FigCraftBridge/render-outsets.json";
        const int SamplePixels = 400;
        const byte InkAlpha = 32;

        public static Dictionary<string, float[]> Load()
        {
            try
            {
                return File.Exists(StorePath)
                    ? JsonConvert.DeserializeObject<Dictionary<string, float[]>>(File.ReadAllText(StorePath)) ?? new Dictionary<string, float[]>()
                    : new Dictionary<string, float[]>();
            }
            catch (JsonException)
            {
                return new Dictionary<string, float[]>();
            }
        }

        public static void Save(Dictionary<string, float[]> outsets)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath) ?? ".");
            File.WriteAllText(StorePath, JsonConvert.SerializeObject(outsets));
        }

        /// <summary>Sets <c>absoluteRenderBounds</c> on a REST node from its layout box grown by the outsets.</summary>
        public static void Apply(JObject node, float[] outsets)
        {
            if (!(node["absoluteBoundingBox"] is JObject box) || outsets == null || outsets.Length != 4) return;
            node["absoluteRenderBounds"] = new JObject
            {
                ["x"] = box.Value<float>("x") - outsets[0],
                ["y"] = box.Value<float>("y") - outsets[1],
                ["width"] = box.Value<float>("width") + outsets[0] + outsets[2],
                ["height"] = box.Value<float>("height") + outsets[1] + outsets[3],
            };
        }

        /// <summary>Main thread only. Rewrites <paramref name="fullPath"/> trimmed; false when the box is not found.</summary>
        public static bool Fit(string fullPath, string boxPath, int scale, out float[] outsets)
        {
            outsets = null;
            var full = Load(fullPath, out var fullWidth, out var fullHeight);
            var box = Load(boxPath, out var boxWidth, out var boxHeight);
            if (full == null || box == null || boxWidth > fullWidth || boxHeight > fullHeight) return false;

            var samples = new List<int>();
            var step = Mathf.Max(1, boxWidth * boxHeight / (SamplePixels * 4));
            for (var i = 0; i < box.Length && samples.Count < SamplePixels; i += step)
                if (box[i].a > InkAlpha) samples.Add(i);
            if (samples.Count == 0) return false;

            int Score(int offsetX, int offsetY)
            {
                var sum = 0;
                foreach (var i in samples)
                {
                    int x = i % boxWidth, yTop = i / boxWidth;
                    var a = box[i];
                    var b = full[(offsetY + yTop) * fullWidth + offsetX + x];
                    sum += Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a);
                }
                return sum;
            }

            int bestX = (fullWidth - boxWidth) / 2, bestY = (fullHeight - boxHeight) / 2;
            var best = Score(bestX, bestY);
            if (best > samples.Count * 4)
                for (var y = 0; y <= fullHeight - boxHeight; y++)
                for (var x = 0; x <= fullWidth - boxWidth; x++)
                {
                    var score = Score(x, y);
                    if (score >= best) continue;
                    best = score;
                    bestX = x;
                    bestY = y;
                }
            if (best > samples.Count * 24) return false;

            int minX = bestX, minY = bestY, maxX = bestX + boxWidth - 1, maxY = bestY + boxHeight - 1;
            for (var y = 0; y < fullHeight; y++)
            for (var x = 0; x < fullWidth; x++)
            {
                if (full[y * fullWidth + x].a == 0) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            int cropWidth = maxX - minX + 1, cropHeight = maxY - minY + 1;
            var cropped = new Color32[cropWidth * cropHeight];
            for (var y = 0; y < cropHeight; y++)
            for (var x = 0; x < cropWidth; x++)
                cropped[(cropHeight - 1 - y) * cropWidth + x] = full[(minY + y) * fullWidth + minX + x];
            var texture = new Texture2D(cropWidth, cropHeight, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(cropped);
                File.WriteAllBytes(fullPath, texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            outsets = new[]
            {
                (float)(bestX - minX) / scale, (float)(bestY - minY) / scale,
                (float)(maxX - (bestX + boxWidth - 1)) / scale, (float)(maxY - (bestY + boxHeight - 1)) / scale,
            };
            return true;
        }

        /// <summary>Pixels top row first.</summary>
        static Color32[] Load(string path, out int width, out int height)
        {
            width = height = 0;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path))) return null;
                width = texture.width;
                height = texture.height;
                var bottomUp = texture.GetPixels32();
                var topDown = new Color32[bottomUp.Length];
                for (var y = 0; y < height; y++)
                    System.Array.Copy(bottomUp, (height - 1 - y) * width, topDown, y * width, width);
                return topDown;
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
