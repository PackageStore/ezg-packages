#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    using System;
    using System.IO;
    using UnityEngine;

    // ---------------------------------------------------------------------------
    // Crash/reload-safe scratch cache for generated icons.
    //
    // Problem solved: a generated icon lives only in memory (Texture2D + PngBytes on the
    // review item) until the artist Approves + Writes it to _Incoming. An Editor script
    // recompile / domain reload re-creates the EditorWindow and wipes those non-serialized
    // fields — so any generated-but-unwritten icon is lost.
    //
    // This cache writes the raw PNG to disk the instant generation succeeds, BEFORE approval.
    // On the next "Load Rows" the window restores the latest cached PNG per row (see
    // IconGeneratorWindow.TryRestoreFromCache), so nothing is lost across reloads.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Persists generated icon PNGs to a project-local scratch folder with guaranteed-unique
    /// filenames, and restores the most recent one per row.
    ///
    /// Why PNG (not PSD): the PNG is the lossless source from Gemini. It both restores the
    /// in-window preview AND re-encodes to the final <c>.psd</c> on Write (so a recovered icon
    /// even picks up the latest <see cref="PsdEncoder"/> format). PSD is a one-way export here.
    ///
    /// Location: <c>&lt;project&gt;/Library/EzgIconCsvGenCache/</c> — <c>Library/</c> is
    /// gitignored, survives domain reloads, and is safe to delete (it is only a cache).
    ///
    /// Uniqueness: each save is <c>{stem}__{yyyyMMdd-HHmmss}-{guid8}.png</c>. The timestamp +
    /// random GUID suffix means re-generating the same row — or two rows whose final names
    /// collide — NEVER overwrites an earlier file (per the "never get rewrited" requirement).
    /// </summary>
    internal static class IconGenCache
    {
        private const string CACHE_DIR_NAME   = "EzgIconCsvGenCache";
        private const string FILE_NAME_SEP    = "__";   // delimiter between stem and unique suffix
        private const string PNG_EXTENSION    = ".png";

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes <paramref name="pngBytes"/> to the cache under a guaranteed-unique filename and
        /// returns its absolute path. Never throws — on any IO failure it logs a warning and
        /// returns null so a caching hiccup can never block or fail generation.
        /// </summary>
        public static string? Save(IconRowModel row, byte[] pngBytes)
        {
            if (pngBytes == null || pngBytes.Length == 0) return null;

            try
            {
                var dir       = EnsureCacheDir();
                var stem      = SafeStem(row);
                var shortGuid = Guid.NewGuid().ToString("N").Substring(0, 8);
                var fileName  = $"{stem}{FILE_NAME_SEP}{DateTime.Now:yyyyMMdd-HHmmss}-{shortGuid}{PNG_EXTENSION}";
                var fullPath  = Path.Combine(dir, fileName).Replace('\\', '/');

                File.WriteAllBytes(fullPath, pngBytes);
                return fullPath;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[IconGenerator] Failed to cache generated PNG for '{row.OutputFileName}': {ex.Message}. " +
                    "Generation continues, but this icon will not survive a domain reload until written.");
                return null;
            }
        }

        /// <summary>
        /// Loads the most recently cached PNG for <paramref name="row"/> (matched by filename stem),
        /// if any. Returns false (with null outs) when no cache entry exists or on IO error.
        /// </summary>
        public static bool TryLoadLatest(IconRowModel row, out byte[]? pngBytes, out string? path)
        {
            pngBytes = null;
            path     = null;

            try
            {
                var dir = CacheDirPath();
                if (!Directory.Exists(dir)) return false;

                var stem = SafeStem(row);
                // The FILE_NAME_SEP delimiter prevents prefix collisions
                // (e.g. "Sword_Common" must NOT match "Sword_CommonPlus").
                var pattern = $"{stem}{FILE_NAME_SEP}*{PNG_EXTENSION}";
                var matches = Directory.GetFiles(dir, pattern);
                if (matches.Length == 0) return false;

                var newest     = matches[0];
                var newestTime = File.GetLastWriteTimeUtc(newest);
                for (var i = 1; i < matches.Length; i++)
                {
                    var t = File.GetLastWriteTimeUtc(matches[i]);
                    if (t > newestTime) { newest = matches[i]; newestTime = t; }
                }

                var bytes = File.ReadAllBytes(newest);
                if (bytes.Length == 0) return false;

                pngBytes = bytes;
                path     = newest.Replace('\\', '/');
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[IconGenerator] Failed to read icon cache for '{row.OutputFileName}': {ex.Message}.");
                return false;
            }
        }

        /// <summary>Absolute path to the cache directory (does not create it).</summary>
        public static string CacheDirPath()
        {
            // Application.dataPath ends with "/Assets" — strip it to reach the project root.
            var projectRoot = Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length);
            return Path.Combine(projectRoot, "Library", CACHE_DIR_NAME).Replace('\\', '/');
        }

        // ── Private helpers ─────────────────────────────────────────────────────────

        private static string EnsureCacheDir()
        {
            var dir = CacheDirPath();
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Filename stem for a row — the output filename without extension, falling back to
        /// group name + id. Strips characters that are illegal in filenames defensively.
        /// </summary>
        private static string SafeStem(IconRowModel row)
        {
            var stem = Path.GetFileNameWithoutExtension(row.OutputFileName);
            if (string.IsNullOrEmpty(stem)) stem = $"{row.GroupName}_{row.Id}";

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                stem = stem.Replace(c, '_');
            }
            return stem;
        }
    }
}
