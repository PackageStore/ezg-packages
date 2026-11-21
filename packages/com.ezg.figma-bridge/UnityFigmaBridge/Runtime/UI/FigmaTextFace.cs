using TMPro;
using UnityEngine;

namespace UnityFigmaBridge.Runtime.UI
{
    /// <summary>
    ///     The face of a stroked text, drawn above the whole stroked line. TextMeshPro draws each glyph's
    ///     outline with its face in one quad, so the outline of a glyph covers the face of the glyph
    ///     before it wherever they overlap - a thick stroke turns "COMMON" into "CCMMCN". Figma draws
    ///     the stroke of the whole line under every face. The text node's own component keeps the
    ///     stroke (and shadow) and stays the one game code reads and writes; this child copies its
    ///     text and layout every frame they change and draws only the face, so every face sits above
    ///     every stroke.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class FigmaTextFace : MonoBehaviour
    {
        public TMP_Text source;

        private TMP_Text m_Face;

        private void OnEnable() => Sync();

        private void LateUpdate() => Sync();

        /// <summary>Copies what decides the glyph layout and the face colour from the stroked text.</summary>
        public void Sync()
        {
            if (source == null) return;
            if (m_Face == null) m_Face = GetComponent<TMP_Text>();
            var face = m_Face;
            face.enabled = source.enabled;
            if (face.font != source.font) face.font = source.font;
            if (!ReferenceEquals(face.text, source.text)) face.text = source.text;
            face.color = source.color;
            face.enableVertexGradient = source.enableVertexGradient;
            if (source.enableVertexGradient) face.colorGradient = source.colorGradient;
            face.richText = source.richText;
            face.fontStyle = source.fontStyle;
            face.enableAutoSizing = source.enableAutoSizing;
            face.fontSizeMin = source.fontSizeMin;
            face.fontSizeMax = source.fontSizeMax;
            face.fontSize = source.fontSize;
            face.alignment = source.alignment;
            face.textWrappingMode = source.textWrappingMode;
            face.overflowMode = source.overflowMode;
            face.characterSpacing = source.characterSpacing;
            face.wordSpacing = source.wordSpacing;
            face.lineSpacing = source.lineSpacing;
            face.paragraphSpacing = source.paragraphSpacing;
            face.margin = source.margin;
            face.maxVisibleCharacters = source.maxVisibleCharacters;
            face.maxVisibleLines = source.maxVisibleLines;
        }
    }
}
