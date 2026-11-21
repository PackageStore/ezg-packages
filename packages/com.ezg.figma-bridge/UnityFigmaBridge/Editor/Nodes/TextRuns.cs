using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;

namespace UnityFigmaBridge.Editor.Nodes
{
    /// <summary>
    ///     A TEXT node whose characters do not all share one style (<c>characterStyleOverrides</c> into
    ///     <c>styleOverrideTable</c>): "638 +11" with a smaller green "+11", "21  (-15% dmg taken)" with a
    ///     larger number. TextMeshPro has one size and colour per component, so the runs become rich
    ///     text tags around the component's own style.
    ///     <para>
    ///     The component takes the style of the FIRST character, as Figma does when the whole text is
    ///     typed over: a game that sets the text at runtime gets the size the design shows for its
    ///     leading run, not the node's default style that may cover no character at all. Every run
    ///     whose size, colour or weight differs from it is wrapped: <c>&lt;size=N%&gt;</c> (relative, so
    ///     TMP auto-size scales the runs together), <c>&lt;color=#RRGGBBAA&gt;</c>, and <c>&lt;b&gt;</c> for
    ///     a heavier weight. A run in another font family keeps the component's font.
    ///     </para>
    /// </summary>
    public sealed class TextRuns
    {
        /// <summary>Style of the first character: the node style with that character's override on top.</summary>
        public TypeStyle BaseStyle;

        /// <summary>Fill of the first character (the node fill unless its override has its own).</summary>
        public Paint BaseFill;

        /// <summary>The characters with a tag around each run that differs from the first character.</summary>
        public string RichText;

        private struct Run
        {
            public float Size;
            public int Weight;
            public Paint Fill;
        }

        /// <summary>The runs of a TEXT node, or null when every character has the node style.</summary>
        public static TextRuns Read(Node node, Paint nodeFill)
        {
            var overrides = node.characterStyleOverrides;
            var table = node.styleOverrideTable;
            if (node.style == null || string.IsNullOrEmpty(node.characters) || overrides == null || table == null) return null;
            var any = false;
            foreach (var id in overrides) any |= id != 0 && table.ContainsKey(id.ToString(CultureInfo.InvariantCulture));
            if (!any) return null;

            var chars = node.characters;
            var first = RunAt(node, 0, nodeFill);
            var runs = new TextRuns { BaseStyle = Merge(node.style, Override(node, 0)), BaseFill = first.Fill };
            var rich = new StringBuilder(chars.Length + 32);
            var start = 0;
            for (var i = 1; i <= chars.Length; i++)
            {
                // Overrides index UTF-16 code units, as the characters string does
                if (i < chars.Length && Same(RunAt(node, i, nodeFill), RunAt(node, start, nodeFill))) continue;
                Append(rich, chars.Substring(start, i - start), RunAt(node, start, nodeFill), first);
                start = i;
            }

            runs.RichText = rich.ToString();
            return runs;
        }

        private static TypeStyle Override(Node node, int index)
        {
            var overrides = node.characterStyleOverrides;
            // The array stops at the last overridden character: the rest have the node style
            if (index >= overrides.Length || overrides[index] == 0) return null;
            return node.styleOverrideTable.TryGetValue(overrides[index].ToString(CultureInfo.InvariantCulture), out var style) ? style : null;
        }

        private static Run RunAt(Node node, int index, Paint nodeFill)
        {
            var o = Override(node, index);
            return new Run
            {
                Size = o != null && o.fontSize > 0f ? o.fontSize : node.style.fontSize,
                Weight = o != null && o.fontWeight > 0 ? o.fontWeight : node.style.fontWeight,
                Fill = o?.fills != null && o.fills.Length > 0 ? TopVisible(o.fills) : nodeFill,
            };
        }

        /// <summary>
        ///     The node style with an override's set fields on top. An override lists only what it
        ///     changes, so a zero size, weight or line height means "not set".
        /// </summary>
        private static TypeStyle Merge(TypeStyle style, TypeStyle o)
        {
            if (o == null) return style;
            var merged = (TypeStyle)style.ShallowCopy();
            if (o.fontSize > 0f)
            {
                merged.fontSize = o.fontSize;
                // A line height set in pixels scales with the size unless the override sets its own
                merged.lineHeightPx = o.lineHeightPx > 0f ? o.lineHeightPx : style.lineHeightPx * o.fontSize / style.fontSize;
            }
            if (o.fontWeight > 0) merged.fontWeight = o.fontWeight;
            if (o.letterSpacing != 0f) merged.letterSpacing = o.letterSpacing;
            if (o.italic) merged.italic = true;
            return merged;
        }

        private static bool Same(Run a, Run b) =>
            Mathf.Approximately(a.Size, b.Size) && a.Weight == b.Weight && Hex(a.Fill) == Hex(b.Fill);

        private static void Append(StringBuilder rich, string text, Run run, Run first)
        {
            text = text.Replace("<", "<noparse><</noparse>");
            var size = !Mathf.Approximately(run.Size, first.Size) && first.Size > 0f;
            var colour = Hex(run.Fill) != Hex(first.Fill) && run.Fill != null;
            var bold = run.Weight >= first.Weight + 100;
            if (size) rich.Append("<size=").Append((run.Size / first.Size * 100f).ToString("0.##", CultureInfo.InvariantCulture)).Append("%>");
            if (colour) rich.Append("<color=#").Append(Hex(run.Fill)).Append('>');
            if (bold) rich.Append("<b>");
            rich.Append(text);
            if (bold) rich.Append("</b>");
            if (colour) rich.Append("</color>");
            if (size) rich.Append("</size>");
        }

        private static string Hex(Paint fill) =>
            fill == null ? "" : ColorUtility.ToHtmlStringRGBA(FigmaDataUtils.GetUnityFillColor(fill));

        private static Paint TopVisible(Paint[] paints)
        {
            for (var i = paints.Length - 1; i >= 0; i--)
                if (paints[i] != null && paints[i].visible && !FigmaDataUtils.IsShaderPaint(paints[i])) return paints[i];
            return null;
        }
    }
}
