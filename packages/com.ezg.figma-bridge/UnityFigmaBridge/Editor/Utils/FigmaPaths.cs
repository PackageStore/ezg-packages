using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Settings;
using UnityFigmaBridge.Runtime.UI;

namespace UnityFigmaBridge.Editor.Utils
{
    public static class FigmaPaths
    {
        private const string DefaultRoot = "Assets/Figma";

        // Fonts and their material presets are TextMeshPro assets, and TMP is a hard requirement of
        // this package, so they default beside TMP's own fonts rather than under the Figma root. A
        // downloaded Fredoka_600.ttf, the Fredoka_600_SDF.asset built from it and its material
        // presets all sit together.
        private const string DefaultFontsRoot = "Assets/TextMesh Pro/Fonts";

        public static string FigmaAssetsRootFolder { get; private set; } = DefaultRoot;
        public static string FigmaPagePrefabFolder { get; private set; } = $"{DefaultRoot}/Pages";
        public static string FigmaScreenPrefabFolder { get; private set; } = $"{DefaultRoot}/Screens";
        public static string FigmaComponentPrefabFolder { get; private set; } = $"{DefaultRoot}/Components";
        public static string FigmaImageFillFolder { get; private set; } = $"{DefaultRoot}/ImageFills";
        public static string FigmaServerRenderedImagesFolder { get; private set; } = $"{DefaultRoot}/ServerRenderedImages";
        public static string FigmaFontMaterialPresetsFolder { get; private set; } = DefaultFontsRoot;
        public static string FigmaFontsFolder { get; private set; } = DefaultFontsRoot;

        private static Dictionary<string, FigmaScreenNameOverride> s_ScreenNameLookup;
        private static bool s_OnlyImportListedScreens;

        public static void Configure(UnityFigmaBridgeSettings settings)
        {
            var root = NormalisePath(settings.AssetsRootFolder, DefaultRoot);
            FigmaAssetsRootFolder = root;
            FigmaPagePrefabFolder = NormalisePath(settings.PagePrefabFolder, $"{root}/Pages");
            FigmaScreenPrefabFolder = NormalisePath(settings.ScreenPrefabFolder, $"{root}/Screens");
            FigmaComponentPrefabFolder = NormalisePath(settings.ComponentPrefabFolder, $"{root}/Components");
            FigmaImageFillFolder = NormalisePath(settings.ImageFillFolder, $"{root}/ImageFills");
            FigmaServerRenderedImagesFolder = $"{root}/ServerRenderedImages";

            // Deliberately not derived from root: these are TMP assets, not Figma output.
            FigmaFontsFolder = NormalisePath(settings.FontsFolder, DefaultFontsRoot);
            FigmaFontMaterialPresetsFolder =
                NormalisePath(settings.FontMaterialPresetsFolder, FigmaFontsFolder);

            s_OnlyImportListedScreens = settings.OnlyImportListedScreens;
            s_ScreenNameLookup = new Dictionary<string, FigmaScreenNameOverride>();
            if (settings.ScreenNameOverrides != null)
            {
                foreach (var entry in settings.ScreenNameOverrides)
                {
                    if (string.IsNullOrWhiteSpace(entry.FrameName)) continue;
                    s_ScreenNameLookup[entry.FrameName] = entry;
                }
            }
        }

        private static string NormalisePath(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            var trimmed = value.TrimEnd('/','\\').Trim();
            if (!trimmed.StartsWith("Assets/") && !trimmed.StartsWith("Assets\\"))
            {
                Debug.LogWarning($"[FigmaPaths] Path '{trimmed}' does not start with Assets/; using default '{fallback}'");
                return fallback;
            }
            return trimmed;
        }

