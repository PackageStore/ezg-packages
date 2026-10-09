using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Fonts;
using UnityFigmaBridge.Editor.PostProcess;
using UnityFigmaBridge.Editor.Settings;
using UnityFigmaBridge.Editor.Utils;
using UnityFigmaBridge.Runtime.UI;
using Color = UnityEngine.Color;

namespace UnityFigmaBridge.Editor.Nodes
{
    public static class FigmaNodeManager
    {
        /// <summary>
        /// In FixedRectAutoSize mode TMP may shrink a label down to this fraction of the design
        /// size before it falls back to an ellipsis.
        /// </summary>
        private const float AutoSizeMinRatio = 0.5f;

        /// <summary>
        /// Applies the Figma Node properties to a Unity Game object (components created in CreateUnityComponentsForNode)
        /// </summary>
        /// <param name="nodeGameObject"></param>
        /// <param name="node"></param>
        /// <param name="figmaImportProcessData"></param>
        public static void ApplyUnityComponentPropertiesForNode(GameObject nodeGameObject,Node node, FigmaImportProcessData figmaImportProcessData)
        {
            var settings = figmaImportProcessData.Settings;

            switch (node.type)
            {
                case NodeType.FRAME:
                case NodeType.RECTANGLE:
                case NodeType.ELLIPSE:
                case NodeType.STAR:
                case NodeType.COMPONENT:
                case NodeType.INSTANCE:
                case NodeType.SECTION:
                    var needsImageComponent = node.fills.Length > 0 || node.strokes.Length > 0;
                    if (NodeIsSubstitution(node, figmaImportProcessData)) break;
                    if (!needsImageComponent) break;

                    ApplyImage(nodeGameObject, node, figmaImportProcessData);
                    break;
                case NodeType.LINE:
                    break;
                case NodeType.DOCUMENT:
                    break;
                case NodeType.CANVAS:
                    break;
                case NodeType.GROUP:
                    break;
                case NodeType.VECTOR:
                    break;
                case NodeType.BOOLEAN_OPERATION:
                    break;
                case NodeType.REGULAR_POLYGON:
                    break;
                case NodeType.TEXT:
                    // Get the best fit TextMeshPro font this font (handled when document processed)
                    var text = nodeGameObject.GetComponent<TextMeshProUGUI>();
                    var matchingFontMapping = figmaImportProcessData.FontMap.GetFontMapping(node.style.fontFamily, node.style.fontWeight);
                    if (matchingFontMapping?.FontAsset == null)
                    {
                        Debug.LogError($"[FigmaNodeManager] No font asset resolved for '{node.style.fontFamily}' " +
                                       $"weight {node.style.fontWeight} on node '{node.name}'.");
                        return;
                    }
                    text.font = matchingFontMapping.FontAsset;

                    text.text = node.characters;
                    ApplyTextFill(text, TopVisiblePaint(node.fills), node, matchingFontMapping.FontAsset);
                    text.fontSize = node.style.fontSize;
                    text.characterSpacing = (settings != null ? settings.CharacterSpacing : 0f) +
                                            PixelsToEm(node.style.letterSpacing, node.style.fontSize);
                    ApplyLineMetrics(text, node, matchingFontMapping.FontAsset);

                    text.horizontalAlignment = node.style.textAlignHorizontal switch
                    {
                        TypeStyle.TextAlignHorizontal.LEFT => HorizontalAlignmentOptions.Left,
                        TypeStyle.TextAlignHorizontal.CENTER => HorizontalAlignmentOptions.Center,
                        TypeStyle.TextAlignHorizontal.JUSTIFIED => HorizontalAlignmentOptions.Justified,
                        TypeStyle.TextAlignHorizontal.RIGHT => HorizontalAlignmentOptions.Right,
                        _ => HorizontalAlignmentOptions.Left
                    };

                    text.verticalAlignment = node.style.textAlignVertical switch
                    {
                        TypeStyle.TextAlignVertical.TOP => VerticalAlignmentOptions.Top,
                        TypeStyle.TextAlignVertical.CENTER => VerticalAlignmentOptions.Middle,
                        TypeStyle.TextAlignVertical.BOTTOM => VerticalAlignmentOptions.Bottom,
                        _ => VerticalAlignmentOptions.Top,
                    };

                    // Add on styling attributes depending on text case
                    text.fontStyle |= node.style.textCase switch
                    {
                        TypeStyle.TextCase.LOWER => FontStyles.LowerCase,
                        TypeStyle.TextCase.UPPER => FontStyles.UpperCase,
                        TypeStyle.TextCase.SMALL_CAPS => FontStyles.SmallCaps,
                        _ => 0
                    };

                    // Add on styling attributes depending on text decoration
                    text.fontStyle |= node.style.textDecoration switch
                    {
                        TypeStyle.TextDecoration.UNDERLINE => FontStyles.Underline,
                        TypeStyle.TextDecoration.STRIKETHROUGH => FontStyles.Strikethrough,
                        _ => 0
                    };

                    // We only use TextMeshPro's italic functionality for now
                    if (node.style.italic) text.fontStyle |= FontStyles.Italic;

                    // Auto width never wraps in Figma
                    text.textWrappingMode = node.style.textAutoResize == TypeStyle.TextAutoResize.WIDTH_AND_HEIGHT
                        ? TextWrappingModes.NoWrap
                        : TextWrappingModes.Normal;

                    if (settings != null && settings.TextFitMode == TextFitMode.FixedRectAutoSize)
                    {
                        // The rect is fixed (padded by NodeTransformManager); the glyphs adapt to it
                        // instead of the rect adapting to the glyphs.
                        var existingFitter = nodeGameObject.GetComponent<ContentSizeFitter>();
                        if (existingFitter != null) UnityEngine.Object.DestroyImmediate(existingFitter);
                        text.enableAutoSizing = true;
                        text.fontSizeMax = node.style.fontSize;
                        text.fontSizeMin = node.style.fontSize * AutoSizeMinRatio;
                        text.overflowMode = TextOverflowModes.Ellipsis;
                    }
                    else if (node.style.textAutoResize != TypeStyle.TextAutoResize.NONE)
                    {
                        // Handle text auto resize
                        var contentSizeFitter = UnityUiUtils.GetOrAddComponent<ContentSizeFitter>(nodeGameObject);

                        switch (node.style.textAutoResize)
                        {
                            case TypeStyle.TextAutoResize.NONE:
                                contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                                contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
                                break;
                            case TypeStyle.TextAutoResize.HEIGHT:
                                contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                                contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                                break;
                            case TypeStyle.TextAutoResize.WIDTH_AND_HEIGHT:
                                contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                                contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                                break;
                            case TypeStyle.TextAutoResize.TRUNCATE:
                                contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                                contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
                                break;
                        }
                        // TMP leaves negative margins out of its preferred height, so a trimmed box keeps its Figma height
                        if (IsCapHeightTrimmed(node.style))
                            contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
                    }

                    // Stroke and drop shadow are drawn by a material preset
                    var textEffect = TextEffect.FromNode(node);
                    if (!textEffect.Any) return;
                    text.fontMaterial = FontManager.GetEffectMaterialPreset(matchingFontMapping, textEffect);
                    break;
                case NodeType.SLICE:
                    break;
                case NodeType.COMPONENT_SET:
                    break;
                case NodeType.STICKY:
                    // Sticky note - unused
                    break;
                case NodeType.SHAPE_WITH_TEXT:
                    break;
                case NodeType.CONNECTOR:
                    break;
            }

            // Setup opacity - this is done by applying a CanvasGroup
            // Only apply if the opacity is less than 1, or of there is a CanvasGroup already
            if (node.opacity < 1 || nodeGameObject.GetComponent<CanvasGroup>()!=null)
            {
                var canvasGroup = nodeGameObject.GetComponent<CanvasGroup>();
                if (canvasGroup == null) canvasGroup = nodeGameObject.AddComponent<CanvasGroup>();
                canvasGroup.alpha = node.opacity;
            }
            // Setup visibility
            nodeGameObject.SetActive(node.visible);
        }

