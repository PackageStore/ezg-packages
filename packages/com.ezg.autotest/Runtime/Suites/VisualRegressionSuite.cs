using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Suite "Visual Regression": mở từng màn, ẩn object động (đồng hồ, bộ đếm…), chụp full độ phân giải rồi so
    ///     từng pixel với ảnh baseline. Chưa có baseline ⇒ lưu ảnh hiện tại làm baseline. Report có đủ 3 ảnh
    ///     baseline / hiện tại / diff (pixel khác tô đỏ trên nền baseline xám mờ) để QA so cạnh nhau.
    /// </summary>
    public sealed class VisualRegressionSuite : AutoTestSuite
    {
        /// <summary>Chờ thêm sau settle để animation mở màn dừng hẳn trước khi chụp.</summary>
        const float EXTRA_SETTLE_SECONDS = 0.5f;

        const string DEFAULT_BASELINE_FOLDER = "AutoTestBaselines";
        const string REPORT_FOLDER = "visual";
        const string CURRENT_SCREEN_PREFIX = "current-";
        const string KIND_CURRENT = "current";
        const string KIND_BASELINE = "baseline";
        const string KIND_DIFF = "diff";
        const string PNG_EXTENSION = ".png";

        /// <summary>Nhường một frame sau mỗi ngần này pixel để Editor còn phản hồi và nút Dừng có tác dụng.</summary>
        const int PIXELS_PER_YIELD = 500000;
        const float STABILITY_GAP_SECONDS = 0.4f;
        const float MIN_COVER_ALPHA = 0.05f;
        const float BACKDROP_SCREEN_FRACTION = 0.5f;
        const int IGNORED_DIM_DIVISOR = 4;
        const int IGNORED_BLUE_BOOST = 40;

        /// <summary>Nền diff = độ sáng baseline × 3/10 (xám tối) để pixel đỏ nổi bật.</summary>
        const int DIFF_DIM_NUMERATOR = 3;

        const int DIFF_DIM_DENOMINATOR = 10;

        // Trọng số độ sáng (Rec. 601) nhân 256: 0.299 / 0.587 / 0.114.
        const int LUMA_R = 77;
        const int LUMA_G = 150;
        const int LUMA_B = 29;
        const int LUMA_SHIFT = 8;
        const byte OPAQUE = 255;
        const double PERCENT = 100.0;

        // PNG: chữ ký 8 byte + IHDR; width/height big-endian ở byte 16–23.
        const int PNG_HEADER_BYTES = 24;
        const int PNG_WIDTH_OFFSET = 16;
        const int PNG_HEIGHT_OFFSET = 20;

        static readonly Color32 DIFF_COLOR = new(255, 0, 0, 255);
        static readonly byte[] PNG_SIGNATURE = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public override string Id => "visual";
        public override string DisplayName => "Visual Regression";

        public override string Description =>
            "Mở từng màn, ẩn object động (đồng hồ, bộ đếm…), chụp ảnh và so từng pixel với baseline đã duyệt. Khác " +
            "quá ngưỡng ⇒ lỗi kèm ảnh diff (pixel khác tô đỏ). Chưa có baseline ⇒ tự tạo từ ảnh hiện tại. Baseline " +
            "lưu theo độ phân giải trong thư mục cấu hình (commit vào git để cả team dùng chung).";

        public override ExecutionMode Mode => ExecutionMode.Play;
        public override int Order => 70;
        public override string Icon => "d_ViewToolZoom";
        public override bool MutatesPlayerData => true;

        public override IEnumerable<AutoTestCase> BuildCases(AutoTestBuildContext ctx)
        {
            return UiAuditUtil.BuildScreenCases(ctx, Describe, CompareScreen, 0, EXTRA_SETTLE_SECONDS, true);
        }

        static string Describe(AutoTestFeature feature)
        {
            return feature == null
                ? "Chụp màn hình đang hiển thị và so với baseline của scene hiện tại."
                : $"Mở màn {feature.Name}, chụp và so với baseline.";
        }

        #region API cho Editor

        /// <summary>
        ///     Thư mục gốc chứa baseline (tuyệt đối). Editor: &lt;project&gt;/&lt;baselineFolder&gt;; device:
        ///     &lt;persistentDataPath&gt;/&lt;baselineFolder&gt; (baseline riêng từng máy). Baseline thật nằm trong thư mục
        ///     con "&lt;rộng&gt;x&lt;cao&gt;".
        /// </summary>
        public static string BaselineFolder(AutoTestConfig cfg)
        {
            var folder = cfg?.visual?.baselineFolder;
            if (string.IsNullOrWhiteSpace(folder)) folder = DEFAULT_BASELINE_FOLDER;
            return Path.GetFullPath(Path.IsPathRooted(folder) ? folder : Path.Combine(StorageRoot(), folder));
        }

        /// <summary>Thư mục baseline của một độ phân giải.</summary>
        public static string BaselineFolder(AutoTestConfig cfg, int width, int height)
        {
            return Path.Combine(BaselineFolder(cfg), $"{width}x{height}");
        }

        /// <summary>
        ///     Đường dẫn baseline của một màn. <paramref name="screenName" /> = tên feature (case "screen-&lt;Feature&gt;"),
        ///     hoặc "current-&lt;Scene&gt;" với case "current-screen".
        /// </summary>
        public static string BaselinePath(AutoTestConfig cfg, string screenName, int width, int height)
        {
            return Path.Combine(BaselineFolder(cfg, width, height), FileSafeName(screenName) + PNG_EXTENSION);
        }

        /// <summary>
        ///     Chấp nhận ảnh hiện tại làm baseline mới (copy đè). <paramref name="currentPngAbsolutePath" /> là file
        ///     "visual/&lt;Tên&gt;_current.png" trong thư mục report — với màn khác baseline, file này luôn giữ full độ phân
        ///     giải. Thư mục độ phân giải lấy từ kích thước PNG. Trả false nếu không đọc/copy được.
        /// </summary>
        public static bool AcceptAsBaseline(string currentPngAbsolutePath, string featureName, AutoTestConfig cfg)
        {
            try
            {
                if (string.IsNullOrEmpty(currentPngAbsolutePath) || !File.Exists(currentPngAbsolutePath)) return false;
                if (string.IsNullOrEmpty(featureName)) featureName = ScreenNameFromReportImage(currentPngAbsolutePath);
                if (string.IsNullOrEmpty(featureName)) return false;
                if (!TryReadPngSize(currentPngAbsolutePath, out var width, out var height)) return false;
                var dest = BaselinePath(cfg, featureName, width, height);
                var dir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.Copy(currentPngAbsolutePath, dest, true);
                AutoTestLog.Info($"Đã cập nhật baseline {featureName} ({width}x{height}): {dest}");
                return true;
            }
            catch (Exception e)
            {
                AutoTestLog.Warn($"Không cập nhật được baseline {featureName}: {e.Message}");
                return false;
            }
        }

        /// <summary>Tên màn từ tên ảnh report "&lt;Tên&gt;_current|baseline|diff.png" (null nếu không đúng mẫu).</summary>
        public static string ScreenNameFromReportImage(string reportImagePath)
        {
            if (string.IsNullOrEmpty(reportImagePath)) return null;
            var name = Path.GetFileNameWithoutExtension(reportImagePath);
            foreach (var kind in new[] { KIND_CURRENT, KIND_BASELINE, KIND_DIFF })
            {
                var suffix = "_" + kind;
                if (name.EndsWith(suffix, StringComparison.Ordinal)) return name.Substring(0, name.Length - suffix.Length);
            }

            return null;
        }

        /// <summary>Đọc kích thước ảnh từ header PNG (không cần load cả ảnh).</summary>
        public static bool TryReadPngSize(string path, out int width, out int height)
        {
            width = 0;
            height = 0;
            try
            {
                var header = new byte[PNG_HEADER_BYTES];
                using (var stream = File.OpenRead(path))
                {
                    var read = 0;
                    while (read < header.Length)
                    {
                        var n = stream.Read(header, read, header.Length - read);
                        if (n <= 0) return false;
                        read += n;
                    }
                }

                for (var i = 0; i < PNG_SIGNATURE.Length; i++)
                    if (header[i] != PNG_SIGNATURE[i])
                        return false;
                width = ReadBigEndian(header, PNG_WIDTH_OFFSET);
                height = ReadBigEndian(header, PNG_HEIGHT_OFFSET);
                return width > 0 && height > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

        #region Case

        static async Task CompareScreen(AutoTestContext ctx, ScreenScope scope)
        {
            var cfg = ctx.Config.visual;
            var screenName = scope.IsCurrentScreen
                ? CURRENT_SCREEN_PREFIX + SceneManager.GetActiveScene().name
                : scope.Name;
            var hidden = new List<HiddenGraphic>();
            Texture2D current = null;
            Texture2D baseline = null;
            Texture2D diff = null;
            try
            {
                using (ctx.Step("Ẩn object động"))
                {
                    HideDynamicObjects(cfg, hidden);
                    if (hidden.Count > 0) ctx.Log($"Ẩn {hidden.Count} graphic khớp maskObjectPatterns trước khi chụp.");
                }

                Texture2D second = null;
                var isolated = new List<Canvas>();
                using (ctx.Step("Chụp màn hình (2 khung để lọc vùng động)"))
                {
                    try
                    {
                        IsolateScreen(scope.Root, isolated);
                        if (isolated.Count > 0)
                        {
                            ctx.Log($"Tạm tắt {isolated.Count} canvas khác (HUD thế giới, overlay debug…) khi chụp.");
                            await ctx.NextFrame();
                        }

                        current = await AutoTestCapture.CaptureScreen(ctx.Token);
                        await ctx.WaitSeconds(STABILITY_GAP_SECONDS);
                        second = await AutoTestCapture.CaptureScreen(ctx.Token);
                    }
                    finally
                    {
                        Restore(hidden);
                        RestoreCanvases(isolated);
                    }
                }

                if (current == null) ctx.Skip("Không chụp được màn hình (batchmode -nographics / không có GPU).");
                await ForceOpaque(ctx, current);

                // Mask so sánh = vùng UI của chính màn đang test (bỏ gameplay/HUD phía sau) ∩ pixel đứng yên giữa 2 khung
                // (bỏ animation, particle, số nhảy). Không đụng camera hay object của game.
                bool[] compareMask;
                int dynamicPixels;
                using (ctx.Step("Tính vùng so sánh"))
                {
                    compareMask = BuildCoverageMask(ctx, scope.Root, current.width, current.height);
                    dynamicPixels = second != null && second.width == current.width && second.height == current.height
                        ? await MaskUnstable(ctx, current.GetPixels32(), second.GetPixels32(), compareMask,
                            Mathf.Clamp(cfg.pixelTolerance, 0, OPAQUE))
                        : 0;
                    if (second != null) Object.Destroy(second);
                }

                var width = current.width;
                var height = current.height;
                var baselinePath = BaselinePath(ctx.Config, screenName, width, height);
                var scale = ctx.Config.general.screenshotScale;

                if (!File.Exists(baselinePath))
                {
                    using (ctx.Step("Tạo baseline mới"))
                    {
                        AutoTestCapture.SavePng(current, baselinePath, 1f);
                        var rel = SaveReportImage(ctx, current, screenName, KIND_CURRENT, scale);
                        UiAuditUtil.AttachImage(ctx, $"Baseline mới — {scope.Name}", rel);
                        ctx.Report(Severity.Info, "Visual.NewBaseline", "Tạo baseline mới",
                            $"Chưa có baseline {width}x{height} cho {scope.Name} — đã lưu ảnh hiện tại làm baseline: {baselinePath}",
                            scope.Name).screenshot = rel;
                    }

                    return;
                }

                using (ctx.Step("So sánh với baseline"))
                {
                    baseline = LoadPng(baselinePath);
                    if (baseline == null)
                    {
                        var rel = SaveReportImage(ctx, current, screenName, KIND_CURRENT, 1f);
                        UiAuditUtil.AttachImage(ctx, $"Hiện tại — {scope.Name}", rel);
                        ctx.Report(Severity.Minor, "Visual.BadBaseline", "Không đọc được ảnh baseline",
                            $"File baseline hỏng hoặc không phải PNG: {baselinePath}", scope.Name).screenshot = rel;
                        return;
                    }

                    if (baseline.width != width || baseline.height != height)
                    {
                        // Lưu ảnh hiện tại full độ phân giải để Editor chấp nhận làm baseline được.
                        var currentRel = SaveReportImage(ctx, current, screenName, KIND_CURRENT, 1f);
                        var baselineRel = SaveReportImage(ctx, baseline, screenName, KIND_BASELINE, scale);
                        UiAuditUtil.AttachImage(ctx, $"Baseline — {scope.Name}", baselineRel);
                        UiAuditUtil.AttachImage(ctx, $"Hiện tại — {scope.Name}", currentRel);
                        ctx.Report(Severity.Minor, "Visual.Resolution", "Baseline khác độ phân giải",
                            $"Baseline {baseline.width}x{baseline.height}, ảnh chụp {width}x{height} — bỏ qua so sánh. " +
                            "Chạy lại đúng độ phân giải Game view hoặc chấp nhận ảnh hiện tại làm baseline.", scope.Name,
                            null, $"{width}x{height}", $"{baseline.width}x{baseline.height}").screenshot = currentRel;
                        return;
                    }

                    var currentPixels = current.GetPixels32();
                    var baselinePixels = baseline.GetPixels32();
                    var diffPixels = new Color32[currentPixels.Length];
                    var differing = await ComputeDiff(ctx, currentPixels, baselinePixels, diffPixels,
                        Mathf.Clamp(cfg.pixelTolerance, 0, OPAQUE), compareMask);
                    var total = CountTrue(compareMask, currentPixels.Length);
                    var percent = total > 0 ? differing * PERCENT / total : 0;
                    UiAuditUtil.AddMetric(ctx, "Vùng so sánh", total * PERCENT / Math.Max(1, currentPixels.Length), "%");
                    UiAuditUtil.AddMetric(ctx, "Pixel động bỏ qua", dynamicPixels * PERCENT / Math.Max(1, currentPixels.Length),
                        "%");

                    diff = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    diff.SetPixels32(diffPixels);
                    diff.Apply(false);

                    var failed = percent > cfg.maxDiffPercent;
                    // Màn khác baseline: giữ ảnh hiện tại full độ phân giải để "chấp nhận làm baseline" không bị mờ.
                    var currentPath = SaveReportImage(ctx, current, screenName, KIND_CURRENT, failed ? 1f : scale);
                    var baselineReport = SaveReportImage(ctx, baseline, screenName, KIND_BASELINE, scale);
                    var diffPath = SaveReportImage(ctx, diff, screenName, KIND_DIFF, scale);
                    UiAuditUtil.AttachImage(ctx, $"Baseline — {scope.Name}", baselineReport);
                    UiAuditUtil.AttachImage(ctx, $"Hiện tại — {scope.Name}", currentPath);
                    UiAuditUtil.AttachImage(ctx, $"Khác biệt — {scope.Name}", diffPath);

                    UiAuditUtil.AddMetric(ctx, "Khác biệt", percent, "%", cfg.maxDiffPercent);
                    ctx.Log($"{scope.Name}: {differing}/{total} pixel khác ({UiAuditUtil.Num(percent, "0.###")}%), " +
                            $"ngưỡng {UiAuditUtil.Num(cfg.maxDiffPercent)}%.");

                    if (!failed) return;
                    var pct = UiAuditUtil.Num(percent);
                    var issue = ctx.Report(Severity.Major, "Visual.Diff", $"Giao diện khác baseline {pct}%",
                        $"{differing}/{total} pixel lệch quá {cfg.pixelTolerance}/255 ở ít nhất một kênh màu (pixel đỏ trong " +
                        "ảnh diff). Nếu thay đổi là cố ý: chấp nhận ảnh hiện tại làm baseline mới.", scope.Name, null,
                        $"≤ {UiAuditUtil.Num(cfg.maxDiffPercent)}%", $"{pct}%",
                        $"1. {scope.ReproOpenStep}\n2. Chờ màn hiển thị xong\n3. So với ảnh baseline");
                    issue.screenshot = diffPath;
                }
            }
            finally
            {
                Restore(hidden);
                if (current != null) Object.Destroy(current);
                if (baseline != null) Object.Destroy(baseline);
                if (diff != null) Object.Destroy(diff);
            }
        }

        #endregion

        #region Ẩn object động

        struct HiddenGraphic
        {
            public CanvasRenderer Renderer;
            public float Alpha;
        }

        /// <summary>Tạm đặt alpha 0 cho Graphic (trên mọi Canvas active) có tên/cha khớp maskObjectPatterns.</summary>
        static void HideDynamicObjects(VisualRegressionConfig cfg, List<HiddenGraphic> hidden)
        {
            var matcher = new AncestorNameMatcher(cfg.maskObjectPatterns);
            if (matcher.IsEmpty) return;
            var graphics = new List<Graphic>();
            foreach (var canvas in UiAuditUtil.ActiveRootCanvases())
            {
                canvas.GetComponentsInChildren(false, graphics);
                foreach (var g in graphics)
                {
                    if (g == null || !matcher.Matches(g.transform, canvas.transform)) continue;
                    var cr = g.canvasRenderer;
                    if (cr == null) continue;
                    hidden.Add(new HiddenGraphic { Renderer = cr, Alpha = cr.GetAlpha() });
                    cr.SetAlpha(0f);
                }
            }
        }

        /// <summary>
        ///     Tắt tạm (Canvas.enabled — không chạy OnDisable của game) mọi root canvas KHÁC canvas chứa màn đang test:
        ///     HUD gắn thế giới, bubble trạm, overlay debug… vẽ đè lên popup và đổi theo mỗi lượt chạy.
        /// </summary>
        static void IsolateScreen(GameObject root, List<Canvas> disabled)
        {
            if (root == null) return;
            var own = root.GetComponentInParent<Canvas>();
            var ownRoot = own != null ? own.rootCanvas : null;
            if (ownRoot == null) return;
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (canvas == null || !canvas.enabled || !canvas.isRootCanvas || canvas == ownRoot) continue;
                canvas.enabled = false;
                disabled.Add(canvas);
            }
        }

        static void RestoreCanvases(List<Canvas> disabled)
        {
            foreach (var c in disabled)
                if (c != null)
                    c.enabled = true;
            disabled.Clear();
        }

        /// <summary>Trả alpha cũ (idempotent — gọi nhiều lần an toàn).</summary>
        static void Restore(List<HiddenGraphic> hidden)
        {
            foreach (var h in hidden)
                if (h.Renderer != null)
                    h.Renderer.SetAlpha(h.Alpha);
            hidden.Clear();
        }

        #endregion

        #region Xử lý ảnh

        /// <summary>Ép alpha = 255 (backbuffer có thể có alpha rác) để PNG baseline/report hiển thị đúng.</summary>
        static async Task ForceOpaque(AutoTestContext ctx, Texture2D tex)
        {
            var pixels = tex.GetPixels32();
            for (var start = 0; start < pixels.Length; start += PIXELS_PER_YIELD)
            {
                var end = Math.Min(pixels.Length, start + PIXELS_PER_YIELD);
                for (var i = start; i < end; i++) pixels[i].a = OPAQUE;
                if (end < pixels.Length) await YieldFrame(ctx);
            }

            tex.SetPixels32(pixels);
            tex.Apply(false);
        }

        /// <summary>
        ///     So từng pixel (RGB): lệch &gt; <paramref name="tolerance" /> ở một kênh ⇒ khác, tô đỏ; còn lại = baseline xám
        ///     tối. Trả số pixel khác.
        /// </summary>
        static async Task<int> ComputeDiff(AutoTestContext ctx, Color32[] current, Color32[] baseline, Color32[] output,
            int tolerance, bool[] mask)
        {
            var total = Math.Min(current.Length, baseline.Length);
            var differing = 0;
            for (var start = 0; start < total; start += PIXELS_PER_YIELD)
            {
                var end = Math.Min(total, start + PIXELS_PER_YIELD);
                for (var i = start; i < end; i++)
                {
                    var a = current[i];
                    var b = baseline[i];
                    if (mask != null && !mask[i])
                    {
                        // Ngoài vùng so sánh: tô xanh tối để QA thấy phần nào bị bỏ qua.
                        var q = (byte)(((b.r * LUMA_R + b.g * LUMA_G + b.b * LUMA_B) >> LUMA_SHIFT) / IGNORED_DIM_DIVISOR);
                        output[i] = new Color32(0, q, (byte)Math.Min(OPAQUE, q + IGNORED_BLUE_BOOST), OPAQUE);
                        continue;
                    }

                    var dr = a.r - b.r;
                    var dg = a.g - b.g;
                    var db = a.b - b.b;
                    if (dr < 0) dr = -dr;
                    if (dg < 0) dg = -dg;
                    if (db < 0) db = -db;
                    if (dr > tolerance || dg > tolerance || db > tolerance)
                    {
                        differing++;
                        output[i] = DIFF_COLOR;
                        continue;
                    }

                    var luma = (b.r * LUMA_R + b.g * LUMA_G + b.b * LUMA_B) >> LUMA_SHIFT;
                    var v = (byte)(luma * DIFF_DIM_NUMERATOR / DIFF_DIM_DENOMINATOR);
                    output[i] = new Color32(v, v, v, OPAQUE);
                }

                if (end < total) await YieldFrame(ctx);
            }

            return differing;
        }

        /// <summary>
        ///     Vùng UI của màn đang test: hợp các hình chữ nhật Graphic đang hiển thị dưới root, bỏ Graphic phủ ≥ nửa màn
        ///     (lớp nền mờ / nền toàn màn làm lộ gameplay phía sau). root null / không có Graphic ⇒ so toàn ảnh.
        /// </summary>
        static bool[] BuildCoverageMask(AutoTestContext ctx, GameObject root, int width, int height)
        {
            var mask = new bool[width * height];
            if (root == null) return Fill(mask);
            var screenArea = (float)width * height;
            var scaleX = width / (float)Mathf.Max(1, Screen.width);
            var scaleY = height / (float)Mathf.Max(1, Screen.height);
            var any = false;
            foreach (var g in root.GetComponentsInChildren<Graphic>(false))
            {
                if (g == null || !g.enabled || g.color.a < MIN_COVER_ALPHA) continue;
                if (UiDriver.EffectiveAlpha(g.transform) < MIN_COVER_ALPHA) continue;
                var r = ctx.Ui.GetScreenRect(g.rectTransform);
                var xMin = Mathf.Clamp(Mathf.FloorToInt(r.xMin * scaleX), 0, width);
                var xMax = Mathf.Clamp(Mathf.CeilToInt(r.xMax * scaleX), 0, width);
                var yMin = Mathf.Clamp(Mathf.FloorToInt(r.yMin * scaleY), 0, height);
                var yMax = Mathf.Clamp(Mathf.CeilToInt(r.yMax * scaleY), 0, height);
                if (xMax <= xMin || yMax <= yMin) continue;
                if ((xMax - xMin) * (float)(yMax - yMin) >= screenArea * BACKDROP_SCREEN_FRACTION) continue;
                any = true;
                for (var y = yMin; y < yMax; y++)
                {
                    var row = y * width;
                    for (var x = xMin; x < xMax; x++) mask[row + x] = true;
                }
            }

            return any ? mask : Fill(mask);
        }

        static bool[] Fill(bool[] mask)
        {
            for (var i = 0; i < mask.Length; i++) mask[i] = true;
            return mask;
        }

        /// <summary>Bỏ khỏi mask các pixel đổi màu giữa 2 khung (animation/particle). Trả số pixel động đã bỏ.</summary>
        static async Task<int> MaskUnstable(AutoTestContext ctx, Color32[] a, Color32[] b, bool[] mask, int tolerance)
        {
            var total = Math.Min(Math.Min(a.Length, b.Length), mask.Length);
            var removed = 0;
            for (var start = 0; start < total; start += PIXELS_PER_YIELD)
            {
                var end = Math.Min(total, start + PIXELS_PER_YIELD);
                for (var i = start; i < end; i++)
                {
                    if (!mask[i]) continue;
                    var p = a[i];
                    var q = b[i];
                    if (Math.Abs(p.r - q.r) > tolerance || Math.Abs(p.g - q.g) > tolerance ||
                        Math.Abs(p.b - q.b) > tolerance)
                    {
                        mask[i] = false;
                        removed++;
                    }
                }

                if (end < total) await YieldFrame(ctx);
            }

            return removed;
        }

        static int CountTrue(bool[] mask, int fallback)
        {
            if (mask == null) return fallback;
            var n = 0;
            foreach (var m in mask)
                if (m)
                    n++;
            return n;
        }

        static async Task YieldFrame(AutoTestContext ctx)
        {
            ctx.ThrowIfCancelled();
            await ctx.NextFrame();
        }

        static Texture2D LoadPng(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(bytes, false)) return tex;
                Object.Destroy(tex);
            }
            catch (Exception e)
            {
                AutoTestLog.Warn($"Không đọc được baseline {path}: {e.Message}");
            }

            return null;
        }

        /// <summary>Lưu ảnh vào &lt;report&gt;/visual/&lt;Tên&gt;_&lt;kind&gt;.png, trả đường dẫn tương đối (null nếu lỗi).</summary>
        static string SaveReportImage(AutoTestContext ctx, Texture2D tex, string screenName, string kind, float scale)
        {
            var outputDir = ctx.Session.OutputDir;
            if (tex == null || string.IsNullOrEmpty(outputDir)) return null;
            try
            {
                var rel = $"{REPORT_FOLDER}/{FileSafeName(screenName)}_{kind}{PNG_EXTENSION}";
                AutoTestCapture.SavePng(tex, Path.Combine(outputDir, rel), scale);
                return rel;
            }
            catch (Exception e)
            {
                AutoTestLog.Warn($"Không lưu được ảnh {kind} của {screenName}: {e.Message}");
                return null;
            }
        }

        static string FileSafeName(string name)
        {
            return AutoTestContext.Sanitize(name);
        }

        static string StorageRoot()
        {
            if (!Application.isEditor) return Application.persistentDataPath;
            var parent = Directory.GetParent(Application.dataPath);
            return parent != null ? parent.FullName : Application.dataPath;
        }

        static int ReadBigEndian(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
        }

        #endregion
    }
}
