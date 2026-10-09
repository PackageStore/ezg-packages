using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;
using Color = UnityEngine.Color;

namespace UnityFigmaBridge.Editor.Fonts
{
    /// <summary>A text node's stroke and drop shadow in design pixels, as TextMeshPro has to draw them.</summary>
    public struct TextEffect
    {
        public float FontSize;

        public bool Outline;
        public Color OutlineColor;
        public float StrokeWidth;
        public Node.StrokeAlign StrokeAlign;

        public bool Shadow;
        public Color ShadowColor;
        /// <summary>Figma offset: x right, y down.</summary>
        public Vector2 ShadowOffset;
        public float ShadowRadius;
        public float ShadowSpread;

        public bool Any => Outline || Shadow;

        /// <summary>How far the stroke reaches outside the glyph.</summary>
        public float StrokeOuterEdge => !Outline ? 0f : StrokeAlign switch
        {
            Node.StrokeAlign.OUTSIDE => StrokeWidth,
            Node.StrokeAlign.CENTER => StrokeWidth * 0.5f,
            _ => 0f
        };

        /// <summary>
        ///     Farthest distance from a glyph edge the effect reads the distance field at. The atlas
        ///     padding must cover it, or the shadow picks up the neighbouring glyph in the atlas.
        /// </summary>
        public float Reach => Mathf.Max(StrokeOuterEdge,
            Shadow ? StrokeOuterEdge + ShadowSpread + ShadowRadius + Mathf.Max(Mathf.Abs(ShadowOffset.x), Mathf.Abs(ShadowOffset.y)) : 0f);

        public static TextEffect FromNode(Node node)
        {
            var effect = new TextEffect { FontSize = node.style?.fontSize ?? 0f };

            Paint stroke = null;
            if (node.strokeWeight > 0f && node.strokes != null)
                for (var i = node.strokes.Length - 1; i >= 0 && stroke == null; i--)
                    if (node.strokes[i] != null && node.strokes[i].visible && !FigmaDataUtils.IsShaderPaint(node.strokes[i]))
                        stroke = node.strokes[i];
            if (stroke != null)
            {
                effect.Outline = true;
                effect.OutlineColor = FigmaDataUtils.GetUnityFillColor(stroke);
                effect.StrokeWidth = node.strokeWeight;
                effect.StrokeAlign = node.strokeAlign;
            }

            if (node.effects == null) return effect;
            foreach (var shadow in node.effects)
            {
                if (!shadow.visible || shadow.type != Effect.EffectType.DROP_SHADOW) continue;
                effect.Shadow = true;
                effect.ShadowColor = FigmaDataUtils.ToUnityColor(shadow.color);
                effect.ShadowOffset = shadow.offset == null ? Vector2.zero : new Vector2(shadow.offset.x, shadow.offset.y);
                effect.ShadowRadius = shadow.radius;
                effect.ShadowSpread = shadow.spread;
            }
            return effect;
        }
    }
}