        /// <summary>
        ///     Sprite, colour and visibility carry over to a stock <see cref="Image"/>. Stroke, corner
        ///     radius, gradient and the ellipse/star shapes have no plain-Image equivalent: a childless
        ///     node with one of them never reaches here (it is server-rendered, see
        ///     <see cref="FigmaDataUtils.NeedsShapeRender"/>); a node with children gets a flat Image
        ///     so the layout is visible and is listed in
        ///     <see cref="FigmaImportProcessData.ShapeOnlyNodes"/> for the post-processor.
        /// </summary>
        private static void ApplyImage(GameObject nodeGameObject, Node node, FigmaImportProcessData figmaImportProcessData)
        {
            var image = nodeGameObject.GetComponent<Image>();
            if (image == null) image = nodeGameObject.AddComponent<Image>();

            if (FrameShapeSprite.IsNeeded(node))
            {
                var shapeSprite = FrameShapeSprite.Build(node, figmaImportProcessData.Settings);
                if (shapeSprite != null)
                {
                    image.sprite = shapeSprite;
                    image.color = SolidTint.TryGetShapeTint(node, out var tint) ? tint : Color.white;
                    image.type = shapeSprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
                    image.pixelsPerUnitMultiplier = 1f;
                    image.preserveAspect = false;
                    image.enabled = true;
                    return;
                }
            }

            // Figma paints fills bottom to top: the last visible one is what a flat colour shows
            var firstFill = TopVisiblePaint(node.fills);
            var hasHiddenFillsOnly = firstFill == null && node.fills != null && node.fills.Length > 0;
            Sprite sprite = null;
            var isPattern = false;
            if (firstFill != null && firstFill.type == Paint.PaintType.IMAGE && !string.IsNullOrEmpty(firstFill.imageRef))
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    ImageFillCover.SpritePathFor(FigmaPaths.GetPathForImageFill(firstFill.imageRef), firstFill, node));
            else if (firstFill != null && firstFill.type == Paint.PaintType.PATTERN)
            {
                sprite = LoadPatternSourceSprite(firstFill, figmaImportProcessData);
                isPattern = sprite != null;
            }

