using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Utils;

namespace UnityFigmaBridge.Editor.Nodes
{
    /// <summary>
    ///     Figma auto layout as a Horizontal or Vertical LayoutGroup, when Enable Auto Layout is on.
    ///     The group controls its children's size from a LayoutElement each, which holds the Figma
    ///     size, so a child keeps its design size unless it hugs its text or fills the parent. A
    ///     hidden child is inactive and takes no space, as in Figma. A HUG frame gets a
    ///     ContentSizeFitter, unless an auto-layout parent already sizes it.
    /// </summary>
    public static class AutoLayout
    {
        private const string Hug = "HUG";
        private const string Fill = "FILL";

        private static bool IsAutoLayout(Node node) =>
            node.layoutMode == Node.LayoutMode.HORIZONTAL || node.layoutMode == Node.LayoutMode.VERTICAL;

        public static void Apply(GameObject target, Node node, FigmaImportProcessData figmaImportProcessData)
        {
            if (!figmaImportProcessData.Settings.EnableAutoLayout || !IsAutoLayout(node)) return;
            var horizontal = node.layoutMode == Node.LayoutMode.HORIZONTAL;
            var existing = target.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (existing != null && existing is HorizontalLayoutGroup != horizontal &&
                !PrefabUtility.IsPartOfPrefabInstance(existing))
                Object.DestroyImmediate(existing);

            HorizontalOrVerticalLayoutGroup group = horizontal
                ? UnityUiUtils.GetOrAddComponent<HorizontalLayoutGroup>(target)
                : UnityUiUtils.GetOrAddComponent<VerticalLayoutGroup>(target);
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = group.childForceExpandHeight = false;
            group.childScaleWidth = group.childScaleHeight = false;
            group.childAlignment = Alignment(node, horizontal);
            group.padding = new RectOffset(Mathf.RoundToInt(node.paddingLeft), Mathf.RoundToInt(node.paddingRight),
                Mathf.RoundToInt(node.paddingTop), Mathf.RoundToInt(node.paddingBottom));
            group.spacing = node.primaryAxisAlignItems == Node.PrimaryAxisAlignItems.SPACE_BETWEEN
                ? SpaceBetweenGap(node, horizontal)
                : node.itemSpacing;
            // The generator draws the children in reverse for "first on top"; this keeps them placed in Figma's order
            group.reverseArrangement = node.itemReverseZIndex;

            var hugsWidth = node.layoutSizingHorizontal == Hug;
            var hugsHeight = node.layoutSizingVertical == Hug;
            var fitter = target.GetComponent<ContentSizeFitter>();
            if (!hugsWidth && !hugsHeight)
            {
                if (fitter != null) fitter.enabled = false;
                return;
            }
            fitter = UnityUiUtils.GetOrAddComponent<ContentSizeFitter>(target);
            fitter.enabled = true;
            fitter.horizontalFit = hugsWidth ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = hugsHeight ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
        }

        /// <summary>Sizes one child of an auto-layout parent, as a LayoutElement the group reads.</summary>
        public static void ApplyChild(GameObject child, Node childNode, Node parentNode, FigmaImportProcessData figmaImportProcessData)
        {
            if (!figmaImportProcessData.Settings.EnableAutoLayout || !IsAutoLayout(parentNode)) return;
            var element = UnityUiUtils.GetOrAddComponent<LayoutElement>(child);
            element.ignoreLayout = childNode.layoutPositioning == "ABSOLUTE";
            if (element.ignoreLayout) return;

            // The parent group sizes this child, so its own fitter would fight it
            var fitter = child.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;

            var fillsWidth = childNode.layoutSizingHorizontal == Fill;
            var fillsHeight = childNode.layoutSizingVertical == Fill;
            var autoResize = childNode.type == NodeType.TEXT && childNode.style != null
                ? childNode.style.textAutoResize
                : TypeStyle.TextAutoResize.NONE;
            var textWidth = autoResize == TypeStyle.TextAutoResize.WIDTH_AND_HEIGHT;
            var textHeight = textWidth || autoResize == TypeStyle.TextAutoResize.HEIGHT;
            var size = childNode.size;
            element.preferredWidth = fillsWidth || textWidth || size == null ? -1f : size.x;
            element.preferredHeight = fillsHeight || textHeight || size == null ? -1f : size.y;
            element.flexibleWidth = fillsWidth ? 1f : -1f;
            element.flexibleHeight = fillsHeight ? 1f : -1f;
            // The transform pass pins the minimum to the design size; a hugging or filling axis must be free to shrink
            if (fillsWidth || textWidth) element.minWidth = -1f;
            if (fillsHeight || textHeight) element.minHeight = -1f;
        }

        private static TextAnchor Alignment(Node node, bool horizontal)
        {
            var primary = node.primaryAxisAlignItems switch
            {
                Node.PrimaryAxisAlignItems.CENTER => 1,
                Node.PrimaryAxisAlignItems.MAX => 2,
                _ => 0
            };
            var counter = node.counterAxisAlignItems switch
            {
                Node.CounterAxisAlignItems.CENTER => 1,
                Node.CounterAxisAlignItems.MAX => 2,
                _ => 0
            };
            var column = horizontal ? primary : counter;
            var row = horizontal ? counter : primary;
            // TextAnchor runs UpperLeft..LowerRight, three per row
            return (TextAnchor)(row * 3 + column);
        }

        /// <summary>UGUI has no space-between, so the gap is the one Figma used at import size.</summary>
        private static float SpaceBetweenGap(Node node, bool horizontal)
        {
            var placed = (node.children ?? new Node[] { })
                .Where(c => c.visible && c.layoutPositioning != "ABSOLUTE" && c.size != null).ToList();
            if (placed.Count < 2 || node.size == null) return node.itemSpacing;
            var length = horizontal
                ? node.size.x - node.paddingLeft - node.paddingRight - placed.Sum(c => c.size.x)
                : node.size.y - node.paddingTop - node.paddingBottom - placed.Sum(c => c.size.y);
            return Mathf.Max(0f, length / (placed.Count - 1));
        }
    }
}
