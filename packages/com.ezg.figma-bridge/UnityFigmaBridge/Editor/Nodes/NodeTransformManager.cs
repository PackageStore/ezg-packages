using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Settings;

namespace UnityFigmaBridge.Editor.Nodes
{
    public static class NodeTransformManager
    {
        /// <summary>
        /// Applies the transform of a Figma Node to a Unity RectTransform.
        /// </summary>
        /// <param name="targetRectTransform"></param>
        /// <param name="figmaNode"></param>
        /// <param name="figmaParentNode"></param>
        /// <param name="centerPivot"></param>
        /// <param name="settings">Import settings; null keeps the 0.2 behaviour (LayoutElement on every node, no text padding).</param>
        public static void ApplyFigmaTransform(RectTransform targetRectTransform, Node figmaNode, Node figmaParentNode,
            bool centerPivot, UnityFigmaBridgeSettings settings = null)
        {
            // Default to top left alignment
            targetRectTransform.anchorMin = targetRectTransform.anchorMax = new Vector2(0, 1);
            targetRectTransform.pivot = new Vector2(0, 1);

            if (figmaNode.relativeTransform != null)
            {
                // Apply the "relativeTransform" from Figma Node for translation and rotation
                targetRectTransform.anchoredPosition = new Vector2(figmaNode.relativeTransform[0, 2],
                    -figmaNode.relativeTransform[1, 2]);
                var rotation = Mathf.Rad2Deg *
                               Mathf.Atan2(-figmaNode.relativeTransform[1, 0], figmaNode.relativeTransform[0, 0]);
                targetRectTransform.localRotation = Quaternion.Euler(0, 0, rotation);
            }

            // The sign is set, not toggled: a component instance applies this again to every layer
            // its prefab already mirrored, and a toggle would flip those layers back.
            var scale = targetRectTransform.localScale;
            var mirrorX = figmaNode.relativeTransform[0, 0] < 0;
            var mirrorY = figmaNode.relativeTransform[1, 1] < 0;
            targetRectTransform.localScale = new Vector3(mirrorX ? -Mathf.Abs(scale.x) : Mathf.Abs(scale.x),
                mirrorY ? -Mathf.Abs(scale.y) : Mathf.Abs(scale.y), scale.z);
            if (mirrorX) targetRectTransform.localRotation *= Quaternion.Euler(0, 0, -180);

            // Apply the "size" figmaNode from figma to set size
            targetRectTransform.sizeDelta = new Vector2(figmaNode.size.x, figmaNode.size.y);

            // Add a layout element and set its preferred size. In OnlyUnderAutoLayout mode a node
            // gets one only when its parent is an auto-layout frame, which is the only place a
            // layout group could read it.
            if (ShouldAddLayoutElement(figmaParentNode, settings))
            {
                // Get-or-add: a component instance already carries the element its prefab was built with
                var layoutElement = targetRectTransform.gameObject.GetComponent<LayoutElement>();
                if (layoutElement == null) layoutElement = targetRectTransform.gameObject.AddComponent<LayoutElement>();
                layoutElement.preferredWidth = figmaNode.size.x;
                layoutElement.preferredHeight = figmaNode.size.y;

                layoutElement.minHeight = figmaNode.absoluteBoundingBox.height;
                layoutElement.minWidth = figmaNode.absoluteBoundingBox.width;
                AutoLayout.ReleaseHugAxes(layoutElement, figmaNode, settings);
            }

            // Apply constraints
            // Groups in Figma dont have their own constraints, so to setup effectively, we need to use the first child's constraints
            var useFirstChildForConstraints = figmaNode.type == NodeType.GROUP && figmaNode.children != null &&
                                              figmaNode.children.Length > 0;
            var constraintsSourceNode = useFirstChildForConstraints ? figmaNode.children[0] : figmaNode;

            // Some nodes will not have a constraints node (eg SECTION nodes)
            if (constraintsSourceNode.constraints!=null) ApplyFigmaConstraints(targetRectTransform, constraintsSourceNode, figmaParentNode);

            // Auto-resized text keeps a box that hugs the glyphs of the design font. Rendered with
            // another font it overflows, so in FixedRectAutoSize mode the box grows by the padding
            // factors while its aligned edge stays put.
            if (figmaNode.type == NodeType.TEXT && settings != null &&
                settings.TextFitMode == TextFitMode.FixedRectAutoSize && figmaNode.style != null)
                ApplyTextPadding(targetRectTransform, figmaNode, settings);

            // We'll also use these properties to apply pivot after, where required
            // We disable center pivot for Text nodes, as this creates behaviour different from Figma when autosizing
            if (figmaNode.type==NodeType.TEXT) centerPivot = false;
            if (centerPivot) SetPivot(targetRectTransform, new Vector2(0.5f, 0.5f));
        }

