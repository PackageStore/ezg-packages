#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    using System;
    using System.IO;
    using System.Text;
    using UnityEngine;

    /// <summary>
    /// Encodes a raster image as a PSD (Photoshop Document) containing ONE real,
    /// editable (unlocked) layer — NOT a flattened "locked Background".
    ///
    /// Why a layer section (instead of merged-only): a PSD that has only the merged
    /// Image Data section and an empty Layer-and-Mask section opens in Photoshop as a
    /// single LOCKED "Background" layer. To open as an unlocked, editable layer we MUST
    /// emit a Layer-and-Mask Information section with at least one named layer record.
    /// This is the in-engine equivalent of ag-psd's <c>writePsd(psd, { noBackground: true })</c>.
    ///
    /// Spec: Adobe Photoshop File Format specification — File Header (§2),
    ///       Layer and Mask Information (§4), Image Data (§6).
    ///
    /// Decisions (locked):
    ///   #10 — Native resolution: no resize. Width/height come from the source texture.
    ///   #11 — Solid-white background: RGB channels composited over white (no source
    ///          alpha bleed). The single layer is opaque (alpha = 255) so the output
    ///          looks identical to the previous flat encoder — it is just unlocked now.
    ///
    /// PSD format rules this encoder obeys:
    ///   - ALL multi-byte integers are BIG-ENDIAN. .NET BinaryWriter is little-endian;
    ///     every multi-byte write goes through the <c>Write*BE</c> helpers.
    ///   - Sections in order: Header → Color Mode Data → Image Resources →
    ///     Layer and Mask Info → (merged) Image Data.
    ///   - Channel/section lengths are computed by buffering content into a nested
    ///     MemoryStream and measuring it — no hand-maintained byte arithmetic.
    ///   - Planar channel data, raw (compression = 0). Row order: Unity
    ///     <see cref="Texture2D.GetPixels32"/> is BOTTOM-UP; PSD is TOP-DOWN → flipped.
    /// </summary>
    internal static class PsdEncoder
    {
        // ── PSD format constants (all big-endian on disk) ────────────────────────

        /// <summary>PSD file signature: ASCII "8BPS".</summary>
        private static readonly byte[] SIGNATURE = { 0x38, 0x42, 0x50, 0x53 }; // "8BPS"

        /// <summary>PSD format version: 1 (standard PSD, not PSB).</summary>
        private const ushort VERSION = 1;

        /// <summary>Bit depth per channel: 8 bits.</summary>
        private const ushort DEPTH = 8;

        /// <summary>Color mode: 3 = RGB.</summary>
        private const ushort COLOR_MODE_RGB = 3;

        /// <summary>Channels in the MERGED composite (Image Data section): RGB, no alpha (decision #11).</summary>
        private const ushort MERGED_CHANNEL_COUNT = 3;

        /// <summary>Image data compression: 0 = raw (no compression).</summary>
        private const ushort COMPRESSION_RAW = 0;

        /// <summary>Layer channel count: A, R, G, B (alpha lets Photoshop treat it as a normal, unlocked layer).</summary>
        private const ushort LAYER_CHANNEL_COUNT = 4;

        /// <summary>Name of the single editable layer (anything other than "Background" keeps it unlocked).</summary>
        private const string LAYER_NAME = "Icon";

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Encodes the given PNG bytes as a single-unlocked-layer PSD at native resolution.
        /// </summary>
        /// <param name="pngBytes">Raw PNG bytes from Gemini (decoded via <see cref="Texture2D.LoadImage"/>).</param>
        /// <returns>A byte array containing a valid single-layer PSD file (no locked Background).</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="pngBytes"/> cannot be decoded.</exception>
        public static byte[] Encode(byte[] pngBytes)
        {
            if (pngBytes == null || pngBytes.Length == 0)
                throw new ArgumentException("pngBytes must be non-null and non-empty.", nameof(pngBytes));

            // Decode PNG → Texture2D to access width, height, and per-pixel color data.
            // M2: linear:false (sRGB) — source PNG is sRGB-encoded. Using linear:true
            // would make GetPixels32() byte values incorrect for channels stored in sRGB.
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: false);
            try
            {
                if (!tex.LoadImage(pngBytes, markNonReadable: false))
                    throw new ArgumentException("Failed to decode PNG bytes into a Texture2D.", nameof(pngBytes));

                return EncodeTexture(tex);
            }
            finally
            {
                // Always destroy the temporary texture to avoid memory leaks in the Editor.
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        /// <summary>
        /// Encodes an already-loaded <see cref="Texture2D"/> as a single-unlocked-layer PSD.
        /// The texture must be readable (not marked as non-readable in the importer).
        /// </summary>
        public static byte[] EncodeTexture(Texture2D source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var width  = source.width;
            var height = source.height;

            // GetPixels32 returns rows from bottom (row 0) to top (row height-1).
            // PSD requires top-to-bottom — BuildPlanes flips during the copy.
            var pixels = source.GetPixels32();

            // Build the four planar channel buffers ONCE; reused by the layer and the
            // merged composite. R/G/B are composited over white; A is fully opaque.
            BuildPlanes(pixels, width, height, out var rPlane, out var gPlane, out var bPlane, out var aPlane);

            using var ms     = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            WriteHeader(writer, width, height);
            WriteColorModeData(writer);
            WriteImageResources(writer);
            WriteLayerAndMaskInfo(writer, width, height, rPlane, gPlane, bPlane, aPlane);
            WriteMergedImageData(writer, rPlane, gPlane, bPlane);

            return ms.ToArray();
        }

        // ── Plane builder ───────────────────────────────────────────────────────

        /// <summary>
        /// Builds top-down planar channel buffers from bottom-up Unity pixels.
        /// R/G/B are composited over solid white (decision #11); A is all-255 (opaque).
        /// </summary>
        private static void BuildPlanes(
            Color32[] pixels, int width, int height,
            out byte[] rPlane, out byte[] gPlane, out byte[] bPlane, out byte[] aPlane)
        {
            var total = width * height;
            rPlane = new byte[total];
            gPlane = new byte[total];
            bPlane = new byte[total];
            aPlane = new byte[total];

            for (var psdRow = 0; psdRow < height; psdRow++)
            {
                var unityRow = height - 1 - psdRow; // flip: Unity row 0 is the bottom row
                for (var col = 0; col < width; col++)
                {
                    var unityIndex = unityRow * width + col;
                    var outIndex   = psdRow  * width + col;

                    var p = pixels[unityIndex];

                    // Composite over white: outC = alpha * srcC + (1 - alpha) * 255
                    var alpha = p.a / 255.0f;
                    rPlane[outIndex] = (byte)Mathf.RoundToInt(alpha * p.r + (1f - alpha) * 255f);
                    gPlane[outIndex] = (byte)Mathf.RoundToInt(alpha * p.g + (1f - alpha) * 255f);
                    bPlane[outIndex] = (byte)Mathf.RoundToInt(alpha * p.b + (1f - alpha) * 255f);
                    aPlane[outIndex] = 255; // opaque layer — visually identical to the old flat output
                }
            }
        }

        // ── Section writers ───────────────────────────────────────────────────────

        /// <summary>
        /// Section 1 — File Header (26 bytes). Channels = 3 (the MERGED composite is RGB;
        /// per-layer channel counts are declared independently in the layer record).
        /// </summary>
        private static void WriteHeader(BinaryWriter w, int width, int height)
        {
            w.Write(SIGNATURE);                     // 4 bytes: "8BPS"
            WriteUInt16BE(w, VERSION);              // 2 bytes: version = 1
            w.Write(new byte[6]);                   // 6 bytes: reserved zeros
            WriteUInt16BE(w, MERGED_CHANNEL_COUNT); // 2 bytes: channels = 3 (merged RGB)
            WriteUInt32BE(w, (uint)height);         // 4 bytes: height (rows)
            WriteUInt32BE(w, (uint)width);          // 4 bytes: width (cols)
            WriteUInt16BE(w, DEPTH);                // 2 bytes: bit depth = 8
            WriteUInt16BE(w, COLOR_MODE_RGB);       // 2 bytes: color mode = 3 (RGB)
        }

        /// <summary>Section 2 — Color Mode Data. Empty for RGB; length = 0.</summary>
        private static void WriteColorModeData(BinaryWriter w) => WriteUInt32BE(w, 0u);

        /// <summary>Section 3 — Image Resources. None needed; length = 0.</summary>
        private static void WriteImageResources(BinaryWriter w) => WriteUInt32BE(w, 0u);

        /// <summary>
        /// Section 4 — Layer and Mask Information.
        /// Emits ONE layer ("Icon", 4 channels A/R/G/B, blend "norm", opacity 255,
        /// flags 0 = visible + unlocked). The presence of this layer record is what
        /// makes Photoshop open an editable layer instead of a locked Background.
        ///
        /// All lengths are measured from buffered content (no hand arithmetic).
        /// Layout written:
        ///   [4] section length
        ///   └─ [4] layer-info length
        ///      ├─ [2] layer count (= 1)
        ///      ├─ layer record (rect, channel-info, blend, flags, extra-data + name)
        ///      └─ channel image data: A, R, G, B  (each: [2] compression=0 + plane bytes)
        ///   └─ [4] global layer mask info length (= 0)
        /// </summary>
        private static void WriteLayerAndMaskInfo(
            BinaryWriter w, int width, int height,
            byte[] rPlane, byte[] gPlane, byte[] bPlane, byte[] aPlane)
        {
            var planeBytes = (uint)(width * height);
            var channelLength = 2u + planeBytes; // compression UInt16 + raw plane

            // ---- Build the layer-info block (layer count + records + channel data) ----
            using var layerInfoMs = new MemoryStream();
            using (var lw = new BinaryWriter(layerInfoMs, Encoding.ASCII, leaveOpen: true))
            {
                WriteInt16BE(lw, 1); // layer count = 1

                // -- Layer record --
                // Bounds: full canvas, top-left origin.
                WriteUInt32BE(lw, 0u);             // top
                WriteUInt32BE(lw, 0u);             // left
                WriteUInt32BE(lw, (uint)height);   // bottom
                WriteUInt32BE(lw, (uint)width);    // right

                WriteUInt16BE(lw, LAYER_CHANNEL_COUNT); // 4 channels

                // Channel info: id (Int16) + data length (UInt32). Order MUST match the
                // channel-image-data order written below: A(-1), R(0), G(1), B(2).
                WriteInt16BE(lw, -1); WriteUInt32BE(lw, channelLength); // alpha
                WriteInt16BE(lw,  0); WriteUInt32BE(lw, channelLength); // red
                WriteInt16BE(lw,  1); WriteUInt32BE(lw, channelLength); // green
                WriteInt16BE(lw,  2); WriteUInt32BE(lw, channelLength); // blue

                lw.Write(Encoding.ASCII.GetBytes("8BIM")); // blend mode signature
                lw.Write(Encoding.ASCII.GetBytes("norm")); // blend mode key = normal
                lw.Write((byte)255); // opacity (fully opaque)
                lw.Write((byte)0);   // clipping = base
                lw.Write((byte)0);   // flags = 0 → visible, transparency NOT locked
                lw.Write((byte)0);   // filler

                // -- Extra data (measured) --
                using var extraMs = new MemoryStream();
                using (var ew = new BinaryWriter(extraMs, Encoding.ASCII, leaveOpen: true))
                {
                    WriteUInt32BE(ew, 0u); // layer mask data length = 0 (no mask)
                    WriteUInt32BE(ew, 0u); // layer blending ranges length = 0
                    WritePascalStringPadded(ew, LAYER_NAME, pad: 4); // layer name
                }
                var extraBytes = extraMs.ToArray();
                WriteUInt32BE(lw, (uint)extraBytes.Length);
                lw.Write(extraBytes);

                // -- Channel image data (same order as channel info): A, R, G, B --
                WriteRawChannel(lw, aPlane);
                WriteRawChannel(lw, rPlane);
                WriteRawChannel(lw, gPlane);
                WriteRawChannel(lw, bPlane);
            }

            var layerInfoBytes = layerInfoMs.ToArray();
            // Layer-info length is rounded up to a multiple of 2 (pad the content to match).
            if ((layerInfoBytes.Length & 1) == 1)
            {
                Array.Resize(ref layerInfoBytes, layerInfoBytes.Length + 1);
            }

            // ---- Build the full Layer-and-Mask content, then prefix its length ----
            using var lmMs = new MemoryStream();
            using (var mw = new BinaryWriter(lmMs, Encoding.ASCII, leaveOpen: true))
            {
                WriteUInt32BE(mw, (uint)layerInfoBytes.Length); // layer-info length
                mw.Write(layerInfoBytes);
                WriteUInt32BE(mw, 0u);                          // global layer mask info length = 0
            }
            var lmBytes = lmMs.ToArray();

            WriteUInt32BE(w, (uint)lmBytes.Length); // Layer-and-Mask section length
            w.Write(lmBytes);
        }

        /// <summary>
        /// Section 5 — (merged) Image Data: the flattened composite preview.
        /// Compression UInt16 = 0 (raw), then planar R, G, B (top-to-bottom).
        /// </summary>
        private static void WriteMergedImageData(BinaryWriter w, byte[] rPlane, byte[] gPlane, byte[] bPlane)
        {
            WriteUInt16BE(w, COMPRESSION_RAW);
            w.Write(rPlane);
            w.Write(gPlane);
            w.Write(bPlane);
        }

        // ── Low-level writers ───────────────────────────────────────────────────

        /// <summary>Writes one raw (uncompressed) channel plane: 2-byte compression flag + bytes.</summary>
        private static void WriteRawChannel(BinaryWriter w, byte[] plane)
        {
            WriteUInt16BE(w, COMPRESSION_RAW);
            w.Write(plane);
        }

        /// <summary>
        /// Writes a Pascal string (1 length byte + ASCII chars) padded with zeros so the
        /// TOTAL written length is a multiple of <paramref name="pad"/>.
        /// </summary>
        private static void WritePascalStringPadded(BinaryWriter w, string s, int pad)
        {
            var bytes = Encoding.ASCII.GetBytes(s);
            if (bytes.Length > 255)
                throw new ArgumentException($"Layer name '{s}' exceeds 255 bytes.", nameof(s));

            w.Write((byte)bytes.Length);
            w.Write(bytes);

            var written = 1 + bytes.Length;
            var rem     = written % pad;
            if (rem != 0)
            {
                var padCount = pad - rem;
                for (var i = 0; i < padCount; i++) w.Write((byte)0);
            }
        }

        // ── Big-endian helpers ────────────────────────────────────────────────────
        //
        // .NET BinaryWriter always writes little-endian; PSD requires big-endian for all
        // multi-byte integers. Every multi-byte write MUST go through these helpers.

        private static void WriteUInt16BE(BinaryWriter w, ushort value)
        {
            w.Write((byte)(value >> 8));
            w.Write((byte)(value & 0xFF));
        }

        private static void WriteInt16BE(BinaryWriter w, short value)
        {
            w.Write((byte)((value >> 8) & 0xFF));
            w.Write((byte)(value & 0xFF));
        }

        private static void WriteUInt32BE(BinaryWriter w, uint value)
        {
            w.Write((byte)((value >> 24) & 0xFF));
            w.Write((byte)((value >> 16) & 0xFF));
            w.Write((byte)((value >>  8) & 0xFF));
            w.Write((byte)( value        & 0xFF));
        }
    }
}
