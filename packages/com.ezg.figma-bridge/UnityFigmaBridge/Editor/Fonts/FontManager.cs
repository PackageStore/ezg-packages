using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Utils;
using Color = UnityEngine.Color;
using MathUtils = UnityFigmaBridge.Editor.Utils.MathUtils;

namespace UnityFigmaBridge.Editor.Fonts
{

    public class FigmaFontMapEntry
    {
        public string FontFamily;
        public int FontWeight;
        public TMP_FontAsset FontAsset;
        public readonly HashSet<uint> RequiredCharacters = new HashSet<uint>();
        public List<FontMaterialVariation> FontmaterialVariations = new List<FontMaterialVariation>();
    }


    /// <summary>
    /// Class to map text effects (outline and shadow) to material presets
    /// </summary>
    public class FontMaterialVariation
    {
        public bool OutlineEnabled;
        public Color OutlineColor;
        public float OutlineThickness;

        public bool ShadowEnabled;
        public Color ShadowColor;

        public Material MaterialPreset;

    }


    public class FigmaFontMap
    {
        public List<FigmaFontMapEntry> FontMapEntries = new List<FigmaFontMapEntry>();

        public FigmaFontMapEntry GetFontMapping(string fontFamily, int fontWeight)
        {
            return FontMapEntries.FirstOrDefault(fontMapEntry => fontMapEntry.FontFamily == fontFamily && fontMapEntry.FontWeight == fontWeight);
        }
    }

    /// <summary>
    /// Functionality to manage fonts, retrive and generate font assets
    /// </summary>
    public static class FontManager
    {
        /// <summary>
        /// Rounding outline width to a step no eye can separate keeps one material preset per
        /// design intent instead of one per floating point difference.
        /// </summary>
        private const float OutlineWidthStep = 0.05f;

        private static readonly Dictionary<int, string> s_WeightStyleNames = new Dictionary<int, string>
        {
            { 100, "Thin" }, { 200, "ExtraLight" }, { 300, "Light" }, { 400, "Regular" },
            { 500, "Medium" }, { 600, "SemiBold" }, { 700, "Bold" }, { 800, "ExtraBold" }, { 900, "Black" }
        };

        /// <summary>
        /// Generates a map of fonts found in the document and font to map to, downloading any font
        /// the project does not already hold and baking every character the document uses.
        /// </summary>
        public static async Task<FigmaFontMap> GenerateFontMapForDocument(FigmaFile figmaFile, bool enableGoogleFontsDownload)
        {
            var fontMap = new FigmaFontMap();
            var textNodes = new List<Node>();
            FigmaDataUtils.FindAllNodesOfType(figmaFile.document,NodeType.TEXT, textNodes, 0);

            foreach (var textNode in textNodes)
            {
                var fontFamily = textNode.style?.fontFamily;
                var fontWeight = textNode.style?.fontWeight ?? 0;
                var fontMapEntry = fontMap.GetFontMapping(fontFamily, fontWeight);
                if (fontMapEntry == null)
                {
                    fontMapEntry = new FigmaFontMapEntry
                    {
                        FontFamily = fontFamily,
                        FontWeight = fontWeight
                    };
                    fontMapEntry.RequiredCharacters.UnionWith(TextMeshProFontUtils.BaseCharacterSet);
                    fontMap.FontMapEntries.Add(fontMapEntry);
                }

                fontMapEntry.RequiredCharacters.UnionWith(TextMeshProFontUtils.ToCodePoints(textNode.characters));
            }

            var allProjectFontAssets = AssetDatabase.FindAssets($"t:TMP_FontAsset").Select(guid => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid))).ToList();

            foreach (var fontMapEntry in fontMap.FontMapEntries)
            {
                fontMapEntry.FontAsset = await ResolveFontAsset(fontMapEntry, allProjectFontAssets, enableGoogleFontsDownload);
                if (fontMapEntry.FontAsset != null) BakeRequiredCharacters(fontMapEntry);
            }