        private static bool ShouldAddLayoutElement(Node figmaParentNode, UnityFigmaBridgeSettings settings)
        {
            if (settings == null || settings.AddLayoutElements == LayoutElementMode.Always) return true;
            return figmaParentNode != null && figmaParentNode.layoutMode != Node.LayoutMode.NONE;
        }

        /// <summary>
        ///     Grow an auto-resized text rect by the configured padding. Works in the parent's space
        ///     through anchoredPosition and sizeDelta: the pivot is top-left here, so a wider box
        ///     keeps its left edge unless the text is centred or right aligned, and a taller box
        ///     keeps its top edge unless the text is middle or bottom aligned. An axis the
        ///     constraints already stretch is left alone.
        /// </summary>
        private static void ApplyTextPadding(RectTransform rectTransform, Node textNode, UnityFigmaBridgeSettings settings)
        {
            var autoResize = textNode.style.textAutoResize;
            if (autoResize == TypeStyle.TextAutoResize.NONE || autoResize == TypeStyle.TextAutoResize.TRUNCATE) return;

            var stretchX = !Mathf.Approximately(rectTransform.anchorMin.x, rectTransform.anchorMax.x);
            var stretchY = !Mathf.Approximately(rectTransform.anchorMin.y, rectTransform.anchorMax.y);

            var size = rectTransform.sizeDelta;
            var widthFactor = autoResize == TypeStyle.TextAutoResize.WIDTH_AND_HEIGHT ? settings.TextWidthPadding : 1f;
            var heightFactor = settings.TextHeightPadding;

            var deltaWidth = stretchX ? 0f : size.x * (Mathf.Max(1f, widthFactor) - 1f);
            var deltaHeight = stretchY ? 0f : size.y * (Mathf.Max(1f, heightFactor) - 1f);
            if (deltaWidth <= 0f && deltaHeight <= 0f) return;

            rectTransform.sizeDelta = new Vector2(size.x + deltaWidth, size.y + deltaHeight);

            var position = rectTransform.anchoredPosition;
            position.x -= textNode.style.textAlignHorizontal switch
            {
                TypeStyle.TextAlignHorizontal.CENTER => deltaWidth * 0.5f,
                TypeStyle.TextAlignHorizontal.RIGHT => deltaWidth,
                _ => 0f
            };
            position.y += textNode.style.textAlignVertical switch
            {
                TypeStyle.TextAlignVertical.CENTER => deltaHeight * 0.5f,
                TypeStyle.TextAlignVertical.BOTTOM => deltaHeight,
                _ => 0f
            };
            rectTransform.anchoredPosition = position;
        }

        /// <summary>
        /// Applies constraints to a given RectTransform based on a given Figma Node
        /// </summary>
        /// <param name="targetRectTransform"></param>
        /// <param name="figmaNode"></param>
        /// <param name="figmaParentNode"></param>
        private static void ApplyFigmaConstraints(RectTransform targetRectTransform, Node figmaNode,Node figmaParentNode)
        {
             // Setup anchor positions
            (targetRectTransform.anchorMin, targetRectTransform.anchorMax) = AnchorPositionsForFigmaConstraints(figmaNode.constraints);

            // We'll need to use the size of the parent node to determine anchor position
            var parentNodeSize = figmaParentNode.size != null ? figmaParentNode.size : new Vector { x = 0, y = 0 };

            // TODO - Implement SCALE constraint

            // Modify anchor position according to constraint
            var anchoredPosition = targetRectTransform.anchoredPosition;

            anchoredPosition.x += figmaNode.constraints.horizontal switch
            {
                LayoutConstraint.HorizontalLayoutConstraint.CENTER => -parentNodeSize.x * 0.5f,
                LayoutConstraint.HorizontalLayoutConstraint.RIGHT => -parentNodeSize.x,
                _ => 0
            };

            anchoredPosition.y += figmaNode.constraints.vertical switch
            {
                LayoutConstraint.VerticalLayoutConstraint.CENTER => parentNodeSize.y * 0.5f,
                LayoutConstraint.VerticalLayoutConstraint.BOTTOM => parentNodeSize.y,
                _ => 0
            };

            targetRectTransform.anchoredPosition = anchoredPosition;

            switch (figmaNode.constraints.horizontal)
            {
                case LayoutConstraint.HorizontalLayoutConstraint.LEFT_RIGHT:
                case LayoutConstraint.HorizontalLayoutConstraint.SCALE:
                    var sizeDelta = targetRectTransform.sizeDelta;
                    targetRectTransform.sizeDelta = new Vector2(sizeDelta.x-parentNodeSize.x, sizeDelta.y);
                    break;
            }

            switch (figmaNode.constraints.vertical)
            {
                case LayoutConstraint.VerticalLayoutConstraint.TOP_BOTTOM:
                case LayoutConstraint.VerticalLayoutConstraint.SCALE:
                    var sizeDelta = targetRectTransform.sizeDelta;
                    targetRectTransform.sizeDelta = new Vector2(sizeDelta.x, sizeDelta.y - parentNodeSize.y);
                    break;
            }

        }

