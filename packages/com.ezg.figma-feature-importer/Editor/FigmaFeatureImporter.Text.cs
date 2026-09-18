#if UNITY_EDITOR
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ezg.FigmaFeatureImporter.Editor
{
    /// <summary>Bước 7 (§5.2): font khoá TiltWarp2, rect chữ cố định có lề, autosize + Ellipsis.</summary>
    public static partial class FigmaFeatureImporter
    {
        /// <summary>Chữ được co tối đa tới nửa cỡ thiết kế trước khi cắt bằng dấu ba chấm.</summary>
        private const float TEXT_AUTO_SIZE_MIN_RATIO = 0.5f;

        private static readonly Regex TextConventionName = new(@"^(Text|Value)_", RegexOptions.Compiled);

        private static void FixTexts(Ctx ctx)
        {
            var font = ctx.Settings.font;
            foreach (var text in ctx.Body.GetComponentsInChildren<TMP_Text>(true))
            {
                if (IsInsideTemplate(ctx, text.transform)) continue;

                // Luật [FONT]: một font duy nhất. Đổi font là mất material preset (viền) của bridge —
                // với FontOverride của bridge 0.3.0 hai bên đã trùng nên không mất gì.
                if (font != null && text.font != font) text.font = font;

                // Bridge 0.2.x: rect bám chữ qua ContentSizeFitter → đổi sang rect cố định có lề (B4).
                var fitter = text.GetComponent<ContentSizeFitter>();
                if (fitter != null)
                {
                    PadTextRect(text, fitter, ctx.Settings.textWidthPadding, ctx.Settings.textHeightPadding);
                    DestroyNow(fitter);
                }

                if (!text.enableAutoSizing)
                {
                    var designSize = text.fontSize;
                    text.enableAutoSizing = true;
                    text.fontSizeMax = designSize;
                    text.fontSizeMin = designSize * TEXT_AUTO_SIZE_MIN_RATIO;
                }
                text.overflowMode = TextOverflowModes.Ellipsis;

                if (!TextConventionName.IsMatch(text.name)) ctx.Report.unnamedTexts.Add(text.name);
            }
        }

        /// <summary>
        ///     Nới rect theo hệ số, giữ mép theo alignment, tính trong không gian cha bằng
        ///     anchoredPosition/sizeDelta (world corners trong prefab contents vô nghĩa). Trục đang
        ///     stretch theo constraint thì để nguyên.
        /// </summary>
        private static void PadTextRect(TMP_Text text, ContentSizeFitter fitter, float widthFactor, float heightFactor)
        {
            var rt = text.rectTransform;
            var stretchX = !Mathf.Approximately(rt.anchorMin.x, rt.anchorMax.x);
            var stretchY = !Mathf.Approximately(rt.anchorMin.y, rt.anchorMax.y);
            var padWidth = fitter.horizontalFit == ContentSizeFitter.FitMode.PreferredSize && !stretchX;
            var padHeight = fitter.verticalFit == ContentSizeFitter.FitMode.PreferredSize && !stretchY;

            var size = rt.sizeDelta;
            var deltaWidth = padWidth ? size.x * (Mathf.Max(1f, widthFactor) - 1f) : 0f;
            var deltaHeight = padHeight ? size.y * (Mathf.Max(1f, heightFactor) - 1f) : 0f;
            if (deltaWidth <= 0f && deltaHeight <= 0f) return;

            rt.sizeDelta = new Vector2(size.x + deltaWidth, size.y + deltaHeight);

            // Mép giữ nguyên theo alignment; pivot bất kỳ nên dịch điểm pivot bù phần rect nở ra.
            var leftShift = text.horizontalAlignment switch
            {
                HorizontalAlignmentOptions.Center => -deltaWidth * 0.5f,
                HorizontalAlignmentOptions.Right => -deltaWidth,
                _ => 0f
            };
            var topShift = text.verticalAlignment switch
            {
                VerticalAlignmentOptions.Middle => deltaHeight * 0.5f,
                VerticalAlignmentOptions.Bottom => deltaHeight,
                _ => 0f
            };

            var pivot = rt.pivot;
            var position = rt.anchoredPosition;
            position.x += leftShift + deltaWidth * pivot.x;
            position.y += topShift - deltaHeight * (1f - pivot.y);
            rt.anchoredPosition = position;
        }
    }
}
#endif
