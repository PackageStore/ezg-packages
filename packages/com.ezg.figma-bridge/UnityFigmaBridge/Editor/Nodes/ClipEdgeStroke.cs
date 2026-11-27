using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace UnityFigmaBridge.Editor.Nodes
{
    /// <summary>
    ///     A card in Figma is often a frame with only an inside stroke and a child frame that clips its
    ///     content, inset by the stroke weight with the inner radius (a ring around a clipped panel).
    ///     Figma draws the frame's stroke over its children and clips with an anti-aliased edge. The
    ///     import draws the ring as the frame's own Image, before the children, and clips with a
    ///     <see cref="Mask"/>, whose stencil keeps or drops whole pixels: the panel showed a staircase
    ///     over the inside edge of the ring at every rounded corner. The ring is drawn again as the last
    ///     child of the clipping frame (<see cref="ChildName"/>, covering the parent's box). The stencil
    ///     cuts it to the clip area, so only the inside edge is drawn again; the outside edge and every
    ///     other layer stay as they were. Runs after the 9-slice pass, so it copies the final sprite.
    /// </summary>
    public static class ClipEdgeStroke
    {
        public const string ChildName = "[Stroke]";

        /// <summary>Design units the clipping frame may reach past the parent's box.</summary>
        private const float Tolerance = 1f;

        public static void Run(FigmaImportProcessData figmaImportProcessData)
        {
            var hollow = new Dictionary<Sprite, bool>();
            int edges = 0, prefabsTouched = 0;
            // Inner components first, so an instance finds its component's ring copy and only overrides it
            var prefabs = Components.ComponentManager.NestedFirst(figmaImportProcessData.ComponentData.AllComponentPrefabs,
                figmaImportProcessData);
            prefabs.AddRange(figmaImportProcessData.ScreenPrefabs);
            foreach (var prefab in prefabs)
            {
                if (prefab == null || !HasCandidate(prefab, hollow)) continue;
                var assetPath = AssetDatabase.GetAssetPath(prefab);
                var contents = PrefabUtility.LoadPrefabContents(assetPath);
                try
                {
                    var changed = false;
                    foreach (var mask in contents.GetComponentsInChildren<Mask>(true))
                    {
                        if (!TryGetRing(mask, hollow, out var ring)) continue;
                        edges++;
                        changed |= Apply((RectTransform)mask.transform, ring);
                    }
                    if (!changed) continue;
                    PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
                    prefabsTouched++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            Debug.Log($"[ClipEdgeStroke] {edges} clipping frame(s) inside a stroke ring, {prefabsTouched} prefab(s) touched");
        }

        private static bool HasCandidate(GameObject prefab, Dictionary<Sprite, bool> hollow)
        {
            foreach (var mask in prefab.GetComponentsInChildren<Mask>(true))
                if (TryGetRing(mask, hollow, out _)) return true;
            return false;
        }

        /// <summary>
        ///     The parent's Image when it is a hollow sliced ring (no fill: the centre texel is clear)
        ///     and the clipping frame lies inside the parent's box. A filled parent would cover the
        ///     children if drawn again.
        /// </summary>
        private static bool TryGetRing(Mask mask, Dictionary<Sprite, bool> hollow, out Image ring)
        {
            ring = null;
            var clip = mask.transform as RectTransform;
            var parent = clip != null ? clip.parent as RectTransform : null;
            if (parent == null || mask.showMaskGraphic) return false;
            var image = parent.GetComponent<Image>();
            if (image == null || image.sprite == null || image.type != Image.Type.Sliced || !IsHollow(image.sprite, hollow)) return false;
            if (clip.localScale != Vector3.one || clip.localRotation != Quaternion.identity) return false;
            Rect p = parent.rect, c = clip.rect;
            Vector2 min = (Vector2)clip.localPosition + c.min, max = (Vector2)clip.localPosition + c.max;
            if (min.x < p.xMin - Tolerance || min.y < p.yMin - Tolerance || max.x > p.xMax + Tolerance || max.y > p.yMax + Tolerance) return false;
            ring = image;
            return true;
        }

        /// <summary>Places or refreshes the ring copy; true when anything changed.</summary>
        private static bool Apply(RectTransform clip, Image ring)
        {
            var parent = (RectTransform)clip.parent;
            var changed = false;
            var edge = clip.Find(ChildName) as RectTransform;
            if (edge == null)
            {
                // Inside a component instance the copy comes from the component; none there means none here
                if (PrefabUtility.IsPartOfPrefabInstance(clip)) return false;
                edge = new GameObject(ChildName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
                edge.SetParent(clip, false);
                changed = true;
            }
            if (!edge.gameObject.activeSelf)
            {
                edge.gameObject.SetActive(true);
                changed = true;
            }

            // The parent's box in the clipping frame's space: stretched with it, out by how far it is inset
            Rect p = parent.rect, c = clip.rect;
            var offsetMin = p.min - ((Vector2)clip.localPosition + c.min);
            var offsetMax = p.max - ((Vector2)clip.localPosition + c.max);
            if (edge.anchorMin != Vector2.zero || edge.anchorMax != Vector2.one || edge.localScale != Vector3.one ||
                (edge.offsetMin - offsetMin).sqrMagnitude > 1e-6f || (edge.offsetMax - offsetMax).sqrMagnitude > 1e-6f)
            {
                edge.anchorMin = Vector2.zero;
                edge.anchorMax = Vector2.one;
                edge.pivot = new Vector2(0.5f, 0.5f);
                edge.localScale = Vector3.one;
                edge.offsetMin = offsetMin;
                edge.offsetMax = offsetMax;
                changed = true;
            }
            if (edge.GetSiblingIndex() != clip.childCount - 1)
            {
                edge.SetAsLastSibling();
                changed = true;
            }

            var image = edge.GetComponent<Image>();
            if (image.sprite != ring.sprite || image.type != ring.type || image.color != ring.color || image.material != ring.material ||
                !Mathf.Approximately(image.pixelsPerUnitMultiplier, ring.pixelsPerUnitMultiplier) || image.raycastTarget)
            {
                image.sprite = ring.sprite;
                image.type = ring.type;
                image.color = ring.color;
                image.material = ring.material;
                image.pixelsPerUnitMultiplier = ring.pixelsPerUnitMultiplier;
                image.raycastTarget = false;
                changed = true;
            }
            return changed;
        }

        private static bool IsHollow(Sprite sprite, Dictionary<Sprite, bool> cache)
        {
            if (cache.TryGetValue(sprite, out var hollow)) return hollow;
            hollow = false;
            var path = AssetDatabase.GetAssetPath(sprite.texture);
            if (File.Exists(path))
            {
                var texture = new Texture2D(2, 2);
                if (texture.LoadImage(File.ReadAllBytes(path)))
                {
                    var r = sprite.rect;
                    hollow = texture.GetPixel((int)(r.x + r.width / 2), (int)(r.y + r.height / 2)).a < 0.01f;
                }
                Object.DestroyImmediate(texture);
            }
            cache[sprite] = hollow;
            return hollow;
        }
    }
}
