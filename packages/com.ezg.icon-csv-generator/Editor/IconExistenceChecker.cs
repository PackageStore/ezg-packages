#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    using System.Collections.Generic;
    using System.IO;
    using UnityEngine;

    // ---------------------------------------------------------------------------
    // Idempotency checker: determines whether an icon already exists in either
    // the _Incoming staging area OR anywhere in the Assets/ tree (stem match).
    //
    // Subfolder routing is now driven by the group's sanitized groupName rather
    // than a hard-coded category enum.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Checks whether an icon with a given filename already exists in:
    /// 1. <c>&lt;incomingRoot&gt;/&lt;groupName&gt;/&lt;filename&gt;</c>
    /// 2. Anywhere under <c>Assets/</c> with a matching filename stem (any extension).
    ///    This catches promoted files (e.g. .psd promoted to Sprites/ tree, or a .png
    ///    created by Unity's texture importer), even when Assets/Art/2D/ doesn't exist yet.
    /// </summary>
    internal static class IconExistenceChecker
    {
        // ── Constants ────────────────────────────────────────────────────────────

        /// <summary>Root of the staging area (relative to project root); configured via settings.</summary>
        private static string INCOMING_ROOT => IconGenPaths.IncomingRoot;

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a <see cref="HashSet{T}"/> of all filename stems (no extension, lower-case)
        /// under <c>Assets/</c> in a single directory walk.
        ///
        /// Call this once when loading rows; pass the result to
        /// <see cref="ExistsFast"/> for each row to avoid O(rows × files) cost.
        /// </summary>
        public static HashSet<string> BuildStemSet()
        {
            var stems = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var assetsRoot = AbsoluteFromRelative("Assets");
            if (!Directory.Exists(assetsRoot)) return stems;

            try
            {
                foreach (var file in Directory.EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories))
                {
                    if (file.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase)) continue;
                    stems.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[IconExistenceChecker] Error building stem set from Assets/: {ex.Message}");
            }

            return stems;
        }

        /// <summary>
        /// Returns true when the icon for the given row already exists, using a
        /// pre-built stem set (see <see cref="BuildStemSet"/>).
        /// </summary>
        public static bool ExistsFast(IconRowModel row, HashSet<string> assetsStemSet)
        {
            return ExistsInIncoming(row) || ExistsInAssetsTreeFast(row, assetsStemSet);
        }

        /// <summary>Returns the full absolute path to the _Incoming location for this row.</summary>
        public static string GetIncomingPath(IconRowModel row)
        {
            var subfolder    = SubfolderForGroup(row.GroupName);
            var relativePath = $"{INCOMING_ROOT}/{subfolder}/{row.OutputFileName}";
            return AbsoluteFromRelative(relativePath);
        }

        /// <summary>
        /// Returns the sanitized subfolder name inside _Incoming for the given group name.
        /// Current seed group names (Currency / Weapon / EquipmentGear / Skills / Realms)
        /// contain no whitespace or invalid chars and sanitize to themselves.
        /// </summary>
        public static string SubfolderForGroup(string groupName)
        {
            return IconTokenResolver.Sanitize(groupName);
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private static bool ExistsInIncoming(IconRowModel row)
        {
            return File.Exists(GetIncomingPath(row));
        }

        private static bool ExistsInAssetsTreeFast(IconRowModel row, HashSet<string> stemSet)
        {
            var stem = Path.GetFileNameWithoutExtension(row.OutputFileName);
            return stemSet.Contains(stem);
        }

        private static string AbsoluteFromRelative(string assetsRelativePath)
        {
            var dataPath    = Application.dataPath;
            var projectRoot = dataPath.Substring(0, dataPath.Length - "Assets".Length);
            return Path.Combine(projectRoot, assetsRelativePath).Replace('\\', '/');
        }
    }
}
