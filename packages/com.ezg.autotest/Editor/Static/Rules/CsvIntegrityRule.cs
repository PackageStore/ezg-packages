using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     CSV config theo định dạng template EZG (dấu phẩy, MỘT dòng header snake_case, UTF-8, ô có thể bao nháy,
    ///     dòng có cột đầu trống = phần tử con của dòng trước): lỗi định dạng làm importer đọc sai/lệch cột, khoá
    ///     trùng, ô không phải số trong cột số, và CSV đã sửa nhưng chưa import lại thành asset.
    /// </summary>
    public sealed class CsvIntegrityRule : IStaticRule
    {
        const string CATEGORY = "Data.Csv";
        const int ISSUES_PER_KIND_PER_FILE = 20;
        const int LIST_CAP = 20;
        const int MIN_NUMERIC_SAMPLE = 5;
        const double NUMERIC_RATIO = 0.9;
        const double STALE_TOLERANCE_SECONDS = 2.0;

        // Cột "_id" có ≥ 50% dòng lặp giá trị ⇒ là cột nhóm (vd map_id của bảng level), không phải khoá chính.
        const double GROUPING_DUPLICATE_RATIO = 0.5;

        static readonly Regex VALID_HEADER = new("^[a-z0-9_]+$");

        public string Id => "csv-integrity";
        public string Name => "CSV config: định dạng & import";

        public string Description =>
            "CSV trong CsvConfig: file rỗng, header lỗi/trùng, dòng lệch cột, dấu nháy sai, khoá trùng, chữ trong cột số, " +
            "CSV mới hơn asset đã import (quên import lại).";

        public string Category => "Dữ liệu";
        public int Order => 200;

        public async Task Run(AutoTestContext ctx)
        {
            var cfg = ctx.Config.staticCheck;
            var files = CollectFiles(ctx, cfg);
            if (files.Count == 0)
                ctx.Skip(cfg.csvFolders != null && cfg.csvFolders.Count > 0
                    ? "Không có file .csv trong các thư mục staticCheck.csvFolders."
                    : "Không tìm thấy file .csv nào trong thư mục CsvConfig thuộc phạm vi quét.");

            var idRegex = CompileIdPattern(ctx);
            var totalRows = 0;
            var checkedFiles = 0;
            for (var i = 0; i < files.Count; i++)
            {
                await StaticCheckUtil.Yield(ctx, i, files.Count, "CSV");
                var path = files[i];
                try
                {
                    totalRows += CheckFile(ctx, path, idRegex);
                    checkedFiles++;
                }
                catch (Exception e) when (!DataRuleUtil.IsFlowControl(e))
                {
                    ctx.Report(Severity.Info, CATEGORY + ".Read", "Không đọc được file", e.Message, path);
                }
            }

            ctx.Metric("File CSV", checkedFiles, "file");
            ctx.Metric("Dòng dữ liệu", totalRows, "dòng");
        }

        #region Thu thập file

        static List<string> CollectFiles(AutoTestContext ctx, StaticCheckConfig cfg)
        {
            var folders = (cfg.csvFolders ?? new List<string>()).Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
            if (folders.Count == 0)
                return StaticCheckUtil.FindFiles(cfg, ".csv")
                    .Where(p => p.Replace('\\', '/').IndexOf("/CsvConfig/", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

            var result = new List<string>();
            foreach (var folder in folders)
            {
                var rel = folder.Trim().Replace('\\', '/').TrimEnd('/');
                var abs = StaticCheckUtil.AbsolutePath(rel);
                if (!Directory.Exists(abs))
                {
                    ctx.Report(Severity.Minor, CATEGORY + ".Config", "Thư mục CSV trong settings không tồn tại",
                        $"staticCheck.csvFolders có \"{folder}\" nhưng thư mục này không có trên đĩa.", rel);
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(abs, "*.csv", SearchOption.AllDirectories))
                    if (file.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                        result.Add(StaticCheckUtil.ToAssetPath(file));
            }

            return result.Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
        }

        static Regex CompileIdPattern(AutoTestContext ctx)
        {
            var pattern = ctx.Config.economy?.idFieldPattern;
            if (string.IsNullOrWhiteSpace(pattern)) return null;
            try
            {
                return new Regex(pattern, RegexOptions.CultureInvariant);
            }
            catch (ArgumentException e)
            {
                ctx.Report(Severity.Minor, CATEGORY + ".Config", "Regex idFieldPattern trong settings không hợp lệ",
                    $"economy.idFieldPattern = \"{pattern}\": {e.Message}. Chỉ nhận cột \"id\"/\"*_id\" là khoá.");
                return null;
            }
        }

        #endregion

        #region Kiểm tra một file

        /// <summary>Trả số dòng dữ liệu của file.</summary>
        static int CheckFile(AutoTestContext ctx, string path, Regex idRegex)
        {
            var abs = StaticCheckUtil.AbsolutePath(path);
            CheckImportFreshness(ctx, path, abs);

            var text = DataRuleUtil.ReadUtf8(abs, out var invalidUtf8);
            if (invalidUtf8)
                ctx.Report(Severity.Major, CATEGORY + ".Encoding", "File CSV không phải UTF-8",
                    "Có byte không hợp lệ theo UTF-8 (thường do Excel lưu kiểu ANSI) → chữ có dấu/ký tự đặc biệt bị lỗi sau khi import. Lưu lại dạng \"CSV UTF-8\".",
                    path);

            if (string.IsNullOrWhiteSpace(text.TrimStart('﻿')))
            {
                ctx.Report(Severity.Major, CATEGORY + ".Empty", "File CSV rỗng",
                    "File không có cả dòng header — importer không tạo được dữ liệu, code đọc bảng này sẽ nhận null.",
                    path);
                return 0;
            }

            var parsed = CsvTextParser.Parse(text, ',');
            var budget = new IssueBudget(ISSUES_PER_KIND_PER_FILE);
            ReportQuoteProblems(ctx, path, parsed, budget);
            if (parsed.Records.Count == 0) return 0;

            var header = parsed.Records[0].Cells;
            var headerLine = parsed.Records[0].Line;
            CheckHeader(ctx, path, headerLine, header);

            // Record từ dòng mở nháy lỗi trở đi đã bị nuốt vào một ô — không phân tích tiếp để tránh báo lỗi dây chuyền.
            var rows = new List<CsvRecord>();
            for (var r = 1; r < parsed.Records.Count; r++)
            {
                var rec = parsed.Records[r];
                if (parsed.Unclosed && rec.Line >= parsed.UnclosedLine) break;
                if (DataRuleUtil.IsBlankRow(rec.Cells) || DataRuleUtil.IsImporterCommentRow(rec.Cells)) continue;
                rows.Add(rec);
            }

            CheckUnnamedColumns(ctx, path, headerLine, header, rows);
            CheckRowWidths(ctx, path, header, rows, budget);
            CheckKeys(ctx, path, header, rows, idRegex, budget);
            CheckNumericColumns(ctx, path, header, rows, budget);
            budget.ReportOverflow(ctx, CATEGORY, path);
            return rows.Count;
        }

        static void ReportQuoteProblems(AutoTestContext ctx, string path, CsvParseResult parsed, IssueBudget budget)
        {
            foreach (var p in parsed.Problems)
            {
                var location = DataRuleUtil.LineLocation(path, p.Line);
                switch (p.Kind)
                {
                    case CsvProblemKind.UnclosedQuote:
                        ctx.Report(Severity.Critical, CATEGORY + ".Quote", "Dấu nháy kép không đóng",
                                $"Dấu \" mở ở dòng {p.Line} không có dấu đóng — importer gộp toàn bộ phần còn lại của file vào một ô (mất mọi dòng phía sau).",
                                location)
                            .WithContentId(ctx, path, "unclosed");
                        break;
                    case CsvProblemKind.QuoteInsideField:
                        if (!budget.Take("Dấu nháy giữa ô")) break;
                        ctx.Report(Severity.Major, CATEGORY + ".Quote", "Dấu nháy kép nằm giữa ô",
                                $"Dòng {p.Line}: ô không bao nháy nhưng có ký tự \" ở giữa — importer EZG hiểu là mở chuỗi trích dẫn, nuốt dấu phẩy/dòng phía sau → lệch cột. Bao cả ô trong \"...\" và viết \"\" cho dấu nháy.",
                                location)
                            .WithContentId(ctx, path, "quote-inside|" + DataRuleUtil.RecordTextAt(parsed, p.Line));
                        break;
                    case CsvProblemKind.TextAfterClosingQuote:
                        if (!budget.Take("Ký tự sau dấu nháy đóng")) break;
                        ctx.Report(Severity.Major, CATEGORY + ".Quote", "Có ký tự sau dấu nháy đóng",
                                $"Dòng {p.Line}: sau dấu \" đóng còn ký tự trước dấu phẩy (vd \"abc\" x) — importer tách phần thừa thành ô mới → các cột sau bị lệch.",
                                location)
                            .WithContentId(ctx, path, "after-quote|" + DataRuleUtil.RecordTextAt(parsed, p.Line));
                        break;
                }
            }
        }

        static void CheckHeader(AutoTestContext ctx, string path, int line, List<string> header)
        {
            var location = DataRuleUtil.LineLocation(path, line);
            var lastNamed = -1;
            for (var c = 0; c < header.Count; c++)
                if (!string.IsNullOrWhiteSpace(header[c]))
                    lastNamed = c;

            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var c = 0; c <= lastNamed; c++)
            {
                var name = header[c];
                if (string.IsNullOrWhiteSpace(name)) continue;

                if (seen.TryGetValue(name, out var first))
                    ctx.Report(Severity.Critical, CATEGORY + ".Header", "Header có tên cột trùng",
                            $"Cột \"{name}\" xuất hiện ở {ColumnLabel(first)} và {ColumnLabel(c)}. Importer EZG ném lỗi \"Key is duplicate\" và DỪNG map header ⇒ mọi cột từ {ColumnLabel(c)} trở đi nhận giá trị mặc định.",
                            location)
                        .WithContentId(ctx, path, "dup-header|" + name);
                else
                    seen[name] = c;

                if (VALID_HEADER.IsMatch(name)) continue;
                if (!string.Equals(name, name.ToLowerInvariant(), StringComparison.Ordinal))
                    ctx.Report(Severity.Critical, CATEGORY + ".Header", "Tên cột có chữ hoa",
                            $"\"{name}\" ({ColumnLabel(c)}): importer EZG chỉ nhận header chữ thường snake_case — gặp chữ hoa sẽ báo \"Key is not valid\" và DỪNG map ⇒ cột này và mọi cột sau nhận giá trị mặc định.",
                            location, null, name.ToLowerInvariant(), name)
                        .WithContentId(ctx, path, "upper-header|" + name);
                else if (name.Trim().Length != name.Length)
                    ctx.Report(Severity.Major, CATEGORY + ".Header", "Tên cột có khoảng trắng thừa",
                            $"\"{name}\" ({ColumnLabel(c)}): importer không trim tên cột ⇒ không khớp field nào, dữ liệu cột này bị bỏ qua.",
                            location, null, name.Trim(), name)
                        .WithContentId(ctx, path, "space-header|" + name);
                else
                    ctx.Report(Severity.Minor, CATEGORY + ".Header", "Tên cột không theo snake_case",
                            $"\"{name}\" ({ColumnLabel(c)}) có ký tự ngoài a-z, 0-9, \"_\" — sẽ không khớp field C# nào (dữ liệu cột bị bỏ qua).",
                            location)
                        .WithContentId(ctx, path, "odd-header|" + name);
            }
        }

        /// <summary>
        ///     Cột không tên nằm giữa header. Importer EZG map tên rỗng như một key: 1 cột ⇒ dữ liệu cột đó bị bỏ qua;
        ///     từ cột không tên thứ 2 ⇒ "Key is duplicate", dừng map ⇒ mọi cột có tên phía sau nhận giá trị mặc định.
        /// </summary>
        static void CheckUnnamedColumns(AutoTestContext ctx, string path, int line, List<string> header,
            List<CsvRecord> rows)
        {
            var lastNamed = -1;
            for (var c = 0; c < header.Count; c++)
                if (!string.IsNullOrWhiteSpace(header[c]))
                    lastNamed = c;

            var unnamed = new List<int>();
            for (var c = 0; c < lastNamed; c++)
                if (string.IsNullOrWhiteSpace(header[c]))
                    unnamed.Add(c);
            if (unnamed.Count == 0) return;

            var location = DataRuleUtil.LineLocation(path, line);
            if (unnamed.Count >= 2)
            {
                var lost = new List<string>();
                for (var c = unnamed[1] + 1; c <= lastNamed; c++)
                    if (!string.IsNullOrWhiteSpace(header[c]))
                        lost.Add(header[c]);
                ctx.Report(Severity.Critical, CATEGORY + ".Header", "Header có nhiều cột không tên — importer dừng map các cột sau",
                    $"{string.Join(", ", unnamed.Select(ColumnLabel))} không có tên. Từ cột không tên thứ 2 importer báo trùng khoá và dừng map ⇒ các cột sau nhận giá trị mặc định: {DataRuleUtil.JoinCapped(lost, LIST_CAP)}",
                    location);
                return;
            }

            var column = unnamed[0];
            var withData = new List<string>();
            foreach (var rec in rows)
                if (column < rec.Cells.Count && !string.IsNullOrWhiteSpace(rec.Cells[column]))
                    withData.Add(rec.Line.ToString(CultureInfo.InvariantCulture));
            if (withData.Count > 0)
                ctx.Report(Severity.Major, CATEGORY + ".Header", "Header có cột không tên",
                    $"{ColumnLabel(column)} không có tên nhưng có dữ liệu ở {withData.Count} dòng ({DataRuleUtil.JoinCapped(withData, LIST_CAP)}) — importer bỏ qua dữ liệu cột này. Đặt tên cột đúng field hoặc xoá cột.",
                    location);
            else
                ctx.Report(Severity.Minor, CATEGORY + ".Header", "Header có cột trống không tên",
                    $"{ColumnLabel(column)} không có tên và không có dữ liệu — nên xoá cột thừa (thêm một cột không tên nữa là importer dừng map các cột phía sau).",
                    location);
        }

        static void CheckRowWidths(AutoTestContext ctx, string path, List<string> header, List<CsvRecord> rows,
            IssueBudget budget)
        {
            var lastNamed = -1;
            for (var c = 0; c < header.Count; c++)
                if (!string.IsNullOrWhiteSpace(header[c]))
                    lastNamed = c;

            var fewer = new List<string>();
            var unnamedData = new List<string>();
            foreach (var rec in rows)
            {
                var width = EffectiveWidth(rec.Cells);
                if (width > header.Count)
                {
                    if (budget.Take("Dòng lệch cột"))
                        ctx.Report(Severity.Major, CATEGORY + ".Width", "Dòng lệch cột",
                                $"Dòng {rec.Line} có {width} ô dữ liệu nhưng header chỉ có {header.Count} cột. Ô thừa: {Preview(rec.Cells, header.Count)} — thường do thiếu dấu nháy quanh ô có dấu phẩy, hoặc thừa dấu phẩy.",
                                DataRuleUtil.LineLocation(path, rec.Line), null, header.Count + " cột", width + " ô")
                            .WithContentId(ctx, path, "width|" + string.Join(",", rec.Cells));
                    continue;
                }

                if (rec.Cells.Count < header.Count) fewer.Add(rec.Line.ToString(CultureInfo.InvariantCulture));
                for (var c = lastNamed + 1; c < Math.Min(width, header.Count); c++)
                    if (!string.IsNullOrWhiteSpace(rec.Cells[c]))
                    {
                        unnamedData.Add(rec.Line.ToString(CultureInfo.InvariantCulture));
                        break;
                    }
            }

            if (unnamedData.Count > 0)
                ctx.Report(Severity.Minor, CATEGORY + ".Width", "Dữ liệu nằm ở cột không có tên",
                    $"{unnamedData.Count} dòng có dữ liệu ở cột cuối không có tên header (importer bỏ qua — ghi chú thì nên xoá khỏi file config). Dòng: {DataRuleUtil.JoinCapped(unnamedData, LIST_CAP)}",
                    path);

            if (fewer.Count > 0)
                ctx.Report(Severity.Info, CATEGORY + ".Width", "Dòng có ít ô hơn header",
                    $"{fewer.Count} dòng thiếu dấu phẩy cuối (ô thiếu coi như trống). Dòng: {DataRuleUtil.JoinCapped(fewer, LIST_CAP)}",
                    path);
        }

        static void CheckKeys(AutoTestContext ctx, string path, List<string> header, List<CsvRecord> rows,
            Regex idRegex, IssueBudget budget)
        {
            if (header.Count == 0 || rows.Count == 0) return;
            var keyHeader = header[0].Trim();
            var keyLines = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var keyRows = 0;
            foreach (var rec in rows)
            {
                var key = rec.Cells.Count > 0 ? rec.Cells[0] : "";
                // Cột đầu trống = phần tử con của dòng trước (mảng lồng) — không phải khoá trống.
                if (key.Length == 0) continue;
                var trimmed = key.Trim();
                if (!string.Equals(trimmed, key, StringComparison.Ordinal) && budget.Take("Khoá có khoảng trắng"))
                    ctx.Report(Severity.Minor, CATEGORY + ".Key", "Khoá cột đầu có khoảng trắng thừa",
                            $"Dòng {rec.Line}: \"{key}\" ở cột \"{keyHeader}\" có khoảng trắng đầu/cuối — tra theo khoá (string) sẽ không khớp."
                            + (trimmed.Length == 0 ? " Ô chỉ có khoảng trắng: importer vẫn tính là một phần tử mới." : ""),
                            DataRuleUtil.LineLocation(path, rec.Line), null, trimmed, key)
                        .WithContentId(ctx, path, "key-space|" + key);
                if (trimmed.Length == 0) continue;
                keyRows++;
                if (!keyLines.TryGetValue(trimmed, out var lines)) keyLines[trimmed] = lines = new List<int>();
                lines.Add(rec.Line);
            }

            var strictId = keyHeader.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                           (idRegex != null && idRegex.IsMatch(keyHeader));
            var suffixId = keyHeader.EndsWith("_id", StringComparison.OrdinalIgnoreCase);
            if (!strictId && !suffixId) return;

            var duplicatedRows = keyRows - keyLines.Count;
            if (!strictId && keyRows > 0 && (double)duplicatedRows / keyRows >= GROUPING_DUPLICATE_RATIO)
            {
                ctx.Log($"{path}: cột \"{keyHeader}\" lặp nhiều ({keyLines.Count} giá trị / {keyRows} dòng) → coi là cột nhóm, không kiểm tra trùng khoá.");
                return;
            }

            foreach (var pair in keyLines)
            {
                if (pair.Value.Count < 2 || !budget.Take("Khoá trùng")) continue;
                ctx.Report(Severity.Major, CATEGORY + ".DuplicateKey", "Khoá trùng ở cột đầu",
                        $"\"{keyHeader}\" = \"{pair.Key}\" xuất hiện ở dòng {string.Join(", ", pair.Value)} — tra theo khoá sẽ lấy nhầm/ghi đè phần tử.",
                        DataRuleUtil.LineLocation(path, pair.Value[0]), null, "duy nhất",
                        pair.Value.Count + " lần")
                    .WithContentId(ctx, path, "dup-key|" + pair.Key);
            }
        }

        static void CheckNumericColumns(AutoTestContext ctx, string path, List<string> header, List<CsvRecord> rows,
            IssueBudget budget)
        {
            for (var c = 0; c < header.Count; c++)
            {
                if (string.IsNullOrWhiteSpace(header[c])) continue;
                var nonEmpty = 0;
                var numeric = 0;
                foreach (var rec in rows)
                {
                    if (c >= rec.Cells.Count) continue;
                    var v = rec.Cells[c].Trim();
                    if (v.Length == 0) continue;
                    nonEmpty++;
                    if (IsNumber(v)) numeric++;
                }

                if (nonEmpty < MIN_NUMERIC_SAMPLE || numeric == nonEmpty || numeric < nonEmpty * NUMERIC_RATIO)
                    continue;

                foreach (var rec in rows)
                {
                    if (c >= rec.Cells.Count) continue;
                    var v = rec.Cells[c].Trim();
                    if (v.Length == 0 || IsNumber(v)) continue;
                    if (!budget.Take("Giá trị không phải số")) continue;
                    ctx.Report(Severity.Major, CATEGORY + ".Number", "Giá trị không phải số ở cột số",
                            $"Dòng {rec.Line}, cột \"{header[c]}\" ({ColumnLabel(c)}): \"{v}\" — {numeric}/{nonEmpty} giá trị của cột là số. Field kiểu số sẽ nhận 0 (importer chỉ log cảnh báo).",
                            DataRuleUtil.LineLocation(path, rec.Line), null, "số", v)
                        .WithContentId(ctx, path, "nan|" + header[c] + "|" + v);
                }
            }
        }

        #endregion

        #region Import freshness

        /// <summary>Asset sinh ra từ CSV (mặc định ../Resources/&lt;tên&gt;.asset) phải mới hơn CSV.</summary>
        static void CheckImportFreshness(AutoTestContext ctx, string path, string abs)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var csvDir = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
            var parent = Path.GetDirectoryName(csvDir)?.Replace('\\', '/') ?? "";
            var sibling = (parent.Length > 0 ? parent + "/" : "") + "Resources/" + name + ".asset";
            var assetPath = File.Exists(StaticCheckUtil.AbsolutePath(sibling)) ? sibling : FindAssetByName(name);
            if (assetPath == null)
            {
                ctx.Report(Severity.Minor, CATEGORY + ".Import", "Chưa có asset import cho CSV",
                    $"Không thấy {sibling} (cũng không có asset \"{name}.asset\" nào khác) — CSV chưa được import thành dữ liệu game. Bỏ qua nếu CSV này được import ở chỗ khác.",
                    path, null, sibling, "không có");
                return;
            }

            var csvTime = File.GetLastWriteTimeUtc(abs);
            var assetTime = File.GetLastWriteTimeUtc(StaticCheckUtil.AbsolutePath(assetPath));
            var delta = (csvTime - assetTime).TotalSeconds;
            if (delta <= STALE_TOLERANCE_SECONDS) return;
            ctx.Report(Severity.Major, CATEGORY + ".Import", "CSV mới hơn asset đã import — chưa import lại",
                $"CSV sửa lúc {Stamp(csvTime)}, asset {assetPath} import lúc {Stamp(assetTime)} (cũ hơn {FormatDelta(delta)}). Game vẫn đọc dữ liệu cũ — chạy lại CSV importer. (Nếu vừa pull/checkout mà không sửa CSV, git có thể đã đặt lại giờ sửa file — import lại cho chắc.)",
                path, null, "asset mới hơn CSV", "asset cũ hơn " + FormatDelta(delta));
        }

        static string FindAssetByName(string name)
        {
            string fallback = null;
            foreach (var guid in AssetDatabase.FindAssets(name))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(Path.GetFileName(p), name + ".asset", StringComparison.OrdinalIgnoreCase)) continue;
                if (p.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0) return p;
                fallback ??= p;
            }

            return fallback;
        }

        static string Stamp(DateTime utc)
        {
            return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        static string FormatDelta(double seconds)
        {
            var inv = CultureInfo.InvariantCulture;
            if (seconds < 120) return seconds.ToString("0", inv) + "s";
            if (seconds < 7200) return (seconds / 60).ToString("0", inv) + " phút";
            if (seconds < 172800) return (seconds / 3600).ToString("0.#", inv) + " giờ";
            return (seconds / 86400).ToString("0.#", inv) + " ngày";
        }

        #endregion

        #region Helpers

        static bool IsNumber(string v)
        {
            if (v.Length == 0) return false;
            var c = v[0];
            if (!(char.IsDigit(c) || c == '-' || c == '+' || c == '.')) return false;
            return double.TryParse(v,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out _);
        }

        static int EffectiveWidth(List<string> cells)
        {
            var w = cells.Count;
            while (w > 0 && string.IsNullOrWhiteSpace(cells[w - 1])) w--;
            return w;
        }

        static string Preview(List<string> cells, int from)
        {
            var parts = new List<string>();
            for (var c = from; c < cells.Count; c++)
                if (!string.IsNullOrWhiteSpace(cells[c]))
                    parts.Add("\"" + DataRuleUtil.Truncate(cells[c], 40) + "\"");
            return DataRuleUtil.JoinCapped(parts, 5);
        }

        /// <summary>"cột 3 (C)" — số thứ tự 1-based kèm tên cột kiểu Excel cho QA mở file dễ tìm.</summary>
        static string ColumnLabel(int index)
        {
            var n = index + 1;
            var letters = "";
            while (n > 0)
            {
                var rem = (n - 1) % 26;
                letters = (char)('A' + rem) + letters;
                n = (n - 1) / 26;
            }

            return $"cột {index + 1} ({letters})";
        }

        #endregion
    }
}
