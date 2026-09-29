#if UNITY_EDITOR || EZG_AUTOTEST
using System.Threading.Tasks;
using UnityEngine;

namespace Ezg.AutoTest.Samples
{
    /// <summary>
    ///     Mẫu: mở một màn hình theo tên enum feature qua adapter, kiểm tra mọi nút đang hiển thị + interactable
    ///     đều bấm được (không bị object khác che), chụp màn hình rồi đóng lại. Chỉ chạy khi adapter là
    ///     <see cref="EzgTemplateAdapter" /> (project theo template EZG) — adapter khác ⇒ bỏ qua có lý do.
    /// </summary>
    [AutoTestScenario("Mẫu: Mở Shop và kiểm tra nút", Category = "Mẫu", Order = 900, TimeoutSeconds = 60,
        Description = "Mở màn Shop qua UIManager, kiểm tra nút hiển thị đều bấm được, chụp màn hình rồi đóng.")]
    public sealed class ExampleShopScenario : AutoTestScenario
    {
        /// <summary>Tên giá trị trong enum Features của game (không phân biệt hoa thường).</summary>
        const string FEATURE_NAME = "Shop";

        /// <summary>Giới hạn số nút báo lỗi để report không bị ngập khi cả màn bị che.</summary>
        const int MAX_REPORTED_BUTTONS = 20;

        AutoTestFeature _feature;

        public override async Task SetUp(AutoTestContext ctx)
        {
            // Chờ game boot xong + đóng popup đầu game (gọi nhiều lần vẫn an toàn).
            await GameFlow.EnsureReady(ctx);

            var ezg = ctx.Game as EzgTemplateAdapter;
            if (ezg == null)
                ctx.Skip($"Adapter hiện tại ({ctx.Game.Name}) không phải EzgTemplateAdapter — mẫu này dành cho template EZG.");
            if ((ctx.Game.Capabilities & AdapterCapabilities.Features) == 0)
                ctx.Skip("Adapter không điều khiển được màn hình (thiếu capability Features).");

            _feature = ezg.FindFeature(FEATURE_NAME);
            if (_feature == null) ctx.Skip($"Game không có feature '{FEATURE_NAME}' trong enum Features.");
        }

        public override async Task Run(AutoTestContext ctx)
        {
            GameObject root;
            using (ctx.Step($"Mở màn {FEATURE_NAME}"))
            {
                var open = await GameFlow.OpenFeature(ctx, _feature);
                ctx.Assert(open.Root != null, $"Không mở được màn {FEATURE_NAME}: {open.Error}", Severity.Critical);
                ctx.Metric($"Thời gian mở {FEATURE_NAME}", open.OpenMs, "ms", max: ctx.Config.smoke.slowOpenMs,
                    severity: Severity.Minor);
                root = open.Root;
            }

            using (ctx.Step("Kiểm tra các nút trên màn hình bấm được"))
            {
                var buttons = ctx.Ui.GetClickables(root, false);
                ctx.Check(buttons.Count > 0, $"Màn {FEATURE_NAME} không có nút nào", severity: Severity.Minor);

                var reported = 0;
                foreach (var selectable in buttons)
                {
                    var go = selectable.gameObject;
                    // Nút ẩn (tab chưa chọn) hoặc cố ý khoá (không đủ tiền) không phải lỗi.
                    if (!ctx.Ui.IsVisible(go) || !ctx.Ui.IsInteractable(go)) continue;
                    if (ctx.Ui.IsClickable(go, out var reason)) continue;

                    ctx.Report(Severity.Minor, "UI.Blocked", $"Nút '{go.name}' hiển thị nhưng không bấm được", reason,
                        objectPath: UiDriver.PathOf(go.transform));
                    if (++reported >= MAX_REPORTED_BUTTONS) break;
                }

                ctx.Log($"Đã kiểm tra {buttons.Count} nút, {reported} nút bị che/không nhận raycast.");
            }

            using (ctx.Step($"Chụp màn {FEATURE_NAME}"))
            {
                await ctx.Screenshot(FEATURE_NAME);
            }

            using (ctx.Step($"Đóng màn {FEATURE_NAME}"))
            {
                var closed = await GameFlow.CloseFeature(ctx, _feature);
                ctx.Check(closed, $"Màn {FEATURE_NAME} không đóng được", severity: Severity.Major);
            }
        }

        public override async Task TearDown(AutoTestContext ctx)
        {
            // Đóng mọi màn còn mở (chỉ khi game đã boot — tránh đóng nhầm HUD khi SetUp lỗi giữa chừng).
            if (GameFlow.IsReady(ctx.Session)) await GameFlow.ReturnToBaseline(ctx);
        }
    }
}
#endif
