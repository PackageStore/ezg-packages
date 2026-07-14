#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    using System;
    using UnityEngine;

    /// <summary>
    /// Filter mode used by <see cref="IconCsvGroup"/> to select or exclude rows
    /// based on the value of a specific CSV column.
    /// </summary>
    internal enum IconRowFilterMode
    {
        /// <summary>No filter — every row in the CSV is included.</summary>
        None = 0,

        /// <summary>Include only rows where the filter column equals <see cref="IconCsvGroup.filterValue"/>.</summary>
        Equals = 1,

        /// <summary>Include only rows where the filter column does NOT equal <see cref="IconCsvGroup.filterValue"/>.</summary>
        NotEquals = 2,
    }

    /// <summary>
    /// User-nameable group of icons driven by a single CSV file.
    /// Stored as a list on <see cref="IconGeneratorSettings"/>; serialized directly into the
    /// settings asset so no separate per-category ScriptableObjects are needed.
    ///
    /// All serialized field names in this class are the authoritative YAML keys for Phase 3.
    /// </summary>
    [Serializable]
    internal sealed class IconCsvGroup
    {
        // ── Identity ───────────────────────────────────────────────────────────────

        /// <summary>Display name shown in the window and used as the _Incoming subfolder name.</summary>
        public string groupName = string.Empty;

        // ── CSV source ─────────────────────────────────────────────────────────────

        /// <summary>Project-relative path to the source CSV (e.g. Assets/Data/Foo.csv).</summary>
        public string csvPath = string.Empty;

        /// <summary>
        /// Name of the CSV column used as the row's unique identifier.
        /// Rows with an empty/whitespace value in this column are skipped.
        /// Default "Id" matches the equipment and currency blueprints; Realms uses "RealmId".
        /// </summary>
        public string idColumn = "Id";

        // ── Row filter ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Column name to filter on (case-sensitive header match).
        /// Ignored when <see cref="filterMode"/> is <see cref="IconRowFilterMode.None"/>.
        /// </summary>
        public string filterColumn = string.Empty;

        /// <summary>Filter mode: None passes all rows; Equals/NotEquals checks filterColumn vs filterValue.</summary>
        public IconRowFilterMode filterMode = IconRowFilterMode.None;

        /// <summary>
        /// Value compared against the filter column (case-sensitive).
        /// Ignored when <see cref="filterMode"/> is <see cref="IconRowFilterMode.None"/>.
        /// </summary>
        public string filterValue = string.Empty;

        // ── Output ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Filename pattern using {ColumnName} tokens resolved against the row's CSV fields.
        /// ".psd" is appended automatically. Token values are sanitized for filenames.
        /// Example: "S_Icon_{ItemType}_{Id}_{Rarity}".
        /// </summary>
        public string filenamePattern = string.Empty;

        // ── Prompt ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Gemini generation prompt template. Uses {ColumnName} tokens resolved against raw CSV values.
        /// All prompts must end with the solid-pure-white-background clause (design mandate #11).
        /// </summary>
        [TextArea(4, 12)]
        public string promptTemplate = string.Empty;

        // ── Reference images ───────────────────────────────────────────────────────

        /// <summary>
        /// Optional reference images sent alongside every Gemini request for this group.
        /// Supports PNG, JPG, and WEBP assets. Read directly from disk — no Read/Write import flag needed.
        /// </summary>
        [Tooltip(
            "Optional reference images attached to every Gemini request for this group. " +
            "Supports PNG, JPG, and WEBP assets. Read directly from disk — no Read/Write import flag needed.")]
        public Texture2D[] referenceImages = Array.Empty<Texture2D>();
    }
}
