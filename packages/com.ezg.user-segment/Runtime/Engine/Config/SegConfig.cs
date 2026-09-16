using System.Collections.Generic;

namespace Ezg.UserSegment.Engine
{
    public sealed class FormulaDef
    {
        public string Id;
        public AstNode Expr;
    }

    public sealed class CaseDef
    {
        public string Value;
        public AstNode When;
    }

    public sealed class SegmentDef
    {
        public string Id;
        public bool IsTag;

        // Tag
        public AstNode Enter;
        public AstNode Exit;
        public string DefHash;

        // Partition
        public string Default;
        public List<CaseDef> Cases = new List<CaseDef>();
    }

    public sealed class CapDef
    {
        public int Count;
        public int WindowS;
    }

    /// <summary>Conflict group của action — §8.3.</summary>
    public static class ActionGroup
    {
        public const string Reward = "reward";
        public const string Difficulty = "difficulty";
        public const string Offer = "offer";
        public const string Message = "message";
        public const string Notification = "notification";
        public static readonly string[] All = { Reward, Difficulty, Offer, Message, Notification };
    }

    public sealed class ActionDef
    {
        public string Id;
        public ActionType Type;
        public Dictionary<string, object> Params = new Dictionary<string, object>();
        public string Group;
        public bool OneShot;
        public int CooldownS;

        /// <summary>null = không cap.</summary>
        public CapDef Cap;
    }

    public sealed class RuleDef
    {
        public string Id;
        public bool Enabled;
        public int Priority;
        public string[] On;
        public bool FireEdge;
        public AstNode When;

        /// <summary>action_id hoặc "none".</summary>
        public string Then;

        public long ValidUntil;
        public string DefHash;

        public const string NONE = "none";
    }

    public sealed class VariantDef
    {
        public string Id;
        public double Weight;
        public Dictionary<string, string> RuleActions = new Dictionary<string, string>();
    }

    public sealed class ExperimentDef
    {
        public string Id;
        public string Layer;
        public double Allocation;
        public long EndsAt;
        public List<VariantDef> Variants = new List<VariantDef>();
    }

    /// <summary>Config đã compile, tường minh toàn phần — §C.1. Kèm index và thứ tự topo do <see cref="ConfigValidator" /> điền.</summary>
    public sealed class SegConfig
    {
        public int SchemaVersion;
        public string MinSdkVersion;
        public string GameId;
        public string Env;
        public long Version;
        public string PublishedAt;
        public string SourceCommit;
        public bool Enabled;
        public int SessionTimeoutS;
        public int MaxActionsPerDay;
        public double HoldoutAllocation;
        public Dictionary<string, double> Const = new Dictionary<string, double>();
        public List<FormulaDef> Formulas = new List<FormulaDef>();
        public List<SegmentDef> Segments = new List<SegmentDef>();
        public List<ActionDef> Actions = new List<ActionDef>();
        public List<RuleDef> Rules = new List<RuleDef>();
        public List<ExperimentDef> Experiments = new List<ExperimentDef>();

        // ---- Index (điền sau validate) ----
        public Dictionary<string, FormulaDef> FormulaById = new Dictionary<string, FormulaDef>();
        public Dictionary<string, SegmentDef> SegmentById = new Dictionary<string, SegmentDef>();
        public Dictionary<string, ActionDef> ActionById = new Dictionary<string, ActionDef>();
        public Dictionary<string, RuleDef> RuleById = new Dictionary<string, RuleDef>();

        /// <summary>Thứ tự evaluate formula ∪ segment (id) theo topo — §6.3.</summary>
        public List<string> EvalOrder = new List<string>();

        /// <summary>Rule đã sort priority giảm, id tăng — §8.2.</summary>
        public List<RuleDef> RulesOrdered = new List<RuleDef>();

        public const int SUPPORTED_SCHEMA = 3;
    }
}
