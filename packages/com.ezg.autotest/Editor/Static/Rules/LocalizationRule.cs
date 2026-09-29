using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Dữ liệu localize kiểu template EZG: thư mục LocalizationData/&lt;ngôn ngữ&gt;/&lt;tab&gt;.csv, mỗi dòng
    ///     <c>key~value</c>. So từng ngôn ngữ với ngôn ngữ gốc (en): thiếu tab/key, giá trị rỗng, key trùng, placeholder
    ///     {0} lệch, thẻ rich-text lệch; và key gắn trên prefab (LocalizeHelper) không tồn tại.
    /// </summary>
    public sealed class LocalizationRule : IStaticRule
    {
        const string CATEGORY = "Data.Localize";
        const string DEFAULT_FOLDER_NAME = "LocalizationData";
        const string REFERENCE_LANGUAGE = "en";
        const string HELPER_TYPE_NAME = "LocalizeHelper";
        const string HELPER_KEY_FIELD = "Key";
        const int KEY_LIST_CAP = 30;
        const int ISSUES_PER_KIND_PER_FILE = 20;
        const int PLACEHOLDER_ISSUE_CAP = 150;
        const int PREFAB_KEY_ISSUE_CAP = 200;
        const int LOCATIONS_PER_KEY = 8;
        const double STALE_TOLERANCE_SECONDS = 2.0;

        // {0}, {1:N0}, {2,5} → chỉ số placeholder của string.Format.
        static readonly Regex PLACEHOLDER = new(@"\{(\d+)(?:[,:][^{}]*)?\}", RegexOptions.CultureInvariant);

        // Thẻ rich-text có cặp đóng/mở (sprite/quad tự đóng nên không tính).
        static readonly Regex RICH_TAG = new(@"<\s*(/?)\s*(color|b|i|u|s|size|sup|sub|mark|font|material)\b[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public string Id => "localization";
        public string Name => "Localize: key, bản dịch, placeholder";

        public string Description =>
            "So mọi ngôn ngữ với ngôn ngữ gốc (en): thiếu tab/key, giá trị rỗng, key trùng, placeholder {0} lệch (crash " +
            "string.Format), thẻ rich-text lệch, lỗi định dạng làm importer cắt chữ; key gắn trên prefab không tồn tại.";

        public string Category => "Dữ liệu";
        public int Order => 210;

        public async Task Run(AutoTestContext ctx)
        {
            var cfg = ctx.Config.staticCheck;
            var roots = FindRoots(ctx, cfg);
            if (roots.Count == 0)
                ctx.Skip("Không tìm thấy thư mục localize (LocalizationData/<ngôn ngữ>/<tab>.csv) trong phạm vi quét — " +
                         "đặt staticCheck.localizationFolder nếu project để chỗ khác.");

            var referenceKeys = new HashSet<string>(StringComparer.Ordinal);
            var totals = new int[3]; // ngôn ngữ, key, tab
            foreach (var root in roots) await CheckRoot(ctx, root, referenceKeys, totals);

            await CheckPrefabKeys(ctx, cfg, referenceKeys);

            ctx.Metric("Ngôn ngữ", totals[0]);
            ctx.Metric("Key (ngôn ngữ gốc)", totals[1]);
            ctx.Metric("Tab", totals[2]);
        }

        #region Tìm thư mục

        static List<string> FindRoots(AutoTestContext ctx, StaticCheckConfig cfg)
        {
            var result = new List<string>();
            if (!string.IsNullOrWhiteSpace(cfg.localizationFolder))
            {
                var configured = cfg.localizationFolder.Trim().Replace('\\', '/').TrimEnd('/');
                if (!Directory.Exists(StaticCheckUtil.AbsolutePath(configured)))
                    ctx.Report(Severity.Major, CATEGORY + ".Config", "Thư mục localize trong settings không tồn tại",
                        $"staticCheck.localizationFolder = \"{configured}\" không có trên đĩa — không kiểm tra được localize.",
                        configured);
                else if (LanguageFolders(configured).Count == 0)
                    ctx.Report(Severity.Major, CATEGORY + ".Config", "Thư mục localize không có dữ liệu ngôn ngữ",
                        $"\"{configured}\" không có thư mục con nào chứa file .csv (<ngôn ngữ>/<tab>.csv).", configured);
                else
                    result.Add(configured);
                return result;
            }

            var folders = cfg.includeFolders != null && cfg.includeFolders.Count > 0
                ? cfg.includeFolders
                : new List<string> { "Assets" };
            foreach (var folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                var abs = StaticCheckUtil.AbsolutePath(folder.Trim());
                if (!Directory.Exists(abs)) continue;
                foreach (var dir in Directory.EnumerateDirectories(abs, DEFAULT_FOLDER_NAME, SearchOption.AllDirectories))
                {
                    var rel = StaticCheckUtil.ToAssetPath(dir);
                    if (StaticCheckUtil.InScope(rel, cfg) && LanguageFolders(rel).Count > 0 && !result.Contains(rel))
                        result.Add(rel);
                }
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>Thư mục con có ít nhất một file .csv = một ngôn ngữ.</summary>
        static List<string> LanguageFolders(string root)
        {
            var list = new List<string>();
            var abs = StaticCheckUtil.AbsolutePath(root);
            if (!Directory.Exists(abs)) return list;
            foreach (var dir in Directory.GetDirectories(abs))
                if (Directory.EnumerateFiles(dir, "*.csv").Any())
                    list.Add(Path.GetFileName(dir));
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        #endregion

        #region Parse

        sealed class Entry
        {
            public string Value;
            public int Line;
        }

        sealed class TabData
        {
            public string Path;
            public readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
        }

        sealed class LangData
        {
            public string Name;
            public string Folder;
            public readonly Dictionary<string, TabData> Tabs = new(StringComparer.OrdinalIgnoreCase);

            public int KeyCount
            {
                get
                {
                    var n = 0;
                    foreach (var t in Tabs.Values) n += t.Entries.Count;
                    return n;
                }
            }
        }

        static TabData ParseTab(AutoTestContext ctx, string path)
        {
            var tab = new TabData { Path = path };
            var text = DataRuleUtil.ReadUtf8(StaticCheckUtil.AbsolutePath(path), out var invalidUtf8);
            if (invalidUtf8)
                ctx.Report(Severity.Major, CATEGORY + ".Encoding", "File localize không phải UTF-8",
                    "Có byte không hợp lệ theo UTF-8 → chữ có dấu/CJK/Thái hiển thị lỗi. Tải lại từ sheet hoặc lưu lại dạng UTF-8.",
                    path);

            if (string.IsNullOrWhiteSpace(text.TrimStart('﻿')))
            {
                ctx.Report(Severity.Major, CATEGORY + ".Empty", "File localize rỗng",
                    "Không có cả header key~value — cả tab không có bản dịch nào.", path);
                return tab;
            }

            var parsed = CsvTextParser.Parse(text, '~');
            var budget = new IssueBudget(ISSUES_PER_KIND_PER_FILE);
            ReportQuoteProblems(ctx, path, parsed, budget);

            var records = parsed.Records;
            var start = 0;
            if (records.Count > 0)
            {
                var head = records[0].Cells;
                var headerLoose = head.Count >= 2 && head[0].Trim() == "key" && head[1].Trim() == "value";
                var headerExact = headerLoose && head[0] == "key" && head[1] == "value";
                if (headerLoose) start = 1;
                if (headerLoose && !headerExact)
                    ctx.Report(Severity.Major, CATEGORY + ".Header", "Header key~value có khoảng trắng thừa",
                        "Importer không trim tên cột ⇒ không khớp field key/value, cả tab nạp giá trị rỗng. Sửa dòng đầu thành đúng \"key~value\".",
                        DataRuleUtil.LineLocation(path, records[0].Line), null, "key~value", string.Join("~", head));
                else if (!headerLoose)
                    ctx.Report(Severity.Critical, CATEGORY + ".Header", "Thiếu header key~value",
                        $"Dòng đầu là \"{DataRuleUtil.Truncate(string.Join("~", head), 80)}\". Importer lấy dòng đầu làm tên cột ⇒ không tìm thấy cột key/value, cả tab nạp sai. Thêm dòng \"key~value\" lên đầu file.",
                        DataRuleUtil.LineLocation(path, records[0].Line), null, "key~value");
            }

            var duplicates = new List<string>();
            var extraTilde = new List<string>();
            var spacedKeys = new List<string>();
            var emptyKeyLines = new List<string>();
            var ignoredRows = new List<string>();
            var firstDuplicateLine = 0;
            for (var r = start; r < records.Count; r++)
            {
                var rec = records[r];
                if (parsed.Unclosed && rec.Line >= parsed.UnclosedLine) break;
                var cells = rec.Cells;
                // Dòng trắng hoàn toàn thì importer bỏ qua; dòng "~" vẫn thành một record key rỗng.
                if (cells.Count == 1 && string.IsNullOrWhiteSpace(cells[0])) continue;

                var key = cells[0];
                var value = cells.Count > 1 ? string.Join("~", cells.Skip(1)) : "";
                if (DataRuleUtil.IsImporterCommentRow(cells))
                {
                    ignoredRows.Add($"{key} (dòng {rec.Line})");
                    continue;
                }

                if (key.Trim().Length == 0)
                {
                    emptyKeyLines.Add(rec.Line.ToString());
                    continue;
                }

                if (key.Trim().Length != key.Length) spacedKeys.Add($"\"{key}\" (dòng {rec.Line})");
                if (cells.Count > 2) extraTilde.Add($"{key} (dòng {rec.Line})");

                if (tab.Entries.TryGetValue(key, out var existing))
                {
                    duplicates.Add($"{key} (dòng {existing.Line}, {rec.Line})");
                    if (firstDuplicateLine == 0) firstDuplicateLine = rec.Line;
                    continue;
                }

                tab.Entries[key] = new Entry { Value = value, Line = rec.Line };
            }

            if (duplicates.Count > 0)
                ctx.Report(Severity.Critical, CATEGORY + ".DuplicateKey", "Key trùng trong file localize",
                    $"{duplicates.Count} key trùng: {DataRuleUtil.JoinCapped(duplicates, KEY_LIST_CAP)}. Importer (Dictionary.Add) ném lỗi ở key trùng đầu tiên ⇒ mọi key phía sau trong tab KHÔNG được nạp.",
                    DataRuleUtil.LineLocation(path, firstDuplicateLine));
            if (emptyKeyLines.Count > 0)
                ctx.Report(Severity.Major, CATEGORY + ".EmptyKey", "Dòng có giá trị nhưng không có key",
                    $"Dòng: {DataRuleUtil.JoinCapped(emptyKeyLines, KEY_LIST_CAP)}. Importer thêm key rỗng — từ dòng thứ hai sẽ trùng key và hỏng cả tab.",
                    path);
            if (extraTilde.Count > 0)
                ctx.Report(Severity.Major, CATEGORY + ".Tilde", "Giá trị chứa dấu \"~\" — importer cắt mất phần sau",
                    $"\"~\" là ký tự phân cách của file localize; phần sau dấu \"~\" thứ hai bị bỏ. Key: {DataRuleUtil.JoinCapped(extraTilde, KEY_LIST_CAP)}",
                    path);
            if (spacedKeys.Count > 0)
                ctx.Report(Severity.Minor, CATEGORY + ".KeySpace", "Key có khoảng trắng thừa",
                    $"Tra key chính xác từng ký tự nên key có khoảng trắng đầu/cuối sẽ không bao giờ khớp: {DataRuleUtil.JoinCapped(spacedKeys, KEY_LIST_CAP)}",
                    path);
            if (ignoredRows.Count > 0)
                ctx.Report(Severity.Major, CATEGORY + ".Ignored", "Dòng bị importer coi là comment và bỏ qua",
                    $"Có ô đúng bằng \"/\" hoặc \"//\" ⇒ importer bỏ cả dòng: {DataRuleUtil.JoinCapped(ignoredRows, KEY_LIST_CAP)}",
                    path);
            budget.ReportOverflow(ctx, CATEGORY, path);
            return tab;
        }

        static void ReportQuoteProblems(AutoTestContext ctx, string path, CsvParseResult parsed, IssueBudget budget)
        {
            // Cùng một dòng có thể vừa "nháy giữa ô" vừa "chữ sau nháy đóng" ⇒ gộp thành một issue.
            var reportedLines = new HashSet<int>();
            foreach (var p in parsed.Problems)
            {
                var location = DataRuleUtil.LineLocation(path, p.Line);
                if (p.Kind == CsvProblemKind.UnclosedQuote)
                {
                    ctx.Report(Severity.Critical, CATEGORY + ".Quote", "Dấu nháy kép không đóng trong file localize",
                            $"Dấu \" ở dòng {p.Line} không có dấu đóng — importer nuốt mọi dòng phía sau vào một giá trị (mất toàn bộ key sau đó). Dùng dấu nháy cong “ ” hoặc bao cả giá trị trong \"...\" với \"\" cho dấu nháy.",
                            location)
                        .WithContentId(ctx, path, "unclosed");
                    continue;
                }

                if (!reportedLines.Add(p.Line) || !budget.Take("Dấu nháy trong giá trị")) continue;
                ctx.Report(Severity.Major, CATEGORY + ".Quote", "Giá trị chứa dấu nháy kép \" — importer làm mất chữ",
                        $"Dòng {p.Line}: importer EZG hiểu \" là mở/đóng chuỗi trích dẫn ⇒ dấu nháy biến mất, phần chữ sau dấu nháy đóng bị cắt. Dùng dấu nháy cong “ ” hoặc bao cả giá trị trong \"...\" với \"\" cho dấu nháy.",
                        location)
                    .WithContentId(ctx, path, "quote|" + DataRuleUtil.RecordTextAt(parsed, p.Line));
            }
        }

        #endregion

        #region So sánh ngôn ngữ

        static async Task CheckRoot(AutoTestContext ctx, string root, HashSet<string> referenceKeys, int[] totals)
        {
            var langs = new List<LangData>();
            var langNames = LanguageFolders(root);
            var index = 0;
            foreach (var name in langNames)
            {
                var lang = new LangData { Name = name, Folder = root + "/" + name };
                var files = Directory.GetFiles(StaticCheckUtil.AbsolutePath(lang.Folder), "*.csv")
                    .Where(f => f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f, StringComparer.Ordinal);
                foreach (var file in files)
                {
                    await StaticCheckUtil.Yield(ctx, index++, 0, "Localize");
                    var path = lang.Folder + "/" + Path.GetFileName(file);
                    try
                    {
                        lang.Tabs[Path.GetFileNameWithoutExtension(file)] = ParseTab(ctx, path);
                    }
                    catch (Exception e) when (!DataRuleUtil.IsFlowControl(e))
                    {
                        ctx.Report(Severity.Info, CATEGORY + ".Read", "Không đọc được file", e.Message, path);
                    }
                }

                langs.Add(lang);
            }

            if (langs.Count == 0) return;
            var reference = langs.FirstOrDefault(l =>
                                string.Equals(l.Name, REFERENCE_LANGUAGE, StringComparison.OrdinalIgnoreCase)) ??
                            langs.OrderByDescending(l => l.KeyCount).First();
            if (!string.Equals(reference.Name, REFERENCE_LANGUAGE, StringComparison.OrdinalIgnoreCase))
                ctx.Log($"{root}: không có thư mục \"{REFERENCE_LANGUAGE}\" — lấy \"{reference.Name}\" (nhiều key nhất) làm ngôn ngữ gốc.");

            foreach (var tab in reference.Tabs.Values)
            foreach (var key in tab.Entries.Keys)
                referenceKeys.Add(key);

            totals[0] += langs.Count;
            totals[1] += reference.KeyCount;
            totals[2] += reference.Tabs.Count;

            var placeholderIssues = 0;
            var placeholderOverflow = 0;
            foreach (var lang in langs)
            {
                await StaticCheckUtil.Yield(ctx, index++, 0, "Localize");
                CheckValues(ctx, lang);
                if (lang == reference) continue;

                foreach (var pair in reference.Tabs)
                {
                    if (!lang.Tabs.TryGetValue(pair.Key, out var tab))
                    {
                        ctx.Report(Severity.Major, CATEGORY + ".MissingTab", "Ngôn ngữ thiếu file tab",
                                $"\"{lang.Name}\" không có {pair.Key}.csv (ngôn ngữ gốc \"{reference.Name}\" có {pair.Value.Entries.Count} key) → mọi text của tab này hiện key/fallback khi chọn ngôn ngữ đó.",
                                lang.Folder, null, pair.Key + ".csv", "không có")
                            .WithContentId(ctx, lang.Folder, pair.Key);
                        continue;
                    }

                    CompareTab(ctx, lang, reference, pair.Value, tab, ref placeholderIssues, ref placeholderOverflow);
                }

                var extraTabs = lang.Tabs.Keys.Where(t => !reference.Tabs.ContainsKey(t)).ToList();
                if (extraTabs.Count > 0)
                    ctx.Report(Severity.Info, CATEGORY + ".ExtraTab", "Tab không có ở ngôn ngữ gốc",
                            $"\"{lang.Name}\" có {string.Join(", ", extraTabs.Select(t => t + ".csv"))} nhưng \"{reference.Name}\" không có.",
                            lang.Folder)
                        .WithContentId(ctx, lang.Folder, string.Join(",", extraTabs));
            }

            if (placeholderOverflow > 0)
                ctx.Report(Severity.Info, CATEGORY + ".Placeholder", "Còn placeholder lệch không liệt kê hết",
                    $"Thêm {placeholderOverflow} key lệch placeholder (chỉ liệt kê {PLACEHOLDER_ISSUE_CAP} key đầu).",
                    root);

            CheckImportFreshness(ctx, root, langs);
        }

        /// <summary>Kiểm tra trong từng file: giá trị rỗng, thẻ rich-text không cân.</summary>
        static void CheckValues(AutoTestContext ctx, LangData lang)
        {
            foreach (var pair in lang.Tabs)
            {
                var tab = pair.Value;
                var empty = new List<string>();
                var unbalanced = new List<string>();
                foreach (var e in tab.Entries)
                {
                    if (string.IsNullOrWhiteSpace(e.Value.Value))
                    {
                        empty.Add(e.Key);
                        continue;
                    }

                    if (e.Value.Value.IndexOf('<') < 0) continue;
                    var reason = Imbalance(CountTags(e.Value.Value));
                    if (reason != null) unbalanced.Add($"{e.Key} ({reason})");
                }

                if (empty.Count > 0)
                    ctx.Report(Severity.Minor, CATEGORY + ".EmptyValue", "Giá trị dịch rỗng",
                            $"{empty.Count} key của \"{lang.Name}\" không có chữ → hiển thị trống/fallback: {DataRuleUtil.JoinCapped(empty, KEY_LIST_CAP)}",
                            tab.Path)
                        .WithContentId(ctx, tab.Path, "empty");
                if (unbalanced.Count > 0)
                    ctx.Report(Severity.Minor, CATEGORY + ".RichText", "Thẻ rich-text không cân đóng/mở",
                            $"{unbalanced.Count} giá trị có thẻ mở/đóng không khớp (chữ sẽ hiện nguyên thẻ hoặc đổi màu/đậm lan sang phần sau): {DataRuleUtil.JoinCapped(unbalanced, KEY_LIST_CAP)}",
                            tab.Path)
                        .WithContentId(ctx, tab.Path, "unbalanced");
            }
        }

        static void CompareTab(AutoTestContext ctx, LangData lang, LangData reference, TabData refTab, TabData tab,
            ref int placeholderIssues, ref int placeholderOverflow)
        {
            var missing = new List<string>();
            var tagDiff = new List<string>();
            foreach (var pair in refTab.Entries)
            {
                if (!tab.Entries.TryGetValue(pair.Key, out var entry))
                {
                    missing.Add(pair.Key);
                    continue;
                }

                var refValue = pair.Value.Value ?? "";
                var value = entry.Value ?? "";
                if (string.IsNullOrWhiteSpace(refValue) || string.IsNullOrWhiteSpace(value)) continue;

                var refPlaceholders = Placeholders(refValue);
                var placeholders = Placeholders(value);
                if (!refPlaceholders.SetEquals(placeholders))
                {
                    if (placeholderIssues >= PLACEHOLDER_ISSUE_CAP)
                    {
                        placeholderOverflow++;
                    }
                    else
                    {
                        placeholderIssues++;
                        var crash = placeholders.Any(p => !refPlaceholders.Contains(p));
                        ctx.Report(Severity.Major, CATEGORY + ".Placeholder",
                                "Placeholder lệch → string.Format sẽ crash/hiển thị sai",
                                $"Key \"{pair.Key}\" ({lang.Name}): gốc \"{reference.Name}\" có {Describe(refPlaceholders)}, bản dịch có {Describe(placeholders)}"
                                + (crash
                                    ? " — bản dịch dùng chỉ số mà code không truyền ⇒ FormatException."
                                    : " — thiếu số liệu trong câu hiển thị.")
                                + $"\nGốc: {DataRuleUtil.Truncate(refValue, 120)}\nDịch: {DataRuleUtil.Truncate(value, 120)}",
                                DataRuleUtil.LineLocation(tab.Path, entry.Line), pair.Key, Describe(refPlaceholders),
                                Describe(placeholders))
                            .WithContentId(ctx, tab.Path, pair.Key);
                    }
                }

                if (refValue.IndexOf('<') < 0 && value.IndexOf('<') < 0) continue;
                var refTags = CountTags(refValue);
                var tags = CountTags(value);
                var diff = TagDifference(refTags, tags);
                if (diff != null) tagDiff.Add($"{pair.Key} ({diff})");
            }

            if (missing.Count > 0)
                ctx.Report(Severity.Minor, CATEGORY + ".MissingKey", "Thiếu key so với ngôn ngữ gốc",
                        $"{missing.Count} key có ở \"{reference.Name}\" nhưng thiếu ở \"{lang.Name}\" (hiện key/fallback khi chọn ngôn ngữ này): {DataRuleUtil.JoinCapped(missing, KEY_LIST_CAP)}",
                        tab.Path, null, refTab.Entries.Count + " key", tab.Entries.Count + " key")
                    .WithContentId(ctx, tab.Path, "missing");

            var extra = tab.Entries.Keys.Where(k => !refTab.Entries.ContainsKey(k)).ToList();
            if (extra.Count > 0)
                ctx.Report(Severity.Info, CATEGORY + ".ExtraKey", "Key thừa (không có ở ngôn ngữ gốc)",
                        $"{extra.Count} key chỉ có ở \"{lang.Name}\" — có thể là key cũ đã xoá khỏi bản gốc: {DataRuleUtil.JoinCapped(extra, KEY_LIST_CAP)}",
                        tab.Path)
                    .WithContentId(ctx, tab.Path, "extra");

            if (tagDiff.Count > 0)
                ctx.Report(Severity.Minor, CATEGORY + ".RichText", "Thẻ rich-text khác ngôn ngữ gốc",
                        $"{tagDiff.Count} giá trị có số thẻ <color>/<b>/<size>… khác bản \"{reference.Name}\": {DataRuleUtil.JoinCapped(tagDiff, KEY_LIST_CAP)}",
                        tab.Path)
                    .WithContentId(ctx, tab.Path, "tag-diff");
        }

        /// <summary>CSV localize mới hơn asset đã sinh (../Resources/&lt;thư mục&gt;/&lt;ngôn ngữ&gt;/&lt;tab&gt;.asset).</summary>
        static void CheckImportFreshness(AutoTestContext ctx, string root, List<LangData> langs)
        {
            var parent = Path.GetDirectoryName(root)?.Replace('\\', '/') ?? "";
            var assetRoot = (parent.Length > 0 ? parent + "/" : "") + "Resources/" + Path.GetFileName(root);
            if (!Directory.Exists(StaticCheckUtil.AbsolutePath(assetRoot))) return;

            var stale = new List<string>();
            foreach (var lang in langs)
            foreach (var pair in lang.Tabs)
            {
                var asset = $"{assetRoot}/{lang.Name}/{pair.Key}.asset";
                var assetAbs = StaticCheckUtil.AbsolutePath(asset);
                if (!File.Exists(assetAbs)) continue;
                var delta = (File.GetLastWriteTimeUtc(StaticCheckUtil.AbsolutePath(pair.Value.Path)) -
                             File.GetLastWriteTimeUtc(assetAbs)).TotalSeconds;
                if (delta > STALE_TOLERANCE_SECONDS) stale.Add($"{lang.Name}/{pair.Key}");
            }

            if (stale.Count > 0)
                ctx.Report(Severity.Major, CATEGORY + ".Import", "CSV localize mới hơn asset — chưa generate lại",
                    $"{stale.Count} file CSV sửa sau lần sinh asset trong {assetRoot} ⇒ game vẫn hiển thị bản dịch cũ: {DataRuleUtil.JoinCapped(stale, KEY_LIST_CAP)}. Chạy lại bước generate asset localize. (Nếu vừa pull/checkout mà không sửa CSV, git có thể đã đặt lại giờ sửa file.)",
                    root);
        }

        #endregion

        #region Placeholder + rich text

        static HashSet<string> Placeholders(string value)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in PLACEHOLDER.Matches(value)) set.Add(m.Groups[1].Value);
            return set;
        }

        static string Describe(HashSet<string> placeholders)
        {
            if (placeholders.Count == 0) return "không có placeholder";
            return string.Join(" ", placeholders.OrderBy(p => p.Length).ThenBy(p => p, StringComparer.Ordinal)
                .Select(p => "{" + p + "}"));
        }

        /// <summary>tag → (số thẻ mở, số thẻ đóng).</summary>
        static Dictionary<string, int[]> CountTags(string value)
        {
            var result = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in RICH_TAG.Matches(value))
            {
                var tag = m.Groups[2].Value.ToLowerInvariant();
                if (!result.TryGetValue(tag, out var counts)) result[tag] = counts = new int[2];
                counts[m.Groups[1].Value.Length > 0 ? 1 : 0]++;
            }

            return result;
        }

        static string Imbalance(Dictionary<string, int[]> tags)
        {
            var parts = new List<string>();
            foreach (var pair in tags)
                if (pair.Value[0] != pair.Value[1])
                    parts.Add($"<{pair.Key}> mở {pair.Value[0]}/đóng {pair.Value[1]}");
            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        static string TagDifference(Dictionary<string, int[]> reference, Dictionary<string, int[]> translation)
        {
            var parts = new List<string>();
            foreach (var tag in reference.Keys.Union(translation.Keys, StringComparer.OrdinalIgnoreCase))
            {
                reference.TryGetValue(tag, out var r);
                translation.TryGetValue(tag, out var t);
                var ro = r?[0] ?? 0;
                var rc = r?[1] ?? 0;
                var to = t?[0] ?? 0;
                var tc = t?[1] ?? 0;
                if (ro != to || rc != tc) parts.Add($"<{tag}> gốc {ro}/{rc}, dịch {to}/{tc}");
            }

            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        #endregion

        #region Key trên prefab

        static async Task CheckPrefabKeys(AutoTestContext ctx, StaticCheckConfig cfg, HashSet<string> referenceKeys)
        {
            if (referenceKeys.Count == 0) return;
            var helperTypes = TypeCache.GetTypesDerivedFrom<MonoBehaviour>()
                .Where(t => t.Name == HELPER_TYPE_NAME && !t.IsAbstract).ToList();
            if (helperTypes.Count == 0)
            {
                ctx.Report(Severity.Info, CATEGORY + ".PrefabKey",
                    $"Không có component {HELPER_TYPE_NAME} — bỏ qua kiểm tra key trên prefab",
                    $"Project không có MonoBehaviour tên {HELPER_TYPE_NAME} (component gắn key localize theo template EZG).");
                return;
            }

            var scriptGuids = ScriptGuids(helperTypes);
            var prefabs = StaticCheckUtil.FindAssets("t:Prefab", cfg);
            var candidates = new List<string>();
            for (var i = 0; i < prefabs.Count; i++)
            {
                await StaticCheckUtil.Yield(ctx, i, prefabs.Count, "Lọc prefab có key localize");
                if (MayContainHelper(prefabs[i], scriptGuids)) candidates.Add(prefabs[i]);
            }

            // key → danh sách "prefab › object" nơi dùng.
            var missing = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var firstLocation = new Dictionary<string, KeyValuePair<string, string>>(StringComparer.Ordinal);
            var fromText = new HashSet<string>(StringComparer.Ordinal);
            var checkedKeys = 0;
            var fieldMissingReported = false;
            for (var i = 0; i < candidates.Count; i++)
            {
                await StaticCheckUtil.Yield(ctx, i, candidates.Count, "Key localize trên prefab");
                var path = candidates[i];
                try
                {
                    var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (root == null) continue;
                    foreach (var type in helperTypes)
                    foreach (var comp in root.GetComponentsInChildren(type, true))
                    {
                        if (comp == null) continue;
                        string key;
                        using (var so = new SerializedObject(comp))
                        {
                            var prop = so.FindProperty(HELPER_KEY_FIELD);
                            if (prop == null || prop.propertyType != SerializedPropertyType.String)
                            {
                                if (!fieldMissingReported)
                                    ctx.Report(Severity.Info, CATEGORY + ".PrefabKey",
                                        $"{HELPER_TYPE_NAME} không có field \"{HELPER_KEY_FIELD}\" kiểu string — bỏ qua kiểm tra key trên prefab",
                                        type.FullName);
                                fieldMissingReported = true;
                                continue;
                            }

                            key = prop.stringValue;
                        }

                        var usesText = false;
                        if (string.IsNullOrEmpty(key))
                        {
                            key = TextOf(comp.gameObject);
                            usesText = true;
                        }

                        if (string.IsNullOrWhiteSpace(key)) continue;
                        checkedKeys++;
                        if (referenceKeys.Contains(key)) continue;

                        var objectPath = StaticCheckUtil.HierarchyPath(comp.transform);
                        if (!missing.TryGetValue(key, out var places))
                        {
                            missing[key] = places = new List<string>();
                            firstLocation[key] = new KeyValuePair<string, string>(path, objectPath);
                        }

                        places.Add(path + " › " + objectPath);
                        if (usesText) fromText.Add(key);
                    }
                }
                catch (Exception e) when (!DataRuleUtil.IsFlowControl(e))
                {
                    ctx.Report(Severity.Info, CATEGORY + ".Read", "Không đọc được prefab", e.Message, path);
                }
            }

            var reported = 0;
            foreach (var pair in missing.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (reported++ >= PREFAB_KEY_ISSUE_CAP) break;
                var first = firstLocation[pair.Key];
                var hint = ClosestKeyHint(pair.Key, referenceKeys);
                ctx.Report(Severity.Major, CATEGORY + ".PrefabKey", "Key localize không tồn tại",
                        $"Key \"{pair.Key}\"" +
                        (fromText.Contains(pair.Key) ? $" ({HELPER_TYPE_NAME} để trống Key → lấy chữ của Text làm key)" : "") +
                        $" không có trong tab nào của ngôn ngữ gốc ⇒ hiện \"<Category>: {pair.Key}\" trên màn hình.{hint} " +
                        $"Dùng ở {pair.Value.Count} chỗ: {DataRuleUtil.JoinCapped(pair.Value, LOCATIONS_PER_KEY, "; ")}",
                        first.Key, first.Value, "key có trong file localize", pair.Key)
                    .WithContentId(ctx, first.Key, pair.Key);
            }

            if (missing.Count > PREFAB_KEY_ISSUE_CAP)
                ctx.Report(Severity.Info, CATEGORY + ".PrefabKey", "Còn key localize thiếu không liệt kê hết",
                    $"Tổng {missing.Count} key không tồn tại, chỉ liệt kê {PREFAB_KEY_ISSUE_CAP} key đầu.");

            ctx.Metric("Key localize trên prefab", checkedKeys);
        }

        static List<string> ScriptGuids(List<Type> types)
        {
            var result = new List<string>();
            foreach (var type in types)
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript " + type.Name))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (script != null && script.GetClass() == type) result.Add(guid);
            }

            return result;
        }

        /// <summary>
        ///     Lọc nhanh bằng text YAML: prefab có GUID script của component, hoặc override field Key (variant/nested).
        ///     Không lọc được (không có GUID, prefab dạng binary) ⇒ coi như có.
        /// </summary>
        static bool MayContainHelper(string path, List<string> scriptGuids)
        {
            if (scriptGuids.Count == 0) return true;
            string text;
            try
            {
                text = File.ReadAllText(StaticCheckUtil.AbsolutePath(path));
            }
            catch (Exception)
            {
                return true;
            }

            if (!text.StartsWith("%YAML", StringComparison.Ordinal)) return true;
            foreach (var guid in scriptGuids)
                if (text.IndexOf(guid, StringComparison.Ordinal) >= 0)
                    return true;
            return text.IndexOf("propertyPath: " + HELPER_KEY_FIELD + "\n", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("propertyPath: " + HELPER_KEY_FIELD + "\r", StringComparison.Ordinal) >= 0;
        }

        static string TextOf(GameObject go)
        {
            var text = go.GetComponent<UnityEngine.UI.Text>();
            if (text != null) return text.text;
            var tmp = go.GetComponent<TMPro.TMP_Text>();
            return tmp != null ? tmp.text : null;
        }

        static string ClosestKeyHint(string key, HashSet<string> referenceKeys)
        {
            var trimmed = key.Trim();
            if (trimmed != key && referenceKeys.Contains(trimmed))
                return $" (Có key \"{trimmed}\" — Key trên prefab thừa khoảng trắng.)";
            foreach (var k in referenceKeys)
                if (string.Equals(k, trimmed, StringComparison.OrdinalIgnoreCase))
                    return $" (Có key \"{k}\" — khác hoa/thường.)";
            return "";
        }

        #endregion
    }
}
