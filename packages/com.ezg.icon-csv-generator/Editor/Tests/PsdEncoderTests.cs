#nullable enable

namespace Ezg.IconCsvGenerator.Editor.Tests
{
    using System;
    using System.Text;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// EditMode tests for <see cref="PsdEncoder"/>.
    ///
    /// Run via: Unity Editor → Window → General → Test Runner → EditMode → Ezg.IconCsvGenerator.Editor.Tests
    ///
    /// The encoder now emits ONE unlocked layer ("Icon") + a merged RGB composite, so the
    /// merged Image Data no longer sits at a fixed offset — tests locate it by walking the
    /// section-length fields (see <see cref="MergedImageDataOffset"/>).
    ///
    /// Gate: ALL tests must pass (green).
    /// </summary>
    [TestFixture]
    internal sealed class PsdEncoderTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a readable Texture2D filled with a solid <paramref name="color"/>.
        /// Caller is responsible for calling <see cref="UnityEngine.Object.DestroyImmediate"/> afterward.
        /// </summary>
        private static Texture2D MakeSolidTexture(int width, int height, Color32 color)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: true);
            var pixels = new Color32[width * height];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = color;
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Walks the three fixed-position section-length fields (Color Mode Data, Image
        /// Resources, Layer and Mask Information) to return the byte offset of the merged
        /// Image Data section's 2-byte compression field. Robust to layer-section size.
        /// </summary>
        private static int MergedImageDataOffset(byte[] psd)
        {
            var o = 26;                                 // after File Header
            o += 4 + (int)ReadUInt32BE(psd, o);         // skip Color Mode Data  (len field + body)
            o += 4 + (int)ReadUInt32BE(psd, o);         // skip Image Resources  (len field + body)
            o += 4 + (int)ReadUInt32BE(psd, o);         // skip Layer and Mask   (len field + body)
            return o;                                   // → merged Image Data compression field
        }

        // ── Test 1: Header bytes ──────────────────────────────────────────────────

        /// <summary>
        /// A 4×4 white texture must produce a PSD whose header is exactly:
        ///   "8BPS", version=1, 6 reserved zeros, channels=3 (merged RGB), height=4, width=4, depth=8, colorMode=3.
        /// </summary>
        [Test]
        public void Encode_4x4WhiteTexture_HeaderBytesAreCorrect()
        {
            const int WIDTH  = 4;
            const int HEIGHT = 4;
            var white = new Color32(255, 255, 255, 255);
            var tex   = MakeSolidTexture(WIDTH, HEIGHT, white);

            byte[] psd;
            try
            {
                psd = PsdEncoder.EncodeTexture(tex);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }

            // Signature: bytes 0–3 must be "8BPS"
            Assert.AreEqual(0x38, psd[0], "Byte 0 of signature must be 0x38 ('8')");
            Assert.AreEqual(0x42, psd[1], "Byte 1 of signature must be 0x42 ('B')");
            Assert.AreEqual(0x50, psd[2], "Byte 2 of signature must be 0x50 ('P')");
            Assert.AreEqual(0x53, psd[3], "Byte 3 of signature must be 0x53 ('S')");

            // Version: bytes 4–5 (big-endian UInt16) = 1
            Assert.AreEqual(1, ReadUInt16BE(psd, 4), "PSD version must be 1");

            // Reserved: bytes 6–11 must all be 0
            for (var i = 6; i < 12; i++)
                Assert.AreEqual(0, psd[i], $"Reserved byte {i} must be 0");

            // Channels: bytes 12–13 = 3 (merged composite is RGB; layer alpha is separate)
            Assert.AreEqual(3, ReadUInt16BE(psd, 12), "Merged channel count must be 3 (RGB)");

            // Height / Width: bytes 14–17 / 18–21 (big-endian UInt32)
            Assert.AreEqual(HEIGHT, (int)ReadUInt32BE(psd, 14), "Height must match texture height");
            Assert.AreEqual(WIDTH,  (int)ReadUInt32BE(psd, 18), "Width must match texture width");

            // Depth: bytes 22–23 = 8 ; Color mode: bytes 24–25 = 3 (RGB)
            Assert.AreEqual(8, ReadUInt16BE(psd, 22), "Bit depth must be 8");
            Assert.AreEqual(3, ReadUInt16BE(psd, 24), "Color mode must be 3 (RGB)");
        }

        // ── Test 2: Total byte length ─────────────────────────────────────────────

        /// <summary>
        /// Verifies the total PSD byte length for a 2×2 image with the single-layer format.
        ///
        /// Layout (P = width × height):
        ///   Header                         26
        ///   Color Mode Data                 4  (len=0)
        ///   Image Resources                 4  (len=0)
        ///   Layer and Mask section          4  (section len field)
        ///     layer-info len field          4
        ///     layer count                   2
        ///     layer record                 74  (rect16 + chanCount2 + chanInfo24 + blend8 +
        ///                                        opacity/clip/flags/filler4 + extraLen4 +
        ///                                        extra16[mask4+blend4+name8])
        ///     channel image data        8 + 4P  (4 channels × [compression2 + P])
        ///     global mask len field         4  (=0)
        ///   Merged Image Data compression   2
        ///   Merged Image Data pixels       3P
        ///   ── total ──            132 + 7P
        /// For 2×2 (P=4): 132 + 28 = 160 bytes.
        /// </summary>
        [Test]
        public void Encode_2x2WhiteTexture_TotalLengthIsCorrect()
        {
            const int WIDTH  = 2;
            const int HEIGHT = 2;
            var P = WIDTH * HEIGHT;
            var expectedTotal = 132 + 7 * P;

            var tex = MakeSolidTexture(WIDTH, HEIGHT, new Color32(255, 255, 255, 255));
            byte[] psd;
            try
            {
                psd = PsdEncoder.EncodeTexture(tex);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }

            Assert.AreEqual(expectedTotal, psd.Length,
                $"Total PSD length for {WIDTH}x{HEIGHT} must be {expectedTotal} bytes");
        }

        // ── Test 3: Compression field ─────────────────────────────────────────────

        /// <summary>The merged Image Data compression field (located structurally) must be 0 (raw).</summary>
        [Test]
        public void Encode_2x2Texture_CompressionFieldIsRaw()
        {
            var tex = MakeSolidTexture(2, 2, new Color32(128, 128, 128, 255));
            byte[] psd;
            try
            {
                psd = PsdEncoder.EncodeTexture(tex);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }

            Assert.AreEqual(0, ReadUInt16BE(psd, MergedImageDataOffset(psd)),
                "Merged Image Data compression must be 0 (raw)");
        }

        // ── Test 4: Pixel values — white round-trip ───────────────────────────────

        /// <summary>
        /// A 2×2 solid-white opaque texture must produce all-255 bytes in the merged RGB
        /// planes (3 planes × 4 px = 12 bytes after the compression field).
        /// </summary>
        [Test]
        public void Encode_2x2WhiteTexture_AllMergedPixelBytesAre255()
        {
            var tex = MakeSolidTexture(2, 2, new Color32(255, 255, 255, 255));
            byte[] psd;
            try
            {
                psd = PsdEncoder.EncodeTexture(tex);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }

            var pixelStart = MergedImageDataOffset(psd) + 2; // skip compression UInt16
            for (var i = pixelStart; i < psd.Length; i++)
            {
                Assert.AreEqual(255, psd[i],
                    $"Merged pixel byte at offset {i} (plane offset {i - pixelStart}) must be 255 for white");
            }
        }

        // ── Test 5: Alpha compositing over white ──────────────────────────────────

        /// <summary>
        /// A semi-transparent red pixel (255,0,0,128) composited over white should produce
        /// ~ (255, 127, 127) in the merged RGB planes. Tolerance ±2 for rounding.
        /// </summary>
        [Test]
        public void Encode_1x1SemiTransparentRed_CompositedOverWhite()
        {
            var tex = MakeSolidTexture(1, 1, new Color32(255, 0, 0, 128));
            byte[] psd;
            try
            {
                psd = PsdEncoder.EncodeTexture(tex);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }

            var pixelStart = MergedImageDataOffset(psd) + 2;
            var r = psd[pixelStart + 0];
            var g = psd[pixelStart + 1];
            var b = psd[pixelStart + 2];

            Assert.AreEqual(255, r, "R of semi-transparent red over white must be 255");
            Assert.That(g, Is.InRange(125, 129), "G of semi-transparent red over white must be ~127");
            Assert.That(b, Is.InRange(125, 129), "B of semi-transparent red over white must be ~127");
        }

        // ── Test 6: Row order (not upside-down) ───────────────────────────────────

        /// <summary>
        /// 2×2 texture: top row red, bottom row blue. The merged R plane must be 255 for the
        /// top row (first two bytes) and 0 for the bottom row — catching the Unity-bottom-up /
        /// PSD-top-down flip bug.
        /// </summary>
        [Test]
        public void Encode_2x2RedTopBlueBottom_RowOrderIsTopDown()
        {
            const int WIDTH  = 2;
            const int HEIGHT = 2;

            var tex = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, mipChain: false, linear: true);
            try
            {
                var pixels = new Color32[4];
                pixels[0] = new Color32(0, 0, 255, 255); // Unity bottom row = blue
                pixels[1] = new Color32(0, 0, 255, 255);
                pixels[2] = new Color32(255, 0, 0, 255); // Unity top row = red
                pixels[3] = new Color32(255, 0, 0, 255);
                tex.SetPixels32(pixels);
                tex.Apply();

                var psd = PsdEncoder.EncodeTexture(tex);
                var rPlaneStart = MergedImageDataOffset(psd) + 2; // R plane is first

                Assert.AreEqual(255, psd[rPlaneStart + 0], "R[0] (PSD top-left)  must be red=255");
                Assert.AreEqual(255, psd[rPlaneStart + 1], "R[1] (PSD top-right) must be red=255");
                Assert.AreEqual(  0, psd[rPlaneStart + 2], "R[2] (PSD bot-left)  must be blue=0");
                Assert.AreEqual(  0, psd[rPlaneStart + 3], "R[3] (PSD bot-right) must be blue=0");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        // ── Test 7: The fix — one unlocked layer named "Icon" ─────────────────────

        /// <summary>
        /// The core regression test for the "locked Background" fix: the Layer and Mask
        /// section must contain exactly ONE layer, named "Icon", with flags = 0 (visible,
        /// transparency NOT locked). A merged-only PSD (layer count 0) reopens as a locked
        /// Background — this asserts we no longer produce that.
        /// </summary>
        [Test]
        public void Encode_2x2Texture_EmitsSingleUnlockedLayerNamedIcon()
        {
            var tex = MakeSolidTexture(2, 2, new Color32(10, 20, 30, 255));
            byte[] psd;
            try
            {
                psd = PsdEncoder.EncodeTexture(tex);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }

            // Walk to the Layer and Mask Information section.
            var o = 26;
            o += 4 + (int)ReadUInt32BE(psd, o);   // skip Color Mode Data
            o += 4 + (int)ReadUInt32BE(psd, o);   // skip Image Resources

            var lmSectionLen = (int)ReadUInt32BE(psd, o); o += 4;
            Assert.Greater(lmSectionLen, 0, "Layer and Mask section must be non-empty (no locked Background)");

            var layerInfoLen = (int)ReadUInt32BE(psd, o); o += 4;
            Assert.Greater(layerInfoLen, 0, "Layer-info block must be non-empty");

            var layerCount = ReadInt16BE(psd, o); o += 2;
            Assert.AreEqual(1, layerCount, "Must emit exactly one layer");

            // Layer record: rect(16) + channelCount(2) + channelInfo(count*6) + blend(8)
            o += 16;
            var channelCount = ReadUInt16BE(psd, o); o += 2;
            Assert.AreEqual(4, channelCount, "Layer must declare 4 channels (A,R,G,B)");
            o += channelCount * 6;  // channel info: id(2) + length(4) each
            o += 8;                 // "8BIM" + "norm"

            var opacity  = psd[o]; o += 1;
            var clipping = psd[o]; o += 1;
            var flags    = psd[o]; o += 1;
            o += 1;                 // filler
            Assert.AreEqual(255, opacity, "Layer opacity must be 255");
            Assert.AreEqual(0, clipping, "Layer clipping must be 0 (base)");
            Assert.AreEqual(0, flags, "Layer flags must be 0 → visible + unlocked (not a locked Background)");

            var extraLen = (int)ReadUInt32BE(psd, o); o += 4;
            Assert.Greater(extraLen, 0, "Extra data (with the layer name) must be present");
            o += 4; // layer mask data length (0)
            o += 4; // layer blending ranges length (0)

            var nameLen = psd[o]; o += 1;
            var name = Encoding.ASCII.GetString(psd, o, nameLen);
            Assert.AreEqual("Icon", name, "Layer must be named 'Icon' (anything but 'Background' stays unlocked)");
        }

        // ── Test 8: Null / empty input ────────────────────────────────────────────

        [Test]
        public void Encode_NullBytes_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => PsdEncoder.Encode(null!));
        }

        [Test]
        public void Encode_EmptyBytes_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => PsdEncoder.Encode(Array.Empty<byte>()));
        }

        // ── Big-endian reader helpers (mirror PsdEncoder internals) ──────────────

        private static ushort ReadUInt16BE(byte[] data, int offset)
        {
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        private static short ReadInt16BE(byte[] data, int offset)
        {
            return (short)((data[offset] << 8) | data[offset + 1]);
        }

        private static uint ReadUInt32BE(byte[] data, int offset)
        {
            return ((uint)data[offset    ] << 24)
                 | ((uint)data[offset + 1] << 16)
                 | ((uint)data[offset + 2] <<  8)
                 |  (uint)data[offset + 3];
        }
    }
}
