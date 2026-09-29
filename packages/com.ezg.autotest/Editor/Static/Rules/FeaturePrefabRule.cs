using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Mỗi feature/màn hình (enum Features qua adapter) phải có prefab đúng tên theo quy ước của adapter, và prefab
    ///     đó phải load được trong bản build (nằm trong Resources hoặc có AssetBundle).
    /// </summary>
    public sealed class FeaturePrefabRule : IStaticRule
    {
        const string CATEGORY = "Config.FeaturePrefab";
        const string SCREEN_PREFIX = "screen_";
        const int INDEX_YIELD_EVERY = 400;
        const int LIST_CAP = 50;

        public string Id => "feature-prefabs";
        public string Name => "Feature ↔ prefab màn hình";

        public string Description =>
            "Mọi feature (trừ danh sách loại trừ) có prefab màn hình đúng tên, prefab nằm trong Resources hoặc có " +
            "AssetBundle; liệt kê prefab screen_* không thuộc feature nào.";

        public string Category => "Cấu hình";
        public int Order => 220;

        public async Task Run(AutoTestContext ctx)
        {
            await StaticCheckUtil.Yield(ctx, 0);
            var game = ctx.Game;
            if (!(game is IFeaturePrefabResolver resolver))
            {
                ctx.Skip($"Adapter {(game != null ? game.Name : "(chưa có)")} không hỗ trợ quy ước tên prefab màn hình (IFeaturePrefabResolver).");
                return;
            }

            IReadOnlyList<AutoTestFeature> features;
            try
            {
                features = game.GetFeatures();
            }
            catch (Exception e) when (!DataRuleUtil.IsFlowControl(e))
            {
                ctx.Skip($"Adapter {game.Name} lỗi khi liệt kê feature: {e.Message}");
                return;
            }

            if (features == null || features.Count == 0)
                ctx.Skip($"Adapter {game.Name} không liệt kê được feature nào (enum Features chưa resolve?).");

            var index = await BuildPrefabIndex(ctx);
            var resolvedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var checkedCount = 0;
            var missingCount = 0;
            foreach (var feature in features)
            {
                if (feature == null || string.IsNullOrEmpty(feature.Name)) continue;
                string prefabName;
                try
                {
                    prefabName = resolver.ResolvePrefabName(feature);
                }
                catch (Exception e) when (!DataRuleUtil.IsFlowControl(e))
                {
                    ctx.Report(Severity.Info, CATEGORY, "Adapter không xác định được tên prefab của feature",
                            $"{feature.Name}: {e.Message}")
                        .WithContentId(ctx, "", feature.Name);
                    continue;
                }

                if (string.IsNullOrEmpty(prefabName)) continue;
                resolvedNames.Add(prefabName);
                if (AutoTestFilters.IsFeatureExcluded(ctx.Config, feature.Name, ctx.Hooks)) continue;
                checkedCount++;

                if (!index.TryGetValue(prefabName, out var paths))
                {
                    missingCount++;
                    ctx.Report(Severity.Minor, CATEGORY, $"Feature {feature.Name} chưa có prefab (mở sẽ hiện 'coming soon')",
                        $"Không tìm thấy \"{prefabName}.prefab\" trong Assets — mở màn {feature.Name} sẽ không load được prefab. Bỏ qua nếu feature chưa làm (hoặc thêm vào smoke.excludedFeatures).",
                        null, null, prefabName + ".prefab", "không có");
                    continue;
                }

                var loadable = paths.Where(IsLoadableInBuild).ToList();
                if (loadable.Count == 0)
                    ctx.Report(Severity.Major, CATEGORY,
                        "Prefab màn hình không nằm trong Resources/AssetBundle (build sẽ không load được)",
                        $"Feature {feature.Name}: {string.Join(", ", paths)} không nằm trong thư mục Resources và không gán AssetBundle — Editor có thể vẫn chạy nhưng bản build không load được màn hình.",
                        paths[0], null, "trong Resources/ hoặc có AssetBundle", "không");
                else if (loadable.Count > 1)
                    ctx.Report(Severity.Minor, CATEGORY, "Nhiều prefab trùng tên màn hình",
                        $"Feature {feature.Name} có {loadable.Count} prefab \"{prefabName}\" load được: {string.Join(", ", loadable)} — không chắc bản nào được load.",
                        loadable[0]);
            }

            ReportOrphans(ctx, index, resolvedNames);
            ctx.Metric("Feature kiểm tra", checkedCount);
            ctx.Metric("Feature thiếu prefab", missingCount);
        }

        /// <summary>Tên file prefab (không đuôi, không phân biệt hoa thường) → các đường dẫn trong Assets.</summary>
        static async Task<Dictionary<string, List<string>>> BuildPrefabIndex(AutoTestContext ctx)
        {
            var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            for (var i = 0; i < guids.Length; i++)
            {
                if (i % INDEX_YIELD_EVERY == 0) await StaticCheckUtil.Yield(ctx, i, guids.Length, "Liệt kê prefab");
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                var name = Path.GetFileNameWithoutExtension(path);
                if (!index.TryGetValue(name, out var list)) index[name] = list = new List<string>();
                if (!list.Contains(path)) list.Add(path);
            }

            return index;
        }

        static bool IsLoadableInBuild(string path)
        {
            return path.Replace('\\', '/').IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   !string.IsNullOrEmpty(AssetDatabase.GetImplicitAssetBundleName(path));
        }

        /// <summary>Prefab screen_* không khớp feature nào (chỉ khi adapter dùng quy ước "screen_").</summary>
        static void ReportOrphans(AutoTestContext ctx, Dictionary<string, List<string>> index,
            HashSet<string> resolvedNames)
        {
            if (!resolvedNames.Any(n => n.StartsWith(SCREEN_PREFIX, StringComparison.OrdinalIgnoreCase))) return;
            var cfg = ctx.Config.staticCheck;
            var orphans = index
                .Where(p => p.Key.StartsWith(SCREEN_PREFIX, StringComparison.OrdinalIgnoreCase) &&
                            !resolvedNames.Contains(p.Key))
                .SelectMany(p => p.Value)
                .Where(p => StaticCheckUtil.InScope(p, cfg))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
            if (orphans.Count == 0) return;
            ctx.Report(Severity.Info, CATEGORY, "Prefab screen_* không khớp feature nào",
                $"{orphans.Count} prefab đặt tên như màn hình nhưng không có feature tương ứng (màn cũ, màn con, hoặc sai tên so với enum): {DataRuleUtil.JoinCapped(orphans, LIST_CAP)}");
        }
    }
}
