#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Text;
    using UnityEngine;
    using UnityEngine.Networking;

    /// <summary>
    /// Sends image-generation requests to the Gemini REST API using <see cref="UnityWebRequest"/>
    /// and decodes the returned base64 image bytes.
    ///
    /// Model string: <c>gemini-3.1-flash-image</c> (LOCKED — do not change).
    /// API key comes exclusively from <see cref="IconGeneratorPrefs.GetApiKey()"/> (EditorPrefs).
    /// The key is transmitted in the <c>x-goog-api-key</c> header — never in the URL.
    ///
    /// Request body is built with a manual StringBuilder because JsonUtility cannot
    /// serialize mixed parts lists (text + inlineData) without emitting empty fields
    /// on every part, which breaks the Gemini API.
    ///
    /// Error handling: on any non-200 response or missing image part, the raw Gemini
    /// response body is surfaced in <see cref="GeminiImageResult.Error"/> so the
    /// operator sees exactly what the API returned (no silent fallbacks —
    /// per development-principles.md).
    /// </summary>
    internal static class GeminiImageClient
    {
        // ── Constants ────────────────────────────────────────────────────────────

        /// <summary>Gemini image-generation model (locked decision — do not substitute).</summary>
        private const string MODEL_ID = "gemini-3.1-flash-image";

        /// <summary>Base URL for the Gemini generateContent endpoint.</summary>
        private const string BASE_URL =
            "https://generativelanguage.googleapis.com/v1beta/models/";

        /// <summary>Action suffix appended to the model path.</summary>
        private const string ACTION_SUFFIX = ":generateContent";

        /// <summary>Header name for the API key (key in header, never in URL).</summary>
        private const string API_KEY_HEADER = "x-goog-api-key";

        /// <summary>Content-Type for all POST bodies.</summary>
        private const string CONTENT_TYPE = "application/json";

        // ── Coroutine-based entry point ──────────────────────────────────────────

        /// <summary>
        /// Sends one prompt to Gemini and yields a <see cref="GeminiImageResult"/>.
        /// Must be driven by a coroutine runner (e.g. <see cref="EditorCoroutineRunner"/>).
        ///
        /// Usage:
        /// <code>
        ///   EditorCoroutineRunner.StartCoroutine(
        ///       GeminiImageClient.RequestImage(prompt, "1:1", "1K", refs, result => { ... }));
        /// </code>
        /// </summary>
        /// <param name="resolvedPrompt">Fully-resolved prompt string (no {token} placeholders).</param>
        /// <param name="aspectRatio">
        ///   Gemini aspectRatio value (e.g. "1:1", "16:9"). From <see cref="IconGeneratorSettings"/>.
        /// </param>
        /// <param name="imageSize">
        ///   Gemini imageSize value (e.g. "1K", "2K"). From <see cref="IconGeneratorSettings"/>.
        /// </param>
        /// <param name="referenceImages">
        ///   Optional reference images attached as inlineData parts AFTER the text part.
        ///   Pass an empty list (or null) to skip.
        /// </param>
        /// <param name="onComplete">Called exactly once with the result when the request finishes.</param>
        public static IEnumerator RequestImage(
            string resolvedPrompt,
            string aspectRatio,
            string imageSize,
            IReadOnlyList<ReferenceImage>? referenceImages,
            Action<GeminiImageResult> onComplete)
        {
            var apiKey = IconGeneratorPrefs.GetApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                onComplete(GeminiImageResult.Failure(
                    "[GeminiImageClient] No API key configured. " +
                    "Enter and save your Gemini API key in the Icon Generator window."));
                yield break;
            }

            var url       = BuildUrl();
            var body      = BuildRequestBodyJson(resolvedPrompt, aspectRatio, imageSize, referenceImages);
            var bodyBytes = Encoding.UTF8.GetBytes(body);

            using var request = new UnityWebRequest(url, "POST");
            request.uploadHandler   = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", CONTENT_TYPE);
            request.SetRequestHeader(API_KEY_HEADER, apiKey);

            var op = request.SendWebRequest();

            while (!op.isDone)
            {
                yield return null;
            }

            onComplete(ParseResponse(request));
        }

        // ── Private: URL ─────────────────────────────────────────────────────────

        private static string BuildUrl()
        {
            return BASE_URL + MODEL_ID + ACTION_SUFFIX;
        }

        // ── Private: manual JSON body construction ────────────────────────────────

        /// <summary>
        /// Builds the Gemini generateContent request body as a raw JSON string.
        ///
        /// Resulting shape (camelCase — Gemini REST accepts camelCase):
        /// <code>
        /// {
        ///   "contents": [{ "parts": [
        ///     { "text": "&lt;escaped-prompt&gt;" },
        ///     { "inlineData": { "mimeType": "&lt;mime&gt;", "data": "&lt;base64&gt;" } }
        ///     // ... one per reference image
        ///   ]}],
        ///   "generationConfig": {
        ///     "responseModalities": ["TEXT", "IMAGE"],
        ///     "responseFormat": {
        ///       "image": { "aspectRatio": "&lt;ar&gt;", "imageSize": "&lt;size&gt;" }
        ///     }
        ///   }
        /// }
        /// </code>
        ///
        /// Why manual StringBuilder?
        /// JsonUtility cannot omit unused fields from [Serializable] classes, so mixing
        /// a text-only part and image-only parts in the same array would produce empty
        /// "text":"" on image parts and empty "inlineData":null on the text part —
        /// which causes a Gemini validation error.
        /// </summary>
        private static string BuildRequestBodyJson(
            string resolvedPrompt,
            string aspectRatio,
            string imageSize,
            IReadOnlyList<ReferenceImage>? referenceImages)
        {
            var sb = new StringBuilder(512);

            sb.Append("{\"contents\":[{\"parts\":[");

            // Text part (always first)
            sb.Append("{\"text\":\"");
            AppendJsonEscaped(sb, resolvedPrompt);
            sb.Append("\"}");

            // Reference image parts (one per entry, after the text part)
            if (referenceImages != null)
            {
                foreach (var refImage in referenceImages)
                {
                    sb.Append(",{\"inlineData\":{\"mimeType\":\"");
                    AppendJsonEscaped(sb, refImage.MimeType);
                    sb.Append("\",\"data\":\"");
                    // Base64 uses [A-Za-z0-9+/=] — no JSON escaping needed.
                    sb.Append(refImage.Base64Data);
                    sb.Append("\"}}");
                }
            }

            sb.Append("]");   // close parts
            sb.Append("}]");  // close contents[0], close contents

            // generationConfig
            sb.Append(",\"generationConfig\":{");
            sb.Append("\"responseModalities\":[\"TEXT\",\"IMAGE\"]");

            // responseFormat.image — Gemini v1beta expects PROTO ENUM names here
            // (e.g. ASPECT_RATIO_ONE_BY_ONE, IMAGE_SIZE_ONE_K), NOT the friendly "1:1"/"1K"
            // strings the public docs example shows. Map the UI value to the enum constant;
            // omit any field that doesn't map so the API falls back to its default.
            // Enum names verified against the live discovery doc:
            //   https://generativelanguage.googleapis.com/$discovery/rest?version=v1beta
            var arEnum   = MapAspectRatioEnum(aspectRatio);
            var sizeEnum = MapImageSizeEnum(imageSize);
            if (arEnum != null || sizeEnum != null)
            {
                sb.Append(",\"responseFormat\":{\"image\":{");
                var wroteField = false;
                if (arEnum != null)
                {
                    sb.Append("\"aspectRatio\":\"").Append(arEnum).Append('"');
                    wroteField = true;
                }
                if (sizeEnum != null)
                {
                    if (wroteField) sb.Append(',');
                    sb.Append("\"imageSize\":\"").Append(sizeEnum).Append('"');
                }
                sb.Append("}}");  // close image, close responseFormat
            }

            sb.Append("}");     // close generationConfig

            sb.Append("}");     // close root object

            return sb.ToString();
        }

        /// <summary>
        /// Maps a friendly aspect-ratio label (e.g. "16:9") to the Gemini proto enum constant
        /// (e.g. "ASPECT_RATIO_SIXTEEN_BY_NINE"). Returns null for unknown values (field omitted).
        /// </summary>
        private static string? MapAspectRatioEnum(string friendly) => friendly switch
        {
            "1:1"  => "ASPECT_RATIO_ONE_BY_ONE",
            "2:3"  => "ASPECT_RATIO_TWO_BY_THREE",
            "3:2"  => "ASPECT_RATIO_THREE_BY_TWO",
            "3:4"  => "ASPECT_RATIO_THREE_BY_FOUR",
            "4:3"  => "ASPECT_RATIO_FOUR_BY_THREE",
            "4:5"  => "ASPECT_RATIO_FOUR_BY_FIVE",
            "5:4"  => "ASPECT_RATIO_FIVE_BY_FOUR",
            "9:16" => "ASPECT_RATIO_NINE_BY_SIXTEEN",
            "16:9" => "ASPECT_RATIO_SIXTEEN_BY_NINE",
            "21:9" => "ASPECT_RATIO_TWENTY_ONE_BY_NINE",
            "1:8"  => "ASPECT_RATIO_ONE_BY_EIGHT",
            "8:1"  => "ASPECT_RATIO_EIGHT_BY_ONE",
            "1:4"  => "ASPECT_RATIO_ONE_BY_FOUR",
            "4:1"  => "ASPECT_RATIO_FOUR_BY_ONE",
            _      => null,
        };

        /// <summary>
        /// Maps a friendly image-size label (e.g. "1K") to the Gemini proto enum constant
        /// (e.g. "IMAGE_SIZE_ONE_K"). Returns null for unknown values (field omitted).
        /// </summary>
        private static string? MapImageSizeEnum(string friendly) => friendly switch
        {
            "512" => "IMAGE_SIZE_FIVE_TWELVE",
            "1K"  => "IMAGE_SIZE_ONE_K",
            "2K"  => "IMAGE_SIZE_TWO_K",
            "4K"  => "IMAGE_SIZE_FOUR_K",
            _     => null,
        };

        /// <summary>
        /// Appends <paramref name="value"/> to <paramref name="sb"/> with JSON string escaping
        /// applied: <c>\</c>, <c>"</c>, <c>\n</c>, <c>\r</c>, <c>\t</c>, and control chars
        /// below U+0020 are escaped.  Base64 data (safe alphabet only) does NOT need escaping;
        /// call <c>sb.Append(base64)</c> directly.
        /// </summary>
        private static void AppendJsonEscaped(StringBuilder sb, string value)
        {
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (ch < 0x20)
                            sb.AppendFormat("\\u{0:x4}", (int)ch);
                        else
                            sb.Append(ch);
                        break;
                }
            }
        }

        // ── Private: response parsing ────────────────────────────────────────────

        private static GeminiImageResult ParseResponse(UnityWebRequest request)
        {
            var statusCode = request.responseCode;
            var rawBody    = request.downloadHandler?.text ?? "(no response body)";

            if (request.result != UnityWebRequest.Result.Success)
            {
                // Surface the raw body — it usually explains the Gemini error (quota, bad key, etc.)
                return GeminiImageResult.Failure(
                    $"[GeminiImageClient] HTTP {statusCode} — {request.error}\n" +
                    $"Raw response body:\n{rawBody}");
            }

            // Parse the response JSON
            GeminiResponse? response;
            try
            {
                response = JsonUtility.FromJson<GeminiResponse>(rawBody);
            }
            catch (Exception ex)
            {
                return GeminiImageResult.Failure(
                    $"[GeminiImageClient] Failed to parse Gemini response JSON: {ex.Message}\n" +
                    $"Raw response body:\n{rawBody}");
            }

            if (response == null)
            {
                return GeminiImageResult.Failure(
                    $"[GeminiImageClient] Gemini returned null/empty JSON.\nRaw body:\n{rawBody}");
            }

            // Walk candidates[0].content.parts — find the first inlineData part
            var candidates = response.candidates;
            if (candidates == null || candidates.Count == 0)
            {
                return GeminiImageResult.Failure(
                    $"[GeminiImageClient] Response contained no candidates.\nRaw body:\n{rawBody}");
            }

            var parts = candidates[0].content?.parts;
            if (parts == null || parts.Count == 0)
            {
                return GeminiImageResult.Failure(
                    $"[GeminiImageClient] Response candidate had no parts.\nRaw body:\n{rawBody}");
            }

            GeminiInlineData? imageData = null;
            foreach (var part in parts)
            {
                if (part.inlineData != null && !string.IsNullOrEmpty(part.inlineData.data))
                {
                    imageData = part.inlineData;
                    break;
                }
            }

            if (imageData == null)
            {
                return GeminiImageResult.Failure(
                    $"[GeminiImageClient] No inlineData image part found in response.\nRaw body:\n{rawBody}");
            }

            byte[] imageBytes;
            try
            {
                imageBytes = Convert.FromBase64String(imageData.data);
            }
            catch (FormatException ex)
            {
                return GeminiImageResult.Failure(
                    $"[GeminiImageClient] Failed to decode base64 image data: {ex.Message}\nRaw body:\n{rawBody}");
            }

            return GeminiImageResult.Success(imageBytes, imageData.mimeType);
        }
    }
}
