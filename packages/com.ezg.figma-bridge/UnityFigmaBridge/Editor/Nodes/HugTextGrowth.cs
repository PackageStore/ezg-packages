using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;

namespace UnityFigmaBridge.Editor.Nodes
{
    /// <summary>
    ///     The side a single-line auto-width text keeps when its characters change, as Figma moves it.
    ///     Figma grows a text directly in a frame from its text alignment (a right aligned "Lv.1" keeps
    ///     its right edge), which the bridge already does. A text inside an auto-layout frame is placed
    ///     by that frame instead: in a frame that hugs its width the frame grows, and Figma grows a
    ///     hugging frame from its horizontal constraint (a LEFT frame keeps its left edge, whatever the
    ///     alignment of the text in it); in a fixed or filling frame the text stays where the frame's
    ///     alignment puts it. So a name in a "Name" frame that hugs it, constrained LEFT and holding a
    ///     CENTER aligned text, keeps its left edge in Figma, while the bridge kept the text centred on
    ///     the sample's box and a shorter or longer name moved off the left edge.
    ///     <para>
    ///         The text is aligned to that side (and <see cref="NodeTransformManager"/> pads its box away
    ///         from it), so new characters grow the way Figma grows them. The sample does not move: the
    ///         project font is rarely as wide as Figma's, and a line placed by the old alignment would
    ///         move with the new one, so in <c>FixedRectAutoSize</c> a margin keeps the sample where it was
    ///         (<see cref="KeepSamplePlace"/>). Without auto layout in Unity
    ///         a frame that draws something keeps its design size, so a text in a hugging frame with a
    ///         fill, stroke or effect keeps its own alignment inside that plate.
    ///     </para>
    /// </summary>
    public static class HugTextGrowth
    {
        /// <summary>Parent of every node in the document, by reference (ids can repeat in a spliced document).</summary>
        public static Dictionary<Node, Node> BuildParentLookup(FigmaFile file)
        {
            var parents = new Dictionary<Node, Node>();
            if (file?.document != null) AddChildren(file.document, parents);
            return parents;
        }

        private static void AddChildren(Node node, Dictionary<Node, Node> parents)
        {
            if (node.children == null) return;
            foreach (var child in node.children)
            {
                if (child == null) continue;
                parents[child] = node;
                AddChildren(child, parents);
            }
        }

        /// <summary>
        ///     The side <paramref name="text"/> grows from when it differs from what its own alignment
        ///     gives, or null when its own alignment already is Figma's (a text directly in a frame, a
        ///     wrapping or multi-line text, a stretched one, or one in a plate that keeps its size).
        /// </summary>
        public static TypeStyle.TextAlignHorizontal? Side(Node text, IReadOnlyDictionary<Node, Node> parents)
        {
            if (text == null || text.type != NodeType.TEXT || text.style == null || parents == null) return null;
            if (text.style.textAutoResize != TypeStyle.TextAutoResize.WIDTH_AND_HEIGHT) return null;
            // A multi-line text shows its alignment between its lines
            if (!string.IsNullOrEmpty(text.characters) && text.characters.IndexOfAny(LineBreaks) >= 0) return null;
            if (text.layoutSizingHorizontal == "FILL") return null;

            var side = GrowthSide(text, parents);
            return side == text.style.textAlignHorizontal ? null : side;
        }

        private static readonly char[] LineBreaks = { '\n', '\r', '\u2028', '\u2029' };

