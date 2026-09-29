using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ezg.AutoTest.Editor
{
    /// <summary>Một record CSV đã parse (có thể trải nhiều dòng nếu có ô trong dấu nháy chứa xuống dòng).</summary>
    internal sealed class CsvRecord
    {
        /// <summary>Số dòng (1-based) nơi record bắt đầu trong file.</summary>
        public int Line;

        public List<string> Cells = new();
    }

    internal enum CsvProblemKind
    {
        /// <summary>Hết file mà dấu nháy kép chưa đóng.</summary>
        UnclosedQuote = 0,

        /// <summary>Dấu nháy kép nằm giữa ô không được bao nháy (importer EZG hiểu là mở chuỗi trích dẫn).</summary>
        QuoteInsideField = 1,

        /// <summary>Có ký tự sau dấu nháy đóng trước dấu phân cách (importer EZG tách thành ô mới → lệch cột).</summary>
        TextAfterClosingQuote = 2
    }

    internal sealed class CsvProblem
    {
        public CsvProblemKind Kind;
        public int Line;
    }

    internal sealed class CsvParseResult
    {
        public readonly List<CsvRecord> Records = new();
        public readonly List<CsvProblem> Problems = new();

        /// <summary>True khi hết file vẫn còn trong dấu nháy — các record từ dòng mở nháy trở đi không đáng tin.</summary>
        public bool Unclosed;

        public int UnclosedLine;
    }

    /// <summary>
    ///     Parser CSV nhỏ theo RFC4180 (ô bao nháy, "" thoát dấu nháy, dấu phân cách/xuống dòng trong nháy), thêm
    ///     <c>\"</c> như importer EZG. Không bao giờ ném lỗi — lỗi định dạng ghi vào <see cref="CsvParseResult.Problems" />.
    /// </summary>
    internal static class CsvTextParser
    {
        const int MAX_PROBLEMS = 500;

        public static CsvParseResult Parse(string text, char delimiter)
        {
            var result = new CsvParseResult();
            if (string.IsNullOrEmpty(text)) return result;

            var n = text.Length;
            var start = text[0] == '﻿' ? 1 : 0;
            var cells = new List<string>();
            var sb = new StringBuilder();
            var line = 1;
            var recordLine = 1;
            var inQuotes = false;
            var fieldQuoted = false;
            var afterClose = false;
            var afterCloseReported = false;
            var quoteLine = 0;

            void AddProblem(CsvProblemKind kind, int atLine)
            {
                if (result.Problems.Count >= MAX_PROBLEMS) return;
                // Nhiều dấu nháy lỗi trên cùng một dòng ⇒ chỉ ghi một lần.
                foreach (var existing in result.Problems)
                    if (existing.Kind == kind && existing.Line == atLine)
                        return;
                result.Problems.Add(new CsvProblem { Kind = kind, Line = atLine });
            }

            void EndField()
            {
                cells.Add(sb.ToString());
                sb.Clear();
                fieldQuoted = false;
                afterClose = false;
                afterCloseReported = false;
            }

            void EndRecord()
            {
                result.Records.Add(new CsvRecord { Line = recordLine, Cells = cells });
                cells = new List<string>();
            }

            for (var i = start; i < n; i++)
            {
                var c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < n && text[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                            continue;
                        }

                        inQuotes = false;
                        afterClose = true;
                        continue;
                    }

                    if (c == '\\' && i + 1 < n && text[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                        continue;
                    }

                    if (c == '\n')
                    {
                        line++;
                    }
                    else if (c == '\r')
                    {
                        if (i + 1 < n && text[i + 1] == '\n')
                        {
                            sb.Append('\r');
                            i++;
                            c = '\n';
                        }

                        line++;
                    }

                    sb.Append(c);
                    continue;
                }

                if (c == delimiter)
                {
                    EndField();
                    continue;
                }

                if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < n && text[i + 1] == '\n') i++;
                    EndField();
                    EndRecord();
                    line++;
                    recordLine = line;
                    continue;
                }

                if (c == '"')
                {
                    if (sb.Length == 0 && !fieldQuoted)
                    {
                        inQuotes = true;
                        fieldQuoted = true;
                        quoteLine = line;
                        continue;
                    }

                    AddProblem(afterClose ? CsvProblemKind.TextAfterClosingQuote : CsvProblemKind.QuoteInsideField,
                        line);
                    afterCloseReported = afterCloseReported || afterClose;
                    sb.Append(c);
                    continue;
                }

                if (afterClose && !afterCloseReported)
                {
                    AddProblem(CsvProblemKind.TextAfterClosingQuote, line);
                    afterCloseReported = true;
                }

                sb.Append(c);
            }

            if (inQuotes)
            {
                result.Unclosed = true;
                result.UnclosedLine = quoteLine;
                AddProblem(CsvProblemKind.UnclosedQuote, quoteLine);
            }

            if (cells.Count > 0 || sb.Length > 0 || fieldQuoted)
            {
                EndField();
                EndRecord();
            }

            return result;
        }
    }

    /// <summary>Tiện ích dùng chung cho các luật dữ liệu/code/build (đọc file, rút gọn chuỗi, fingerprint).</summary>
    internal static class DataRuleUtil
    {
        static readonly UTF8Encoding STRICT_UTF8 = new(false, true);

        /// <summary>Đọc file dạng UTF-8; <paramref name="invalidUtf8" /> = true nếu có byte không hợp lệ.</summary>
        public static string ReadUtf8(string absolutePath, out bool invalidUtf8)
        {
            var bytes = File.ReadAllBytes(absolutePath);
            invalidUtf8 = false;
            try
            {
                return STRICT_UTF8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                invalidUtf8 = true;
                return Encoding.UTF8.GetString(bytes);
            }
        }

        public static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
            return s.Substring(0, max) + "…";
        }

        /// <summary>"a, b, c … (+N)" — liệt kê tối đa <paramref name="cap" /> phần tử.</summary>
        public static string JoinCapped(IReadOnlyList<string> items, int cap, string separator = ", ")
        {
            if (items == null || items.Count == 0) return "";
            var sb = new StringBuilder();
            var count = Math.Min(cap, items.Count);
            for (var i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(separator);
                sb.Append(items[i]);
            }

            if (items.Count > cap) sb.Append(" … (+").Append(items.Count - cap).Append(')');
            return sb.ToString();
        }

        /// <summary>
        ///     Gán fingerprint theo NỘI DUNG (không theo số dòng) — nhiều lỗi cùng file/cùng tiêu đề không bị gộp làm
        ///     một, và chèn/xoá dòng phía trên không làm lỗi cũ thành "lỗi mới".
        /// </summary>
        public static TestIssue WithContentId(this TestIssue issue, AutoTestContext ctx, string file, string content)
        {
            if (issue == null) return null;
            issue.id = AutoTestExecutor.Fingerprint(ctx.Result.suiteId, ctx.Result.caseId, issue.category, issue.title,
                file, KeepDigits(Truncate(content, 300)));
            return issue;
        }

        /// <summary>Exception không được nuốt trong try/catch từng file (dừng lượt chạy, skip case).</summary>
        public static bool IsFlowControl(Exception e)
        {
            return e is OperationCanceledException || e is AutoTestSkipException || e is AutoTestAssertException;
        }

        public static bool IsBlankRow(List<string> cells)
        {
            foreach (var c in cells)
                if (!string.IsNullOrWhiteSpace(c))
                    return false;
            return true;
        }

        /// <summary>Importer EZG bỏ cả dòng nếu có một ô đúng bằng "/" hoặc "//" (dòng comment).</summary>
        public static bool IsImporterCommentRow(List<string> cells)
        {
            foreach (var c in cells)
                if (c == "/" || c == "//")
                    return true;
            return false;
        }

        /// <summary>Nội dung record bắt đầu tại/ngay trước dòng (fingerprint lỗi theo nội dung, không theo số dòng).</summary>
        public static string RecordTextAt(CsvParseResult parsed, int line)
        {
            CsvRecord best = null;
            foreach (var r in parsed.Records)
            {
                if (r.Line > line) break;
                best = r;
            }

            return best == null ? "" : string.Join("|", best.Cells);
        }

        public static string LineLocation(string path, int line)
        {
            return line > 0 ? path + ":" + line : path;
        }

        // Fingerprint của executor gộp mọi chữ số thành '#'; ở đây giữ chữ số (key "id=1" ≠ "id=2").
        static string KeepDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (var ch in s)
                if (ch >= '0' && ch <= '9') sb.Append('§').Append((char)('a' + (ch - '0')));
                else sb.Append(ch);
            return sb.ToString();
        }
    }

    /// <summary>Giới hạn số issue mỗi loại trong một phạm vi (file) + đếm phần bị lược để báo tổng.</summary>
    internal sealed class IssueBudget
    {
        readonly int _cap;
        readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

        public IssueBudget(int cap)
        {
            _cap = cap;
        }

        /// <summary>True nếu còn được ghi issue loại <paramref name="kind" />.</summary>
        public bool Take(string kind)
        {
            _counts.TryGetValue(kind, out var c);
            _counts[kind] = c + 1;
            return c < _cap;
        }

        /// <summary>Ghi Info tổng cho các loại vượt giới hạn.</summary>
        public void ReportOverflow(AutoTestContext ctx, string category, string location)
        {
            foreach (var pair in _counts)
                if (pair.Value > _cap)
                    ctx.Report(Severity.Info, category, "Còn lỗi cùng loại không liệt kê hết",
                            $"\"{pair.Key}\": {pair.Value} lỗi, chỉ liệt kê {_cap} lỗi đầu.", location)
                        .WithContentId(ctx, location, pair.Key);
        }
    }
}
