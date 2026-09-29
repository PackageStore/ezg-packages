using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Một luật kiểm tra tĩnh (không vào Play). Implement interface này (class có constructor rỗng) là luật
    ///     tự hiện thành một case trong suite "Kiểm tra tĩnh" — không cần đăng ký.
    /// </summary>
    public interface IStaticRule
    {
        /// <summary>Id ổn định, chữ thường-gạch ngang (vd "missing-scripts").</summary>
        string Id { get; }

        string Name { get; }
        string Description { get; }

        /// <summary>Nhóm hiển thị: "Asset", "Cấu hình", "Dữ liệu", "Code", "Build".</summary>
        string Category { get; }

        /// <summary>Thứ tự chạy (nhỏ trước).</summary>
        int Order { get; }

        /// <summary>
        ///     Chạy luật. Ghi lỗi bằng ctx.Report(...) (location = asset path, objectPath = hierarchy path),
        ///     số đo bằng ctx.Metric(...). Vòng lặp dài phải <c>await StaticCheckUtil.Yield(ctx, i)</c> để UI còn
        ///     phản hồi và nút Dừng có tác dụng. TUYỆT ĐỐI không sửa/lưu asset.
        /// </summary>
        Task Run(AutoTestContext ctx);
    }

    /// <summary>Suite "Kiểm tra tĩnh" — gom mọi IStaticRule.</summary>
    public sealed class StaticCheckSuite : AutoTestSuite
    {
        public override string Id => "static";
        public override string DisplayName => "Kiểm tra tĩnh";

        public override string Description =>
            "Quét asset, cấu hình và code KHÔNG cần vào Play: missing script/reference, material hỏng (magenta), " +
            "texture/audio nặng, CSV lệch cột, localize thiếu key/placeholder, Player Settings release, " +
            "GUID trùng, shader lỗi, luật code.";

        public override ExecutionMode Mode => ExecutionMode.Edit;
        public override int Order => 10;
        public override string Icon => "d_Search Icon";
        public override bool SupportsDevice => false;

        public override IEnumerable<AutoTestCase> BuildCases(AutoTestBuildContext ctx)
        {
            foreach (var rule in CreateRules())
            {
                var r = rule;
                yield return new AutoTestCase(r.Id, r.Name, r.Description, c => r.Run(c), r.Category, 600f);
            }
        }

        public static List<IStaticRule> CreateRules()
        {
            var rules = new List<IStaticRule>();
            foreach (var t in TypeCache.GetTypesDerivedFrom<IStaticRule>())
            {
                if (t.IsAbstract || t.IsInterface || t.GetConstructor(Type.EmptyTypes) == null) continue;
                try
                {
                    rules.Add((IStaticRule)Activator.CreateInstance(t));
                }
                catch (Exception e)
                {
                    AutoTestLog.Warn($"Không tạo được luật tĩnh {t.FullName}: {e.Message}");
                }
            }

            return rules.OrderBy(r => r.Order).ThenBy(r => r.Name).ToList();
        }
    }

    /// <summary>Tiện ích dùng chung cho các luật tĩnh.</summary>
    public static class StaticCheckUtil
    {
        const int YIELD_EVERY = 40;

        /// <summary>Nhường 1 frame mỗi <see cref="YIELD_EVERY" /> phần tử + kiểm tra nút Dừng.</summary>
        public static async Task Yield(AutoTestContext ctx, int index, int total = 0, string label = null)
        {
            ctx.ThrowIfCancelled();
            if (index % YIELD_EVERY != 0) return;
            if (total > 0) ctx.Session.ReportProgress(ctx.Result, $"{label ?? ctx.Result.name} {index}/{total}");
            await ctx.NextFrame();
        }

        /// <summary>Path có nằm trong phạm vi quét (includeFolders, trừ excludeFolders) không.</summary>
        public static bool InScope(string assetPath, StaticCheckConfig cfg)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            var p = assetPath.Replace('\\', '/');
            if (!p.StartsWith("Assets/", StringComparison.Ordinal) && p != "Assets") return false;
            foreach (var ex in cfg.excludeFolders)
                if (!string.IsNullOrWhiteSpace(ex) && IsUnder(p, ex))
                    return false;
            if (cfg.includeFolders == null || cfg.includeFolders.Count == 0) return true;
            foreach (var inc in cfg.includeFolders)
                if (!string.IsNullOrWhiteSpace(inc) && IsUnder(p, inc))
                    return true;
            return false;
        }

        static bool IsUnder(string path, string folder)
        {
            var f = folder.Replace('\\', '/').TrimEnd('/');
            return path.Equals(f, StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(f + "/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Tìm asset theo filter AssetDatabase (vd "t:Prefab"), lọc theo phạm vi quét.</summary>
        public static List<string> FindAssets(string filter, StaticCheckConfig cfg)
        {
            var roots = cfg.includeFolders != null && cfg.includeFolders.Count > 0
                ? cfg.includeFolders.Where(f => AssetDatabase.IsValidFolder(f)).ToArray()
                : new[] { "Assets" };
            if (roots.Length == 0) roots = new[] { "Assets" };
            return AssetDatabase.FindAssets(filter, roots)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => InScope(p, cfg))
                .Distinct()
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Tìm file trên đĩa theo đuôi (nhanh hơn AssetDatabase cho quét text).</summary>
        public static List<string> FindFiles(StaticCheckConfig cfg, params string[] extensions)
        {
            var root = ProjectRoot();
            var result = new List<string>();
            var folders = cfg.includeFolders != null && cfg.includeFolders.Count > 0
                ? cfg.includeFolders
                : new List<string> { "Assets" };
            foreach (var folder in folders)
            {
                var abs = Path.Combine(root, folder);
                if (!Directory.Exists(abs)) continue;
                foreach (var ext in extensions)
                foreach (var file in Directory.EnumerateFiles(abs, "*" + ext, SearchOption.AllDirectories))
                {
                    var rel = ToAssetPath(file);
                    if (InScope(rel, cfg)) result.Add(rel);
                }
            }

            return result.Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
        }

        public static string ProjectRoot()
        {
            return Path.GetDirectoryName(UnityEngine.Application.dataPath)!.Replace('\\', '/');
        }

        public static string ToAssetPath(string absolute)
        {
            var root = ProjectRoot() + "/";
            var p = absolute.Replace('\\', '/');
            return p.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? p.Substring(root.Length) : p;
        }

        public static string AbsolutePath(string assetPath)
        {
            return Path.Combine(ProjectRoot(), assetPath).Replace('\\', '/');
        }

        /// <summary>Đường dẫn hierarchy "Root/Child/Leaf".</summary>
        public static string HierarchyPath(UnityEngine.Transform t)
        {
            if (t == null) return "";
            var parts = new List<string>();
            while (t != null)
            {
                parts.Add(t.name);
                t = t.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
