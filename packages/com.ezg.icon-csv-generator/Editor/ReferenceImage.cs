#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    // ---------------------------------------------------------------------------
    // A pre-loaded reference image to accompany a Gemini image-generation request.
    // Raw bytes are read directly from the asset file (no Texture2D.EncodeToPNG
    // round-trip) so no Read/Write-enabled import setting is required.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// A reference image to attach to a Gemini request as an inlineData part.
    /// Constructed by <see cref="ReferenceImageReader"/> from a <c>Texture2D</c> asset.
    /// </summary>
    internal sealed class ReferenceImage
    {
        /// <summary>MIME type derived from the source file extension (e.g. "image/png").</summary>
        public string MimeType { get; private set; }

        /// <summary>Base64-encoded content of the original asset file (not re-encoded).</summary>
        public string Base64Data { get; private set; }

        public ReferenceImage(string mimeType, string base64Data)
        {
            this.MimeType   = mimeType;
            this.Base64Data = base64Data;
        }
    }
}
