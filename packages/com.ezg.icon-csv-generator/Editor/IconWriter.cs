#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;

    // ---------------------------------------------------------------------------
    // Writes approved icons as flat PSD files to _Incoming/<groupName>/.
    //
    // Decisions enforced:
    //   #4  — Only Approved rows are written. Rejected/Skipped/Pending are skipped.
    //   #10 — No resize: PsdEncoder uses native resolution.
    //   #11 — Solid white background: PsdEncoder composites alpha over white.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Encodes approved <see cref="IconReviewStateItem"/> rows as flat PSDs and writes them
    /// into <c>&lt;incomingRoot&gt;/&lt;groupName&gt;/</c>.
    ///
    /// After writing the batch, calls <c>AssetDatabase.Refresh()</c> exactly once so Unity
    /// picks up the new files without triggering heavy reimport multiple times.
    ///
    /// The tool does NOT promote assets. The operator runs
    /// <c>t1k:unity:base:asset-import</c> afterwards to validate + promote.
    /// </summary>
    internal static class IconWriter
    {
        // ── Constants ────────────────────────────────────────────────────────────

        /// <summary>Root staging directory (project-relative); configured via settings.</summary>
        private static string INCOMING_ROOT_RELATIVE => IconGenPaths.IncomingRoot;

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>Result of a single row write attempt.</summary>
        internal sealed class WriteResult
        {
            public IconRowModel Row { get; set; }
            public bool         Success { get; set; }
            public string?      WrittenPath { get; set; }
            public string?      Error { get; set; }

            public WriteResult(IconRowModel row)
            {
                this.Row = row;
            }
        }

        /// <summary>
        /// Writes the given items' PSDs to <c>_Incoming/&lt;groupName&gt;/</c>. The caller decides which
        /// rows to pass (the selected rows for "Save Selected", or one row for a per-cell "Save").
        /// </summary>
        /// <param name="items">Rows to write. Rows without generated PNG bytes are skipped with an error.</param>
        /// <returns>One <see cref="WriteResult"/> per row attempted.</returns>
        /// <remarks>
        /// A written icon ALWAYS overwrites any existing staged file — saving an image IS the
        /// explicit decision to replace it. Skip-existing idempotency lives at the generation
        /// stage (row selection / force-regenerate toggle), not at write time.
        /// </remarks>
        public static List<WriteResult> WriteItems(IReadOnlyList<IconReviewStateItem> items)
        {
            var results = new List<WriteResult>();

            foreach (var item in items)
            {
                if (item.PngBytes == null || item.PngBytes.Length == 0)
                {
                    var res = new WriteResult(item.Row) { Success = false, Error = "No PNG bytes available (not generated). Cannot encode PSD." };
                    results.Add(res);
                    Debug.LogError($"[IconWriter] Row '{item.Row.OutputFileName}' has no PNG bytes — skipped.");
                    continue;
                }

                results.Add(WriteOne(item));
            }

            // Only refresh when at least one file was successfully written.
            // Refreshing on all-skip/all-fail is wasteful and can itself trigger a
            // domain reload that kills any other in-flight coroutines.
            var anySuccess = false;
            foreach (var r in results) { if (r.Success) { anySuccess = true; break; } }
            if (anySuccess)
            {
                AssetDatabase.Refresh();
            }

            return results;
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private static WriteResult WriteOne(IconReviewStateItem item)
        {
            var result = new WriteResult(item.Row);

            try
            {
                var targetPath = BuildTargetPath(item.Row);
                result.WrittenPath = targetPath;

                // Ensure the directory exists.
                var dir = Path.GetDirectoryName(targetPath)!;
                Directory.CreateDirectory(dir);

                // Encode and write.
                var psdBytes = PsdEncoder.Encode(item.PngBytes!);
                File.WriteAllBytes(targetPath, psdBytes);

                result.Success = true;
                Debug.Log($"[IconWriter] Written: {targetPath} ({psdBytes.Length:N0} bytes)");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error   = ex.Message;
                Debug.LogError($"[IconWriter] Failed to write '{item.Row.OutputFileName}': {ex}");
            }

            return result;
        }

        private static string BuildTargetPath(IconRowModel row)
        {
            var dataPath    = Application.dataPath;
            var projectRoot = dataPath.Substring(0, dataPath.Length - "Assets".Length);
            var subfolder   = IconExistenceChecker.SubfolderForGroup(row.GroupName);
            var relative    = $"{INCOMING_ROOT_RELATIVE}/{subfolder}/{row.OutputFileName}";
            return Path.Combine(projectRoot, relative).Replace('\\', '/');
        }
    }
}
