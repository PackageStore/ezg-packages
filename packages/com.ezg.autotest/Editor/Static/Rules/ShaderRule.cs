using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Shader lỗi biên dịch (trong phạm vi quét) và shader có quá 128 local keyword (quét MỌI shader dưới
    ///     Assets, kể cả third-party — nguồn thường gặp của lỗi rò bộ nhớ khi compile variant trên Unity 6).
    /// </summary>
    public sealed class ShaderRule : IStaticRule
    {
        const string ISSUE_CATEGORY = "Asset.Shader";
        const int MAX_SAFE_KEYWORDS = 128;
        const int MAX_MESSAGES_PER_SHADER = 5;
        const int MAX_WARNING_SHADERS_LISTED = 30;

        public string Id => "shaders";
        public string Name => "Shader lỗi / quá nhiều keyword";

        public string Description =>
            "Shader trong phạm vi quét có lỗi biên dịch (render magenta), và mọi shader trong Assets có hơn 128 " +
            "keyword (Unity 6 rò bộ nhớ khi compile variant, spam cảnh báo allocator).";

        public string Category => "Asset";
        public int Order => 35;

        public async Task Run(AutoTestContext ctx)
        {
            var cfg = ctx.Config.staticCheck;
            var sink = new AssetIssueSink(ctx, ISSUE_CATEGORY);
            var paths = AssetDatabase.FindAssets("t:Shader", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            var inScope = 0;
            var withErrors = 0;
            var tooManyKeywords = 0;
            var maxKeywords = 0L;
            var warnings = new List<string>();
            for (var i = 0; i < paths.Count; i++)
            {
                await StaticCheckUtil.Yield(ctx, i, paths.Count, "Quét shader");
                var path = paths[i];
                try
                {
                    var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                    if (shader == null) continue;

                    if (StaticCheckUtil.InScope(path, cfg))
                    {
                        inScope++;
                        if (CheckMessages(shader, path, sink, warnings)) withErrors++;
                    }

                    long keywords = shader.keywordSpace.keywordCount;
                    if (keywords > maxKeywords) maxKeywords = keywords;
                    if (keywords > MAX_SAFE_KEYWORDS)
                    {
                        tooManyKeywords++;
                        sink.Report(Severity.Major, "Shader có >128 keyword (lỗi rò bộ nhớ của Unity 6)",
                            $"Shader '{shader.name}' khai báo {keywords} local keyword. Unity 6 rò bộ nhớ khi compile " +
                            "variant của shader quá 128 keyword (cảnh báo 'TLS Allocator … unfreed allocations'). Comment " +
                            "bớt shader_feature_local không material/script nào bật. Lưu ý: update/reimport plugin sẽ " +
                            "ghi đè lại chỗ sửa.", path, null, $"≤ {MAX_SAFE_KEYWORDS} keyword",
                            keywords.ToString(CultureInfo.InvariantCulture));
                    }
                }
                catch (Exception e)
                {
                    sink.ReportUnreadable(path, e);
                }
            }

            if (warnings.Count > 0)
                ctx.Report(Severity.Info, ISSUE_CATEGORY, "Shader có cảnh báo biên dịch",
                    $"{warnings.Count} shader trong phạm vi có cảnh báo khi biên dịch (không chặn build, nên dọn):" +
                    AssetRuleUtil.BulletList(warnings, MAX_WARNING_SHADERS_LISTED));

            ctx.Metric("Shader trong phạm vi", inScope);
            ctx.Metric("Shader toàn Assets", paths.Count);
            ctx.Metric("Shader lỗi biên dịch", withErrors);
            ctx.Metric("Shader >128 keyword", tooManyKeywords);
            ctx.Metric("Keyword nhiều nhất trong 1 shader", maxKeywords);
            sink.Flush();
        }

        /// <summary>True nếu shader có lỗi biên dịch (đã ghi issue).</summary>
        static bool CheckMessages(Shader shader, string path, AssetIssueSink sink, List<string> warnings)
        {
            var errors = new List<string>();
            var warningCount = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var msg in ShaderUtil.GetShaderMessages(shader))
            {
                if (msg.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                {
                    var line = string.IsNullOrEmpty(msg.file)
                        ? msg.message
                        : $"{msg.message} ({msg.file}:{msg.line}, {msg.platform})";
                    if (seen.Add(line)) errors.Add(line);
                }
                else
                {
                    warningCount++;
                }
            }

            if (warningCount > 0) warnings.Add($"{path} — {warningCount} cảnh báo");

            if (errors.Count == 0 && !ShaderUtil.ShaderHasError(shader)) return false;
            var detail = errors.Count > 0
                ? AssetRuleUtil.BulletList(errors, MAX_MESSAGES_PER_SHADER)
                : "\n(Unity báo shader có lỗi nhưng không còn giữ thông điệp — reimport shader để xem chi tiết.)";
            sink.Report(Severity.Major, "Shader lỗi biên dịch",
                $"Shader '{shader.name}' biên dịch lỗi → mọi material dùng nó render magenta." + detail, path, null,
                "Biên dịch không lỗi", $"{Math.Max(errors.Count, 1)} lỗi");
            return true;
        }
    }
}
