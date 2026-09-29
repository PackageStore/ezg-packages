using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Cấu hình AssetBundle: tên bundle không còn asset nào, và asset vừa nằm trong thư mục Resources vừa gán
    ///     assetBundleName (bị build 2 lần: một bản trong resources.assets, một bản trong bundle). Project không dùng
    ///     bundle thì luật đạt, chỉ ghi số đo.
    /// </summary>
    public sealed class AssetBundleRule : IStaticRule
    {
        const string ISSUE_CATEGORY = "Asset.AssetBundle";
        const string RESOURCES_SEGMENT = "/Resources/";
        const int MAX_LIST = 20;

        public string Id => "asset-bundles";
        public string Name => "AssetBundle (tên rỗng, trùng Resources)";

        public string Description =>
            "Tên AssetBundle không còn asset nào, và asset vừa nằm trong Resources vừa gán AssetBundle (bị build 2 " +
            "lần, phình dung lượng build).";

        public string Category => "Asset";
        public int Order => 60;

        public async Task Run(AutoTestContext ctx)
        {
            var sink = new AssetIssueSink(ctx, ISSUE_CATEGORY);
            var names = AssetDatabase.GetAllAssetBundleNames();
            ctx.Metric("Số AssetBundle", names.Length);
            if (names.Length == 0)
            {
                ctx.Log("Project không gán AssetBundle nào — bỏ qua.");
                return;
            }

            var unused = new HashSet<string>(AssetDatabase.GetUnusedAssetBundleNames(), StringComparer.Ordinal);
            var empty = new List<string>();
            var totalAssets = 0;
            var duplicatedAssets = 0;
            for (var i = 0; i < names.Length; i++)
            {
                await StaticCheckUtil.Yield(ctx, i, names.Length, "Quét AssetBundle");
                var bundle = names[i];
                try
                {
                    var paths = AssetDatabase.GetAssetPathsFromAssetBundle(bundle);
                    totalAssets += paths.Length;
                    if (paths.Length == 0 || unused.Contains(bundle))
                    {
                        empty.Add(bundle);
                        continue;
                    }

                    var inResources = new List<string>();
                    foreach (var p in paths)
                        if (IsInResources(p))
                            inResources.Add(p);
                    if (inResources.Count == 0) continue;

                    duplicatedAssets += inResources.Count;
                    sink.Report(Severity.Minor, "Asset nằm cả Resources và AssetBundle (bị build 2 lần)",
                        $"{inResources.Count}/{paths.Length} asset của bundle '{bundle}' nằm trong thư mục Resources → " +
                        "vừa vào resources.assets của build, vừa vào bundle (tốn dung lượng, có thể load nhầm bản). Chuyển " +
                        "asset ra khỏi Resources nếu chỉ load qua bundle, hoặc bỏ assetBundleName nếu chỉ load qua " +
                        "Resources." + AssetRuleUtil.BulletList(inResources, MAX_LIST),
                        inResources[0], null, "Chỉ một cơ chế load", "Resources + AssetBundle");
                }
                catch (Exception e)
                {
                    sink.ReportUnreadable(bundle, e);
                }
            }

            if (empty.Count > 0)
                ctx.Report(Severity.Info, ISSUE_CATEGORY, "Tên AssetBundle không còn asset nào",
                    $"{empty.Count} tên bundle không gán cho asset nào (thừa lại sau khi đổi tên/xoá). Dọn bằng " +
                    "AssetDatabase.RemoveUnusedAssetBundleNames() hoặc menu Remove Unused Names trong Inspector:" +
                    AssetRuleUtil.BulletList(empty, MAX_LIST));

            ctx.Metric("Asset trong AssetBundle", totalAssets);
            ctx.Metric("Bundle rỗng", empty.Count);
            ctx.Metric("Asset nằm cả Resources", duplicatedAssets);
            sink.Flush();
        }

        static bool IsInResources(string path)
        {
            return path.IndexOf(RESOURCES_SEGMENT, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
