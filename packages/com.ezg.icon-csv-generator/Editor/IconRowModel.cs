#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    using System.Collections.Generic;

    /// <summary>
    /// Flat data model for one icon-generation row.
    /// Populated by <see cref="IconCsvLoader"/> and enriched by <see cref="IconFilenameBuilder"/>.
    /// All raw CSV columns are available via <see cref="Fields"/> so prompt templates can bind any
    /// column by name using {token} substitution without coupling the model to specific columns.
    /// </summary>
    internal sealed class IconRowModel
    {
        /// <summary>
        /// Zero-based index into <see cref="IconGeneratorSettings.groups"/> that produced this row.
        /// Used by the window to route generate/write actions to the correct group.
        /// </summary>
        public int GroupIndex { get; set; }

        /// <summary>Display name of the group (equals <see cref="IconGeneratorSettings.groups"/>[GroupIndex].groupName).</summary>
        public string GroupName { get; set; } = string.Empty;

        /// <summary>Row identifier — the CSV id-column value (per-group idColumn setting).</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Convenience getter: ItemType column value (equipment groups use this for display/search).
        /// Returns empty string when the column is absent.
        /// </summary>
        public string Slot => this.Fields.GetValueOrDefault("ItemType", string.Empty);

        /// <summary>
        /// Convenience getter: Rarity column value (for display/search).
        /// Returns empty string when the column is absent.
        /// </summary>
        public string Rarity => this.Fields.GetValueOrDefault("Rarity", string.Empty);

        /// <summary>
        /// All raw CSV columns keyed by header name.
        /// Used by <see cref="IconTokenResolver"/> to bind {field} placeholders in prompt templates.
        /// </summary>
        public Dictionary<string, string> Fields { get; set; } = new();

        /// <summary>Derived output filename; set by <see cref="IconFilenameBuilder.Build"/>.</summary>
        public string OutputFileName { get; set; } = string.Empty;
    }
}
