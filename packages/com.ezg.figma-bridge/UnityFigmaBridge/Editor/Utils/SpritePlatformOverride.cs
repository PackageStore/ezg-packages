using System.IO;
using UnityEditor;
using UnityEngine;
using UnityFigmaBridge.Editor.Settings;

namespace UnityFigmaBridge.Editor.Utils
{
    /// <summary>
    /// Android and iOS override tabs (format and compression quality from the settings asset) for
    /// every texture the bridge writes.
    /// </summary>
    public static class SpritePlatformOverride
    {
        private static readonly string[] Platforms = { "Android", "iPhone" };

        /// <returns>True when the importer changed and needs a reimport.</returns>
        public static bool Apply(TextureImporter importer, UnityFigmaBridgeSettings settings)
        {
            if (settings == null || !settings.OverrideMobileFormat) return false;
            var quality = (int)settings.MobileCompressionQuality;
            var changed = false;
            foreach (var platform in Platforms)
            {
                var platformSettings = importer.GetPlatformTextureSettings(platform);
                if (platformSettings.overridden &&
                    platformSettings.format == settings.MobileTextureFormat &&
                    platformSettings.compressionQuality == quality) continue;

                // Max size is copied only when the tab is first turned on, so a size set by hand stays.
                if (!platformSettings.overridden) platformSettings.maxTextureSize = importer.maxTextureSize;
                platformSettings.overridden = true;
                platformSettings.format = settings.MobileTextureFormat;
                platformSettings.compressionQuality = quality;
                importer.SetPlatformTextureSettings(platformSettings);
                changed = true;
            }
            return changed;
        }

        /// <summary>Applies the override to every PNG under the folders, reimporting only the ones that change.</summary>
        public static void ApplyToFolders(UnityFigmaBridgeSettings settings, params string[] folders)
        {
            if (settings == null || !settings.OverrideMobileFormat) return;
            var changed = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var folder in folders)
                {
                    if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
                    foreach (var file in Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories))
                    {
                        if (AssetImporter.GetAtPath(file.Replace('\\', '/')) is not TextureImporter importer) continue;
                        if (!Apply(importer, settings)) continue;
                        importer.SaveAndReimport();
                        changed++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            if (changed > 0)
                Debug.Log($"[SpritePlatformOverride] {changed} texture(s) set to {settings.MobileTextureFormat} " +
                          $"({settings.MobileCompressionQuality}) for Android and iOS");
        }
    }
}
