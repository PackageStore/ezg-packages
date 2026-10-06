using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Nodes;
using UnityFigmaBridge.Editor.Utils;
using Object = UnityEngine.Object;

namespace UnityFigmaBridge.Editor.NineSlice
{
    public static class ServerRenderTint
    {
        /// <param name="serverRenderNodes">Renders to process: downloaded by this import online, every render offline.</param>
        /// <param name="allRenderNodes">The full import list (FigmaImportProcessData.ServerRenderNodes).</param>
        /// <returns>Number of PNG files rewritten.</returns>
        public static int Run(List<ServerRenderNodeData> serverRenderNodes, List<ServerRenderNodeData> allRenderNodes)
        {
            if (serverRenderNodes == null || allRenderNodes == null || serverRenderNodes.Count == 0) return 0;

            var count = 0;
            var visited = new HashSet<string>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var entry in serverRenderNodes)
                {
                    if (entry == null || entry.SourceNode == null) continue;
                    var id = entry.SourceNode.id;
                    if (!visited.Add(id)) continue;
                    if (!SolidTint.TryGetEntryTint(id, allRenderNodes, out _)) continue;
                    var path = FigmaPaths.GetPathForServerRenderedImage(id, allRenderNodes);
                    if (!File.Exists(path)) continue;
                    if (Whiten(path)) count++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            if (count > 0) Debug.Log($"[FigmaBridge] Whitened {count} tintable render(s)");
            return count;
        }

        private static bool Whiten(string path)
        {
            var previousBytes = File.ReadAllBytes(path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(previousBytes)) return false;
                var width = texture.width;
                var height = texture.height;
                var pixels = texture.GetPixels32();

                var needsWrite = false;
                for (var i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    if (p.a > 0 && (p.r != 255 || p.g != 255 || p.b != 255))
                    {
                        needsWrite = true;
                        break;
                    }
                }
                if (!needsWrite) return false;

                for (var i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    pixels[i] = new Color32(255, 255, 255, p.a);
                }

                var currentBytes = ServerRenderSlicer.EncodePng(new ServerRenderSlicer.SliceResult
                {
                    Pixels = pixels,
                    Width = width,
                    Height = height
                });
                File.WriteAllBytes(path, currentBytes);
                ServerRenderSlicer.RestampMarker(path, previousBytes, currentBytes);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                return true;
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
