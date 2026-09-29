using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

namespace Ezg.AutoTest.Editor
{
    /// <summary>File trong phạm vi quét nặng hơn ngưỡng (làm phình repo/build) + đính kèm top file nặng nhất.</summary>
    public sealed class LargeFileRule : IStaticRule
    {
        const string ISSUE_CATEGORY = "Asset.LargeFile";
        const int TOP_FILES = 20;

        public string Id => "large-files";
        public string Name => "File quá nặng";

        public string Description =>
            "File trong phạm vi quét vượt dung lượng tối đa (làm phình repo, build và AssetBundle). Đính kèm danh " +
            "sách 20 file nặng nhất.";

        public string Category => "Asset";
        public int Order => 50;

        public async Task Run(AutoTestContext ctx)
        {
            var cfg = ctx.Config.staticCheck;
            var sink = new AssetIssueSink(ctx, ISSUE_CATEGORY);
            var files = AssetRuleUtil.EnumerateScopeFiles(cfg);
            var budget = new FrameBudget();
            var totalBytes = 0L;
            var limitMb = cfg.maxAssetFileMb;
            for (var i = 0; i < files.Count; i++)
            {
                await budget.Tick(ctx, i, files.Count, "Quét dung lượng file");
                var f = files[i];
                totalBytes += f.Size;
                var mb = AssetRuleUtil.ToMb(f.Size);
                if (mb > limitMb)
                    sink.Report(Severity.Minor, "File quá nặng",
                        $"File {Fmt(mb)} MB (ngưỡng {Fmt(limitMb)} MB). Nén/cắt bớt, tách ra tải qua AssetBundle, hoặc " +
                        "chuyển khỏi thư mục Assets nếu không dùng trong game.", f.Path, null, $"≤ {Fmt(limitMb)} MB",
                        $"{Fmt(mb)} MB");
            }

            var sorted = new List<ScopeFile>(files);
            sorted.Sort((a, b) => b.Size.CompareTo(a.Size));
            var csv = new StringBuilder("path,size_mb\n");
            for (var i = 0; i < sorted.Count && i < TOP_FILES; i++)
                csv.Append('"').Append(sorted[i].Path.Replace("\"", "\"\"")).Append("\",")
                    .Append(Fmt(AssetRuleUtil.ToMb(sorted[i].Size))).Append('\n');
            if (sorted.Count > 0) ctx.Attach("largest-files.csv", csv.ToString(), "Top file nặng nhất");

            ctx.Metric("File đã quét", files.Count);
            ctx.Metric("Tổng dung lượng", AssetRuleUtil.ToMb(totalBytes), "MB");
            ctx.Metric("File lớn nhất", sorted.Count > 0 ? AssetRuleUtil.ToMb(sorted[0].Size) : 0, "MB");
            sink.Flush();
        }

        static string Fmt(double v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
