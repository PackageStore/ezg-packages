using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Liệt kê feature "chết" — enum màn hình còn khai báo nhưng game không mở tới (repo kế thừa từ project cũ chưa
    ///     dọn). Chỉ Info: không phải lỗi, nhưng là lý do Smoke / UI Audit / Button Sweep / Visual bỏ qua các màn đó, và
    ///     là danh sách để dev dọn enum / prefab / asset thừa. Xem <see cref="LivenessConfig" /> +
    ///     <see cref="FeatureLivenessScanner" />.
    /// </summary>
    public sealed class FeatureLivenessRule : IStaticRule
    {
        const string CATEGORY_DEAD = "Feature.Dead";
        const string CATEGORY_OBSERVED = "Feature.LiveObservedOnly";
        const string CATEGORY_SCAN = "Feature.LivenessScan";
        const int LIST_CAP = 40;

        public string Id => "feature-liveness";
        public string Name => "Feature không còn dùng (liveness)";

        public string Description =>
            "Quét code + prefab/scene/asset xem feature nào không ai tham chiếu (ngoài map bundle) — màn 'chết' kế thừa từ " +
            "project cũ. Các suite Play bỏ qua chúng; dòng Info ở đây là danh sách để dọn dẹp.";

        public string Category => "Cấu hình";
        public int Order => 225;

        public async Task Run(AutoTestContext ctx)
        {
            await StaticCheckUtil.Yield(ctx, 0);
            var l = ctx.Config.liveness;
            if (l == null || !l.enabled) ctx.Skip("liveness.enabled = false trong Settings.");

            var result = FeatureLivenessScanner.Resolve(ctx.Config, ctx.Game, true);
            if (result == null || result.Error != null && result.Features.Count == 0)
                ctx.Skip(result?.Error ?? "Không quét được liveness.");
            FeatureLivenessScanner.Apply(result, l);

            ctx.Metric("Feature trong enum", result.Features.Count);
            ctx.Metric("File .cs đã quét", result.CodeFilesScanned);
            ctx.Metric("File prefab/scene/asset đã quét", result.YamlFilesScanned);
            ctx.Metric("Thời gian quét", Math.Round(result.Milliseconds), "ms");
            ctx.Log($"Nguồn: {result.Source} ({result.ResolvedAt}); enum {result.EnumName}.");

            var dead = 0;
            var serializedOnly = 0;
            var observedOnly = new List<string>();
            foreach (var f in result.Features.OrderBy(f => f.name, StringComparer.Ordinal))
            {
                await StaticCheckUtil.Yield(ctx, dead + serializedOnly);
                var isDead = AutoTestFilters.IsDeadFeature(ctx.Config, f.name, out var reason);
                if (!f.HasAnyReference && !isDead && (AutoTestFilters.ContainsName(l.observedLive, f.name) ||
                                                      AutoTestFilters.ContainsName(l.forceLive, f.name)))
                    observedOnly.Add(f.name);
                if (f.SerializedOnly) serializedOnly++;
                if (!isDead) continue;

                dead++;
                ctx.Report(Severity.Info, CATEGORY_DEAD, $"Feature {f.name} không còn được dùng",
                        Describe(f, reason), f.name, null, "Có nơi mở màn này", ReasonLabel(reason))
                    .WithContentId(ctx, "", f.name + "|" + reason);
            }

            ctx.Metric("Feature chết (bỏ qua ở suite Play)", dead);
            ctx.Metric("Feature sống chỉ nhờ prefab/asset", serializedOnly);
            if (observedOnly.Count > 0)
                ctx.Report(Severity.Info, CATEGORY_OBSERVED,
                        $"{observedOnly.Count} feature sống nhờ tool QA / forceLive nhưng quét tĩnh không thấy tham chiếu",
                        "Tool QA đã thấy mở (observedLive) hoặc dev khai forceLive, còn code/prefab/scene/asset không tham chiếu " +
                        "tới. Có thể màn được mở theo tên chuỗi động, qua server/remote config, hoặc dữ liệu ngoài Assets: " +
                        DataRuleUtil.JoinCapped(observedOnly, LIST_CAP))
                    .WithContentId(ctx, "", string.Join(",", observedOnly));
            if (result.Error != null)
                ctx.Report(Severity.Info, CATEGORY_SCAN, "Quét liveness chưa đầy đủ", result.Error);
        }

        static string Describe(FeatureLivenessScanner.FeatureInfo f, string reason)
        {
            switch (reason)
            {
                case "forceDead":
                    return "Nằm trong liveness.forceDead của Settings (dev khai là chết).";
                case "deadFeaturePatterns":
                    return "Tên khớp liveness.deadFeaturePatterns (vd hậu tố _Retired).";
                default:
                    return $"Không tìm thấy tham chiếu nào trong Assets: code 0 (ngoài map bundle), prefab/scene/asset 0 " +
                           $"(ngoài prefab màn của chính nó), chuỗi tên 0. Giá trị enum = {f.value}. Smoke / UI Audit / Button " +
                           "Sweep / Visual bỏ qua màn này. Nếu game vẫn mở được (qua remote config, tên động…): thêm vào " +
                           "liveness.forceLive hoặc để tool QA quan sát (observedLive); nếu thật sự thừa: dọn enum + prefab + asset.";
            }
        }

        static string ReasonLabel(string reason)
        {
            switch (reason)
            {
                case "forceDead": return "forceDead";
                case "deadFeaturePatterns": return "khớp deadFeaturePatterns";
                default: return "0 tham chiếu";
            }
        }
    }
}
