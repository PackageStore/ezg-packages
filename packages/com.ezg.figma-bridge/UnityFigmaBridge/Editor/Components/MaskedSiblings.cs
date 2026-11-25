using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;
using Object = UnityEngine.Object;

namespace UnityFigmaBridge.Editor.Components
{
    /// <summary>
    ///     A "Use as mask" layer masks the siblings above it, and a UGUI Mask only masks its own
    ///     children, so the build pass parents those siblings under the mask's object
    ///     (<c>SetParent(mask, worldPositionStays: true)</c>). An instance's pass matches each Figma
    ///     child among the direct children of its Figma parent's object, where a masked layer is not,
    ///     so it was skipped: no own render, colour, text or visibility from the instance (a stripe
    ///     fill under a progress mask kept the component's 188 px render on a 748 px bar).
    /// </summary>
    internal static class MaskedSiblings
    {
        /// <summary>
        ///     The object of <paramref name="childNode"/> when it sits under the object of a mask layer
        ///     that comes before it in <paramref name="parentNode"/>; <paramref name="maskHolder"/> is
        ///     that mask object. Null when it is not there.
        /// </summary>
        public static GameObject FindMaskedChild(Node childNode, Node parentNode, Transform parentTransform,
            out Transform maskHolder)
        {
            maskHolder = null;
            if (parentNode?.children == null || parentTransform == null) return null;
            foreach (var sibling in parentNode.children)
            {
                if (sibling == null || sibling == childNode) break;
                if (!sibling.isMask) continue;
                var holder = ComponentManager.FindMatchingChildForFigmaNode(sibling, parentTransform);
                if (holder == null) continue;
                var found = ComponentManager.FindMatchingChildForFigmaNode(childNode, holder.transform);
                if (found == null) continue;
                maskHolder = holder.transform;
                return found;
            }
            return null;
        }

        /// <summary>
        ///     The instance pass lays a node out in its Figma parent's space, while a masked layer lives
        ///     under the mask object, whose box is usually smaller (a progress mask over part of a bar).
        ///     A probe takes the layout under the Figma parent, and the layer keeps the probe's world box
        ///     under the mask: same pivot point, size, rotation and scale, its anchors and pivot kept.
        /// </summary>
        public static void KeepUnderMask(RectTransform target, RectTransform figmaParent, RectTransform maskHolder)
        {
            if (target == null || figmaParent == null || maskHolder == null) return;
            var probe = new GameObject("[MaskedPlacement]", typeof(RectTransform)).transform as RectTransform;
            try
            {
                probe.SetParent(figmaParent, false);
                probe.anchorMin = target.anchorMin;
                probe.anchorMax = target.anchorMax;
                probe.pivot = target.pivot;
                probe.localRotation = target.localRotation;
                probe.localScale = target.localScale;
                probe.sizeDelta = target.sizeDelta;
                probe.anchoredPosition = target.anchoredPosition;
                PlaceLike(target, probe, maskHolder);
            }
            finally
            {
                Object.DestroyImmediate(probe.gameObject);
            }
        }

        /// <summary>
        ///     The build pass parents a masked layer, laid out under its Figma parent, under the mask
        ///     object. <c>SetParent(mask, worldPositionStays: true)</c> could leave a stretched layer the
        ///     size of the mask (a 188 px stripe fill squeezed into the 75 px a 40 % progress mask shows),
        ///     so the layer's box is set again after the move.
        /// </summary>
        public static void MoveUnderMask(RectTransform target, RectTransform maskHolder)
        {
            if (target == null || maskHolder == null) return;
            var probe = new GameObject("[MaskedPlacement]", typeof(RectTransform)).transform as RectTransform;
            try
            {
                probe.SetParent(target.parent, false);
                probe.anchorMin = target.anchorMin;
                probe.anchorMax = target.anchorMax;
                probe.pivot = target.pivot;
                probe.localRotation = target.localRotation;
                probe.localScale = target.localScale;
                probe.sizeDelta = target.sizeDelta;
                probe.anchoredPosition = target.anchoredPosition;
                target.SetParent(maskHolder, true);
                PlaceLike(target, probe, maskHolder);
            }
            finally
            {
                Object.DestroyImmediate(probe.gameObject);
            }
        }

        /// <summary>The target, a child of <paramref name="holder"/>, takes the world box of <paramref name="reference"/>.</summary>
        private static void PlaceLike(RectTransform target, RectTransform reference, RectTransform holder)
        {
            target.localRotation = Quaternion.Inverse(holder.rotation) * reference.rotation;
            var holderScale = holder.lossyScale;
            var referenceScale = reference.lossyScale;
            target.localScale = new Vector3(Ratio(referenceScale.x, holderScale.x), Ratio(referenceScale.y, holderScale.y),
                target.localScale.z);
            target.sizeDelta = reference.rect.size - Vector2.Scale(holder.rect.size, target.anchorMax - target.anchorMin);
            target.position = reference.position;
        }

        private static float Ratio(float value, float of) => Mathf.Abs(of) > 1e-6f ? value / of : value;
    }
}