            image.sprite = sprite;

            if (firstFill == null)
                image.color = new Color(1f, 1f, 1f, 0f); // stroke only: nothing a flat Image can draw
            else if (FigmaDataUtils.IsGradient(firstFill) && firstFill.gradientStops != null && firstFill.gradientStops.Length > 0)
                image.color = AverageGradientColor(firstFill);
            else
                image.color = FigmaDataUtils.GetUnityFillColor(firstFill);

            image.enabled = !hasHiddenFillsOnly;

            if (isPattern)
            {
                // Image.Tiled draws tiles of (sprite size / pixelsPerUnitMultiplier)
                image.type = Image.Type.Tiled;
                image.pixelsPerUnitMultiplier = PatternRenderToTileRatio(firstFill, figmaImportProcessData);
            }
            // Only the 9-slice pass slices a fill: a border already on its importer can be left from an
            // earlier import, or belong to a slice grid elsewhere that shares the fill
            else if (sprite != null && firstFill.scaleMode == Paint.ScaleMode.TILE) image.type = Image.Type.Tiled;
            else image.type = Image.Type.Simple;
            image.preserveAspect = sprite != null && !isPattern && firstFill.scaleMode == Paint.ScaleMode.FIT;

            var hasStroke = node.strokes != null && node.strokeWeight > 0 &&
                            System.Array.Exists(node.strokes, s => s != null && !FigmaDataUtils.IsShaderPaint(s));
            var cornerRadius = FigmaDataUtils.MaxCornerRadius(node);
            var isGradient = firstFill != null && FigmaDataUtils.IsGradient(firstFill);
            var isShape = node.type == NodeType.ELLIPSE || node.type == NodeType.STAR;
            if (!hasStroke && cornerRadius <= 0f && !isGradient && !isShape) return;

