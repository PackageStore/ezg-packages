#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Builds <see cref="GUIContent"/> for IMGUI buttons as "[icon] text", using lucide icons
    /// that were rasterized to PNG under <see cref="IconsFolder"/>.
    ///
    /// Fail-soft: if an icon asset is missing (e.g. not imported yet), the button falls back to
    /// text-only — the UI never breaks and the tool still compiles/runs without the PNGs.
    ///
    /// Icons are lucide (https://lucide.dev), ISC license; file names match lucide icon names
    /// (e.g. "sparkles", "refresh-cw"). To add one: drop <c>{name}.png</c> into the ButtonIcons
    /// folder — <see cref="ButtonIconPostprocessor"/> applies crisp GUI import settings automatically.
    /// </summary>
    internal static class LucideButtonIcons
    {
        /// <summary>Project-relative folder holding the rasterized lucide PNGs.</summary>
        internal const string IconsFolder =
            "Packages/com.ezg.icon-csv-generator/Editor/ButtonIcons";

        // Cache keyed by icon name. Only HITS are cached, so a not-yet-imported icon is
        // re-attempted until it appears (then cached). Stale (destroyed) refs fall back to reload.
        private static readonly Dictionary<string, Texture2D> Cache = new();

        /// <summary>
        /// GUIContent showing the lucide <paramref name="iconName"/> followed by <paramref name="text"/>.
        /// Falls back to text-only if the icon asset is missing.
        /// </summary>
        public static GUIContent Content(string iconName, string text, string? tooltip = null)
        {
            var tex = Load(iconName);
            return tex != null
                ? new GUIContent(" " + text, tex, tooltip ?? string.Empty)
                : new GUIContent(text, tooltip ?? string.Empty);
        }

        private static Texture2D? Load(string iconName)
        {
            if (Cache.TryGetValue(iconName, out var cached) && cached != null)
                return cached;

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{IconsFolder}/{iconName}.png");
            if (tex != null) Cache[iconName] = tex; // cache hits only; misses retried next call
            return tex;
        }
    }
}
