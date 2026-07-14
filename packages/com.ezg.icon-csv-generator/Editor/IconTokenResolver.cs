#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;
    using UnityEngine;

    /// <summary>
    /// Shared {token} resolver for both prompt strings and filename patterns.
    /// SSOT for token substitution (code-conventions.md § "No Duplicated Logic").
    ///
    /// Two resolution paths:
    /// - Raw (for prompts): token values are substituted as-is from the CSV fields.
    /// - Sanitized (for filenames): whitespace, control chars, and cross-platform filename-reserved
    ///   characters are removed; a warning is logged when a value changes.
    ///
    /// Unknown {token} → InvalidOperationException (no silent fallback per development-principles.md).
    /// </summary>
    internal static class IconTokenResolver
    {
        private static readonly Regex TOKEN_PATTERN = new Regex(
            @"\{(\w+)\}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves all {token} placeholders in the template using raw CSV field values.
        /// Used for prompt strings (raw values, no sanitization).
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// When a {token} has no matching key in <paramref name="fields"/>.
        /// </exception>
        public static string ResolveRaw(
            string template,
            IReadOnlyDictionary<string, string> fields,
            string rowId)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (fields == null) throw new ArgumentNullException(nameof(fields));

            return TOKEN_PATTERN.Replace(template, match =>
            {
                var token = match.Groups[1].Value;
                if (fields.TryGetValue(token, out var value))
                {
                    return value;
                }

                throw new InvalidOperationException(
                    $"[IconTokenResolver] Template token '{{{token}}}' has no matching CSV column " +
                    $"for row Id='{rowId}'. Available columns: [{string.Join(", ", fields.Keys)}]. " +
                    $"Fix the template or filenamePattern to only reference columns present in the CSV.");
            });
        }

        /// <summary>
        /// Resolves all {token} placeholders using sanitized CSV field values.
        /// Used for filename pattern resolution.
        /// Sanitization: see <see cref="Sanitize"/> — strips whitespace, control chars, and
        /// cross-platform filename-reserved characters. Logs a warning when the value changes.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// When a {token} has no matching key in <paramref name="fields"/>.
        /// </exception>
        public static string ResolveSanitized(
            string template,
            IReadOnlyDictionary<string, string> fields,
            string rowId)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (fields == null) throw new ArgumentNullException(nameof(fields));

            return TOKEN_PATTERN.Replace(template, match =>
            {
                var token = match.Groups[1].Value;
                if (!fields.TryGetValue(token, out var raw))
                {
                    throw new InvalidOperationException(
                        $"[IconTokenResolver] Filename token '{{{token}}}' has no matching CSV column " +
                        $"for row Id='{rowId}'. Available columns: [{string.Join(", ", fields.Keys)}]. " +
                        $"Fix the filenamePattern to only reference columns present in the CSV.");
                }

                var sanitized = Sanitize(raw);
                if (sanitized != raw)
                {
                    Debug.Log(
                        $"[IconTokenResolver] Token '{{{token}}}' value sanitized for filename: " +
                        $"'{raw}' → '{sanitized}' (row Id='{rowId}').");
                }

                return sanitized;
            });
        }

        // ── Sanitization ──────────────────────────────────────────────────────────

        // Cross-platform-hostile filename characters: the union of Windows + Unix reserved
        // characters. NOT Path.GetInvalidFileNameChars() — that returns only '/' and '\0' on
        // macOS/Linux, so a '?' or '*' would survive on the artist's Mac and then break a Windows
        // checkout, the asset-import pipeline, or the build. Generated icon names are committed to
        // git and consumed on every target OS, so the hostile set must be platform-independent.
        private static readonly char[] INVALID_FILENAME_CHARS =
        {
            '<', '>', ':', '"', '/', '\\', '|', '?', '*',
        };

        /// <summary>
        /// Strips whitespace, control characters, and the cross-platform filename-reserved
        /// characters <c>&lt; &gt; : " / \ | ? *</c> from a value so it is safe as a filename stem
        /// or path segment on every target OS. Does NOT log — callers that want a changed-value log
        /// use <see cref="ResolveSanitized"/>. SSOT for filename/path-segment sanitization
        /// (e.g. group name → _Incoming subfolder name).
        /// </summary>
        public static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c)) continue;
                var invalid = false;
                foreach (var bad in INVALID_FILENAME_CHARS)
                {
                    if (c == bad) { invalid = true; break; }
                }
                if (!invalid) sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