        /// <summary>
        /// Applies the transform of a Figma Node to a Unity RectTransform using absolute position. This is
        /// required for Server rendered images, that have the transform (eg rotation) baked in
        /// </summary>
        /// <param name="targetRectTransform"></param>
        /// <param name="figmaNode"></param>
        /// <param name="figmaParentNode"></param>
        /// <param name="centerPivot"></param>
        /// <param name="settings">Import settings; null keeps the 0.2 behaviour.</param>
        /// <param name="ownRender">True when the Image shows this node's own render, which has every mirror
        /// above it baked in; false when it keeps a component's render, which the instance still mirrors.</param>
        public static void ApplyAbsoluteBoundsFigmaTransform(RectTransform targetRectTransform, Node figmaNode,
            Node figmaParentNode, bool centerPivot, UnityFigmaBridgeSettings settings = null, bool ownRender = true)
        {
            // Default to top left alignment
            targetRectTransform.anchorMin = targetRectTransform.anchorMax = new Vector2(0, 1);
            targetRectTransform.pivot = new Vector2(0, 1);

            // A server render covers the render bounds (outside strokes, shadows), not the layout box
            var bounds = figmaNode.absoluteRenderBounds ?? figmaNode.absoluteBoundingBox;
            targetRectTransform.sizeDelta = new Vector2(bounds.width, bounds.height);

            if (ShouldAddLayoutElement(figmaParentNode, settings))
            {
                //Add a layout element and set its preferred size
                var layoutElement = targetRectTransform.gameObject.GetComponent<LayoutElement>();
                if (layoutElement == null) layoutElement = targetRectTransform.gameObject.AddComponent<LayoutElement>();
                layoutElement.preferredWidth = bounds.width;
                layoutElement.preferredHeight = bounds.height;

                layoutElement.minHeight = bounds.height;
                layoutElement.minWidth = bounds.width;
            }

            // Position will be relative to parent absoluteBoundingBox (if it exists). Pages have no absoluteBoundingBox so assume pos of 0,0
            var figmaParentNodePosition = figmaParentNode.absoluteBoundingBox != null
                ? new Vector2(figmaParentNode.absoluteBoundingBox.x, figmaParentNode.absoluteBoundingBox.y)
                : Vector2.zero;

            targetRectTransform.anchoredPosition=new Vector2(bounds.x-figmaParentNodePosition.x,
                -(bounds.y-figmaParentNodePosition.y));

            if (figmaParentNode.absoluteBoundingBox != null)
                PlaceUnderMirroredParent(targetRectTransform, bounds, figmaParentNode.absoluteBoundingBox, ownRender);

            // Some nodes will not have a constraints node (eg SECTION nodes)
            if (figmaNode.constraints!=null) ApplyFigmaConstraints(targetRectTransform, figmaNode, figmaParentNode);

            // We'll also use these properties to apply pivot after, where required
            if (centerPivot) SetPivot(targetRectTransform, new Vector2(0.5f, 0.5f));

        }

        /// <summary>
        ///     Absolute bounds are in Figma page space, but a parent mirrored on one axis (a negative
        ///     scale) flips its local space. The place is measured from the parent's mirrored edge, and
        ///     an own render, which already shows the mirror, takes the parent's flip so it is not
        ///     mirrored twice. A rotated parent keeps the plain placement.
        /// </summary>
        private static void PlaceUnderMirroredParent(RectTransform target, Rectangle bounds, Rectangle parentBounds,
            bool ownRender)
        {
            if (!TryGetMirroredParentAxes(target.parent, out var parentX, out var parentY)) return;

            var scale = target.localScale;
            if (ownRender)
                target.localScale = scale = new Vector3(parentX * Mathf.Abs(scale.x), parentY * Mathf.Abs(scale.y), scale.z);

            var originX = parentX > 0 ? parentBounds.x : parentBounds.x + parentBounds.width;
            var originY = parentY > 0 ? parentBounds.y : parentBounds.y + parentBounds.height;
            var pivotX = parentX * scale.x > 0 ? bounds.x : bounds.x + bounds.width;
            var pivotY = parentY * scale.y > 0 ? bounds.y : bounds.y + bounds.height;
            target.anchoredPosition = new Vector2(parentX * (pivotX - originX), -parentY * (pivotY - originY));
        }