            var record = new ShapeOnlyNode
            {
                PrefabPath = figmaImportProcessData.CurrentPrefabPath ?? string.Empty,
                HierarchyPath = PostProcessorRunner.HierarchyPathWithinPrefab(nodeGameObject.transform),
                NodeId = node.id,
                NodeName = node.name,
                HasSprite = sprite != null,
                StrokeWidth = hasStroke ? node.strokeWeight : 0f,
                CornerRadius = cornerRadius,
                FillType = firstFill?.type.ToString() ?? "NONE",
                Shape = node.type.ToString()
            };
            figmaImportProcessData.ShapeOnlyNodes.Add(record);
            if (string.IsNullOrEmpty(record.PrefabPath))
                figmaImportProcessData.ShapeOnlyPendingRoots.Add((record, HierarchyRootWithinPrefab(nodeGameObject.transform)));
        }

        private static Paint TopVisiblePaint(Paint[] paints)
        {
            if (paints == null) return null;
            for (var i = paints.Length - 1; i >= 0; i--)
                if (paints[i] != null && paints[i].visible && !FigmaDataUtils.IsShaderPaint(paints[i])) return paints[i];
            return null;
        }

        private static float PixelsToEm(float pixels, float fontSize)
        {
            return fontSize > 0f ? pixels / fontSize * 100f : 0f;
        }

        /// <summary>
        ///     TMP has no text-wide gradient, only four corner colours per glyph. A gradient's vertical
        ///     change is sampled at the first line's cap line and baseline, in the box's middle column, so
        ///     a single line reads as in Figma; a change across the line is lost.
        /// </summary>
        private static void ApplyTextFill(TMP_Text text, Paint fill, Node node, TMP_FontAsset fontAsset)
        {
            text.enableVertexGradient = false;
            if (fill == null)
            {
                text.color = new Color(1f, 1f, 1f, 0f);
                return;
            }
            if (!FigmaDataUtils.IsGradient(fill) || fill.gradientStops == null || fill.gradientStops.Length == 0)
            {
                text.color = FigmaDataUtils.GetUnityFillColor(fill);
                return;
            }

            var top = FrameShapeSprite.SampleGradient(fill, FrameShapeSprite.GradientPosition(fill, new Vector2(0.5f, 0f)));
            var bottom = FrameShapeSprite.SampleGradient(fill, FrameShapeSprite.GradientPosition(fill, new Vector2(0.5f, 1f)));
            var glyphSpan = GlyphSpanInBox(node, fontAsset);
            text.color = new Color(1f, 1f, 1f, fill.opacity);
            text.enableVertexGradient = true;
            text.colorGradient = new VertexGradient(
                Color.Lerp(top, bottom, glyphSpan.x), Color.Lerp(top, bottom, glyphSpan.x),
                Color.Lerp(top, bottom, glyphSpan.y), Color.Lerp(top, bottom, glyphSpan.y));
        }

        /// <summary>
        ///     Cap line and baseline of the first line as fractions of the box height: where a capital's
        ///     quad starts and ends, so the corner colours match the gradient at the glyph, not at the box.
        /// </summary>
        private static Vector2 GlyphSpanInBox(Node node, TMP_FontAsset fontAsset)
        {
            var boxHeight = node.size != null ? node.size.y : 0f;
            var face = fontAsset.faceInfo;
            if (boxHeight <= 0f || face.pointSize <= 0f) return new Vector2(0f, 1f);
            var scale = node.style.fontSize / face.pointSize;
            var capTop = FirstLineAscentInBox(node, fontAsset) - face.capLine * scale;
            var baseline = FirstLineAscentInBox(node, fontAsset);
            return new Vector2(Mathf.Clamp01(capTop / boxHeight), Mathf.Clamp01(baseline / boxHeight));
        }

