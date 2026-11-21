using TMPro;
using UnityEngine;
using UnityFigmaBridge.Editor.Fonts;
using UnityFigmaBridge.Runtime.UI;

namespace UnityFigmaBridge.Editor.Nodes
{
    /// <summary>
    ///     A stroked text gets a child that draws its face above the whole stroked line
    ///     (<see cref="FigmaTextFace"/>): the node's own component keeps the stroke and shadow, the
    ///     child uses the same effect with a transparent outline and no shadow, so it shows exactly
    ///     the face Figma shows (eroded by the inside part of the stroke) and no glyph's stroke covers
    ///     another glyph's face.
    /// </summary>
    public static class TextFaceLayer
    {
        public const string ChildName = "[Face]";

        public static void Apply(TMP_Text text, FigmaFontMapEntry fontMapping, TextEffect effect)
        {
            var existing = text.transform.Find(ChildName);
            if (!effect.Outline)
            {
                if (existing != null) Object.DestroyImmediate(existing.gameObject);
                return;
            }

            var faceEffect = effect;
            faceEffect.Shadow = false;
            faceEffect.OutlineColor = new Color(effect.OutlineColor.r, effect.OutlineColor.g, effect.OutlineColor.b, 0f);

            var faceObject = existing != null ? existing.gameObject : new GameObject(ChildName, typeof(RectTransform));
            faceObject.layer = text.gameObject.layer;
            var rect = (RectTransform)faceObject.transform;
            rect.SetParent(text.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = ((RectTransform)text.transform).pivot;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            var face = faceObject.GetComponent<TextMeshProUGUI>() ?? faceObject.AddComponent<TextMeshProUGUI>();
            face.raycastTarget = false;
            face.font = text.font;
            face.fontSharedMaterial = FontManager.GetEffectMaterialPreset(fontMapping, faceEffect);
            var sync = faceObject.GetComponent<FigmaTextFace>() ?? faceObject.AddComponent<FigmaTextFace>();
            sync.source = text;
            sync.Sync();
        }
    }
}
