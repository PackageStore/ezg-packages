using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Quét file .cs trong phạm vi theo các luật regex cấu hình trong settings (staticCheck.codeRules): bỏ qua
    ///     comment (//, ///, dòng bắt đầu bằng *, khối /* */) nhưng vẫn quét chuỗi (bắt secret hardcode).
    /// </summary>
    public sealed class CodeRulesRule : IStaticRule
    {
        const string CATEGORY_PREFIX = "Code.";
        const int ISSUES_PER_RULE = 200;
        const int MAX_LINE_LENGTH = 300;
        static readonly TimeSpan REGEX_TIMEOUT = TimeSpan.FromMilliseconds(250);

        public string Id => "code-rules";
        public string Name => "Luật code (regex)";

        public string Description =>
            "Quét code C# theo luật trong settings: DateTime.Now, async void, GameObject.Find, secret hardcode… " +
            "Mỗi vi phạm kèm file:dòng và dòng code.";

        public string Category => "Code";
        public int Order => 300;

        sealed class CompiledRule
        {
            public CodeRule Source;
            public Regex Pattern;
            public Regex Exclude;
            public int Hits;
            public int Reported;
        }

        public async Task Run(AutoTestContext ctx)
        {
            await StaticCheckUtil.Yield(ctx, 0);
            var cfg = ctx.Config.staticCheck;
            var rules = CompileRules(ctx, cfg);
            if (rules.Count == 0)
                ctx.Skip("Không có luật code nào đang bật (staticCheck.codeRules).");

            var files = StaticCheckUtil.FindFiles(cfg, ".cs");
            var totalLines = 0;
            var scanned = 0;
            var applicable = new List<CompiledRule>(rules.Count);
            for (var i = 0; i < files.Count; i++)
            {
                await StaticCheckUtil.Yield(ctx, i, files.Count, "Quét code");
                var path = files[i];
                applicable.Clear();
                foreach (var r in rules)
                    if (r.Exclude == null || !SafeIsMatch(r.Exclude, path))
                        applicable.Add(r);
                if (applicable.Count == 0) continue;

                try
                {
                    totalLines += ScanFile(ctx, path, applicable);
                    scanned++;
                }
                catch (Exception e) when (!DataRuleUtil.IsFlowControl(e))
                {
                    ctx.Report(Severity.Info, "Code.Read", "Không đọc được file", e.Message, path);
                }
            }

            foreach (var r in rules)
            {
                if (r.Hits > r.Reported)
                    ctx.Report(Severity.Info, CATEGORY_PREFIX + r.Source.id, "Còn vi phạm không liệt kê hết",
                        $"Luật \"{r.Source.id}\": {r.Hits} vi phạm, chỉ liệt kê {r.Reported} đầu tiên.");
                ctx.Metric("Vi phạm " + r.Source.id, r.Hits);
            }

            ctx.Metric("File .cs đã quét", scanned, "file");
            ctx.Metric("Dòng code", totalLines, "dòng");
        }

        static List<CompiledRule> CompileRules(AutoTestContext ctx, StaticCheckConfig cfg)
        {
            var result = new List<CompiledRule>();
            if (cfg.codeRules == null) return result;
            foreach (var rule in cfg.codeRules)
            {
                if (rule == null || !rule.enabled || string.IsNullOrWhiteSpace(rule.pattern)) continue;
                var id = string.IsNullOrWhiteSpace(rule.id) ? "rule" : rule.id.Trim();
                Regex pattern;
                try
                {
                    pattern = new Regex(rule.pattern, RegexOptions.CultureInvariant, REGEX_TIMEOUT);
                }
                catch (ArgumentException e)
                {
                    ctx.Report(Severity.Major, "Code.RuleConfig", "Luật code có regex sai",
                            $"Luật \"{id}\": pattern \"{rule.pattern}\" không hợp lệ ({e.Message}) — luật này bị bỏ qua.",
                            null, "Auto Test Settings/Kiểm tra tĩnh/codeRules/" + id)
                        .WithContentId(ctx, "", id);
                    continue;
                }

                Regex exclude = null;
                if (!string.IsNullOrWhiteSpace(rule.excludePattern))
                    try
                    {
                        exclude = new Regex(rule.excludePattern, RegexOptions.CultureInvariant, REGEX_TIMEOUT);
                    }
                    catch (ArgumentException e)
                    {
                        ctx.Report(Severity.Minor, "Code.RuleConfig", "Luật code có excludePattern sai",
                                $"Luật \"{id}\": excludePattern \"{rule.excludePattern}\" không hợp lệ ({e.Message}) — quét mọi file.",
                                null, "Auto Test Settings/Kiểm tra tĩnh/codeRules/" + id)
                            .WithContentId(ctx, "", id);
                    }

                result.Add(new CompiledRule
                {
                    Source = new CodeRule
                    {
                        id = id, pattern = rule.pattern, excludePattern = rule.excludePattern,
                        message = string.IsNullOrWhiteSpace(rule.message) ? $"Vi phạm luật code \"{id}\"" : rule.message,
                        severity = rule.severity, enabled = true
                    },
                    Pattern = pattern,
                    Exclude = exclude
                });
            }

            return result;
        }

        /// <summary>Trả số dòng của file.</summary>
        static int ScanFile(AutoTestContext ctx, string path, List<CompiledRule> rules)
        {
            var lines = File.ReadAllLines(StaticCheckUtil.AbsolutePath(path));
            var inBlockComment = false;
            // Dòng code giống hệt nhau trong cùng file ⇒ đánh số lần xuất hiện để fingerprint không trùng.
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var ln = 0; ln < lines.Length; ln++)
            {
                var raw = lines[ln];
                var wasInBlock = inBlockComment;
                var code = StripComments(raw, ref inBlockComment);
                var trimmed = raw.TrimStart();
                if (!wasInBlock && (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                                    trimmed.StartsWith("*", StringComparison.Ordinal)))
                    continue;
                if (code.Trim().Length == 0) continue;

                foreach (var rule in rules)
                {
                    if (!SafeIsMatch(rule.Pattern, code)) continue;
                    rule.Hits++;
                    if (rule.Reported >= ISSUES_PER_RULE) continue;
                    rule.Reported++;
                    var snippet = DataRuleUtil.Truncate(raw.Trim(), MAX_LINE_LENGTH);
                    var occurrenceKey = rule.Source.id + "|" + snippet;
                    occurrences.TryGetValue(occurrenceKey, out var nth);
                    occurrences[occurrenceKey] = nth + 1;
                    ctx.Report(rule.Source.severity, CATEGORY_PREFIX + rule.Source.id, rule.Source.message, snippet,
                            path + ":" + (ln + 1))
                        .WithContentId(ctx, path, snippet + (nth > 0 ? "#" + nth : ""));
                }
            }

            return lines.Length;
        }

        static bool SafeIsMatch(Regex regex, string input)
        {
            try
            {
                return regex.IsMatch(input);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        /// <summary>
        ///     Bỏ phần comment của một dòng (// cuối dòng, /* */ kể cả trải nhiều dòng), GIỮ nguyên chuỗi/ký tự literal
        ///     (dấu // trong "http://" không bị hiểu là comment).
        /// </summary>
        static string StripComments(string line, ref bool inBlock)
        {
            var sb = new StringBuilder(line.Length);
            var inString = false;
            var verbatim = false;
            var inChar = false;
            var n = line.Length;
            for (var i = 0; i < n; i++)
            {
                var c = line[i];
                var next = i + 1 < n ? line[i + 1] : '\0';
                if (inBlock)
                {
                    if (c == '*' && next == '/')
                    {
                        inBlock = false;
                        i++;
                    }

                    continue;
                }

                if (inString)
                {
                    sb.Append(c);
                    if (verbatim)
                    {
                        if (c == '"')
                        {
                            if (next == '"')
                            {
                                sb.Append(next);
                                i++;
                            }
                            else
                            {
                                inString = false;
                            }
                        }
                    }
                    else if (c == '\\' && i + 1 < n)
                    {
                        sb.Append(next);
                        i++;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (inChar)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < n)
                    {
                        sb.Append(next);
                        i++;
                    }
                    else if (c == '\'')
                    {
                        inChar = false;
                    }

                    continue;
                }

                if (c == '/' && next == '/') break;
                if (c == '/' && next == '*')
                {
                    inBlock = true;
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    verbatim = (i > 0 && line[i - 1] == '@') ||
                               (i > 1 && line[i - 1] == '$' && line[i - 2] == '@');
                }
                else if (c == '\'')
                {
                    inChar = true;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
