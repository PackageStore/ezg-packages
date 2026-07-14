#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;

    // ---------------------------------------------------------------------------
    // Reads reference image bytes directly from the asset FILE on disk
    // (no Texture2D.EncodeToPNG round-trip, so no Read/Write-enabled import
    // setting is required).  Derives the MIME type from the file extension.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Converts an <see cref="IconCsvGroup.referenceImages"/> list into
    /// <see cref="ReferenceImage"/> instances ready for embedding in a Gemini request.
    /// </summary>
    internal static class ReferenceImageReader
    {
        // ── Constants ─────────────────────────────────────────────────────────────

        private const string MIME_PNG  = "image/png";
        private const string MIME_JPEG = "image/jpeg";
        private const string MIME_WEBP = "image/webp";

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads each non-null <see cref="Texture2D"/> in <paramref name="textures"/>,
        /// derives the MIME type from the file extension, and base64-encodes the raw file bytes.
        ///
        /// Entries with unknown extensions or read errors are skipped with a
        /// <see cref="Debug.LogWarning"/> — no silent fallbacks.
        /// </summary>
        /// <returns>
        /// A list of <see cref="ReferenceImage"/> objects (one per successfully read texture).
        /// May be empty if all textures were null or had unsupported extensions.
        /// </returns>
        public static IReadOnlyList<ReferenceImage> Read(IReadOnlyList<Texture2D> textures)
        {
            var results = new List<ReferenceImage>(textures.Count);

            foreach (var tex in textures)
            {
                if (tex == null) continue;

                var assetPath = AssetDatabase.GetAssetPath(tex);
                if (string.IsNullOrEmpty(assetPath))
                {
                    Debug.LogWarning(
                        $"[IconGenerator] Reference image '{tex.name}' has no asset path " +
                        "(it may be a runtime texture, not an imported asset). Skipping.");
                    continue;
                }

                var mimeType = ExtensionToMime(Path.GetExtension(assetPath));
                if (mimeType == null)
                {
                    Debug.LogWarning(
                        $"[IconGenerator] Reference image '{assetPath}' has an unsupported extension " +
                        $"('{Path.GetExtension(assetPath)}'). Supported: .png, .jpg, .jpeg, .webp. Skipping.");
                    continue;
                }

                // Build absolute path from the Assets-relative assetPath.
                // Application.dataPath ends with "/Assets"; strip it, then append the full assetPath.
                var absolutePath = Path.Combine(
                    Application.dataPath[..^"Assets".Length],
                    assetPath);

                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(absolutePath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[IconGenerator] Could not read reference image bytes from '{absolutePath}': " +
                        $"{ex.Message}. Skipping.");
                    continue;
                }

                var base64 = Convert.ToBase64String(bytes);
                results.Add(new ReferenceImage(mimeType, base64));
            }

            return results;
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        /// <returns>MIME type string, or <c>null</c> if the extension is unsupported.</returns>
        private static string? ExtensionToMime(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".png"  => MIME_PNG,
                ".jpg"  => MIME_JPEG,
                ".jpeg" => MIME_JPEG,
                ".webp" => MIME_WEBP,
                _       => null,
            };
        }
    }
}
