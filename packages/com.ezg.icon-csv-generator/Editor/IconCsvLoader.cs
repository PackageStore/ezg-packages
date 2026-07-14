#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEngine;

    /// <summary>
    /// Parses the CSV files configured in each <see cref="IconCsvGroup"/> and returns a
    /// combined list of <see cref="IconRowModel"/> instances.
    ///
    /// Each group may point to the same or a different CSV. The per-row filter predicate
    /// (None / Equals / NotEquals on a named column) selects the subset of rows that belong
    /// to the group. Duplicate-Id guard is scoped per group (same Id in two groups is legal).
    ///
    /// No silent fallbacks (development-principles.md): missing file → FileNotFoundException;
    /// empty csvPath → ArgumentException.
    /// </summary>
    internal static class IconCsvLoader
    {
        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Loads all rows from every group's CSV and returns them as a combined list.
        /// Groups are processed in index order; GroupIndex and GroupName are set on each row.
        /// Rows with an empty/whitespace id-column value are silently skipped.
        ///
        /// Per-group fault isolation: if one group fails to load (missing CSV, empty path,
        /// malformed data) the failure is logged via <see cref="Debug.LogError"/> and that group
        /// is skipped (contributes 0 rows) so every other correctly-configured group still loads.
        /// LoadAll itself does not throw for a single group's misconfiguration.
        /// </summary>
        public static List<IconRowModel> LoadAll(IReadOnlyList<IconCsvGroup> groups)
        {
            var result = new List<IconRowModel>();
            for (var i = 0; i < groups.Count; i++)
            {
                try
                {
                    result.AddRange(LoadGroup(groups[i], i));
                }
                catch (Exception ex)
                {
                    // A single group's misconfiguration must NOT abort loading of the other groups.
                    // Logged (never silent) and skipped — the group shows 0 rows in the window.
                    Debug.LogError(
                        $"[IconGenerator] Group '{groups[i].groupName}' failed to load and was skipped: {ex.Message}");
                }
            }
            return result;
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private static List<IconRowModel> LoadGroup(IconCsvGroup group, int groupIndex)
        {
            if (string.IsNullOrWhiteSpace(group.csvPath))
            {
                throw new ArgumentException(
                    $"[IconGenerator] Group '{group.groupName}' has an empty csvPath. " +
                    "Assign a CSV in the Icon Generator window's group config.", nameof(group));
            }

            var absolutePath = GetAbsolutePath(group.csvPath);

            if (!File.Exists(absolutePath))
            {
                throw new FileNotFoundException(
                    $"[IconGenerator] CSV not found for group '{group.groupName}': {absolutePath}");
            }

            var rows  = new List<IconRowModel>();
            var lines = File.ReadAllLines(absolutePath);

            if (lines.Length < 2) return rows; // header-only or empty

            var header = SplitCsvLine(lines[0]);
            var seen   = new HashSet<string>(StringComparer.Ordinal); // per-group duplicate guard

            for (var lineIdx = 1; lineIdx < lines.Length; lineIdx++)
            {
                var line = lines[lineIdx];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var cols = SplitCsvLine(line);
                if (cols.Length != header.Length)
                {
                    Debug.LogWarning(
                        $"[IconGenerator] Column count mismatch on line {lineIdx + 1} of '{absolutePath}' " +
                        $"(group '{group.groupName}'). Expected {header.Length}, got {cols.Length}. Skipping.");
                    continue;
                }

                var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var i = 0; i < header.Length; i++)
                {
                    fields[header[i]] = cols[i].Trim();
                }

                // Id filter: skip rows with empty/whitespace id-column value.
                if (!fields.TryGetValue(group.idColumn, out var id) || string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                // Row filter predicate — applied BEFORE the duplicate guard so a filtered-out
                // row never consumes an id slot belonging to an included row (matters when two
                // groups share a CSV, or a group filters one CSV into a subset).
                if (!PassesFilter(fields, group)) continue;

                // Per-group duplicate-Id guard.
                if (!seen.Add(id))
                {
                    Debug.LogWarning(
                        $"[IconGenerator] Duplicate id '{id}' in group '{group.groupName}' " +
                        $"(column '{group.idColumn}' in '{absolutePath}'). Skipping.");
                    continue;
                }

                var row = new IconRowModel
                {
                    GroupIndex = groupIndex,
                    GroupName  = group.groupName,
                    Id         = id,
                    Fields     = fields,
                };

                IconFilenameBuilder.Build(row, group);
                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// Returns true when the row's field values satisfy the group's filter predicate.
        /// None → always true. Equals/NotEquals → case-sensitive comparison.
        /// Missing filter column → treated as an empty value (Equals excludes the row, NotEquals includes it),
        /// keeping Equals/NotEquals groups a complementary partition even for rows lacking the column.
        /// </summary>
        internal static bool PassesFilter(
            IReadOnlyDictionary<string, string> fields,
            IconCsvGroup group)
        {
            if (group.filterMode == IconRowFilterMode.None) return true;

            if (string.IsNullOrWhiteSpace(group.filterColumn))
            {
                // Filter mode set but no column specified — treat as None (pass all).
                Debug.LogWarning(
                    $"[IconGenerator] Group '{group.groupName}' has filterMode={group.filterMode} " +
                    "but filterColumn is empty. Treating as None (passing all rows).");
                return true;
            }

            // A row missing the filter column is treated as an empty value so the predicate stays
            // total: Equals(missing, value) = false (excluded); NotEquals(missing, value) = true
            // (included). This keeps the Equals/NotEquals groups a complementary partition even for
            // rows that lack the column — no gap, no overlap.
            fields.TryGetValue(group.filterColumn, out var columnValue);
            columnValue ??= string.Empty;

            var match = string.Equals(columnValue, group.filterValue, StringComparison.Ordinal);
            return group.filterMode switch
            {
                IconRowFilterMode.Equals    =>  match,
                IconRowFilterMode.NotEquals => !match,
                _                           => true,
            };
        }

        private static string GetAbsolutePath(string assetsRelativePath)
        {
            var projectRoot = Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length);
            return Path.Combine(projectRoot, assetsRelativePath).Replace('\\', '/');
        }

        /// <summary>
        /// RFC-4180-style CSV line split: fields are comma-separated, a field may be wrapped in
        /// double quotes to embed commas, and a doubled quote ("") inside a quoted field is an
        /// escaped literal quote. Surrounding quotes are stripped from the returned value.
        ///
        /// Required because some blueprints (e.g. NamedItem's Lore column) embed commas inside
        /// quoted fields — a naive line.Split(',') over-counts columns, fails the header-count
        /// check in <see cref="LoadGroup"/>, and silently drops those rows.
        /// </summary>
        private static string[] SplitCsvLine(string line)
        {
            var fields   = new List<string>();
            var sb       = new System.Text.StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        // A doubled quote ("") is an escaped literal quote; otherwise the quote closes the field.
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }

            fields.Add(sb.ToString());
            return fields.ToArray();
        }
    }
}
