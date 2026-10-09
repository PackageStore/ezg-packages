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

        /// <summary>Largest <see cref="TextEffect.Reach"/> per pixel of font size among the texts using this font.</summary>
        public float EffectReachPerEm;
    }


    /// <summary>
    /// Class to map text effects (outline and shadow) to material presets
    /// </summary>
    public class FontMaterialVariation
    {
        /// <summary>Preset name: every value the material holds, in design units.</summary>
        public string Name;

        public Material MaterialPreset;
    }


    public class FigmaFontMap
    {
        public List<FigmaFontMapEntry> FontMapEntries = new List<FigmaFontMapEntry>();

        /// <summary>
        /// Set when the settings name a FontOverride: one entry answers every lookup, so every
        /// text node and every material preset is built on that single font asset.
        /// </summary>
        public FigmaFontMapEntry OverrideEntry;

        public FigmaFontMapEntry GetFontMapping(string fontFamily, int fontWeight)
        {
            if (OverrideEntry != null) return OverrideEntry;
            return FontMapEntries.FirstOrDefault(fontMapEntry => fontMapEntry.FontFamily == fontFamily && fontMapEntry.FontWeight == fontWeight);
        }
    }

    /// <summary>
    /// Functionality to manage fonts, retrive and generate font assets
    /// </summary>
    public static class FontManager
    {
        private static readonly Dictionary<int, string> s_WeightStyleNames = new Dictionary<int, string>
        {
            { 100, "Thin" }, { 200, "ExtraLight" }, { 300, "Light" }, { 400, "Regular" },
            { 500, "Medium" }, { 600, "SemiBold" }, { 700, "Bold" }, { 800, "ExtraBold" }, { 900, "Black" }
        };

        /// <summary>
        /// Generates a map of fonts found in the document and font to map to, downloading any font
        /// the project does not already hold and baking every character the document uses.
        /// </summary>
        /// <param name="fontOverride">
        ///     When set, every text in the document uses this asset: no project search, no Google
        ///     Fonts download. The characters the document uses are still baked into it.
        /// </param>
        public static async Task<FigmaFontMap> GenerateFontMapForDocument(FigmaFile figmaFile, bool enableGoogleFontsDownload,
            TMP_FontAsset fontOverride = null)
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

                var effect = TextEffect.FromNode(textNode);
                if (effect.Any && effect.FontSize > 0f)
                    fontMapEntry.EffectReachPerEm = Mathf.Max(fontMapEntry.EffectReachPerEm, effect.Reach / effect.FontSize);
            }

            if (fontOverride != null)
            {
                var overrideEntry = new FigmaFontMapEntry
                {
                    FontFamily = fontOverride.name,
                    FontWeight = 0,
                    FontAsset = fontOverride
                };
                overrideEntry.RequiredCharacters.UnionWith(TextMeshProFontUtils.BaseCharacterSet);
                foreach (var entry in fontMap.FontMapEntries)
                {
                    overrideEntry.RequiredCharacters.UnionWith(entry.RequiredCharacters);
                    overrideEntry.EffectReachPerEm = Mathf.Max(overrideEntry.EffectReachPerEm, entry.EffectReachPerEm);
                }
                fontMap.OverrideEntry = overrideEntry;
                EnsureEffectPadding(overrideEntry);
                BakeRequiredCharacters(overrideEntry);
                AssetDatabase.SaveAssets();
                Debug.Log($"[FontManager] FontOverride '{fontOverride.name}' replaces " +
                          $"{fontMap.FontMapEntries.Count} Figma font(s): " +
                          string.Join(", ", fontMap.FontMapEntries.Select(e => $"{e.FontFamily} {e.FontWeight}")));
                return fontMap;
            }

            var allProjectFontAssets = AssetDatabase.FindAssets($"t:TMP_FontAsset").Select(guid => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid))).ToList();

            foreach (var fontMapEntry in fontMap.FontMapEntries)
            {
                fontMapEntry.FontAsset = await ResolveFontAsset(fontMapEntry, allProjectFontAssets, enableGoogleFontsDownload);
                if (fontMapEntry.FontAsset == null) continue;
                EnsureEffectPadding(fontMapEntry);
                BakeRequiredCharacters(fontMapEntry);
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
            Debug.LogError($"[FontManager] '{fontMapEntry.FontAsset.name}' could not bake " +
                           $"{missingUnicodes.Length} character(s) the document uses: {missingCharacters}. " +
                           "The font file has no glyph for them (or the atlas is full and multi-atlas is off). " +
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

        /// <summary>
        ///     A material preset that draws a Figma stroke and drop shadow at their design size. TMP's
        ///     distance-field units scale with the font size and the atlas, measured on its SDF shaders:
        ///     face dilate moves the glyph edge by G·k pixels per unit, outline width spreads G·k on each
        ///     side of that edge, and underlay offset and dilate move by G·k, where G is the gradient
        ///     scale (atlas padding + 1) and k is
        ///     font size / sampling point size. Ratio scaling is off, so those values reach the shader
        ///     as written; <see cref="EnsureEffectPadding"/> keeps them inside the atlas padding.
        /// </summary>
        public static Material GetEffectMaterialPreset(FigmaFontMapEntry fontMapEntry, TextEffect effect)
        {
            var fontAsset = fontMapEntry.FontAsset;
            // Every colour is written as 8 bit and every size is named to 1/100 px, so quantise first:
            // raw Figma floats would mint a preset per difference no shader can show
            effect.OutlineColor = Quantise(effect.OutlineColor);
            effect.ShadowColor = Quantise(effect.ShadowColor);
            var materialName = MaterialPresetName(fontAsset, effect);

            foreach (var materialPreset in fontMapEntry.FontmaterialVariations)
                if (materialPreset.Name == materialName) return materialPreset.MaterialPreset;

            // The source font asset's material already carries TextMeshPro's own shader, and we keep
            // it - this package must not ship a copy of one
            var newMaterialPreset = new Material(fontAsset.material) { name = materialName };

            var gradientScale = newMaterialPreset.HasProperty("_GradientScale")
                ? newMaterialPreset.GetFloat("_GradientScale") : fontAsset.atlasPadding + 1;
            var pixelsPerUnit = gradientScale * effect.FontSize / fontAsset.faceInfo.pointSize;

            newMaterialPreset.EnableKeyword("RATIOS_OFF");
            TrySetFloat(newMaterialPreset, "_ScaleRatioA", 1f);
            TrySetFloat(newMaterialPreset, "_ScaleRatioB", 1f);
            TrySetFloat(newMaterialPreset, "_ScaleRatioC", 1f);

            var faceDilate = 0f;
            TrySetKeyword(newMaterialPreset, "OUTLINE_ON", effect.Outline);
            if (effect.Outline)
            {
                var outlineWidth = effect.StrokeWidth / (2f * pixelsPerUnit);
                faceDilate = effect.StrokeAlign switch
                {
                    Node.StrokeAlign.OUTSIDE => outlineWidth,
                    Node.StrokeAlign.INSIDE => -outlineWidth,
                    _ => 0f
                };
                TrySetFloat(newMaterialPreset, "_OutlineWidth", outlineWidth);
                TrySetColor(newMaterialPreset, "_OutlineColor", effect.OutlineColor);
            }
            TrySetFloat(newMaterialPreset, "_FaceDilate", faceDilate);

            TrySetKeyword(newMaterialPreset, "UNDERLAY_ON", effect.Shadow);
            if (effect.Shadow)
            {
                // Figma shadows the stroked shape; the underlay starts from the dilated face
                TrySetFloat(newMaterialPreset, "_UnderlayOffsetX", effect.ShadowOffset.x / pixelsPerUnit);
                TrySetFloat(newMaterialPreset, "_UnderlayOffsetY", -effect.ShadowOffset.y / pixelsPerUnit);
                TrySetFloat(newMaterialPreset, "_UnderlayDilate",
                    (effect.StrokeOuterEdge + effect.ShadowSpread) / pixelsPerUnit - faceDilate);
                TrySetFloat(newMaterialPreset, "_UnderlaySoftness", effect.ShadowRadius / (2f * pixelsPerUnit));
                TrySetColor(newMaterialPreset, "_UnderlayColor", effect.ShadowColor);
            }

            AssetDatabase.CreateAsset(newMaterialPreset, $"{FigmaPaths.FigmaFontMaterialPresetsFolder}/{materialName}.mat");

            fontMapEntry.FontmaterialVariations.Add(new FontMaterialVariation
            {
                Name = materialName,
                MaterialPreset = newMaterialPreset
            });
            return newMaterialPreset;
        }

        /// <summary>
        ///     Named after the effect in design pixels rather than a running index, so re-importing the
        ///     same document overwrites the same files instead of leaving a renumbered trail behind.
        /// </summary>
        private static string MaterialPresetName(TMP_FontAsset fontAsset, TextEffect effect)
        {
            var materialName = fontAsset.name;
            if (effect.Outline)
                materialName += $"_o{Px(effect.StrokeWidth)}{effect.StrokeAlign.ToString()[0]}-{ToHex(effect.OutlineColor)}";
            if (effect.Shadow)
                materialName += $"_s{ToHex(effect.ShadowColor)}-{Px(effect.ShadowOffset.x)}x{Px(effect.ShadowOffset.y)}" +
                                $"r{Px(effect.ShadowRadius)}s{Px(effect.ShadowSpread)}";
            return materialName + $"@{Px(effect.FontSize)}";
        }

        private static string Px(float value) =>
            (Mathf.Round(value * 100f) / 100f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        ///     A distance field only holds distances up to its atlas padding. An effect reaching further
        ///     reads past the glyph's cell, so the outline is cut and the shadow picks up the neighbouring
        ///     glyph. A dynamic font asset the bridge made gets the padding its texts need (its atlas is
        ///     rebuilt); any other font asset is left as it is, with a warning.
        /// </summary>
        private static void EnsureEffectPadding(FigmaFontMapEntry fontMapEntry)
        {
            var fontAsset = fontMapEntry.FontAsset;
            if (fontAsset == null || fontMapEntry.EffectReachPerEm <= 0f) return;
            var required = Mathf.CeilToInt(fontMapEntry.EffectReachPerEm * fontAsset.faceInfo.pointSize) + 1;
            if (fontAsset.atlasPadding >= required) return;

            var path = AssetDatabase.GetAssetPath(fontAsset);
            var madeByBridge = path.StartsWith(FigmaPaths.FigmaFontsFolder + "/", System.StringComparison.Ordinal);
            if (!madeByBridge || fontAsset.atlasPopulationMode != AtlasPopulationMode.Dynamic)
            {
                Debug.LogWarning($"[FontManager] '{fontAsset.name}' has atlas padding {fontAsset.atlasPadding}, but its " +
                                 $"text strokes and shadows reach {required - 1} texels: they will be cut. Rebuild the " +
                                 $"font asset with padding {required} or more.");
                return;
            }

            var serializedFont = new SerializedObject(fontAsset);
            serializedFont.FindProperty("m_AtlasPadding").intValue = required;
            serializedFont.ApplyModifiedPropertiesWithoutUndo();
            fontAsset.ClearFontAssetData(true);
            fontAsset.material.SetFloat("_GradientScale", required + 1);
            EditorUtility.SetDirty(fontAsset);
            Debug.Log($"[FontManager] '{fontAsset.name}': atlas padding raised to {required} for its text strokes and shadows");
        }

        private static string ToHex(Color color)
        {
            return ColorUtility.ToHtmlStringRGBA(color).ToLowerInvariant();
        }

        private static Color Quantise(Color color)
        {
            return (Color)(Color32)color;
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
