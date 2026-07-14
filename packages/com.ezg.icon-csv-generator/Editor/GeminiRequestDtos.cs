#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    using System;
    using System.Collections.Generic;

    // ---------------------------------------------------------------------------
    // Gemini generateContent — RESPONSE Data Transfer Objects only.
    //
    // NOTE: The REQUEST DTOs (GeminiRequest, GeminiContent, GeminiPart,
    // GeminiGenerationConfig) were removed in the artist-params extension
    // (issue #57) because JsonUtility cannot serialize mixed parts lists
    // (text + inlineData) without emitting empty fields for unused properties,
    // which would break the Gemini API.  The request body is now built via a
    // manual StringBuilder in GeminiImageClient.BuildRequestBodyJson.
    //
    // The RESPONSE DTOs below remain unchanged — JsonUtility.FromJson<T> works
    // perfectly for deserialising the response because we read all fields
    // (including null ones) and simply pick the populated ones.
    //
    // Endpoint: POST https://generativelanguage.googleapis.com/v1beta/models/
    //           gemini-3.1-flash-image:generateContent
    // Auth:     x-goog-api-key header (key NEVER in the URL — keeps key out of logs)
    // Docs:     ai.google.dev/gemini-api/docs/image-generation (verified 2026-06)
    // ---------------------------------------------------------------------------

    // ── Response ─────────────────────────────────────────────────────────────────

    /// <summary>Top-level response from the Gemini generateContent endpoint.</summary>
    [Serializable]
    internal sealed class GeminiResponse
    {
        public List<GeminiCandidate>? candidates;
        /// <summary>Present on error responses (non-200 bodies still contain a JSON error block).</summary>
        public GeminiErrorWrapper? error;
    }

    [Serializable]
    internal sealed class GeminiCandidate
    {
        public GeminiResponseContent? content;
    }

    [Serializable]
    internal sealed class GeminiResponseContent
    {
        public List<GeminiResponsePart>? parts;
    }

    /// <summary>
    /// A part in the response.  Image parts have <c>inlineData</c> set;
    /// text parts have <c>text</c> set.  We search for the first inlineData part.
    /// </summary>
    [Serializable]
    internal sealed class GeminiResponsePart
    {
        /// <summary>Present for text-modality parts; null for image parts.</summary>
        public string? text;
        /// <summary>Present for image-modality parts; null for text parts.</summary>
        public GeminiInlineData? inlineData;
    }

    /// <summary>Base64-encoded image data returned by the API.</summary>
    [Serializable]
    internal sealed class GeminiInlineData
    {
        /// <summary>MIME type of the returned image, typically <c>image/png</c>.</summary>
        public string mimeType = string.Empty;
        /// <summary>Base64-encoded raw image bytes.</summary>
        public string data = string.Empty;
    }

    /// <summary>Error wrapper present in non-200 JSON bodies from the Gemini API.</summary>
    [Serializable]
    internal sealed class GeminiErrorWrapper
    {
        public int code;
        public string? message;
        public string? status;
    }

    // ── Result (returned to callers) ──────────────────────────────────────────────

    /// <summary>
    /// Result of one <see cref="GeminiImageClient"/> request.
    /// Exactly one of <see cref="PngBytes"/>/<see cref="MimeType"/> (success)
    /// or <see cref="Error"/> (failure) is set.
    /// </summary>
    internal sealed class GeminiImageResult
    {
        /// <summary>Decoded image bytes on success; <c>null</c> on failure.</summary>
        public byte[]? PngBytes { get; private set; }

        /// <summary>MIME type reported by Gemini, e.g. <c>image/png</c>.</summary>
        public string? MimeType { get; private set; }

        /// <summary>Human-readable error including raw API response body; <c>null</c> on success.</summary>
        public string? Error { get; private set; }

        public bool IsSuccess => this.PngBytes != null && this.Error == null;

        public static GeminiImageResult Success(byte[] imageBytes, string mimeType) =>
            new() { PngBytes = imageBytes, MimeType = mimeType };

        public static GeminiImageResult Failure(string error) =>
            new() { Error = error };
    }
}
