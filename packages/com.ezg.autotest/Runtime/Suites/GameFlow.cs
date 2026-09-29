using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Luồng game dùng chung cho mọi suite Play: chờ boot xong, đóng popup đầu game, ghi "màn nền" (HUD),
    ///     mở/đóng màn hình an toàn và đưa game về màn nền giữa các case.
    /// </summary>
    public static class GameFlow
    {
        const string KEY_READY = "flow.ready";
        const string KEY_BASELINE = "flow.baseline";
        const string KEY_BOOT_SECONDS = "flow.bootSeconds";
        const float STABLE_SECONDS = 1f;
        const float CLOSE_TIMEOUT_SECONDS = 3f;
        const float BETWEEN_CLOSE_SECONDS = 0.15f;
        const int MAX_RETURN_PASSES = 4;

        /// <summary>Game đã boot xong trong phiên này chưa.</summary>
        public static bool IsReady(AutoTestSession session)
        {
            return session.Get(KEY_READY, false);
        }

        /// <summary>Thời gian boot (giây, tính từ lúc vào Play) — đo ở lần EnsureReady đầu tiên.</summary>
        public static double BootSeconds(AutoTestSession session)
        {
            return session.Get(KEY_BOOT_SECONDS, -1.0);
        }

        /// <summary>
        ///     Chờ game sẵn sàng (hooks/adapter báo ready và giữ ổn định <see cref="STABLE_SECONDS" />), chạy
        ///     hooks.OnSessionStarted một lần, đóng popup đầu game, ghi màn nền. Gọi ở đầu mọi case Play.
        /// </summary>
        public static async Task EnsureReady(AutoTestContext ctx)
        {
            var session = ctx.Session;
            if (IsReady(session)) return;
            if (!Application.isPlaying) ctx.Skip("Cần Play mode.");

            using (ctx.Step("Chờ game khởi động xong"))
            {
                var cfg = ctx.Config.general;
                var stableSince = -1.0;
                var ok = await AutoTestClock.Until(() =>
                {
                    var ready = CheckReady(ctx);
                    if (!ready)
                    {
                        stableSince = -1;
                        return false;
                    }

                    if (stableSince < 0) stableSince = AutoTestClock.Now;
                    return AutoTestClock.Now - stableSince >= STABLE_SECONDS;
                }, cfg.bootTimeoutSeconds, ctx.Token);

                if (!ok)
                    throw new AutoTestAssertException(
                        $"Game không sẵn sàng sau {cfg.bootTimeoutSeconds:0}s. Trạng thái: {ctx.Game.DescribeState()}",
                        Severity.Blocker);

                session.Set(KEY_BOOT_SECONDS, Time.unscaledTimeAsDouble);
                if (cfg.settleSecondsAfterBoot > 0) await ctx.WaitSeconds(cfg.settleSecondsAfterBoot);
            }

            if (ctx.Hooks != null)
                using (ctx.Step("Hooks.OnSessionStarted"))
                {
                    await ctx.Hooks.OnSessionStarted(ctx);
                }

            using (ctx.Step("Đóng popup đầu game"))
            {
                await DismissStartPopups(ctx);
            }

            session.Set(KEY_BASELINE, ctx.Game.GetOpenFeatures().Select(f => f.Name).ToList());
            session.Set(KEY_READY, true);
            ctx.Log("Màn nền: " + string.Join(", ", Baseline(session)));
        }

        static bool CheckReady(AutoTestContext ctx)
        {
            try
            {
                var hook = ctx.Hooks?.IsGameReady();
                return hook ?? ctx.Game.IsGameReady();
            }
            catch (Exception e)
            {
                AutoTestLog.Warn("Kiểm tra ready lỗi: " + e.Message);
                return false;
            }
        }

        /// <summary>Tên các màn mở sẵn sau khi boot (HUD, currency bar…) — không bao giờ bị đóng.</summary>
        public static List<string> Baseline(AutoTestSession session)
        {
            return session.Get(KEY_BASELINE, new List<string>());
        }

        public static bool IsBaseline(AutoTestSession session, AutoTestFeature feature)
        {
            return Baseline(session).Contains(feature.Name);
        }

        /// <summary>Đóng popup đang mở khớp smoke.dismissAtStartFeatures + hooks.DismissBlockingPopups.</summary>
        public static async Task DismissStartPopups(AutoTestContext ctx)
        {
            if ((ctx.Game.Capabilities & AdapterCapabilities.Features) != 0)
                foreach (var f in ctx.Game.GetOpenFeatures().ToList())
                {
                    if (!AutoTestFilters.MatchesAny(f.Name, ctx.Config.smoke.dismissAtStartFeatures)) continue;
                    ctx.Log($"Đóng popup đầu game: {f.Name}");
                    SafeClose(ctx, f);
                    await ctx.WaitSeconds(BETWEEN_CLOSE_SECONDS);
                }

            if (ctx.Hooks != null) await ctx.Hooks.DismissBlockingPopups(ctx);
        }

        /// <summary>Đóng mọi màn không thuộc màn nền để case sau bắt đầu sạch.</summary>
        public static async Task ReturnToBaseline(AutoTestContext ctx)
        {
            if ((ctx.Game.Capabilities & AdapterCapabilities.Features) == 0) return;
            var baseline = Baseline(ctx.Session);
            for (var pass = 0; pass < MAX_RETURN_PASSES; pass++)
            {
                var extra = ctx.Game.GetOpenFeatures().Where(f => !baseline.Contains(f.Name)).ToList();
                if (extra.Count == 0) break;
                // Đóng từ màn mở sau cùng về trước (thứ tự dict ~ thứ tự mở).
                for (var i = extra.Count - 1; i >= 0; i--)
                {
                    SafeClose(ctx, extra[i]);
                    await AutoTestClock.Seconds(BETWEEN_CLOSE_SECONDS, ctx.Token);
                }
            }

            if (ctx.Hooks != null) await ctx.Hooks.DismissBlockingPopups(ctx);
        }

        /// <summary>Feature đưa vào smoke/UI audit (không bị loại trong settings/hooks).</summary>
        public static List<AutoTestFeature> TestableFeatures(AutoTestConfig config, IGameAdapter game,
            IAutoTestProjectHooks hooks)
        {
            if (game == null || (game.Capabilities & AdapterCapabilities.Features) == 0) return new List<AutoTestFeature>();
            hooks ??= AutoTestRegistry.CreateHooks(); // BuildCases không có hooks → tự tạo để loại trừ đồng nhất
            return game.GetFeatures().Where(f => !AutoTestFilters.IsFeatureExcluded(config, f.Name, hooks)).ToList();
        }

        /// <summary>Kết quả mở một màn.</summary>
        public sealed class OpenResult
        {
            public GameObject Root;
            public double OpenMs;
            public bool WasAlreadyOpen;
            public string Error;
        }

        /// <summary>
        ///     Mở màn hình (data lấy từ hooks.GetFeatureData), đo thời gian, chờ settle. Không ném exception khi
        ///     mở thất bại — trả Error để case tự quyết severity.
        /// </summary>
        public static async Task<OpenResult> OpenFeature(AutoTestContext ctx, AutoTestFeature feature,
            float? settleSeconds = null)
        {
            var result = new OpenResult { WasAlreadyOpen = ctx.Game.IsFeatureShowing(feature) };
            var data = SafeGetData(ctx, feature);
            var start = AutoTestClock.Now;
            try
            {
                result.Root = await ctx.Game.ShowFeature(feature, data, ctx.Config.smoke.screenOpenTimeoutSeconds,
                    ctx.Token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TimeoutException)
            {
                result.Error = $"Mở quá {ctx.Config.smoke.screenOpenTimeoutSeconds:0.#}s chưa xong";
            }
            catch (Exception e)
            {
                result.Error = $"{e.GetType().Name}: {e.Message}";
            }

            result.OpenMs = (AutoTestClock.Now - start) * 1000.0;
            if (result.Root == null && result.Error == null)
                result.Error = "Không mở được (adapter trả null — thiếu prefab / màn 'coming soon'?)";
            var settle = settleSeconds ?? ctx.Config.smoke.screenSettleSeconds;
            if (result.Root != null && settle > 0) await ctx.WaitSeconds(settle);
            return result;
        }

        /// <summary>Đóng màn (bỏ qua nếu là màn nền) và chờ nó biến mất. Trả false nếu không đóng được.</summary>
        public static async Task<bool> CloseFeature(AutoTestContext ctx, AutoTestFeature feature)
        {
            if (IsBaseline(ctx.Session, feature)) return true;
            SafeClose(ctx, feature);
            return await AutoTestClock.Until(() => !ctx.Game.IsFeatureShowing(feature), CLOSE_TIMEOUT_SECONDS,
                ctx.Token);
        }

        static void SafeClose(AutoTestContext ctx, AutoTestFeature feature)
        {
            try
            {
                ctx.Game.CloseFeature(feature);
            }
            catch (Exception e)
            {
                ctx.Report(Severity.Minor, "Feature.Close", $"Lỗi khi đóng màn {feature.Name}", e.Message);
            }
        }

        static object SafeGetData(AutoTestContext ctx, AutoTestFeature feature)
        {
            try
            {
                return ctx.Hooks?.GetFeatureData(feature.Name);
            }
            catch (Exception e)
            {
                ctx.Warn($"Hooks.GetFeatureData({feature.Name}) lỗi", e.Message);
                return null;
            }
        }
    }
}