        private static TypeStyle.TextAlignHorizontal? GrowthSide(Node text, IReadOnlyDictionary<Node, Node> parents)
        {
            var node = text;
            while (parents.TryGetValue(node, out var parent) && parent != null)
            {
                var horizontal = parent.layoutMode == Node.LayoutMode.HORIZONTAL;
                var vertical = parent.layoutMode == Node.LayoutMode.VERTICAL;
                if ((!horizontal && !vertical) || node.layoutPositioning == "ABSOLUTE")
                    // Placed by its own position: a text grows from its alignment, a hugging frame from its constraint
                    return node == text ? null : FromConstraint(node.constraints);

                var placed = parent.children?.Where(c => c != null && c.visible && c.layoutPositioning != "ABSOLUTE").ToList();
                var hugsWidth = parent.layoutSizingHorizontal == "HUG";
                if (horizontal)
                {
                    if (hugsWidth)
                    {
                        if (DrawsSomething(parent)) return null;
                        node = parent;
                        continue;
                    }
                    switch (parent.primaryAxisAlignItems)
                    {
                        case Node.PrimaryAxisAlignItems.CENTER: return TypeStyle.TextAlignHorizontal.CENTER;
                        case Node.PrimaryAxisAlignItems.MAX: return TypeStyle.TextAlignHorizontal.RIGHT;
                        case Node.PrimaryAxisAlignItems.SPACE_BETWEEN:
                            if (placed == null || placed.Count < 2) return TypeStyle.TextAlignHorizontal.LEFT;
                            if (placed[0] == node) return TypeStyle.TextAlignHorizontal.LEFT;
                            if (placed[placed.Count - 1] == node) return TypeStyle.TextAlignHorizontal.RIGHT;
                            return TypeStyle.TextAlignHorizontal.CENTER;
                        default: return TypeStyle.TextAlignHorizontal.LEFT;
                    }
                }

                // Vertical: the counter axis places it, unless it stretches across
                if (node.layoutAlign == Node.LayoutAlign.STRETCH) return null;
                if (hugsWidth && placed != null && placed.Count == 1 && placed[0] == node)
                {
                    // The only child of a column that hugs it: the column grows with it
                    if (DrawsSomething(parent)) return null;
                    node = parent;
                    continue;
                }
                return parent.counterAxisAlignItems switch
                {
                    Node.CounterAxisAlignItems.CENTER => TypeStyle.TextAlignHorizontal.CENTER,
                    Node.CounterAxisAlignItems.MAX => TypeStyle.TextAlignHorizontal.RIGHT,
                    _ => TypeStyle.TextAlignHorizontal.LEFT
                };
            }
            return null;
        }

        /// <summary>
        ///     Sets the horizontal margins of <paramref name="text"/> (configured with its sample, font, size
        ///     and case): none, or for a <paramref name="side"/> from <see cref="Side"/> the margin that puts
        ///     the sample where the text's own alignment drew it in its Figma box. The project font rarely
        ///     draws a line exactly as wide as Figma's box, and the old alignment placed that difference
        ///     (all on one end, or half on each); the new one would place it elsewhere and move the sample.
        ///     Longer or shorter characters still keep the grow side's edge.
        /// </summary>
        public static void KeepSamplePlace(TMP_Text text, Node node, TypeStyle.TextAlignHorizontal? side)
        {
            var margin = text.margin;
            margin.x = margin.z = 0f;
            text.margin = margin;
            if (side == null || node.size == null || node.style == null || string.IsNullOrEmpty(text.text)) return;

            var autoSize = text.enableAutoSizing;
            text.enableAutoSizing = false;
            var drawn = text.GetPreferredValues(text.text, float.PositiveInfinity, float.PositiveInfinity).x;
            text.enableAutoSizing = autoSize;
            if (drawn <= 0f || float.IsInfinity(drawn) || float.IsNaN(drawn)) return;

            // Where the line started in the Figma box with the text's own alignment
            var spare = node.size.x - drawn;
            var start = node.style.textAlignHorizontal switch
            {
                TypeStyle.TextAlignHorizontal.CENTER => spare * 0.5f,
                TypeStyle.TextAlignHorizontal.RIGHT => spare,
                _ => 0f
            };
            // The padded box keeps the grow side's edge of the Figma box (centre for CENTER)
            switch (side)
            {
                case TypeStyle.TextAlignHorizontal.LEFT: margin.x = start; break;
                case TypeStyle.TextAlignHorizontal.RIGHT: margin.z = spare - start; break;
                default:
                    if (start > spare * 0.5f) margin.x = 2f * start - spare;
                    else margin.z = spare - 2f * start;
                    break;
            }
            text.margin = margin;
        }

        private static TypeStyle.TextAlignHorizontal? FromConstraint(LayoutConstraint constraints) =>
            constraints?.horizontal switch
            {
                LayoutConstraint.HorizontalLayoutConstraint.LEFT => TypeStyle.TextAlignHorizontal.LEFT,
                LayoutConstraint.HorizontalLayoutConstraint.RIGHT => TypeStyle.TextAlignHorizontal.RIGHT,
                LayoutConstraint.HorizontalLayoutConstraint.CENTER => TypeStyle.TextAlignHorizontal.CENTER,
                // LEFT_RIGHT and SCALE stretch the frame with its parent, so the text's alignment shows
                _ => null
            };

        /// <summary>A visible fill, stroke or effect: a plate Unity keeps at its design size.</summary>
        private static bool DrawsSomething(Node node) =>
            Visible(node.fills) || Visible(node.strokes) ||
            (node.effects != null && node.effects.Any(e => e != null && e.visible));

        private static bool Visible(Paint[] paints) =>
            paints != null && paints.Any(p => p != null && p.visible && p.opacity > 0f);
    }
}
