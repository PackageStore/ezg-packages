using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Utils;
using Object = UnityEngine.Object;

namespace UnityFigmaBridge.Editor.NineSlice
{
    /// <summary>
    ///     UGUI draws every Image with normal alpha blending, and a MULTIPLY layer drawn that way lightens a
    ///     dark backdrop instead of darkening it. Multiply only ever darkens: over any backdrop it equals
    ///     black at alpha a·(1 − luminance), exactly for grey and closely for the dark tints shades use. A
    ///     server render of a MULTIPLY node is rewritten in that form, so a plain Image draws it.
    /// </summary>
    public static class ServerRenderMultiply
    {
        /// <param name="serverRenderNodes">Renders to process: downloaded by this import online, every render offline.</param>
        /// <param name="allRenderNodes">The full import list (FigmaImportProcessData.ServerRenderNodes).</param>
        /// <returns>Number of PNG files rewritten.</returns>
        public static int Run(List<ServerRenderNodeData> serverRenderNodes, List<ServerRenderNodeData> allRenderNodes)
        {
            if (serverRenderNodes == null || allRenderNodes == null) return 0;
            var count = 0;
            var visited = new HashSet<string>();
            foreach (var entry in serverRenderNodes)
            {
                if (entry?.SourceNode == null || entry.RenderType != ServerRenderType.Substitution) continue;
                if (entry.SourceNode.blendMode != BlendMode.MULTIPLY || !visited.Add(entry.SourceNode.id)) continue;
                var path = FigmaPaths.GetPathForServerRenderedImage(entry.SourceNode.id, allRenderNodes);
                if (File.Exists(path) && Darken(path)) count++;
            }
            if (count > 0) Debug.Log($"[FigmaBridge] {count} MULTIPLY render(s) drawn as black at their darkening alpha");
            return count;
        }

        /// <returns>False when the render already holds no colour (a re-import of a converted file).</returns>
        private static bool Darken(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                var previousBytes = File.ReadAllBytes(path);
                if (!texture.LoadImage(previousBytes)) return false;
                var pixels = texture.GetPixels32();
                var changed = false;
                for (var i = 0; i < pixels.Length; i++)
                {
                    var pixel = pixels[i];
                    if (pixel.a == 0 || (pixel.r == 0 && pixel.g == 0 && pixel.b == 0)) continue;
                    var luminance = (0.2126f * pixel.r + 0.7152f * pixel.g + 0.0722f * pixel.b) / 255f;
                    pixels[i] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(pixel.a * (1f - luminance)));
                    changed = true;
                }
                if (!changed) return false;

                texture.SetPixels32(pixels);
                texture.Apply();
                var currentBytes = texture.EncodeToPNG();
                File.WriteAllBytes(path, currentBytes);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                // A render the slicer already compacted keeps its border; slicing it again would guess
                ServerRenderSlicer.RestampMarker(path, previousBytes, currentBytes);
                return true;
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
