using System.Collections.Generic;
using System.Linq;
using TMPro;

namespace UnityFigmaBridge.Editor.Fonts
{
    public static class TextMeshProFontUtils
    {
        /// <summary>
        /// Printable ASCII, plus the characters TextMeshPro itself reaches for: no-break space,
        /// zero width space and ellipsis.
        /// </summary>
        public static IEnumerable<uint> BaseCharacterSet
        {
            get
            {
                for (uint unicode = 32; unicode <= 126; unicode++) yield return unicode;
                yield return 160;
                yield return 8203;
                yield return 8230;
            }
        }

        /// <summary>
        /// Adds every requested character to the font atlas. Returns the characters that are still
        /// not in the font after that - no glyph in the font file, or no room left in a single-atlas
        /// font - which is the only case TextMeshPro must fall back for.
        /// </summary>
        public static uint[] AddCharactersToFont(TMP_FontAsset tmpFontAsset, IEnumerable<uint> unicodes)
        {
            var wanted = unicodes.Distinct().ToArray();
            if (wanted.Length == 0) return System.Array.Empty<uint>();

            if (tmpFontAsset.atlasPopulationMode == AtlasPopulationMode.Static)
                return wanted.Where(unicode => !tmpFontAsset.HasCharacter((int)unicode)).ToArray();

            // Only ask for what the table does not hold yet. TMP_FontAsset.TryAddCharacters answers
            // `false` with the WHOLE request echoed back as "missing" whenever it had nothing to add
            // (every character already baked), so its out-parameter cannot be trusted on a re-import.
            // Control characters and TMP's synthesized ones (tab, line feed, zero width space...)
            // are already in the table after ReadFontAssetDefinition and never need a glyph.
            var toAdd = wanted.Where(unicode => !tmpFontAsset.HasCharacter((int)unicode)).ToArray();
            if (toAdd.Length == 0) return System.Array.Empty<uint>();

            tmpFontAsset.TryAddCharacters(toAdd, out _);

            // The character table is the source of truth: whatever TryAddCharacters could bake is in
            // it now, whatever it could not (no glyph, atlas full) is not.
            return toAdd.Where(unicode => !tmpFontAsset.HasCharacter((int)unicode)).ToArray();
        }

        /// <summary>
        /// Expands a string into unicode code points, keeping surrogate pairs together.
        /// </summary>
        public static IEnumerable<uint> ToCodePoints(string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;

            for (var i = 0; i < text.Length; i++)
            {
                if (i < text.Length - 1 && char.IsHighSurrogate(text[i]) && char.IsLowSurrogate(text[i + 1]))
                {
                    yield return (uint)char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                    continue;
                }
                yield return text[i];
            }
        }
    }
}