        /// <summary>Distance from the box top to the first baseline, as Figma lays the line out.</summary>
        private static float FirstLineAscentInBox(Node node, TMP_FontAsset fontAsset)
        {
            var face = fontAsset.faceInfo;
            var scale = node.style.fontSize / face.pointSize;
            if (IsCapHeightTrimmed(node.style)) return face.capLine * scale;
            var ascent = face.ascentLine * scale;
            var descent = -face.descentLine * scale;
            var lineHeight = node.style.lineHeightPx > 0f ? node.style.lineHeightPx : ascent + descent;
            return (lineHeight - (ascent + descent)) * 0.5f + ascent;
        }

        private static bool IsCapHeightTrimmed(TypeStyle style)
        {
            return string.Equals(style.leadingTrim, "CAP_HEIGHT", StringComparison.Ordinal);
        }

        /// <summary>
        ///     Figma spaces lines by <c>lineHeightPx</c> and centres the font's ascent and descent in each
        ///     line, so half the leading sits above the first line and below the last. TMP stacks lines by
        ///     the font's own line height from the first ascender to the last descender. Line spacing makes
        ///     up the difference between lines, and the vertical margins the half leading at both ends.
        ///     A cap-height leading trim cuts the box at the first cap line and the last baseline instead.
        /// </summary>
        private static void ApplyLineMetrics(TMP_Text text, Node node, TMP_FontAsset fontAsset)
        {
            var style = node.style;
            var face = fontAsset.faceInfo;
            if (style.fontSize <= 0f || face.pointSize <= 0f) return;
            var scale = style.fontSize / face.pointSize;
            var ascent = face.ascentLine * scale;
            var descent = -face.descentLine * scale;

            if (style.lineHeightPx > 0f)
                text.lineSpacing = PixelsToEm(style.lineHeightPx - face.lineHeight * scale, style.fontSize);

            float marginTop, marginBottom;
            if (IsCapHeightTrimmed(style))
            {
                marginTop = face.capLine * scale - ascent;
                marginBottom = -descent;
            }
            else
            {
                var halfLeading = style.lineHeightPx > 0f ? (style.lineHeightPx - (ascent + descent)) * 0.5f : 0f;
                marginTop = halfLeading;
                marginBottom = halfLeading;
            }
            var margin = text.margin;
            text.margin = new Vector4(margin.x, marginTop, margin.z, marginBottom);
        }

        private static Color AverageGradientColor(Paint paint)
        {
            var sum = Vector4.zero;
            foreach (var stop in paint.gradientStops)
                sum += new Vector4(stop.color.r, stop.color.g, stop.color.b, stop.color.a);
            sum /= paint.gradientStops.Length;
            return new Color(sum.x, sum.y, sum.z, sum.w * paint.opacity);
        }

        /// <summary>Topmost ancestor that still carries a bridge marker: the screen or component root.</summary>
        private static GameObject HierarchyRootWithinPrefab(Transform transform)
        {
            var current = transform;
            while (current.parent != null && current.parent.GetComponent<FigmaNodeObject>() != null)
                current = current.parent;
            return current.gameObject;
        }

        /// <summary>
        ///     Sprite for a PATTERN fill: the server render of the node it repeats (queued by
        ///     <see cref="FigmaDataUtils.FindAllServerRenderNodesInFile"/>). Null when that render is not on
        ///     disk (offline re-import, or the source lives outside the document); the node then imports as
        ///     a flat Image, like any fill whose bitmap is missing.
        /// </summary>
        private static Sprite LoadPatternSourceSprite(Paint fill, FigmaImportProcessData figmaImportProcessData)
        {
            if (string.IsNullOrEmpty(fill.sourceNodeId)) return null;
            var renderNodes = figmaImportProcessData.ServerRenderNodes;
            if (renderNodes.Find(entry => entry.SourceNode.id == fill.sourceNodeId) == null) return null;

            var path = FigmaPaths.GetPathForServerRenderedImage(fill.sourceNodeId, renderNodes);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                Debug.LogWarning($"[FigmaBridge] PATTERN fill: render '{path}' of source node '{fill.sourceNodeId}' is not on disk - the fill imports without a sprite until an online Sync downloads it.");
            else if (!string.IsNullOrEmpty(fill.tileType) && fill.tileType != "RECTANGULAR")
                Debug.LogWarning($"[FigmaBridge] PATTERN fill with tileType {fill.tileType} is drawn as a rectangular tile grid (UGUI has no hexagonal tiling).");
            return sprite;
        }