            AssetDatabase.SaveAssets();
            return fontMap;
        }

        /// <summary>
        /// Finds the font asset for one family and weight, preferring what the project already
        /// holds and downloading the real thing when it does not. Never substitutes silently.
        /// </summary>
        private static async Task<TMP_FontAsset> ResolveFontAsset(FigmaFontMapEntry fontMapEntry,
            List<TMP_FontAsset> allProjectFontAssets, bool enableGoogleFontsDownload)
        {
            if (string.IsNullOrEmpty(fontMapEntry.FontFamily))
            {
                Debug.LogError("[FontManager] A text node names no font family. Using the closest font in the project.");
                return GetClosestFont(allProjectFontAssets, string.Empty, fontMapEntry.FontWeight);
            }

            var previouslyDownloaded = GoogleFontLibraryManager.GetFontAsset(fontMapEntry.FontFamily, fontMapEntry.FontWeight);
            if (IsUsable(previouslyDownloaded)) return previouslyDownloaded;

            var projectFont = FindProjectFont(allProjectFontAssets, fontMapEntry.FontFamily, fontMapEntry.FontWeight);
            if (projectFont != null && !IsUsable(projectFont))
            {
                Debug.LogWarning($"[FontManager] '{projectFont.name}' is the right font for " +
                                 $"'{fontMapEntry.FontFamily}' weight {fontMapEntry.FontWeight}, but it is a dynamic " +
                                 "font asset whose source font file is missing, so it can render nothing. " +
                                 "Downloading a fresh copy.");
                projectFont = null;
            }
            if (projectFont != null) return projectFont;

            if (enableGoogleFontsDownload)
            {
                var downloadedFont = await GoogleFontLibraryManager.ImportFont(fontMapEntry.FontFamily, fontMapEntry.FontWeight);
                if (IsUsable(downloadedFont)) return downloadedFont;

                Debug.LogError($"[FontManager] Google Fonts does not publish '{fontMapEntry.FontFamily}'. " +
                               "Add the font file to the project by hand and build a TextMeshPro font asset from it.");
            }
            else
            {
                Debug.LogError($"[FontManager] '{fontMapEntry.FontFamily}' weight {fontMapEntry.FontWeight} is not in " +
                               "the project, and Google Fonts downloads are switched off in the bridge settings.");
            }

            return GetClosestFont(allProjectFontAssets, fontMapEntry.FontFamily, fontMapEntry.FontWeight);
        }

        /// <summary>
        /// A dynamic font asset builds its atlas from the source font file on demand, so without
        /// that file it can only ever render the handful of glyphs already cached in it.
        /// </summary>
        private static bool IsUsable(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null) return false;
            if (fontAsset.atlasPopulationMode == AtlasPopulationMode.Static) return fontAsset.characterTable.Count > 0;
            return fontAsset.sourceFontFile != null;
        }

        private static TMP_FontAsset FindProjectFont(List<TMP_FontAsset> projectFonts, string fontFamily, int fontWeight)
        {
            var family = NormaliseFontName(fontFamily);
            var style = NormaliseFontName(s_WeightStyleNames.TryGetValue(fontWeight, out var styleName) ? styleName : string.Empty);

            return projectFonts.FirstOrDefault(fontAsset =>
                NormaliseFontName(fontAsset.faceInfo.familyName) == family &&
                (style.Length == 0 || NormaliseFontName(fontAsset.faceInfo.styleName) == style));
        }

        private static string NormaliseFontName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            return name.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
        }

        /// <summary>
        /// Adds every character the document uses to the font atlas, and names the ones the font
        /// file itself has no glyph for - the only characters TextMeshPro must fall back for.
        /// </summary>
        private static void BakeRequiredCharacters(FigmaFontMapEntry fontMapEntry)
        {
            var missingUnicodes = TextMeshProFontUtils.AddCharactersToFont(fontMapEntry.FontAsset, fontMapEntry.RequiredCharacters);
            EditorUtility.SetDirty(fontMapEntry.FontAsset);

            if (missingUnicodes.Length == 0) return;

            var missingCharacters = string.Join(" ", missingUnicodes.Select(unicode => char.ConvertFromUtf32((int)unicode)));
            Debug.LogError($"[FontManager] '{fontMapEntry.FontAsset.name}' has no glyph for " +
                           $"{missingUnicodes.Length} character(s) the document uses: {missingCharacters}. " +
                           "TextMeshPro renders these from a fallback font. Choose a family in Figma that covers them.");
        }

        static string StripFontDetailsFromName(TMP_FontAsset fontAsset)
        {
            // By default fonts are added with a hyphen to denote weight variations, so strip everything from hyphen
            var fontName = fontAsset.name.ToLower();
            var hyphenPoint = fontName.IndexOf('-');
            if (hyphenPoint > -1) fontName = fontName.Substring(0, hyphenPoint);
            // Remove any extra keywords
            var stripWords = new string[]
            {
                "sdf",
                "regular",
                "bold",
                "italic",
                " "
            };
            foreach (var stripWord in stripWords)
            {
                fontName= fontName.Replace(stripWord, "");
            }
            return fontName;
        }

        private static TMP_FontAsset GetClosestFont(List<TMP_FontAsset> projectFonts,string fontFamily,int fontWeight)
        {
            var lowestMatchScore = 10000000;
            TMP_FontAsset closestMatch = null;

            // Make lower case and strip spaces
            var inputNameLower = fontFamily.ToLower().Replace(" ", "");;

            // Use Levenshtein distance to calculate best match from available strings
            foreach (var font in projectFonts)
            {
                if (!IsUsable(font)) continue;

                var strippedFontName = StripFontDetailsFromName(font);

                var newScore = MathUtils.LeventshteinStringDistance(inputNameLower, strippedFontName);
                // A name that contains the other is a real match whatever the edit distance says:
                // "fredokasemibold" vs "fredoka" is 8 edits apart but obviously the same family.
                if (inputNameLower.Length > 0 &&
                    (strippedFontName.Contains(inputNameLower) || inputNameLower.Contains(strippedFontName)))
                    newScore = 0;
                if (newScore < lowestMatchScore)
                {
                    closestMatch = font;
                    lowestMatchScore = newScore;
                }
            }

            // There is no threshold on the match, so an absent font silently becomes whatever
            // unrelated asset happens to be closest - Fredoka became "LiberationSans SDF - Fallback"
            // and every generated material preset was named after it. Still return the closest so
            // text renders, but never let it happen quietly.
            if (closestMatch == null)
            {
                Debug.LogError($"[FontManager] Figma asks for '{fontFamily}' weight {fontWeight} " +
                               "and the project contains no usable TMP_FontAsset at all. Import TMP " +
                               "Essential Resources.");
            }
            else
            {
                Debug.LogError($"[FontManager] Substituting '{closestMatch.name}' for '{fontFamily}' " +
                               $"weight {fontWeight}. This is not the font the design uses.");
            }

            return closestMatch;
        }

        public static Material GetEffectMaterialPreset(FigmaFontMapEntry fontMapEntry, bool shadow, Color shadowColor,
            bool outline, Color outlineColor, float outlineThickness)
        {
            // Every value below is written into the material as 8 bit colour or a rounded width, so
            // quantise before matching. Comparing the raw Figma values instead mints a separate
            // preset for differences no shader can express.
            shadowColor = shadow ? Quantise(shadowColor) : Color.clear;
            outlineColor = outline ? Quantise(outlineColor) : Color.clear;
            outlineThickness = outline ? QuantiseOutlineWidth(outlineThickness) : 0f;

            foreach (var materialPreset in fontMapEntry.FontmaterialVariations)
            {
                if (materialPreset.ShadowEnabled != shadow) continue;
                if (materialPreset.OutlineEnabled != outline) continue;
                if (shadow && materialPreset.ShadowColor != shadowColor) continue;
                if (outline && materialPreset.OutlineColor != outlineColor) continue;
                if (outline && !Mathf.Approximately(materialPreset.OutlineThickness, outlineThickness)) continue;

                return materialPreset.MaterialPreset;
            }

            // No match, create new preset. The source font asset's material already carries
            // TextMeshPro's own shader, and we keep it - this package must not ship a copy of one.
            var newMaterialPreset = new Material(fontMapEntry.FontAsset.material);

            // Named after the effect rather than a running index, so re-importing the same document
            // overwrites the same files instead of leaving a renumbered trail behind.
            var materialName = MaterialPresetName(fontMapEntry, shadow, shadowColor, outline, outlineColor, outlineThickness);
            newMaterialPreset.name = materialName;

            TrySetKeyword(newMaterialPreset, "UNDERLAY_ON", shadow);

            if (shadow)
            {
                TrySetFloat(newMaterialPreset, "_UnderlayOffsetX", 0);
                TrySetFloat(newMaterialPreset, "_UnderlayOffsetY", -0.6f);
                TrySetColor(newMaterialPreset, "_UnderlayColor", shadowColor);
            }

            TrySetKeyword(newMaterialPreset, "OUTLINE_ON", outline);

            if (outline)
            {
                TrySetFloat(newMaterialPreset, "_OutlineWidth", outlineThickness);
                TrySetColor(newMaterialPreset, "_OutlineColor", outlineColor);

                // A Figma stroke sits outside the glyph, while TMP centres its outline on the glyph
                // edge and so eats into it. Dilating the face by the full outline width restores
                // the glyph weight the design shows - half of it left the letters too thin.
                TrySetFloat(newMaterialPreset, "_FaceDilate", outlineThickness);
            }

            AssetDatabase.CreateAsset(newMaterialPreset, $"{FigmaPaths.FigmaFontMaterialPresetsFolder}/{materialName}.mat");

            fontMapEntry.FontmaterialVariations.Add(new FontMaterialVariation
            {
                ShadowEnabled=shadow,
                ShadowColor = shadowColor,
                OutlineEnabled = outline,
                OutlineColor = outlineColor,
                OutlineThickness = outlineThickness,
                MaterialPreset = newMaterialPreset
            });
            return newMaterialPreset;
        }

        private static string MaterialPresetName(FigmaFontMapEntry fontMapEntry, bool shadow, Color shadowColor,
            bool outline, Color outlineColor, float outlineThickness)
        {
            var materialName = fontMapEntry.FontAsset.name;
            if (outline) materialName += $"_o{Mathf.RoundToInt(outlineThickness * 100f):D2}-{ToHex(outlineColor)}";
            if (shadow) materialName += $"_s{ToHex(shadowColor)}";
            return materialName;
        }

        private static string ToHex(Color color)
        {
            return ColorUtility.ToHtmlStringRGBA(color).ToLowerInvariant();
        }

        private static Color Quantise(Color color)
        {
            return (Color)(Color32)color;
        }

        private static float QuantiseOutlineWidth(float outlineThickness)
        {
            return Mathf.Round(Mathf.Clamp01(outlineThickness) / OutlineWidthStep) * OutlineWidthStep;
        }

        // A font asset may carry any TMP shader, and Bitmap variants lack the SDF properties and
        // keywords. Setting a keyword that a shader does not declare throws, so every write is guarded.
        private static void TrySetKeyword(Material material, string keyword, bool enabled)
        {
            var localKeyword = material.shader.keywordSpace.FindKeyword(keyword);
            if (!localKeyword.isValid) return;
            material.SetKeyword(localKeyword, enabled);
        }

        private static void TrySetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        private static void TrySetColor(Material material, string property, Color value)
        {
            if (material.HasProperty(property)) material.SetColor(property, value);
        }
    }
}
