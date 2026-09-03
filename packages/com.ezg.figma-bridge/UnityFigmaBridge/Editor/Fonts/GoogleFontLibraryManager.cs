using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TextCore.LowLevel;
using UnityFigmaBridge.Editor.Utils;

namespace UnityFigmaBridge.Editor.Fonts
{
    /// <summary>
    /// Downloads the exact font a Figma document asks for from Google Fonts and turns it into a
    /// TextMeshPro font asset.
    /// </summary>
    public static class GoogleFontLibraryManager
    {
        // The css2 endpoint picks a file format from the user agent, and serves woff2 to anything
        // that looks like a modern browser. TextMeshPro reads TrueType only, so we ask as an agent
        // with no woff2 support and get a .ttf back.
        private const string TrueTypeUserAgent = "Mozilla/5.0";

        private static readonly Regex s_TrueTypeUrlPattern =
            new Regex(@"url\((?<url>https://[^)]+?\.ttf)\)", RegexOptions.IgnoreCase);

        public static string PathToTtfFont(string fontFamily, int fontWeight)
        {
            return $"{FigmaPaths.FigmaFontsFolder}/{CombinedFontName(fontFamily, fontWeight)}.ttf";
        }

        public static string PathToTmpFont(string fontFamily, int fontWeight)
        {
            return $"{FigmaPaths.FigmaFontsFolder}/{CombinedFontName(fontFamily, fontWeight)}_SDF.asset";
        }

        public static string CombinedFontName(string fontFamily, int fontWeight)
        {
            return $"{fontFamily}_{fontWeight}";
        }

        public static TMP_FontAsset GetFontAsset(string fontFamily, int fontWeight)
        {
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PathToTmpFont(fontFamily, fontWeight));
        }

        /// <summary>
        /// Downloads a font family at a given weight and builds a TextMeshPro font asset from it.
        /// Returns null if Google Fonts does not publish that family.
        /// </summary>
        public static async Task<TMP_FontAsset> ImportFont(string fontFamily, int fontWeight)
        {
            var downloadUrl = await ResolveTrueTypeUrl(fontFamily, fontWeight);
            if (string.IsNullOrEmpty(downloadUrl)) return null;

            var fontData = await Download(downloadUrl);
            if (fontData == null) return null;

            var ttfPath = PathToTtfFont(fontFamily, fontWeight);
            File.WriteAllBytes(ttfPath, fontData);
            AssetDatabase.ImportAsset(ttfPath, ImportAssetOptions.ForceUpdate);

            var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (sourceFont == null)
            {
                Debug.LogError($"[FontManager] Downloaded '{fontFamily}' weight {fontWeight} but Unity " +
                               $"could not import it as a font at '{ttfPath}'.");
                return null;
            }

            var tmpFontAsset = TMP_FontAsset.CreateFontAsset(sourceFont, 90, 9, GlyphRenderMode.SDFAA,
                1024, 1024, AtlasPopulationMode.Dynamic);
            if (tmpFontAsset == null)
            {
                Debug.LogError($"[FontManager] Could not build a TextMeshPro font asset from '{ttfPath}'.");
                return null;
            }

            var tmpPath = PathToTmpFont(fontFamily, fontWeight);
            AssetDatabase.CreateAsset(tmpFontAsset, tmpPath);

            tmpFontAsset.material.name = $"{CombinedFontName(fontFamily, fontWeight)} Atlas Material";
            tmpFontAsset.atlasTexture.name = $"{CombinedFontName(fontFamily, fontWeight)} Atlas";
            AssetDatabase.AddObjectToAsset(tmpFontAsset.material, tmpFontAsset);
            AssetDatabase.AddObjectToAsset(tmpFontAsset.atlasTexture, tmpFontAsset);

            EditorUtility.SetDirty(tmpFontAsset);
            AssetDatabase.SaveAssets();

            Debug.Log($"[FontManager] Downloaded '{fontFamily}' weight {fontWeight} to '{tmpPath}'");
            return tmpFontAsset;
        }

        /// <summary>
        /// Asks the Google Fonts stylesheet API for the file url of one family at one weight. Falls
        /// back to the family's default weight, because css2 rejects a weight the family lacks.
        /// </summary>
        private static async Task<string> ResolveTrueTypeUrl(string fontFamily, int fontWeight)
        {
            var escapedFamily = UnityWebRequest.EscapeURL(fontFamily);
            var url = await FindTrueTypeUrl($"https://fonts.googleapis.com/css2?family={escapedFamily}:wght@{fontWeight}");
            if (!string.IsNullOrEmpty(url)) return url;

            url = await FindTrueTypeUrl($"https://fonts.googleapis.com/css2?family={escapedFamily}");
            if (!string.IsNullOrEmpty(url))
            {
                Debug.LogWarning($"[FontManager] Google Fonts has no weight {fontWeight} for '{fontFamily}'. " +
                                 "Using the family's default weight instead.");
            }
            return url;
        }

        private static async Task<string> FindTrueTypeUrl(string stylesheetUrl)
        {
            using var webRequest = UnityWebRequest.Get(stylesheetUrl);
            webRequest.SetRequestHeader("User-Agent", TrueTypeUserAgent);
            await webRequest.SendWebRequest();

            if (webRequest.result != UnityWebRequest.Result.Success) return string.Empty;

            var match = s_TrueTypeUrlPattern.Match(webRequest.downloadHandler.text);
            return match.Success ? match.Groups["url"].Value : string.Empty;
        }

        private static async Task<byte[]> Download(string url)
        {
            using var webRequest = UnityWebRequest.Get(url);
            await webRequest.SendWebRequest();

            if (webRequest.result == UnityWebRequest.Result.Success) return webRequest.downloadHandler.data;

            Debug.LogError($"[FontManager] Error downloading font file '{url}': {webRequest.error}");
            return null;
        }
    }
}