        /// <summary>
        ///     Rendered pixels per Figma unit of one tile. The source is rendered at
        ///     <see cref="FigmaImportProcessData.ServerRenderScale"/>x and Figma scales the tile by the fill's <c>scalingFactor</c>,
        ///     so a tile of the render must be shrunk by renderScale / scalingFactor to land at design size.
        /// </summary>
        private static float PatternRenderToTileRatio(Paint fill, FigmaImportProcessData figmaImportProcessData)
        {
            var renderScale = figmaImportProcessData.ServerRenderScale > 0 ? figmaImportProcessData.ServerRenderScale : 1f;
            var scalingFactor = fill.scalingFactor > 0f ? fill.scalingFactor : 1f;
            return renderScale / scalingFactor;
        }

        /// <summary>
        /// Create all required components for figma node
        /// </summary>
        /// <param name="nodeGameObject"></param>
        /// <param name="node"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static void CreateUnityComponentsForNode(GameObject nodeGameObject,Node node,FigmaImportProcessData figmaImportProcessData)
        {
            // Background fills
            switch (node.type)
            {
                case NodeType.FRAME:
                case NodeType.RECTANGLE:
                case NodeType.ELLIPSE:
                case NodeType.STAR:
                case NodeType.COMPONENT:
                case NodeType.INSTANCE:
                    if (NodeIsSubstitution(node, figmaImportProcessData)) return;
                    // No longer need to add here - will be generated above as needed
                    break;
                case NodeType.TEXT:
                    // For text nodes, we use TextMeshPro
                    nodeGameObject.AddComponent<TextMeshProUGUI>();
                    break;
                case NodeType.DOCUMENT:
                    break;
                case NodeType.CANVAS:
                    break;
                case NodeType.GROUP:
                    break;
                case NodeType.VECTOR:
                    break;
                case NodeType.BOOLEAN_OPERATION:
                    break;
                case NodeType.LINE:
                    break;
                case NodeType.REGULAR_POLYGON:
                    break;
                case NodeType.SLICE:
                    break;
                case NodeType.COMPONENT_SET:
                    break;
                case NodeType.STICKY:
                    break;
                case NodeType.SHAPE_WITH_TEXT:
                    break;
                case NodeType.CONNECTOR:
                    break;
                case NodeType.SECTION:
                    break;
                case NodeType.TABLE:
                case NodeType.TABLE_CELL:
                case NodeType.WASHI_TAPE:
                default:
                    // Unimplemented type
                    break;
            }
        }

        public static bool NodeIsSubstitution(Node node, FigmaImportProcessData figmaImportProcessData)
        {
            switch (node.type)
            {
                // If this is an instance and the component is a server render node
                case NodeType.INSTANCE when
                    figmaImportProcessData.ServerRenderNodes.Find(serverRenderNode=>serverRenderNode.SourceNode.id==node.componentId && serverRenderNode.RenderType== ServerRenderType.Substitution)!=null:
                // This is is a component and is a server render node
                case NodeType.COMPONENT when
                    figmaImportProcessData.ServerRenderNodes.Find(serverRenderNode=>serverRenderNode.SourceNode.id==node.id && serverRenderNode.RenderType== ServerRenderType.Substitution)!=null:
                    return true;
                default:
                    return false;
            }
        }
    }
}
