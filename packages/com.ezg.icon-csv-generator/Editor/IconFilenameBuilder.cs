#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Derives the output PSD filename for each icon row using the group's configurable
    /// <see cref="IconCsvGroup.filenamePattern"/> and the row's CSV field values.
    ///
    /// Token values are sanitized for filenames (whitespace stripped, invalid filename
    /// chars removed) via <see cref="IconTokenResolver.ResolveSanitized"/>. The prompt
    /// path always uses raw values via <see cref="IconTokenResolver.ResolveRaw"/>.
    ///
    /// Missing token → throws (no silent S_Unknown_ fallback — development-principles.md).
    /// Pure static — unit-testable without a Unity environment.
    /// </summary>
    internal static class IconFilenameBuilder
    {
        private const string PSD_EXTENSION = ".psd";

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Derives <see cref="IconRowModel.OutputFileName"/> from the row's Fields and the
        /// group's filenamePattern and sets it in-place on the row.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">
        /// When a {token} in <see cref="IconCsvGroup.filenamePattern"/> has no matching CSV column.
        /// </exception>
        public static void Build(IconRowModel row, IconCsvGroup group)
        {
            row.OutputFileName = BuildFromPattern(group.filenamePattern, row.Fields, row.Id);
        }

        /// <summary>
        /// Pure-function variant for unit testing: resolves a pattern against explicit fields
        /// without mutating any model.
        /// </summary>
        /// <param name="pattern">Filename pattern, e.g. "S_Icon_{ItemType}_{Id}_{Rarity}".</param>
        /// <param name="fields">CSV field dictionary supplying token values.</param>
        /// <param name="rowId">Row identifier used in error messages.</param>
        /// <returns>The resolved filename with ".psd" appended (idempotent — not doubled if the pattern already ends in .psd).</returns>
        /// <exception cref="System.ArgumentException">When <paramref name="pattern"/> is null/empty/whitespace.</exception>
        /// <exception cref="System.InvalidOperationException">
        /// When a {token} in <paramref name="pattern"/> has no matching key in <paramref name="fields"/>.
        /// </exception>
        public static string BuildFromPattern(
            string pattern,
            IReadOnlyDictionary<string, string> fields,
            string rowId = "")
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                throw new ArgumentException(
                    "Filename pattern is empty. Set a non-empty filenamePattern on the group.", nameof(pattern));
            }

            var stem = IconTokenResolver.ResolveSanitized(pattern, fields, rowId);
            return stem.EndsWith(PSD_EXTENSION, StringComparison.OrdinalIgnoreCase)
                ? stem
                : stem + PSD_EXTENSION;
        }
    }
}
