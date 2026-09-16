using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Ezg.UserSegment.Engine
{
    /// <summary>
    ///     def_hash cho hysteresis (tag) và edge state (rule) — §C.5.2. CLI tính lúc compile; engine chỉ tính lại khi
    ///     config thiếu field (dev / test) để cùng một thuật toán cho cả hai phía.
    /// </summary>
    public static class DefHash
    {
        public static string ForTag(AstNode enter, AstNode exit)
        {
            var sb = new StringBuilder();
            sb.Append("{\"enter\":");
            WriteAst(sb, enter);
            sb.Append(",\"exit\":");
            WriteAst(sb, exit);
            sb.Append('}');
            return Hex16(sb.ToString());
        }

        public static string ForRule(string[] on, AstNode when)
        {
            var sorted = (string[])on.Clone();
            Array.Sort(sorted, StringComparer.Ordinal);
            var sb = new StringBuilder();
            sb.Append("{\"on\":[");
            for (var i = 0; i < sorted.Length; i++)
            {
                if (i > 0) sb.Append(',');
                WriteString(sb, sorted[i]);
            }

            sb.Append("],\"when\":");
            WriteAst(sb, when);
            sb.Append('}');
            return Hex16(sb.ToString());
        }

        public static string Hex16(string canonical)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                var sb = new StringBuilder(16);
                for (var i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>JSON canonical: không whitespace, key sắp ordinal, số dạng round-trip ngắn nhất.</summary>
        public static void WriteAst(StringBuilder sb, AstNode n)
        {
            switch (n.Kind)
            {
                case AstKind.Op:
                    sb.Append("{\"args\":[");
                    for (var i = 0; i < n.Args.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        WriteAst(sb, n.Args[i]);
                    }

                    sb.Append("],\"op\":");
                    WriteString(sb, n.Op);
                    sb.Append('}');
                    break;
                case AstKind.Ref:
                    sb.Append("{\"ref\":");
                    WriteString(sb, n.Ref);
                    if (n.Window != RefWindow.None)
                    {
                        sb.Append(",\"window\":");
                        WriteString(sb, WindowName(n.Window));
                    }

                    sb.Append('}');
                    break;
                default:
                    sb.Append("{\"value\":");
                    switch (n.Value.Type)
                    {
                        case SegType.Bool:
                            sb.Append(n.Value.Num != 0 ? "true" : "false");
                            break;
                        case SegType.String:
                            WriteString(sb, n.Value.Str);
                            break;
                        default:
                            sb.Append(n.Value.Num.ToString("R", CultureInfo.InvariantCulture));
                            break;
                    }

                    sb.Append('}');
                    break;
            }
        }

        public static string WindowName(RefWindow w)
        {
            switch (w)
            {
                case RefWindow.Session: return "session";
                case RefWindow.Days7: return "7d";
                case RefWindow.Days30: return "30d";
                default: return null;
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var ch in s)
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else sb.Append(ch);
                        break;
                }

            sb.Append('"');
        }
    }
}
