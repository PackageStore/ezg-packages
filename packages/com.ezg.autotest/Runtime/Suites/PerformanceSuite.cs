using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Performance: FPS / frame time (p50–p99, hitch), GC alloc mỗi frame, bộ nhớ, draw call, chi phí mở từng
    ///     màn hình và nghi vấn rò bộ nhớ khi mở/đóng lặp lại. Trong Editor số đo chỉ mang tính TƯƠNG ĐỐI (có overhead
    ///     Editor) nên vượt ngưỡng chỉ là Minor; trên device là Major.
    /// </summary>
    public sealed class PerformanceSuite : AutoTestSuite
    {
        const double BYTES_PER_MB = 1024.0 * 1024.0;
        const double BYTES_PER_KB = 1024.0;
        const float SCREEN_COST_WINDOW_SECONDS = 1.5f;
        const int LEAK_CYCLES = 3;
        const int LEAK_OBJECT_GROWTH_THRESHOLD = 20;
        const double LEAK_MEMORY_GROWTH_MB = 8;
        const int TOP_SLOWEST = 5;
        const double TARGET_FPS_TOLERANCE = 0.9;
        const double P95_BUDGET_FACTOR = 1.25;
        const double P99_BUDGET_FACTOR = 1.6;

        public override string Id => "performance";
        public override string DisplayName => "Performance";

        public override string Description =>
            "Đo FPS, frame time p50/p95/p99, hitch, GC alloc/frame, bộ nhớ, draw call/SetPass ở màn chính; chi phí " +
            "mở từng màn hình (ms, GC, frame spike); mở/đóng lặp lại để phát hiện rò bộ nhớ/object. " +
            "Số đo trong Editor chỉ để so sánh tương đối giữa các lần chạy — số chuẩn lấy từ Device / E2E.";

        public override ExecutionMode Mode => ExecutionMode.Play;
        public override int Order => 60;
        public override string Icon => "d_Profiler.CPU";
        public override bool MutatesPlayerData => true;

        public override IEnumerable<AutoTestCase> BuildCases(AutoTestBuildContext ctx)
        {
            var p = ctx.Config.performance;
            yield return new AutoTestCase("idle", "Đứng yên ở màn chính",
                $"Warmup {p.warmupSeconds:0}s rồi lấy mẫu {p.sampleSeconds:0}s: FPS, frame time, GC, bộ nhớ, draw call.",
                Idle, "Frame", p.warmupSeconds + p.sampleSeconds + 60);
            yield return new AutoTestCase("screen-cost", "Chi phí mở từng màn hình",
                "Mở từng màn: thời gian mở, GC alloc và frame dài nhất trong 1.5s sau khi mở.", ScreenCost, "Màn hình",
                600);
            yield return new AutoTestCase("leak-check", "Rò bộ nhớ khi mở/đóng lặp",
                $"Mở/đóng mỗi màn {LEAK_CYCLES} lần, so số object + bộ nhớ trước/sau (đã GC).", LeakCheck, "Bộ nhớ", 900);
        }

        static Severity PerfSeverity(AutoTestContext ctx)
        {
            return ctx.IsDevice ? Severity.Major : Severity.Minor;
        }

        #region Idle

        static async Task Idle(AutoTestContext ctx)
        {
            await GameFlow.EnsureReady(ctx);
            await GameFlow.ReturnToBaseline(ctx);
            var p = ctx.Config.performance;
            using (ctx.Step($"Warmup {p.warmupSeconds:0}s"))
            {
                await ctx.WaitSeconds(p.warmupSeconds);
            }

            using var gcRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            using var drawRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            using var setPassRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            using var batchRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            using var triRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");

            var frames = new List<double>();
            var gc = new List<double>();
            var draws = new List<double>();
            var setPass = new List<double>();
            var batches = new List<double>();
            var tris = new List<double>();
            var gcCountStart = GC.CollectionCount(0);
            using (ctx.Step($"Lấy mẫu {p.sampleSeconds:0}s"))
            {
                var end = AutoTestClock.Now + p.sampleSeconds;
                while (AutoTestClock.Now < end)
                {
                    await ctx.NextFrame();
                    frames.Add(Time.unscaledDeltaTime * 1000.0);
                    if (gcRec.Valid) gc.Add(gcRec.LastValue / BYTES_PER_KB);
                    if (drawRec.Valid) draws.Add(drawRec.LastValue);
                    if (setPassRec.Valid) setPass.Add(setPassRec.LastValue);
                    if (batchRec.Valid) batches.Add(batchRec.LastValue);
                    if (triRec.Valid) tris.Add(triRec.LastValue);
                }
            }

            if (frames.Count == 0) ctx.Fail("Không lấy được frame nào.");
            frames.RemoveAt(0); // frame đầu còn dính thời gian chờ trước đó
            var sev = PerfSeverity(ctx);
            var sorted = frames.OrderBy(f => f).ToList();
            var avg = frames.Average();
            // Game tự khoá FPS (Application.targetFrameRate) thì đo theo mục tiêu của game, không theo cấu hình chung.
            var target = Application.targetFrameRate > 0 ? Math.Min(Application.targetFrameRate, p.targetFps) : p.targetFps;
            var frameBudgetMs = 1000.0 / Math.Max(1, target);
            ctx.Metric("FPS mục tiêu", target, "fps");
            ctx.Metric("FPS trung bình", 1000.0 / avg, "fps", null, target * TARGET_FPS_TOLERANCE, sev);
            ctx.Metric("FPS 1% thấp", 1000.0 / Percentile(sorted, 0.99), "fps");
            ctx.Metric("Frame p50", Percentile(sorted, 0.5), "ms");
            ctx.Metric("Frame p95", Percentile(sorted, 0.95), "ms",
                Math.Max(p.maxP95FrameMs, frameBudgetMs * P95_BUDGET_FACTOR), null, sev);
            ctx.Metric("Frame p99", Percentile(sorted, 0.99), "ms",
                Math.Max(p.maxP99FrameMs, frameBudgetMs * P99_BUDGET_FACTOR), null, sev);
            ctx.Metric("Frame dài nhất", sorted[sorted.Count - 1], "ms");
            ctx.Metric("Hitch (> " + p.hitchThresholdMs.ToString("0", CultureInfo.InvariantCulture) + "ms)",
                frames.Count(f => f > p.hitchThresholdMs), "lần", p.maxHitches, null, sev);
            if (gc.Count > 0)
            {
                ctx.Metric("GC alloc / frame (TB)", gc.Average(), "KB", p.maxGcAllocPerFrameKb, null, sev);
                ctx.Metric("GC alloc / frame (max)", gc.Max(), "KB");
            }

            ctx.Metric("GC collections", GC.CollectionCount(0) - gcCountStart, "lần");
            if (draws.Count > 0) ctx.Metric("Draw calls (TB)", draws.Average(), "", p.maxDrawCalls, null, sev);
            if (setPass.Count > 0) ctx.Metric("SetPass calls (TB)", setPass.Average(), "", p.maxSetPassCalls, null, sev);
            if (batches.Count > 0) ctx.Metric("Batches (TB)", batches.Average(), "");
            if (tris.Count > 0) ctx.Metric("Triangles (TB)", tris.Average(), "");
            ctx.Metric("Bộ nhớ đã cấp (tổng)", Profiler.GetTotalAllocatedMemoryLong() / BYTES_PER_MB, "MB",
                p.maxTotalMemoryMb, null, sev);
            ctx.Metric("Bộ nhớ dự trữ", Profiler.GetTotalReservedMemoryLong() / BYTES_PER_MB, "MB");
            ctx.Metric("Mono heap đang dùng", Profiler.GetMonoUsedSizeLong() / BYTES_PER_MB, "MB");
            ctx.Metric("Graphics driver", Profiler.GetAllocatedMemoryForGraphicsDriver() / BYTES_PER_MB, "MB");
            if (!ctx.IsDevice)
                ctx.Info("Số đo trong Editor có overhead", "Dùng để so sánh giữa các lần chạy; số chuẩn lấy từ Device / E2E.");

            ctx.Attach("frame-times.csv", ToCsv(frames), "Frame time từng frame (ms)");
            await ctx.Screenshot("idle");
        }

        static double Percentile(List<double> sorted, double p)
        {
            if (sorted.Count == 0) return 0;
            var idx = (int)Math.Ceiling(p * sorted.Count) - 1;
            return sorted[Mathf.Clamp(idx, 0, sorted.Count - 1)];
        }

        static string ToCsv(List<double> frames)
        {
            var sb = new StringBuilder("frame,ms\n");
            for (var i = 0; i < frames.Count; i++)
                sb.Append(i).Append(',').Append(frames[i].ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
            return sb.ToString();
        }

        #endregion

        #region Screen cost

        struct ScreenCostRow
        {
            public string Name;
            public double OpenMs;
            public double GcKb;
            public double MaxFrameMs;
        }

        static async Task ScreenCost(AutoTestContext ctx)
        {
            ctx.LogSeverityCap = Severity.Minor; // exception khi mở màn đã được Smoke báo — ở đây chỉ đo chi phí
            await GameFlow.EnsureReady(ctx);
            var features = GameFlow.TestableFeatures(ctx.Config, ctx.Game, ctx.Hooks)
                .Where(f => !GameFlow.IsBaseline(ctx.Session, f)).ToList();
            if (features.Count == 0) ctx.Skip("Adapter không liệt kê được màn hình.");
            var rows = new List<ScreenCostRow>();
            using var gcRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            foreach (var f in features)
            {
                ctx.ThrowIfCancelled();
                using (ctx.Step($"Đo {f.Name}"))
                {
                    var open = await GameFlow.OpenFeature(ctx, f, 0f);
                    if (open.Root == null) continue;
                    double gcBytes = 0;
                    double maxFrame = 0;
                    var end = AutoTestClock.Now + SCREEN_COST_WINDOW_SECONDS;
                    while (AutoTestClock.Now < end)
                    {
                        await ctx.NextFrame();
                        if (gcRec.Valid) gcBytes += gcRec.LastValue;
                        maxFrame = Math.Max(maxFrame, Time.unscaledDeltaTime * 1000.0);
                    }

                    rows.Add(new ScreenCostRow
                        { Name = f.Name, OpenMs = open.OpenMs, GcKb = gcBytes / BYTES_PER_KB, MaxFrameMs = maxFrame });
                    await GameFlow.CloseFeature(ctx, f);
                    await GameFlow.ReturnToBaseline(ctx);
                }
            }

            var sev = PerfSeverity(ctx);
            foreach (var r in rows.OrderByDescending(r => r.OpenMs).Take(TOP_SLOWEST))
                ctx.Metric($"Mở {r.Name}", r.OpenMs, "ms", ctx.Config.smoke.slowOpenMs, null, sev);
            foreach (var r in rows.OrderByDescending(r => r.GcKb).Take(TOP_SLOWEST))
                ctx.Metric($"GC khi mở {r.Name}", r.GcKb, "KB");
            var csv = new StringBuilder("screen,open_ms,gc_kb,max_frame_ms\n");
            foreach (var r in rows.OrderByDescending(r => r.OpenMs))
                csv.Append(r.Name).Append(',')
                    .Append(r.OpenMs.ToString("0.#", CultureInfo.InvariantCulture)).Append(',')
                    .Append(r.GcKb.ToString("0.#", CultureInfo.InvariantCulture)).Append(',')
                    .Append(r.MaxFrameMs.ToString("0.#", CultureInfo.InvariantCulture)).Append('\n');
            ctx.Attach("screen-cost.csv", csv.ToString(), "Chi phí mở từng màn");
        }

        #endregion

        #region Leak check

        static async Task LeakCheck(AutoTestContext ctx)
        {
            ctx.LogSeverityCap = Severity.Minor;
            await GameFlow.EnsureReady(ctx);
            var features = GameFlow.TestableFeatures(ctx.Config, ctx.Game, ctx.Hooks)
                .Where(f => !GameFlow.IsBaseline(ctx.Session, f)).ToList();
            if (features.Count == 0) ctx.Skip("Adapter không liệt kê được màn hình.");
            var suspects = 0;
            foreach (var f in features)
            {
                ctx.ThrowIfCancelled();
                using (ctx.Step($"Mở/đóng {f.Name} × {LEAK_CYCLES}"))
                {
                    // Lượt đầu để nạp cache (prefab, sprite) — không tính.
                    var warm = await GameFlow.OpenFeature(ctx, f, 0.3f);
                    if (warm.Root == null) continue;
                    await GameFlow.CloseFeature(ctx, f);
                    await GameFlow.ReturnToBaseline(ctx);

                    var (objBefore, memBefore) = await Measure(ctx);
                    for (var i = 0; i < LEAK_CYCLES; i++)
                    {
                        await GameFlow.OpenFeature(ctx, f, 0.3f);
                        await GameFlow.CloseFeature(ctx, f);
                        await GameFlow.ReturnToBaseline(ctx);
                    }

                    var (objAfter, memAfter) = await Measure(ctx);
                    var objGrowth = objAfter - objBefore;
                    var memGrowth = (memAfter - memBefore) / BYTES_PER_MB;
                    if (objGrowth > LEAK_OBJECT_GROWTH_THRESHOLD || memGrowth > LEAK_MEMORY_GROWTH_MB)
                    {
                        suspects++;
                        ctx.Report(Severity.Minor, "Perf.Leak", $"Nghi rò khi mở/đóng {f.Name}",
                            $"Sau {LEAK_CYCLES} lần mở/đóng: +{objGrowth} GameObject, +{memGrowth:0.#} MB (đã GC + unload).",
                            f.Name, null, $"≤ +{LEAK_OBJECT_GROWTH_THRESHOLD} object, ≤ +{LEAK_MEMORY_GROWTH_MB} MB",
                            $"+{objGrowth} object, +{memGrowth:0.#} MB");
                    }
                }
            }

            ctx.Metric("Màn nghi rò", suspects, "");
        }

        static async Task<(int objects, long memory)> Measure(AutoTestContext ctx)
        {
            GC.Collect();
            var op = Resources.UnloadUnusedAssets();
            await ctx.WaitUntil(() => op.isDone, 10, "UnloadUnusedAssets", false);
            GC.Collect();
            await ctx.WaitFrames(2);
            var objects = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Length;
            return (objects, Profiler.GetTotalAllocatedMemoryLong());
        }

        #endregion
    }
}
