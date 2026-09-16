using System;
using System.Globalization;

namespace Ezg.UserSegment.Engine
{
    /// <summary>Kiểu trong hệ AST — §C.2.2. TIMESTAMP là alias của Number khi type-check.</summary>
    public enum SegType
    {
        Number,
        Bool,
        String
    }

    /// <summary>Giá trị runtime của một ref / node. Không có null: mặc định 0 / false / "".</summary>
    public readonly struct SegValue
    {
        public readonly SegType Type;
        public readonly double Num;
        public readonly string Str;

        private SegValue(SegType type, double num, string str)
        {
            Type = type;
            Num = num;
            Str = str ?? string.Empty;
        }

        public static SegValue Number(double v) => new SegValue(SegType.Number, v, null);
        public static SegValue Bool(bool v) => new SegValue(SegType.Bool, v ? 1 : 0, null);
        public static SegValue String(string v) => new SegValue(SegType.String, 0, v);

        public static readonly SegValue Zero = Number(0);
        public static readonly SegValue False = Bool(false);
        public static readonly SegValue Empty = String(string.Empty);

        /// <summary>Số học: BOOL → 0 / 1.</summary>
        public double AsNumber => Type == SegType.String ? 0 : Num;

        public bool AsBool => Type != SegType.String && Num != 0;

        public static SegValue Default(SegType t)
        {
            switch (t)
            {
                case SegType.Bool: return False;
                case SegType.String: return Empty;
                default: return Zero;
            }
        }

        public override string ToString()
        {
            switch (Type)
            {
                case SegType.Bool: return Num != 0 ? "true" : "false";
                case SegType.String: return Str;
                default: return Num.ToString("R", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Định dạng cho tracking: NUMBER làm tròn 2 chữ số, BOOL 1 / 0 — §C.7.1.</summary>
        public string ToTrackingString()
        {
            switch (Type)
            {
                case SegType.Bool: return Num != 0 ? "1" : "0";
                case SegType.String: return Str;
                default: return Num.ToString("F2", CultureInfo.InvariantCulture);
            }
        }
    }

    public enum AstKind
    {
        Op,
        Ref,
        Value
    }

    /// <summary>Window của ref counter — §4.3.</summary>
    public enum RefWindow
    {
        None,
        Session,
        Days7,
        Days30
    }

    /// <summary>Node AST đã compile — §C.2.1. Client chỉ đọc, không parse string.</summary>
    public sealed class AstNode
    {
        public AstKind Kind;
        public string Op;
        public AstNode[] Args = Array.Empty<AstNode>();
        public string Ref;
        public RefWindow Window;
        public SegValue Value;

        /// <summary>Kiểu kết quả, điền bởi type-checker.</summary>
        public SegType ResultType;

        public static AstNode MakeValue(SegValue v) => new AstNode { Kind = AstKind.Value, Value = v, ResultType = v.Type };

        public static AstNode MakeRef(string r, RefWindow w = RefWindow.None) =>
            new AstNode { Kind = AstKind.Ref, Ref = r, Window = w };

        public static AstNode MakeOp(string op, params AstNode[] args) =>
            new AstNode { Kind = AstKind.Op, Op = op, Args = args };
    }

    /// <summary>Tên operator MVP — §6.2.</summary>
    public static class AstOps
    {
        public const string ADD = "ADD", SUB = "SUB", MUL = "MUL", DIV = "DIV", MOD = "MOD";
        public const string GT = "GT", GTE = "GTE", LT = "LT", LTE = "LTE", EQ = "EQ", NEQ = "NEQ";
        public const string AND = "AND", OR = "OR", NOT = "NOT", IF = "IF";
        public const string MIN = "MIN", MAX = "MAX", ABS = "ABS", CLAMP = "CLAMP";
        public const string NOW = "NOW", TIME_SINCE = "TIME_SINCE", DAYS_BETWEEN = "DAYS_BETWEEN";

        public static readonly string[] All =
        {
            ADD, SUB, MUL, DIV, MOD, GT, GTE, LT, LTE, EQ, NEQ, AND, OR, NOT, IF, MIN, MAX, ABS, CLAMP, NOW,
            TIME_SINCE, DAYS_BETWEEN
        };

        public static bool IsKnown(string op) => Array.IndexOf(All, op) >= 0;
    }
}
