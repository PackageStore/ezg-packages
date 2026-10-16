using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Đọc / ghi <c>report.json</c> theo schema 0.2 — dành cho tool bên ngoài (qadevice.py của skill
    ///     /auto-test-device, CI, dashboard) đọc mà không cần biết số enum của C#:
    ///     <list type="bullet">
    ///         <item>Mọi enum ra <b>chuỗi</b>: <c>severity</c> = Blocker/Critical/Major/Minor/Info, <c>status</c> =
    ///         Pending/Running/Passed/Warning/Failed/Error/Skipped/Cancelled, <c>mode</c> = Edit/Play/Device.</item>
    ///         <item><c>issues[]</c> phẳng ở gốc: {id, suite, case, group, severity, title, message, location, objectPath,
    ///         screen, screenshot, isNew} — <c>screen</c> = tên feature khi case là "screen-&lt;Feature&gt;".</item>
    ///         <item><c>summary</c> thêm <c>issuesTotal</c>, <c>bySeverity{}</c>, <c>bySuite{}</c> (đếm issue; <c>total</c>
    ///         vẫn là số case như 0.1).</item>
    ///         <item><c>generatedAt</c> (ISO 8601), <c>version</c> (phiên bản package / schema).</item>
    ///     </list>
    ///     Phần còn lại giữ nguyên <c>JsonUtility.ToJson(TestRunReport)</c> nên report 0.1 vẫn đọc được: field mới chỉ
    ///     THÊM, không xoá. <see cref="Deserialize" /> nhận cả enum chuỗi (0.2) lẫn enum số (0.1).
    /// </summary>
    public static class AutoTestReportJson
    {
        /// <summary>Phiên bản package — đổi khi bump package.json (ghi vào report.version khi env.packageVersion trống).</summary>
        public const string PACKAGE_VERSION = "0.2.0";

        const string KEY_SEVERITY = "severity";
        const string KEY_STATUS = "status";
        const string KEY_MODE = "mode";
        const string SCREEN_CASE_PREFIX = "screen-";
        const string INDENT = "    ";

        static readonly Regex SeverityNumber = Number(KEY_SEVERITY);
        static readonly Regex StatusNumber = Number(KEY_STATUS);
        static readonly Regex ModeNumber = Number(KEY_MODE);
        static readonly Regex SeverityName = Name(KEY_SEVERITY);
        static readonly Regex StatusName = Name(KEY_STATUS);
        static readonly Regex ModeName = Name(KEY_MODE);

        /// <summary>Vị trí chèn vào object <c>summary</c> (xuất hiện đúng một lần trong TestRunReport).</summary>
        static readonly Regex SummaryOpen = new("\"summary\"\\s*:\\s*\\{", RegexOptions.CultureInvariant);

        #region Public

        /// <summary>JSON schema 0.2 (xem mô tả class). <paramref name="pretty" /> = thụt dòng 4 khoảng trắng như JsonUtility.</summary>
        public static string Serialize(TestRunReport report, bool pretty)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var json = JsonUtility.ToJson(report, pretty);
            json = EnumsToNames(json);
            json = InsertSummaryExtras(json, report, pretty);
            json = AppendRootExtras(json, report, pretty);
            return json;
        }

        /// <summary>Đọc report 0.1 (enum số) hoặc 0.2 (enum chuỗi). Null nếu JSON rỗng/hỏng.</summary>
        public static TestRunReport Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonUtility.FromJson<TestRunReport>(NamesToEnums(json));
        }

        /// <summary>Đổi enum chuỗi (0.2) về số để JsonUtility đọc được; JSON đã là số thì giữ nguyên.</summary>
        public static string NamesToEnums(string json)
        {
            if (string.IsNullOrEmpty(json)) return json;
            json = SeverityName.Replace(json, m => m.Groups[1].Value + ParseEnum<Severity>(m.Groups[2].Value));
            json = StatusName.Replace(json, m => m.Groups[1].Value + ParseEnum<TestStatus>(m.Groups[2].Value));
            json = ModeName.Replace(json, m => m.Groups[1].Value + ParseEnum<ExecutionMode>(m.Groups[2].Value));
            return json;
        }

        /// <summary>Đổi enum số (JsonUtility) sang tên.</summary>
        public static string EnumsToNames(string json)
        {
            if (string.IsNullOrEmpty(json)) return json;
            json = SeverityNumber.Replace(json, m => m.Groups[1].Value + Quote(EnumName<Severity>(m.Groups[2].Value)));
            json = StatusNumber.Replace(json, m => m.Groups[1].Value + Quote(EnumName<TestStatus>(m.Groups[2].Value)));
            json = ModeNumber.Replace(json, m => m.Groups[1].Value + Quote(EnumName<ExecutionMode>(m.Groups[2].Value)));
            return json;
        }

        /// <summary>Tên feature của một case màn hình ("screen-&lt;Feature&gt;"), rỗng nếu không phải.</summary>
        public static string ScreenOf(string caseId)
        {
            if (string.IsNullOrEmpty(caseId) || !caseId.StartsWith(SCREEN_CASE_PREFIX, StringComparison.Ordinal))
                return "";
            return caseId.Substring(SCREEN_CASE_PREFIX.Length);
        }

        /// <summary>Phiên bản ghi vào report: env.packageVersion nếu có, không thì hằng của assembly.</summary>
        public static string VersionOf(TestRunReport report)
        {
            var v = report?.env?.packageVersion;
            return string.IsNullOrEmpty(v) ? PACKAGE_VERSION : v;
        }

        #endregion

        #region Extras

        static string InsertSummaryExtras(string json, TestRunReport report, bool pretty)
        {
            var m = SummaryOpen.Match(json);
            if (!m.Success) return json;

            var bySeverity = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var bySuite = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var total = 0;
            foreach (var suite in report.suites ?? new List<TestSuiteResult>())
            {
                if (suite?.cases == null) continue;
                foreach (var c in suite.cases)
                {
                    if (c?.issues == null) continue;
                    foreach (var issue in c.issues)
                    {
                        if (issue == null) continue;
                        total++;
                        Bump(bySeverity, EnumName<Severity>(((int)issue.severity).ToString(CultureInfo.InvariantCulture)));
                        Bump(bySuite, suite.suiteId ?? "");
                    }
                }
            }

            var sb = new StringBuilder();
            var nl = pretty ? "\n" + INDENT + INDENT : "";
            var sep = pretty ? ", " : ",";
            var colon = pretty ? ": " : ":";
            sb.Append(nl).Append("\"issuesTotal\"").Append(colon).Append(total.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(nl).Append("\"bySeverity\"").Append(colon);
            AppendCountMap(sb, bySeverity, sep, colon);
            sb.Append(',');
            sb.Append(nl).Append("\"bySuite\"").Append(colon);
            AppendCountMap(sb, bySuite, sep, colon);
            sb.Append(',');
            return json.Insert(m.Index + m.Length, sb.ToString());
        }

        static string AppendRootExtras(string json, TestRunReport report, bool pretty)
        {
            var end = json.LastIndexOf('}');
            if (end < 0) return json;
            // Chèn ngay sau ký tự cuối của field trước (trước "\n}") để JSON pretty không có dòng thừa.
            var insertAt = end;
            while (insertAt > 0 && char.IsWhiteSpace(json[insertAt - 1])) insertAt--;

            var sb = new StringBuilder();
            var nl = pretty ? "\n" + INDENT : "";
            var nl2 = pretty ? "\n" + INDENT + INDENT : "";
            var nl3 = pretty ? "\n" + INDENT + INDENT + INDENT : "";
            var colon = pretty ? ": " : ":";

            sb.Append(',').Append(nl).Append("\"issues\"").Append(colon).Append('[');
            var first = true;
            foreach (var suite in report.suites ?? new List<TestSuiteResult>())
            {
                if (suite?.cases == null) continue;
                foreach (var c in suite.cases)
                {
                    if (c?.issues == null) continue;
                    var screen = ScreenOf(c.caseId);
                    foreach (var issue in c.issues)
                    {
                        if (issue == null) continue;
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append(nl2).Append('{');
                        var fields = new List<(string, string)>
                        {
                            ("id", issue.id),
                            ("suite", suite.suiteId),
                            ("case", c.caseId),
                            ("group", issue.category),
                            ("severity", EnumName<Severity>(((int)issue.severity).ToString(CultureInfo.InvariantCulture))),
                            ("title", issue.title),
                            ("message", issue.message),
                            ("location", issue.location),
                            ("objectPath", issue.objectPath),
                            ("screen", screen),
                            ("screenshot", issue.screenshot)
                        };
                        for (var i = 0; i < fields.Count; i++)
                        {
                            if (i > 0) sb.Append(',');
                            sb.Append(nl3).Append(Quote(fields[i].Item1)).Append(colon);
                            AppendJsonString(sb, fields[i].Item2 ?? "");
                        }

                        sb.Append(',').Append(nl3).Append("\"isNew\"").Append(colon).Append(issue.isNew ? "true" : "false");
                        sb.Append(nl2).Append('}');
                    }
                }
            }

            sb.Append(first ? "" : nl).Append(']');
            sb.Append(',').Append(nl).Append("\"generatedAt\"").Append(colon);
            AppendJsonString(sb, DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture));
            sb.Append(',').Append(nl).Append("\"version\"").Append(colon);
            AppendJsonString(sb, VersionOf(report));
            return json.Insert(insertAt, sb.ToString());
        }

        static void AppendCountMap(StringBuilder sb, SortedDictionary<string, int> map, string sep, string colon)
        {
            sb.Append('{');
            var first = true;
            foreach (var pair in map)
            {
                if (!first) sb.Append(sep);
                first = false;
                AppendJsonString(sb, pair.Key);
                sb.Append(colon).Append(pair.Value.ToString(CultureInfo.InvariantCulture));
            }

            sb.Append('}');
        }

        static void Bump(IDictionary<string, int> map, string key)
        {
            map[key] = map.TryGetValue(key, out var n) ? n + 1 : 1;
        }

        #endregion

        #region Enum / chuỗi

        static Regex Number(string key)
        {
            return new Regex("(\"" + key + "\"\\s*:\\s*)(-?\\d+)", RegexOptions.CultureInvariant);
        }

        static Regex Name(string key)
        {
            return new Regex("(\"" + key + "\"\\s*:\\s*)\"([A-Za-z_][A-Za-z0-9_]*)\"", RegexOptions.CultureInvariant);
        }

        static string EnumName<T>(string number) where T : struct, Enum
        {
            if (!int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return number;
            var value = (T)Enum.ToObject(typeof(T), v);
            return Enum.IsDefined(typeof(T), value) ? value.ToString() : number;
        }

        static string ParseEnum<T>(string name) where T : struct, Enum
        {
            if (Enum.TryParse<T>(name, true, out var value))
                return Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            return "0";
        }

        static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            AppendJsonString(sb, s);
            return sb.ToString();
        }

        /// <summary>Ghi chuỗi JSON có escape (dấu nháy, gạch chéo ngược, ký tự điều khiển).</summary>
        public static void AppendJsonString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var ch in s ?? "")
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(ch);
                        break;
                }

            sb.Append('"');
        }

        #endregion
    }
}
