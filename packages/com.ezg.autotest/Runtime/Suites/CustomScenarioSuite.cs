using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Kịch bản riêng của từng game (gameplay đặc thù): mỗi class kế thừa <see cref="AutoTestScenario" /> là một
    ///     case. Dev viết (hoặc nhờ AI viết) trong thư mục AutoTests của project.
    /// </summary>
    public sealed class CustomScenarioSuite : AutoTestSuite
    {
        public const string SUITE_ID = "scenarios";
        const string EMPTY_CASE_ID = "no-scenarios";

        public override string Id => SUITE_ID;
        public override string DisplayName => "Kịch bản riêng";

        public override string Description =>
            "Kịch bản test gameplay/tính năng đặc thù của game (vd vòng lặp thu hoạch → bán → nâng cấp). " +
            "Mỗi class kế thừa AutoTestScenario là một case; tạo từ template bằng nút \"Tạo kịch bản\" rồi nhờ AI " +
            "triển khai theo Documentation~/AI-SCENARIO-GUIDE.md.";

        public override ExecutionMode Mode => ExecutionMode.Play;
        public override int Order => 90;
        public override string Icon => "d_UnityEditor.AnimationWindow";
        public override bool MutatesPlayerData => true;

        public override IEnumerable<AutoTestCase> BuildCases(AutoTestBuildContext ctx)
        {
            var scenarios = AutoTestRegistry.FindScenarios();
            if (scenarios.Count == 0)
            {
                yield return new AutoTestCase(EMPTY_CASE_ID, "Chưa có kịch bản",
                    "Project chưa có class AutoTestScenario nào.",
                    c =>
                    {
                        c.Skip("Chưa có kịch bản — bấm \"Tạo kịch bản\" trong cửa sổ Auto Test để tạo từ template.");
                        return Task.CompletedTask;
                    }, "Kịch bản");
                yield break;
            }

            foreach (var (type, info) in scenarios)
            {
                if (ctx.IsDevice && !info.RunOnDevice) continue;
                var t = type;
                var attr = info;
                yield return new AutoTestCase(CaseId(t), attr.Name, attr.Description, c => Run(c, t, attr),
                    attr.Category, attr.TimeoutSeconds) { Tags = attr.Tags ?? Array.Empty<string>() };
            }
        }

        /// <summary>Id case của kịch bản = tên class đầy đủ (ổn định khi đổi tên hiển thị).</summary>
        public static string CaseId(Type scenarioType)
        {
            return scenarioType.FullName;
        }

        static async Task Run(AutoTestContext ctx, Type type, AutoTestScenarioAttribute info)
        {
            if (info.Disabled) ctx.Skip("Kịch bản đang tắt (Disabled = true).");
            await GameFlow.EnsureReady(ctx);
            var scenario = (AutoTestScenario)Activator.CreateInstance(type);
            try
            {
                using (ctx.Step("Chuẩn bị (SetUp)"))
                {
                    await scenario.SetUp(ctx);
                }

                await scenario.Run(ctx);
            }
            finally
            {
                try
                {
                    await scenario.TearDown(ctx);
                }
                catch (OperationCanceledException)
                {
                    // Dừng giữa TearDown — bỏ qua, runner vẫn khôi phục dữ liệu.
                }
                catch (Exception e)
                {
                    ctx.Report(Severity.Minor, "Scenario.TearDown", "TearDown của kịch bản lỗi", e.Message);
                }

                await GameFlow.ReturnToBaseline(ctx);
            }
        }
    }
}
