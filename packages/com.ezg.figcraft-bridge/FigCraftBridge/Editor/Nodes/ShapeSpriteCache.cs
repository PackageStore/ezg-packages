using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FigCraftBridge.Editor.FigmaApi;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace FigCraftBridge.Editor.Nodes
{
    /// <summary>
    ///     Remembers which Shapes sprite each set of <see cref="FrameShapeSprite"/> inputs drew, across
    ///     imports, so a shape already on disk is not drawn and PNG-encoded again just to find its name.
    ///     The key is a hash of every input of the draw plus the package version; an entry whose
    ///     sprite is gone is drawn again.
    /// </summary>
    internal static class ShapeSpriteCache
    {
        private const string StorePath = "Library/FigCraftBridge/shape-sprites.json";

        private static Dictionary<string, string> s_Paths;
        private static string s_Salt;

        public static string Key(Node node, int width, int height, Vector4 radii, Paint stroke, Vector4 strokeSides,
            bool white, List<Effect> innerShadows)
        {
            var fills = new List<Paint>();
            if (node.fills != null)
                foreach (var fill in node.fills)
                    if (fill != null && fill.visible && !FigmaDataUtils.IsShaderPaint(fill)) fills.Add(fill);

            var json = JsonConvert.SerializeObject(new
            {
                salt = Salt(),
                width,
                height,
                radii = new[] { radii.x, radii.y, radii.z, radii.w },
                sides = new[] { strokeSides.x, strokeSides.y, strokeSides.z, strokeSides.w },
                white,
                fills,
                stroke,
                innerShadows
            });
            using var md5 = MD5.Create();
            return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", "");
        }

        public static Sprite Get(string key)
        {
            Load();
            if (!s_Paths.TryGetValue(key, out var path)) return null;
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) s_Paths.Remove(key);
            return sprite;
        }

        public static void Set(string key, string path)
        {
            Load();
            if (s_Paths.TryGetValue(key, out var known) && known == path) return;
            s_Paths[key] = path;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath));
                File.WriteAllText(StorePath, JsonConvert.SerializeObject(s_Paths));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FigCraftBridge] Could not write {StorePath}: {e.Message}");
            }
        }

        private static void Load()
        {
            if (s_Paths != null) return;
            s_Paths = new Dictionary<string, string>();
            if (!File.Exists(StorePath)) return;
            try
            {
                s_Paths = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(StorePath)) ?? s_Paths;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FigCraftBridge] Ignoring unreadable {StorePath}: {e.Message}");
            }
        }

        private static string Salt()
        {
            if (s_Salt != null) return s_Salt;
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ShapeSpriteCache).Assembly);
            return s_Salt = info != null ? info.version : string.Empty;
        }
    }
}