        public static string GetPathForImageFill(string imageId)
        {
            if (!FigmaImageFillNamer.TryGetRelativeName(imageId, out var relativeName))
                return $"{FigmaImageFillFolder}/{imageId}.png";

            var path = $"{FigmaImageFillFolder}/{relativeName}.png";
            EnsureDirectory(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            return path;
        }

        public static string GetPathForServerRenderedImage(string nodeId,
            List<ServerRenderNodeData> serverRenderNodeData)
        {
            var matchingEntry = serverRenderNodeData.FirstOrDefault((node) => node.SourceNode.id == nodeId);
            switch (matchingEntry.RenderType)
            {
                case ServerRenderType.Export:
                    return $"Assets/{matchingEntry.SourceNode.name}.png";
                default:
                    var safeNodeId = FigmaDataUtils.ReplaceUnsafeFileCharactersForNodeId(nodeId);
                    return $"{FigmaServerRenderedImagesFolder}/{safeNodeId}.png";
            }
        }

        public static string GetPathForScreenPrefab(Node node, int duplicateCount)
        {
            if (s_ScreenNameLookup != null && s_ScreenNameLookup.TryGetValue(node.name, out var screenOverride))
            {
                if (screenOverride.ExcludeFromImport) return null;

                if (string.IsNullOrWhiteSpace(screenOverride.PrefabName))
                    return $"{FigmaScreenPrefabFolder}/{GetFileNameForNode(node, duplicateCount)}.prefab";

                var safeName = ReplaceUnsafeCharacters(screenOverride.PrefabName);
                if (duplicateCount > 0) safeName += $"_{duplicateCount}";
                return $"{FigmaScreenPrefabFolder}/{safeName}.prefab";
            }

            if (s_OnlyImportListedScreens)
                return null;

            return $"{FigmaScreenPrefabFolder}/{GetFileNameForNode(node, duplicateCount)}.prefab";
        }

        public static string GetPathForPagePrefab(Node node,int duplicateCount)
        {
            return $"{FigmaPagePrefabFolder}/{GetFileNameForNode(node,duplicateCount)}.prefab";
        }

        public static string GetPathForComponentPrefab(string setName, string componentName, int duplicateCount)
        {
            if (setName != null)
            {
                var safeSet = ReplaceUnsafeCharacters(setName);
                var folder = $"{FigmaComponentPrefabFolder}/{safeSet}";
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                var safeVariant = NormaliseVariantName(componentName);
                if (duplicateCount > 0) safeVariant += $"_{duplicateCount}";
                return $"{folder}/{safeVariant}.prefab";
            }

            var safeName = ReplaceUnsafeCharacters(componentName);
            if (duplicateCount > 0) safeName += $"_{duplicateCount}";
            return $"{FigmaComponentPrefabFolder}/{safeName}.prefab";
        }

        /// <summary>
        /// Turn a Figma variant property string like "State=Normal, Color=Green"
        /// into "State-Normal_Color-Green", then sanitise for the filesystem.
        /// </summary>
        private static string NormaliseVariantName(string variantName)
        {
            var normalised = variantName.Replace(", ", "_").Replace("=", "-");
            return ReplaceUnsafeCharacters(normalised);
        }

        public static string GetFileNameForNode(Node node,int duplicateCount)
        {
            var safeNodeTitle=ReplaceUnsafeCharacters(node.name);
            if (duplicateCount > 0) safeNodeTitle += $"_{duplicateCount}";
            return safeNodeTitle;
        }

        private static string ReplaceUnsafeCharacters(string inputFilename)
        {
            var safeFilename=inputFilename.Trim();
            return MakeValidFileName(safeFilename);
        }

        public static string MakeValidFileName(string name)
        {
            string invalidChars = System.Text.RegularExpressions.Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            invalidChars += ".";
            string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);
            return System.Text.RegularExpressions.Regex.Replace(name, invalidRegStr, "_");
        }

        public static void CreateRequiredDirectories()
        {
            EnsureDirectory(FigmaPagePrefabFolder);
            CleanFigmaPrefabs(FigmaPagePrefabFolder);

            EnsureDirectory(FigmaScreenPrefabFolder);
            CleanFigmaPrefabs(FigmaScreenPrefabFolder);

            EnsureDirectory(FigmaComponentPrefabFolder);
            EnsureDirectory(FigmaImageFillFolder);
            EnsureDirectory(FigmaServerRenderedImagesFolder);
            EnsureDirectory(FigmaFontMaterialPresetsFolder);
            CleanLegacyFontMaterialPresets(FigmaFontMaterialPresetsFolder);
            EnsureDirectory(FigmaFontsFolder);
        }

        /// <summary>
        /// Earlier versions named font material presets after a running index, so every import that
        /// produced fewer presets than the one before left the higher numbers behind for good. The
        /// presets are named after the effect now, and these are dead.
        /// </summary>
        private static void CleanLegacyFontMaterialPresets(string folder)
        {
            var legacyNamePattern = new Regex(@"_variant_\d+\.mat$");
            var removed = 0;

            foreach (var file in new DirectoryInfo(folder).GetFiles("*_variant_*.mat"))
            {
                if (!legacyNamePattern.IsMatch(file.Name)) continue;
                if (AssetDatabase.DeleteAsset($"{folder}/{file.Name}")) removed++;
            }

            if (removed > 0)
                Debug.Log($"[FigmaPaths] {folder}: removed {removed} legacy font material preset(s)");
        }

        private static void EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

        /// <summary>
        /// Delete only .prefab files whose root GameObject carries FigmaNodeObject.
        /// Anything else is left untouched.
        /// </summary>
        private static void CleanFigmaPrefabs(string folder)
        {
            var files = new DirectoryInfo(folder).GetFiles("*.prefab");
            int removed = 0;
            int kept = 0;

            foreach (var file in files)
            {
                var assetPath = $"{folder}/{file.Name}";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab != null && prefab.GetComponent<FigmaNodeObject>() != null)
                {
                    AssetDatabase.DeleteAsset(assetPath);
                    removed++;
                }
                else
                {
                    kept++;
                }
            }

            if (removed > 0 || kept > 0)
                Debug.Log($"[FigmaPaths] {folder}: removed {removed} Figma prefab(s), kept {kept} other file(s)");
        }
    }
}