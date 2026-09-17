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
        /// Figma text stroke weight (px) to TMP normalised outline units.
        /// </summary>
        private const float FigmaStrokeWeightToTmpOutline = 0.1f;

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
                    text.color = FigmaDataUtils.GetUnityFillColor(node.fills[0]);
                    text.fontSize = node.style.fontSize;
                    // Figma handles spacing a little differently; the default -0.7 matched it best
                    text.characterSpacing = settings != null ? settings.CharacterSpacing : -0.7f;

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

                    // We use material variants for TextMeshPro to apply text effects
                    var hasShadowEffect = false;
                    Effect shadowEffect=null;
                    foreach (var effect in node.effects)
                    {
                        if (effect.type == Effect.EffectType.DROP_SHADOW)
                        {
                            shadowEffect = effect;
                            hasShadowEffect = true;
                        }
                    }

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
                    }

                    // If no material variation, ignore
                    if (!hasShadowEffect && node.strokes.Length == 0) return;

                    var shadowColor = hasShadowEffect
                        ? FigmaDataUtils.ToUnityColor(shadowEffect.color) : UnityEngine.Color.white;
                    var outlineColor = node.strokes.Length > 0
                        ? FigmaDataUtils.GetUnityFillColor(node.strokes[0]) : UnityEngine.Color.white;
                    var outlineWidth = 0f;
                    if (node.strokes.Length > 0)
                    {
                        // A Figma stroke weight of x reads as x/10 in TMP's normalised outline
                        // units, whatever the font size. Deriving it from font size instead made
                        // one design stroke render at a different weight on every text size.
                        outlineWidth = node.strokeWeight * FigmaStrokeWeightToTmpOutline;
                        // _OutlineWidth and _FaceDilate are both 0..1 in the TMP SDF shader
                        outlineWidth = Mathf.Clamp01(outlineWidth);
                    }
                    var effectMaterialPreset = FontManager.GetEffectMaterialPreset(matchingFontMapping,
                        hasShadowEffect, shadowColor, node.strokes.Length>0, outlineColor, outlineWidth);
                    text.fontMaterial = effectMaterialPreset;



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

            var firstFill = node.fills != null && node.fills.Length > 0 ? node.fills[0] : null;
            Sprite sprite = null;
            var isPattern = false;
            if (firstFill != null && firstFill.type == Paint.PaintType.IMAGE && !string.IsNullOrEmpty(firstFill.imageRef))
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(FigmaPaths.GetPathForImageFill(firstFill.imageRef));
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

            image.enabled = firstFill == null || firstFill.visible;

            if (isPattern)
            {
                // Image.Tiled draws tiles of (sprite size / pixelsPerUnitMultiplier)
                image.type = Image.Type.Tiled;
                image.pixelsPerUnitMultiplier = PatternRenderToTileRatio(firstFill, figmaImportProcessData);
            }
            else if (sprite != null && sprite.border != Vector4.zero) image.type = Image.Type.Sliced;
            else if (sprite != null && firstFill.scaleMode == Paint.ScaleMode.TILE) image.type = Image.Type.Tiled;
            else image.type = Image.Type.Simple;
            image.preserveAspect = sprite != null && !isPattern && firstFill.scaleMode == Paint.ScaleMode.FIT;

            var hasStroke = node.strokes != null && node.strokes.Length > 0 && node.strokeWeight > 0;
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
        ///     <c>ServerRenderImageScale</c>x and Figma scales the tile by the fill's <c>scalingFactor</c>,
        ///     so a tile of the render must be shrunk by renderScale / scalingFactor to land at design size.
        /// </summary>
        private static float PatternRenderToTileRatio(Paint fill, FigmaImportProcessData figmaImportProcessData)
        {
            var settings = figmaImportProcessData.Settings;
            var renderScale = settings != null && settings.ServerRenderImageScale > 0 ? settings.ServerRenderImageScale : 1f;
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
