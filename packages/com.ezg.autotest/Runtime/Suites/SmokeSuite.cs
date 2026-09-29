using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Smoke PlayMode: game boot được, MỌI màn hình mở/đóng được không lỗi, từng scene load được, tổng số lỗi
    ///     log trong phiên. Đây là "cửa chặn" nhanh nhất để biết build có chơi được không.
    /// </summary>
    public sealed class SmokeSuite : AutoTestSuite
    {
        const float MAX_BOOT_SECONDS = 25f;
        const float CLOSE_CHECK_SECONDS = 3f;
        const int MAX_UNIQUE_ERRORS_LISTED = 40;
        const float MIN_ROOT_SIZE_PX = 2f;

        public override string Id => "smoke";
        public override string DisplayName => "Smoke PlayMode";

        public override string Description =>
            "Vào Play từ scene boot: game khởi động được và kịp sẵn sàng, mở lần lượt từng màn hình " +
            "(đo thời gian mở, chụp ảnh, bắt exception/error log, kiểm tra đóng lại được), load từng scene trong " +
            "Build Settings, tổng hợp lỗi log cả phiên.";

        public override ExecutionMode Mode => ExecutionMode.Play;
        public override int Order => 20;
        public override string Icon => "d_PlayButton";
        public override bool MutatesPlayerData => true;

        public override IEnumerable<AutoTestCase> BuildCases(AutoTestBuildContext ctx)
        {
            yield return new AutoTestCase("boot", "Khởi động game",
                "Từ scene boot tới lúc game sẵn sàng thao tác: thời gian boot, lỗi log trong lúc boot, ảnh màn hình chính.",
                Boot, "Boot", ctx.Config.general.bootTimeoutSeconds + 60);

            foreach (var f in GameFlow.TestableFeatures(ctx.Config, ctx.Game, null))
            {
                var feature = f;
                yield return new AutoTestCase("screen-" + f.Name, "Mở màn " + f.Name,
                    $"Mở {f.Name} qua UIManager, chờ ổn định, chụp ảnh, kiểm tra hiển thị + lỗi log, đóng lại.",
                    c => OpenScreen(c, feature), "Màn hình");
            }

            if (ctx.Config.smoke.loadEveryBuildScene)
                yield return new AutoTestCase("scenes", "Load từng scene",
                    "Load lần lượt mọi scene đang bật trong Build Settings (ngoài scene boot), đo thời gian, bắt lỗi. " +
                    "Chạy cuối vì load scene ngoài luồng game có thể làm lệch state.",
                    LoadScenes, "Scene", 300);

            yield return new AutoTestCase("error-budget", "Tổng hợp lỗi cả phiên",
                "Đếm exception / error log trong toàn bộ phiên Play (kể cả lỗi không gắn với case nào).",
                ErrorBudget, "Tổng hợp");
        }

        static async Task Boot(AutoTestContext ctx)
        {
            var caseCursor = AutoTestLogCapture.Cursor;
            await GameFlow.EnsureReady(ctx);
            // Log phát sinh trước khi case bắt đầu (lúc scene boot load) cũng thuộc về boot.
            AutoTestExecutor.CollectLogs(ctx.Result, ctx.Config, ctx.Session.SessionLogCursor, caseCursor,
                AutoTestClock.Now);

            var boot = GameFlow.BootSeconds(ctx.Session);
            if (boot > 0) ctx.Metric("Thời gian boot", boot, "s", MAX_BOOT_SECONDS, null, Severity.Minor);
            ctx.Metric("Màn nền sau boot", GameFlow.Baseline(ctx.Session).Count, "màn");
            ctx.Log($"Scene sau boot: {SceneManager.GetActiveScene().name}");
            await ctx.Screenshot("home");
        }

        static async Task OpenScreen(AutoTestContext ctx, AutoTestFeature feature)
        {
            await GameFlow.EnsureReady(ctx);
            var wasBaseline = GameFlow.IsBaseline(ctx.Session, feature);
            try
            {
                GameFlow.OpenResult open;
                using (ctx.Step($"Mở màn {feature.Name}"))
                {
                    open = await GameFlow.OpenFeature(ctx, feature);
                }

                if (open.Error != null)
                {
                    var noPrefab = open.Error.Contains("null");
                    ctx.Report(noPrefab ? Severity.Minor : Severity.Major,
                        noPrefab ? "Feature.NoPrefab" : "Feature.OpenFailed",
                        noPrefab ? $"Màn {feature.Name} không mở được (thiếu prefab / 'coming soon')" : $"Mở màn {feature.Name} lỗi",
                        open.Error, feature.Name);
                    await ctx.Screenshot("open_failed");
                    return;
                }

                ctx.Metric("Thời gian mở", open.OpenMs, "ms", ctx.Config.smoke.slowOpenMs, null, Severity.Minor);

                using (ctx.Step("Kiểm tra hiển thị"))
                {
                    var root = open.Root;
                    ctx.Check(root != null && root.activeInHierarchy, $"Màn {feature.Name} không active sau khi mở",
                        null, Severity.Major, feature.Name);
                    if (root != null && root.transform is RectTransform rt)
                    {
                        var size = rt.rect.size;
                        ctx.Check(size.x >= MIN_ROOT_SIZE_PX && size.y >= MIN_ROOT_SIZE_PX,
                            $"Màn {feature.Name} có kích thước 0", $"rect = {size}", Severity.Major, feature.Name);
                    }

                    if (root != null)
                    {
                        var canvas = root.GetComponentInParent<Canvas>();
                        ctx.Check(canvas != null, $"Màn {feature.Name} không nằm dưới Canvas nào", null, Severity.Major,
                            feature.Name);
                        ctx.Metric("Graphic đang bật", root.GetComponentsInChildren<UnityEngine.UI.Graphic>().Length, "");
                    }

                    await ctx.Screenshot(feature.Name);
                }

                if (!wasBaseline && !open.WasAlreadyOpen && ctx.Config.smoke.closeScreenAfterOpen)
                    using (ctx.Step($"Đóng màn {feature.Name}"))
                    {
                        var closed = await GameFlow.CloseFeature(ctx, feature);
                        ctx.Check(closed, $"Màn {feature.Name} không đóng được",
                            $"Vẫn còn hiển thị sau {CLOSE_CHECK_SECONDS:0}s kể từ khi gọi đóng.", Severity.Minor,
                            feature.Name);
                    }
            }
            finally
            {
                await GameFlow.ReturnToBaseline(ctx);
            }
        }

        static async Task LoadScenes(AutoTestContext ctx)
        {
            await GameFlow.EnsureReady(ctx);
            // Load scene ngoài luồng game dễ sinh lỗi "dương tính giả" → hạ severity lỗi log xuống Minor.
            ctx.LogSeverityCap = Severity.Minor;
            var boot = ctx.Config.general.bootScenePath;
            var count = SceneManager.sceneCountInBuildSettings;
            for (var i = 0; i < count; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.IsNullOrEmpty(path)) continue;
                if (i == 0 && string.IsNullOrEmpty(boot) || path == boot) continue;
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                using (ctx.Step($"Load scene {name}"))
                {
                    var start = AutoTestClock.Now;
                    var op = SceneManager.LoadSceneAsync(i, LoadSceneMode.Single);
                    if (op == null)
                    {
                        ctx.Report(Severity.Major, "Scene.Load", $"Không load được scene {name}", null, path);
                        continue;
                    }

                    var loaded = await ctx.WaitUntil(() => op.isDone, ctx.Config.general.bootTimeoutSeconds,
                        $"load scene {name}", false);
                    if (!loaded)
                    {
                        ctx.Report(Severity.Major, "Scene.Timeout", $"Scene {name} load quá lâu", null, path);
                        continue;
                    }

                    ctx.Metric($"Load {name}", (AutoTestClock.Now - start) * 1000, "ms");
                    await ctx.WaitSeconds(ctx.Config.smoke.sceneSettleSeconds);
                    ctx.Check(Camera.main != null || UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length > 0,
                        $"Scene {name} không có camera nào", null, Severity.Minor, path);
                    await ctx.Screenshot("scene_" + name);
                }
            }
        }

        static Task ErrorBudget(AutoTestContext ctx)
        {
            var entries = AutoTestLogCapture.Since(ctx.Session.SessionLogCursor);
            var exceptions = 0;
            var errors = 0;
            var warnings = 0;
            var unique = new Dictionary<string, int>();
            foreach (var e in entries)
            {
                if (AutoTestLogCapture.IsIgnored(e.Message)) continue;
                if (e.Type == LogType.Warning)
                {
                    warnings++;
                    continue;
                }

                if (!AutoTestLogCapture.IsErrorType(e.Type)) continue;
                if (e.Type == LogType.Exception) exceptions++;
                else errors++;
                var key = FirstLine(e.Message);
                unique[key] = unique.TryGetValue(key, out var n) ? n + 1 : 1;
            }

            ctx.Metric("Exception trong phiên", exceptions, "", 0, null, Severity.Major);
            ctx.Metric("Error log trong phiên", errors, "");
            ctx.Metric("Warning trong phiên", warnings, "");
            ctx.Metric("Lỗi khác nhau", unique.Count, "");
            if (unique.Count > 0)
            {
                var lines = unique.OrderByDescending(p => p.Value).Take(MAX_UNIQUE_ERRORS_LISTED)
                    .Select(p => $"{p.Value,4}×  {p.Key}");
                ctx.Attach("errors-summary.txt", string.Join("\n", lines), "Tổng hợp lỗi cả phiên");
            }

            return Task.CompletedTask;
        }

        static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var i = s.IndexOf('\n');
            return i < 0 ? s : s.Substring(0, i);
        }
    }
}
