using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Import setting texture cho mobile — chỉ đọc TextureImporter (KHÔNG load texture): quá cỡ, Read/Write,
    ///     sprite bật mipmap, định dạng không nén trên Android/iOS, NPOT không nén được, ước lượng bộ nhớ GPU.
    /// </summary>
    public sealed class TextureRule : IStaticRule
    {
        const string ISSUE_CATEGORY = "Asset.Texture";
        const double MIPMAP_FACTOR = 4d / 3d;
        const double BITS_PER_BYTE = 8d;
        const float BPP_UNCOMPRESSED = 32f;
        const float BPP_COMPRESSED_DEFAULT = 8f;
        const float BPP_COMPRESSED_LOW = 4f;
        const int ASTC_BLOCK_BITS = 128;
        const int COMPRESSION_BLOCK = 4;
        const int MAX_NPOT_LISTED = 30;

        static readonly Regex ASTC_BLOCK = new(@"(\d+)x(\d+)", RegexOptions.Compiled);

        /// <summary>Bit/pixel theo tên TextureImporterFormat (tra bằng tên để không phụ thuộc enum bị đổi/obsolete).</summary>
        static readonly Dictionary<string, float> BITS_PER_PIXEL = new(StringComparer.Ordinal)
        {
            { "Alpha8", 8 }, { "R8", 8 }, { "R16", 16 }, { "RG16", 16 }, { "RG32", 32 }, { "RHalf", 16 },
            { "RGHalf", 32 }, { "RFloat", 32 }, { "RGFloat", 64 }, { "RGB16", 16 }, { "ARGB16", 16 },
            { "RGBA16", 16 }, { "RGB24", 24 }, { "RGBA32", 32 }, { "ARGB32", 32 }, { "RGB48", 48 },
            { "RGBA64", 64 }, { "RGBAHalf", 64 }, { "RGBAFloat", 128 }, { "RGB9E5", 32 },
            { "DXT1", 4 }, { "DXT1Crunched", 4 }, { "DXT5", 8 }, { "DXT5Crunched", 8 }, { "BC4", 4 }, { "BC5", 8 },
            { "BC6H", 8 }, { "BC7", 8 }, { "ETC_RGB4", 4 }, { "ETC_RGB4Crunched", 4 }, { "ETC2_RGB4", 4 },
            { "ETC2_RGB4_PUNCHTHROUGH_ALPHA", 4 }, { "ETC2_RGBA8", 8 }, { "ETC2_RGBA8Crunched", 8 }, { "EAC_R", 4 },
            { "EAC_RG", 8 }, { "PVRTC_RGB2", 2 }, { "PVRTC_RGBA2", 2 }, { "PVRTC_RGB4", 4 }, { "PVRTC_RGBA4", 4 },
            { "ATC_RGB4", 4 }, { "ATC_RGBA8", 8 }
        };

        static readonly HashSet<string> UNCOMPRESSED_OVERRIDE_FORMATS = new(StringComparer.Ordinal)
            { "RGBA32", "ARGB32", "RGB24" };

        public string Id => "textures";
        public string Name => "Texture nặng / sai import setting";

        public string Description =>
            "Texture vượt kích thước tối đa, bật Read/Write, sprite bật mipmap, định dạng không nén trên Android/iOS, " +
            "NPOT không nén được, và ước lượng bộ nhớ GPU từng texture vượt ngưỡng.";

        public string Category => "Asset";
        public int Order => 40;

        struct PlatformView
        {
            public bool Overridden;
            public int MaxSize;
            public TextureImporterFormat Format;
            public TextureImporterCompression Compression;
        }

        public async Task Run(AutoTestContext ctx)
        {
            var cfg = ctx.Config.staticCheck;
            var sink = new AssetIssueSink(ctx, ISSUE_CATEGORY);
            var androidName = NamedBuildTarget.Android.TargetName;
            var iosName = NamedBuildTarget.iOS.TargetName;
            var paths = StaticCheckUtil.FindAssets("t:Texture", cfg);
            var scanned = 0;
            var totalMb = 0d;
            var npot = new List<string>();
            for (var i = 0; i < paths.Count; i++)
            {
                await StaticCheckUtil.Yield(ctx, i, paths.Count, "Quét texture");
                var path = paths[i];
                try
                {
                    if (!(AssetImporter.GetAtPath(path) is TextureImporter imp)) continue;
                    imp.GetSourceTextureWidthAndHeight(out var w, out var h);
                    if (w <= 0 || h <= 0) continue;
                    scanned++;
                    totalMb += CheckTexture(path, imp, w, h, androidName, iosName, cfg, sink, npot);
                }
                catch (Exception e)
                {
                    sink.ReportUnreadable(path, e);
                }
            }

            if (npot.Count > 0)
                ctx.Report(Severity.Info, ISSUE_CATEGORY, "Texture NPOT có thể không nén được trên mobile",
                    $"{npot.Count} texture (không phải sprite) kích thước không phải lũy thừa 2, không chia hết cho " +
                    $"{COMPRESSION_BLOCK}, tắt 'Non-Power of 2' scale và không override Android/iOS → trên máy chỉ hỗ trợ " +
                    "ETC2/PVRTC sẽ rơi về định dạng không nén (tốn RAM gấp 4–8 lần). Resize ảnh gốc, bật NPOT scale " +
                    "hoặc override ASTC:" + AssetRuleUtil.BulletList(npot, MAX_NPOT_LISTED));

            ctx.Metric("Texture đã quét", scanned);
            ctx.Metric("Tổng bộ nhớ texture ước tính", totalMb, "MB");
            sink.Flush();
        }

        /// <summary>Kiểm tra một texture, trả bộ nhớ ước tính (MB, lấy nền tảng mobile nặng nhất).</summary>
        static double CheckTexture(string path, TextureImporter imp, int w, int h, string androidName, string iosName,
            StaticCheckConfig cfg, AssetIssueSink sink, List<string> npot)
        {
            var def = imp.GetDefaultPlatformTextureSettings();
            var defaultView = new PlatformView
            {
                Overridden = false, MaxSize = imp.maxTextureSize, Format = def.format,
                Compression = imp.textureCompression
            };
            var android = ViewOf(imp, androidName, defaultView);
            var ios = ViewOf(imp, iosName, defaultView);
            var isSprite = imp.textureType == TextureImporterType.Sprite;
            var size = $"{w}×{h}";

            // 1. Quá cỡ: ảnh gốc lớn hơn ngưỡng VÀ Max Size còn cho phép import ở cỡ lớn hơn ngưỡng.
            var limit = cfg.maxTextureSize;
            var largestMax = Math.Max(defaultView.MaxSize, Math.Max(android.MaxSize, ios.MaxSize));
            if (Math.Max(w, h) > limit && largestMax > limit)
                sink.Report(Severity.Minor, "Texture quá lớn",
                    $"Ảnh gốc {size}, Max Size: Default {defaultView.MaxSize}, Android {android.MaxSize}, iOS {ios.MaxSize}. " +
                    $"Giảm Max Size xuống ≤ {limit} (hoặc resize ảnh gốc) để giảm RAM/dung lượng build.",
                    path, null, $"≤ {limit}px", $"{size}, Max Size {largestMax}");

            // 2. Read/Write giữ thêm một bản trên RAM CPU.
            if (imp.isReadable)
                sink.Report(Severity.Minor, "Read/Write bật (gấp đôi RAM)",
                    "Texture bật Read/Write → Unity giữ thêm một bản pixel trên RAM CPU. Chỉ bật khi code thật sự " +
                    "GetPixels/SetPixels texture này.", path, null, "Read/Write tắt", "Read/Write bật");

            // 3. Sprite UI không cần mipmap.
            if (isSprite && imp.mipmapEnabled)
                sink.Report(Severity.Minor, "Sprite bật mipmap",
                    "Sprite bật Generate Mip Maps → tốn thêm ~33% bộ nhớ và có thể mờ trên UI. Tắt trừ khi sprite " +
                    "được vẽ thu nhỏ nhiều trong world-space.", path, null, "Mipmap tắt", "Mipmap bật");

            // 4. NPOT không nén được (gộp thành 1 Info cuối luật).
            if (!isSprite && imp.npotScale == TextureImporterNPOTScale.None && !(IsPot(w) && IsPot(h)) &&
                (w % COMPRESSION_BLOCK != 0 || h % COMPRESSION_BLOCK != 0) && !android.Overridden && !ios.Overridden &&
                imp.textureCompression != TextureImporterCompression.Uncompressed)
                npot.Add($"{path} ({size})");

            // 5. Override mobile để định dạng không nén.
            var uncompressed = new List<string>();
            if (android.Overridden && UNCOMPRESSED_OVERRIDE_FORMATS.Contains(android.Format.ToString()))
                uncompressed.Add($"Android: {android.Format}");
            if (ios.Overridden && UNCOMPRESSED_OVERRIDE_FORMATS.Contains(ios.Format.ToString()))
                uncompressed.Add($"iOS: {ios.Format}");
            if (uncompressed.Count > 0)
                sink.Report(Severity.Minor, "Định dạng texture không nén trên mobile",
                    "Override nền tảng đặt định dạng không nén (" + string.Join(", ", uncompressed) + ") → tốn RAM/GPU " +
                    "gấp 4–8 lần ASTC/ETC2. Đổi sang ASTC (hoặc Automatic) trừ khi cần pixel-perfect.",
                    path, null, "Định dạng nén (ASTC/ETC2)", string.Join(", ", uncompressed));

            // 6. Ước lượng bộ nhớ GPU ở cỡ import thực tế, lấy nền tảng mobile nặng nhất.
            var androidMb = EstimateMb(w, h, android, imp, isSprite);
            var iosMb = EstimateMb(w, h, ios, imp, isSprite);
            var mb = Math.Max(androidMb, iosMb);
            if (mb > cfg.maxTextureMemoryMb)
                sink.Report(Severity.Major, "Texture tốn quá nhiều bộ nhớ GPU",
                    $"Ước tính {Fmt(mb)} MB (Android {Fmt(androidMb)} MB, iOS {Fmt(iosMb)} MB; ảnh gốc {size}, " +
                    $"mipmap {(imp.mipmapEnabled ? "bật" : "tắt")}). Giảm Max Size, dùng định dạng nén hoặc tắt mipmap.",
                    path, null, $"≤ {Fmt(cfg.maxTextureMemoryMb)} MB", $"{Fmt(mb)} MB");
            return mb;
        }

        /// <summary>Setting hiệu lực của một nền tảng: override nếu có, không thì dùng Default.</summary>
        static PlatformView ViewOf(TextureImporter imp, string platform, PlatformView fallback)
        {
            var s = imp.GetPlatformTextureSettings(platform);
            if (s == null || !s.overridden) return fallback;

            return new PlatformView
            {
                Overridden = true, MaxSize = s.maxTextureSize, Format = s.format,
                Compression = s.textureCompression
            };
        }

        static double EstimateMb(int w, int h, PlatformView view, TextureImporter imp, bool isSprite)
        {
            if (!isSprite && imp.npotScale != TextureImporterNPOTScale.None && !(IsPot(w) && IsPot(h)))
            {
                w = ToPot(w, imp.npotScale);
                h = ToPot(h, imp.npotScale);
            }

            var largest = Math.Max(w, h);
            if (view.MaxSize > 0 && largest > view.MaxSize)
            {
                var scale = (double)view.MaxSize / largest;
                w = Math.Max(1, (int)Math.Round(w * scale));
                h = Math.Max(1, (int)Math.Round(h * scale));
            }

            var bytes = (double)w * h * BitsPerPixel(view) / BITS_PER_BYTE;
            if (imp.mipmapEnabled) bytes *= MIPMAP_FACTOR;
            return bytes / AssetRuleUtil.BYTES_PER_MB;
        }

        static float BitsPerPixel(PlatformView view)
        {
            var name = view.Format.ToString();
            if (name.StartsWith("Automatic", StringComparison.Ordinal))
                switch (view.Compression)
                {
                    case TextureImporterCompression.Uncompressed: return BPP_UNCOMPRESSED;
                    case TextureImporterCompression.CompressedLQ: return BPP_COMPRESSED_LOW;
                    default: return BPP_COMPRESSED_DEFAULT;
                }

            if (name.Contains("ASTC"))
            {
                var m = ASTC_BLOCK.Match(name);
                if (m.Success)
                {
                    var bx = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    var by = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    if (bx > 0 && by > 0) return (float)ASTC_BLOCK_BITS / (bx * by);
                }

                return BPP_COMPRESSED_DEFAULT;
            }

            var key = name.EndsWith("_SIGNED", StringComparison.Ordinal)
                ? name.Substring(0, name.Length - "_SIGNED".Length)
                : name;
            return BITS_PER_PIXEL.TryGetValue(key, out var bpp) ? bpp : BPP_UNCOMPRESSED;
        }

        static bool IsPot(int v)
        {
            return v > 0 && (v & (v - 1)) == 0;
        }

        static int ToPot(int v, TextureImporterNPOTScale mode)
        {
            var larger = 1;
            while (larger < v) larger <<= 1;
            var smaller = larger == v ? v : larger >> 1;
            switch (mode)
            {
                case TextureImporterNPOTScale.ToLarger: return larger;
                case TextureImporterNPOTScale.ToSmaller: return Math.Max(1, smaller);
                default: return larger - v <= v - smaller ? larger : Math.Max(1, smaller);
            }
        }

        static string Fmt(double v)
        {
            return v.ToString("0.#", CultureInfo.InvariantCulture);
        }
    }
}