        /// <summary>
        ///     The world flip of each axis of <paramref name="parent"/>; true only when exactly one axis
        ///     is flipped and the parent is not rotated off the axes.
        /// </summary>
        private static bool TryGetMirroredParentAxes(Transform parent, out float x, out float y)
        {
            x = y = 1f;
            if (parent == null) return false;
            var right = parent.TransformVector(Vector3.right);
            var up = parent.TransformVector(Vector3.up);
            const float tolerance = 1e-3f;
            if (Mathf.Abs(right.y) > tolerance * Mathf.Abs(right.x) || Mathf.Abs(up.x) > tolerance * Mathf.Abs(up.y))
                return false;
            x = Mathf.Sign(right.x);
            y = Mathf.Sign(up.y);
            return x * y < 0;
        }

        /// <summary>
        /// Modify the pivot point of a RectTransform, and move to ensure position stays the same
        /// </summary>
        /// <param name="target"></param>
        /// <param name="pivot"></param>
        private static void SetPivot(RectTransform target, Vector2 pivot)
        {
            if (!target) return;
            var offset=pivot - target.pivot;
            offset.Scale(target.rect.size);
            var worldPos= target.position + target.TransformVector(offset);
            target.pivot = pivot;
            target.position = worldPos;
        }

        /// <summary>
        /// Apply anchor positions according to constraints
        /// </summary>
        /// <param name="constraint"></param>
        /// <returns></returns>
        private static (Vector2, Vector2) AnchorPositionsForFigmaConstraints(LayoutConstraint constraint)
        {
            var anchorsX = constraint.horizontal switch
            {
                LayoutConstraint.HorizontalLayoutConstraint.LEFT => new Vector2(0, 0),
                LayoutConstraint.HorizontalLayoutConstraint.RIGHT => new Vector2(1, 1),
                LayoutConstraint.HorizontalLayoutConstraint.CENTER => new Vector2(0.5f, 0.5f),
                LayoutConstraint.HorizontalLayoutConstraint.LEFT_RIGHT => new Vector2(0, 1),
                LayoutConstraint.HorizontalLayoutConstraint.SCALE => new Vector2(0, 1),
                _ => new Vector2(0, 1)
            };

            var anchorsY = constraint.vertical switch
            {
                LayoutConstraint.VerticalLayoutConstraint.TOP => new Vector2(1, 1),
                LayoutConstraint.VerticalLayoutConstraint.BOTTOM => new Vector2(0, 0),
                LayoutConstraint.VerticalLayoutConstraint.CENTER => new Vector2(0.5f, 0.5f),
                LayoutConstraint.VerticalLayoutConstraint.TOP_BOTTOM => new Vector2(0, 1),
                LayoutConstraint.VerticalLayoutConstraint.SCALE => new Vector2(0, 1),
                _ => new Vector2(0, 1)
            };
            return (new Vector2(anchorsX.x, anchorsY.x), new Vector2(anchorsX.y, anchorsY.y));
        }

        /// <summary>
        /// Gets the relative bounding box for the contents of all children. Useful for resizing for scroll content when
        /// bounds are not declared explicitly in the document and auto-layout not used
        /// </summary>
        /// <param name="figmaNode"></param>
        /// <returns></returns>
        public static Rect GetRelativeBoundsForAllChildNodes(Node figmaNode)
        {
            if (figmaNode.children == null) return new Rect();
            var mergedRect = new Rect();
            for (var i = 0; i < figmaNode.children.Length; i++)
            {
                var childNode = figmaNode.children[i];
                var relativePosition = new Vector2(childNode.absoluteBoundingBox.x - figmaNode.absoluteBoundingBox.x,
                    childNode.absoluteBoundingBox.y - figmaNode.absoluteBoundingBox.y);
                var size = new Vector2(childNode.absoluteBoundingBox.width, childNode.absoluteBoundingBox.height);

                if (i == 0)
                {
                    mergedRect.xMin = relativePosition.x;
                    mergedRect.xMax = relativePosition.x+size.x;
                    mergedRect.yMin = relativePosition.y;
                    mergedRect.yMax = relativePosition.y+size.y;
                }
                else
                {
                    mergedRect.xMin = Mathf.Min(mergedRect.xMin,relativePosition.x);
                    mergedRect.xMax = Mathf.Max(mergedRect.xMax,relativePosition.x+size.x);
                    mergedRect.yMin = Mathf.Min(mergedRect.yMin,relativePosition.y);
                    mergedRect.yMax = Mathf.Max(mergedRect.yMax,relativePosition.y+size.y);
                }
            }

            return mergedRect;
        }

    }
}
