using System;
using Ezg.BugLogger.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Ezg.BugLogger.Editor
{
    /// <summary>
    ///     Dựng prefab overlay UI của Bug Logger từ CHỈ asset nằm trong package (sprite + font dưới
    ///     <c>Runtime/UI</c>), không chạm template/sprite của project nào ⇒ UI giống hệt nhau ở mọi project.
    ///     Prefab là output của builder: muốn đổi bố cục/kích thước thì sửa ở đây rồi chạy lại menu, đừng sửa tay
    ///     prefab (lần dựng sau sẽ ghi đè).
    /// </summary>
    internal static class BugLoggerUIPrefabBuilder
    {
        #region Fields

        private const string UI_ROOT = "Packages/com.ezg.buglogger/Runtime/UI/";
        public const string PREFAB_PATH = UI_ROOT + "Resources/EzgBugLogger/BugLoggerOverlay.prefab";
        private const string SPRITE_DIR = UI_ROOT + "Sprites/";
        private const string FONT_DIR = UI_ROOT + "Fonts/";

        private const int UI_LAYER = 5;
        private const int SORTING_ORDER = short.MaxValue;

        // Bán kính (px) của sprite 9-slice — chia cho bán kính mong muốn ra pixelsPerUnitMultiplier.
        private const float PILL_SPRITE_RADIUS = 64f;
        private const float BOX_SPRITE_RADIUS = 24f;

        private const float LAUNCHER_SIZE = 128f;
        private const float HEADER_HEIGHT = 156f;
        private const float FOOTER_HEIGHT = 206f;
        private const float CONTENT_PADDING = 40f;
        private const float CHIP_HEIGHT = 72f;
        private const float FIELD_HEIGHT = 108f;
        private const float CONTENT_FIELD_HEIGHT = 320f;
        private const float STEPPER_HEIGHT = 76f;
        private const float STEPPER_BUTTON = 60f;
        private const float THUMB_HEIGHT = 300f;
        private const float TOAST_HEIGHT = 88f;
        private const float MULTILINE_SPACING = 1.12f;

        private static Font _regular;
        private static Font _medium;
        private static Font _semiBold;

        #endregion

        #region Menu

        [MenuItem("Tools/EZG/Bug Logger/Rebuild UI Prefab")]
        public static void Build()
        {
            _regular = LoadFont("BeVietnamPro-Regular");
            _medium = LoadFont("BeVietnamPro-Medium");
            _semiBold = LoadFont("BeVietnamPro-SemiBold");

            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = Node("BugLoggerOverlay", null);
                SceneManager.MoveGameObjectToScene(root.gameObject, scene);
                BuildOverlay(root);

                PrefabUtility.SaveAsPrefabAsset(root.gameObject, PREFAB_PATH, out var success);
                if (!success) throw new InvalidOperationException("Save prefab thất bại: " + PREFAB_PATH);

                Debug.Log("[BugLogger] Đã dựng lại " + PREFAB_PATH);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        #endregion

        #region Overlay

        private static void BuildOverlay(RectTransform root)
        {
            var canvas = root.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SORTING_ORDER;
            canvas.pixelPerfect = false;

            var scaler = root.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.referencePixelsPerUnit = 100f;

            root.gameObject.AddComponent<GraphicRaycaster>();
            root.gameObject.AddComponent<BugTweener>();
            var overlay = root.gameObject.AddComponent<BugLoggerOverlay>();

            var launcher = BuildLauncher(SafeLayer("LauncherLayer", root));
            var viewer = BuildViewer(root, out var viewerRoot);
            var form = BuildForm(root, viewer);
            viewerRoot.SetAsLastSibling();
            var toast = BuildToast(SafeLayer("ToastLayer", root));

            Wire(overlay, ("_canvas", canvas), ("_scaler", scaler), ("_launcher", launcher), ("_form", form),
                ("_toast", toast), ("_viewer", viewer));
        }

        private static RectTransform SafeLayer(string name, RectTransform parent)
        {
            var layer = Node(name, parent);
            Stretch(layer);
            layer.gameObject.AddComponent<BugLoggerSafeArea>();
            return layer;
        }

        #endregion

        #region Launcher

        private static BugLoggerLauncher BuildLauncher(RectTransform parent)
        {
            var launcher = Node("Launcher", parent);
            Place(launcher, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                Vector2.one * LAUNCHER_SIZE);
            var group = launcher.gameObject.AddComponent<CanvasGroup>();

            var visual = Node("Visual", launcher);
            Stretch(visual);

            var shadow = Node("Shadow", visual);
            Place(shadow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -8f),
                Vector2.one * LAUNCHER_SIZE * 1.5f);
            AddImage(shadow, "bl_circle_shadow", BugLoggerPalette.WithAlpha(Color.black, 0.6f));

            var background = Node("Bg", visual);
            Stretch(background);
            AddImage(background, "bl_circle", BugLoggerPalette.WithAlpha(BugLoggerPalette.Card, 0.96f), true);

            var ring = Node("Ring", visual);
            Stretch(ring);
            AddImage(ring, "bl_circle_stroke", BugLoggerPalette.WithAlpha(BugLoggerPalette.Accent, 0.9f));

            var icon = Node("Icon", visual);
            Place(icon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * 64f);
            AddImage(icon, "ic_bug", BugLoggerPalette.Accent);

            var badge = Node("Badge", visual);
            Place(badge, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(-16f, -16f), new Vector2(46f, 46f));
            var badgeOutline = Node("Outline", badge);
            Stretch(badgeOutline, -5f, -5f, -5f, -5f);
            AddImage(badgeOutline, "bl_pill", BugLoggerPalette.Card, false, PILL_SPRITE_RADIUS / 28f);
            Ignore(badgeOutline);
            var badgeFill = Node("Fill", badge);
            Stretch(badgeFill);
            AddImage(badgeFill, "bl_pill", BugLoggerPalette.Accent, false, PILL_SPRITE_RADIUS / 23f);
            Ignore(badgeFill);
            Row(badge, new RectOffset(13, 13, 0, 0), 0f, TextAnchor.MiddleCenter);
            Layout(badge, -1f, 46f, -1f, -1f, 46f);
            var fitter = badge.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var count = Node("Count", badge);
            var countText = AddText(count, "1", _semiBold, 24, BugLoggerPalette.OnAccent, TextAnchor.MiddleCenter);
            countText.horizontalOverflow = HorizontalWrapMode.Overflow;

            var component = launcher.gameObject.AddComponent<BugLoggerLauncher>();
            Wire(component, ("_group", group), ("_visual", visual), ("_badge", badge.gameObject),
                ("_badgeRect", badge), ("_badgeText", countText));
            return component;
        }

        #endregion

        #region Form

        private static BugLoggerForm BuildForm(RectTransform root, BugLoggerImageViewer viewer)
        {
            var formRoot = Node("Form", root);
            Stretch(formRoot);
            var formGroup = formRoot.gameObject.AddComponent<CanvasGroup>();

            var scrim = Node("Scrim", formRoot);
            Stretch(scrim);
            var scrimImage = AddImage(scrim, null, BugLoggerPalette.Scrim, true);

            var frame = Node("CardFrame", formRoot);
            Stretch(frame);
            frame.gameObject.AddComponent<BugLoggerSafeArea>();

            var card = Node("Card", frame);
            Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1024f, 1800f));
            var cardGroup = card.gameObject.AddComponent<CanvasGroup>();

            var shadow = Node("Shadow", card);
            Stretch(shadow, -72f, -88f, -72f, -56f);
            AddImage(shadow, "bl_card_shadow", BugLoggerPalette.Shadow);

            var background = Node("Bg", card);
            Stretch(background);
            AddImage(background, "bl_card", BugLoggerPalette.Card, true);

            var stroke = Node("Stroke", card);
            Stretch(stroke);
            AddImage(stroke, "bl_card_stroke", BugLoggerPalette.WithAlpha(BugLoggerPalette.StrokeStrong, 0.7f));

            // --- Đầu form
            var header = Node("Header", card);
            Place(header, new Vector2(0f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, HEADER_HEIGHT));
            header.anchorMax = Vector2.one;
            Row(header, new RectOffset(40, 32, 20, 16), 24f, TextAnchor.MiddleLeft);

            var badge = Node("Badge", header);
            Layout(badge, 88f, 88f);
            AddImage(badge, "bl_circle", BugLoggerPalette.AccentSoft);
            var badgeIcon = Node("Icon", badge);
            Place(badgeIcon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * 52f);
            AddImage(badgeIcon, "ic_bug", BugLoggerPalette.Accent);

            var titles = Node("Titles", header);
            Layout(titles, -1f, -1f, 1f);
            Column(titles, new RectOffset(), 2f, TextAnchor.MiddleLeft);
            var title = Node("Title", titles);
            AddText(title, BugLoggerStrings.HEADER_TITLE, _semiBold, 44, BugLoggerPalette.TextPrimary,
                TextAnchor.MiddleLeft);
            var subtitleNode = Node("Subtitle", titles);
            var subtitle = AddText(subtitleNode, "Game  ·  v1.0  ·  Scene", _regular, 24,
                BugLoggerPalette.TextSecondary, TextAnchor.MiddleLeft);
            subtitle.verticalOverflow = VerticalWrapMode.Truncate;
            Layout(subtitleNode, -1f, 36f);

            var close = CircleButton(header, "Close", 84f, BugLoggerPalette.Surface, "ic_close", 32f,
                BugLoggerPalette.TextSecondary);
            Layout((RectTransform)close.transform, 84f, 84f);

            var headerDivider = Node("HeaderDivider", card);
            Place(headerDivider, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -HEADER_HEIGHT),
                new Vector2(0f, 2f));
            headerDivider.anchorMax = Vector2.one;
            AddImage(headerDivider, null, BugLoggerPalette.Stroke);

            // --- Thân (cuộn)
            var scrollNode = Node("Scroll", card);
            Stretch(scrollNode, 0f, FOOTER_HEIGHT, 0f, HEADER_HEIGHT + 2f);
            var scroll = scrollNode.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.12f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.12f;
            scroll.scrollSensitivity = 48f;

            var viewport = Node("Viewport", scrollNode);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            AddImage(viewport, null, Color.clear, true);

            var content = Node("Content", viewport);
            Place(content, new Vector2(0f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 1600f));
            content.anchorMax = Vector2.one;
            Column(content, new RectOffset(40, 40, 36, 56), 44f, TextAnchor.UpperLeft);
            var contentFitter = content.gameObject.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;

            var screenshot = BuildScreenshotSection(content, out var shot);
            var titleField = BuildField(content, "TitleSection", BugLoggerStrings.LABEL_TITLE,
                BugLoggerStrings.PLACEHOLDER_TITLE, true, FIELD_HEIGHT, false);
            var contentField = BuildField(content, "ContentSection", BugLoggerStrings.LABEL_CONTENT,
                BugLoggerStrings.PLACEHOLDER_CONTENT, true, CONTENT_FIELD_HEIGHT, true);
            var category = BuildChipSection(content, "CategorySection", BugLoggerStrings.LABEL_CATEGORY, false,
                out var categoryContainer, out var categoryGroup, out _);
            var reproduce = BuildReproduceSection(content, out var hits, out var tries);
            var featureField = BuildField(content, "FeatureSection", BugLoggerStrings.LABEL_FEATURE,
                BugLoggerStrings.PLACEHOLDER_FEATURE, false, FIELD_HEIGHT, false);
            var tags = BuildChipSection(content, "TagSection", BugLoggerStrings.LABEL_TAGS, true,
                out var tagContainer, out var tagGroup, out var tagHint);
            var people = BuildChipSection(content, "PeopleSection", BugLoggerStrings.LABEL_PEOPLE, true,
                out var peopleContainer, out var peopleGroup, out var peopleHint);

            var noteNode = Node("OptionsNote", content);
            var note = AddText(noteNode, BugLoggerStrings.OPTIONS_FALLBACK, _regular, 24, BugLoggerPalette.Warning,
                TextAnchor.MiddleLeft);
            noteNode.gameObject.SetActive(false);

            var fadeTop = Node("FadeTop", scrollNode);
            Place(fadeTop, new Vector2(0f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 32f));
            fadeTop.anchorMax = Vector2.one;
            fadeTop.localScale = new Vector3(1f, -1f, 1f);
            fadeTop.pivot = new Vector2(0.5f, 0f);
            AddImage(fadeTop, "bl_fade_v", BugLoggerPalette.Card);

            var fadeBottom = Node("FadeBottom", scrollNode);
            Place(fadeBottom, new Vector2(0f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 48f));
            fadeBottom.anchorMax = new Vector2(1f, 0f);
            AddImage(fadeBottom, "bl_fade_v", BugLoggerPalette.Card);

            // --- Chân form
            var footer = Node("Footer", card);
            Place(footer, Vector2.zero, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, FOOTER_HEIGHT));
            footer.anchorMax = new Vector2(1f, 0f);
            var footerDivider = Node("Divider", footer);
            Place(footerDivider, new Vector2(0f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 2f));
            footerDivider.anchorMax = Vector2.one;
            AddImage(footerDivider, null, BugLoggerPalette.Stroke);

            var footerBody = Node("Body", footer);
            Stretch(footerBody);
            Column(footerBody, new RectOffset(40, 40, 24, 36), 18f, TextAnchor.UpperCenter);

            var status = Node("Status", footerBody);
            Row(status, new RectOffset(), 12f, TextAnchor.MiddleCenter);
            var dot = Node("Dot", status);
            Layout(dot, 14f, 14f);
            var dotImage = AddImage(dot, "bl_circle", BugLoggerPalette.Success);
            var statusNode = Node("Text", status);
            var statusText = AddText(statusNode, BugLoggerStrings.STATUS_ONLINE, _regular, 24,
                BugLoggerPalette.TextSecondary, TextAnchor.MiddleLeft);
            statusText.horizontalOverflow = HorizontalWrapMode.Overflow;

            var submit = Node("Submit", footerBody);
            Layout(submit, -1f, 116f);
            var submitBackground = AddImage(submit, "bl_r24", BugLoggerPalette.Accent, true,
                BOX_SPRITE_RADIUS / 30f);
            var submitButton = submit.gameObject.AddComponent<Button>();
            submitButton.transition = Selectable.Transition.None;
            submitButton.targetGraphic = submitBackground;
            Pressable(submit, 0.96f);
            var submitContent = Node("Content", submit);
            Stretch(submitContent);
            Row(submitContent, new RectOffset(), 18f, TextAnchor.MiddleCenter);
            var submitIconNode = Node("Icon", submitContent);
            Layout(submitIconNode, 40f, 40f);
            var submitIcon = AddImage(submitIconNode, "ic_send", BugLoggerPalette.OnAccent);
            var submitLabelNode = Node("Label", submitContent);
            var submitLabel = AddText(submitLabelNode, BugLoggerStrings.SUBMIT, _semiBold, 36,
                BugLoggerPalette.OnAccent, TextAnchor.MiddleCenter);
            submitLabel.horizontalOverflow = HorizontalWrapMode.Overflow;

            // --- Template sinh lúc chạy
            var templates = Node("Templates", formRoot);
            Stretch(templates);
            var chip = BuildChipTemplate(templates);
            var skeleton = BuildSkeletonTemplate(templates);
            templates.gameObject.SetActive(true);

            var form = formRoot.gameObject.AddComponent<BugLoggerForm>();
            Wire(form,
                ("_group", formGroup), ("_scrim", scrimImage), ("_cardFrame", frame), ("_card", card),
                ("_cardGroup", cardGroup),
                ("_staggerGroups", new Object[]
                {
                    screenshot, titleField.GetComponent<CanvasGroup>(), contentField.GetComponent<CanvasGroup>(),
                    category, reproduce, featureField.GetComponent<CanvasGroup>(), tags, people
                }),
                ("_subtitle", subtitle), ("_closeButton", close), ("_scroll", scroll),
                ("_screenshotPreview", shot.Preview), ("_thumbnail", shot.Thumbnail),
                ("_thumbnailLayout", shot.PreviewLayout), ("_thumbnailButton", shot.PreviewButton),
                ("_thumbnailGroup", shot.PreviewGroup), ("_screenshotMissing", shot.Missing),
                ("_screenshotHint", shot.Hint), ("_attachRow", shot.AttachRow), ("_attachSwitch", shot.Switch),
                ("_titleField", titleField), ("_contentField", contentField), ("_featureField", featureField),
                ("_hitsStepper", hits), ("_triesStepper", tries),
                ("_categoryContainer", categoryContainer), ("_categoryGroup", categoryGroup),
                ("_tagSection", tags.gameObject), ("_tagContainer", tagContainer), ("_tagGroup", tagGroup),
                ("_tagHint", tagHint),
                ("_peopleSection", people.gameObject), ("_peopleContainer", peopleContainer),
                ("_peopleGroup", peopleGroup), ("_peopleHint", peopleHint),
                ("_optionsNote", note), ("_chipTemplate", chip), ("_skeletonTemplate", skeleton),
                ("_statusDot", dotImage), ("_statusText", statusText), ("_submitButton", submitButton),
                ("_submitBackground", submitBackground), ("_submitIcon", submitIcon), ("_submitLabel", submitLabel),
                ("_iconSend", LoadSprite("ic_send")), ("_iconCheck", LoadSprite("ic_check")),
                ("_viewer", viewer));
            return form;
        }

        private struct ScreenshotParts
        {
            public GameObject Preview;
            public RawImage Thumbnail;
            public LayoutElement PreviewLayout;
            public Button PreviewButton;
            public CanvasGroup PreviewGroup;
            public GameObject Missing;
            public Text Hint;
            public GameObject AttachRow;
            public BugLoggerSwitch Switch;
        }

        private static CanvasGroup BuildScreenshotSection(RectTransform parent, out ScreenshotParts parts)
        {
            var section = Node("ScreenshotSection", parent);
            Row(section, new RectOffset(), 32f, TextAnchor.MiddleLeft);
            var group = section.gameObject.AddComponent<CanvasGroup>();

            var preview = Node("Preview", section);
            var previewLayout = Layout(preview, 169f, THUMB_HEIGHT);
            var previewGroup = preview.gameObject.AddComponent<CanvasGroup>();
            var thumbNode = Node("Thumb", preview);
            Stretch(thumbNode);
            var thumb = thumbNode.gameObject.AddComponent<RawImage>();
            thumb.color = Color.white;
            var corners = Node("Corners", preview);
            Stretch(corners);
            AddImage(corners, "bl_r20_corner_cut", BugLoggerPalette.Card);
            var previewStroke = Node("Stroke", preview);
            Stretch(previewStroke);
            AddImage(previewStroke, "bl_r20_stroke", BugLoggerPalette.StrokeStrong);
            var expand = Node("Expand", preview);
            Place(expand, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-12f, 12f), Vector2.one * 52f);
            AddImage(expand, "bl_circle", BugLoggerPalette.WithAlpha(Color.black, 0.55f));
            var expandIcon = Node("Icon", expand);
            Place(expandIcon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * 24f);
            AddImage(expandIcon, "ic_expand", Color.white);
            var previewButton = preview.gameObject.AddComponent<Button>();
            previewButton.transition = Selectable.Transition.None;
            previewButton.targetGraphic = thumb;
            Pressable(preview, 0.96f);

            var missing = Node("Missing", section);
            Layout(missing, 169f, THUMB_HEIGHT);
            AddImage(missing, "bl_r24", BugLoggerPalette.Surface);
            var missingStroke = Node("Stroke", missing);
            Stretch(missingStroke);
            AddImage(missingStroke, "bl_r24_stroke", BugLoggerPalette.Stroke);
            var missingIcon = Node("Icon", missing);
            Place(missingIcon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * 72f);
            AddImage(missingIcon, "ic_image", BugLoggerPalette.TextMuted);
            missing.gameObject.SetActive(false);

            var info = Node("Info", section);
            Layout(info, -1f, -1f, 1f);
            Column(info, new RectOffset(), 10f, TextAnchor.MiddleLeft);
            var infoTitle = Node("Title", info);
            AddText(infoTitle, BugLoggerStrings.SCREENSHOT_TITLE, _semiBold, 32, BugLoggerPalette.TextPrimary,
                TextAnchor.MiddleLeft);
            var hintNode = Node("Hint", info);
            var hint = AddText(hintNode, "Chạm để xem lớn", _regular, 24, BugLoggerPalette.TextSecondary,
                TextAnchor.MiddleLeft);
            var spacer = Node("Spacer", info);
            Layout(spacer, -1f, 14f);

            var attachRow = Node("AttachRow", info);
            Row(attachRow, new RectOffset(0, 0, 6, 6), 18f, TextAnchor.MiddleLeft);
            AddImage(attachRow, null, Color.clear, true);
            var track = Node("Track", attachRow);
            Layout(track, 92f, 54f);
            var trackImage = AddImage(track, "bl_pill", BugLoggerPalette.Accent, false, PILL_SPRITE_RADIUS / 27f);
            var knob = Node("Knob", track);
            Place(knob, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(19f, 0f), Vector2.one * 42f);
            AddImage(knob, "bl_circle", Color.white);
            var attachLabel = Node("Label", attachRow);
            var attachText = AddText(attachLabel, BugLoggerStrings.SCREENSHOT_ATTACH, _medium, 26,
                BugLoggerPalette.TextPrimary, TextAnchor.MiddleLeft);
            attachText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var toggle = attachRow.gameObject.AddComponent<BugLoggerSwitch>();
            Wire(toggle, ("_track", trackImage), ("_knob", knob), ("_knobTravel", 19f));

            parts = new ScreenshotParts
            {
                Preview = preview.gameObject, Thumbnail = thumb, PreviewLayout = previewLayout,
                PreviewButton = previewButton, PreviewGroup = previewGroup, Missing = missing.gameObject,
                Hint = hint, AttachRow = attachRow.gameObject, Switch = toggle
            };
            return group;
        }

        private static BugLoggerField BuildField(RectTransform parent, string name, string label, string placeholder,
            bool required, float height, bool multiline)
        {
            var section = Node(name, parent);
            Column(section, new RectOffset(), 14f, TextAnchor.UpperLeft);
            section.gameObject.AddComponent<CanvasGroup>();

            var labelRow = LabelRow(section, label, required);
            var counterNode = Node("Counter", labelRow);
            var counter = AddText(counterNode, "0/100", _regular, 24, BugLoggerPalette.TextMuted,
                TextAnchor.MiddleRight);
            counter.horizontalOverflow = HorizontalWrapMode.Overflow;

            var holder = Node("Input", section);
            Layout(holder, -1f, height);
            var box = Node("Box", holder);
            Stretch(box);
            var boxImage = AddImage(box, "bl_r24", BugLoggerPalette.Surface, true);
            var strokeNode = Node("Stroke", box);
            Stretch(strokeNode);
            var stroke = AddImage(strokeNode, "bl_r24_stroke", BugLoggerPalette.Stroke);

            var verticalInset = multiline ? 24f : 0f;
            var anchor = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
            var placeholderNode = Node("Placeholder", box);
            Stretch(placeholderNode, 28f, verticalInset, 28f, verticalInset);
            var placeholderText = AddText(placeholderNode, placeholder, _regular, 32, BugLoggerPalette.TextMuted,
                anchor);
            var textNode = Node("Text", box);
            Stretch(textNode, 28f, verticalInset, 28f, verticalInset);
            var text = AddText(textNode, string.Empty, _regular, 32, BugLoggerPalette.TextPrimary, anchor);
            if (multiline)
            {
                // Dấu tiếng Việt chồng hai tầng ⇒ giãn dòng chút cho khỏi dính dòng trên.
                placeholderText.lineSpacing = MULTILINE_SPACING;
                text.lineSpacing = MULTILINE_SPACING;
            }
            else
            {
                placeholderText.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
            }

            // Ô nhập cho cảm ứng (sửa trong ô trên Android, vuốt trên ô = cuộn form) — xem BugLoggerInputField.
            var input = box.gameObject.AddComponent<BugLoggerInputField>();
            input.transition = Selectable.Transition.None;
            input.targetGraphic = boxImage;
            input.textComponent = text;
            input.placeholder = placeholderText;
            input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            input.contentType = InputField.ContentType.Standard;
            input.customCaretColor = true;
            input.caretColor = BugLoggerPalette.Accent;
            input.caretWidth = 3;
            input.caretBlinkRate = 0.85f;
            input.selectionColor = BugLoggerPalette.WithAlpha(BugLoggerPalette.Accent, 0.35f);

            var errorNode = Node("Error", section);
            var error = AddText(errorNode, string.Empty, _medium, 24, BugLoggerPalette.Danger, TextAnchor.MiddleLeft);
            errorNode.gameObject.SetActive(false);

            var field = section.gameObject.AddComponent<BugLoggerField>();
            Wire(field, ("_input", input), ("_stroke", stroke), ("_shakeTarget", box), ("_counter", counter),
                ("_error", error));
            return field;
        }

        private static RectTransform LabelRow(RectTransform parent, string label, bool required)
        {
            var row = Node("LabelRow", parent);
            Row(row, new RectOffset(4, 4, 0, 0), 6f, TextAnchor.MiddleLeft);
            var labelNode = Node("Label", row);
            var labelText = AddText(labelNode, label, _medium, 28, BugLoggerPalette.TextSecondary,
                TextAnchor.MiddleLeft);
            labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (required)
            {
                var mark = Node("Required", row);
                var markText = AddText(mark, BugLoggerStrings.REQUIRED_MARK, _semiBold, 28, BugLoggerPalette.Accent,
                    TextAnchor.MiddleLeft);
                markText.horizontalOverflow = HorizontalWrapMode.Overflow;
            }

            var spacer = Node("Spacer", row);
            Layout(spacer, -1f, -1f, 1f);
            return row;
        }

        private static CanvasGroup BuildChipSection(RectTransform parent, string name, string label, bool withHint,
            out RectTransform container, out CanvasGroup containerGroup, out Text hint)
        {
            var section = Node(name, parent);
            Column(section, new RectOffset(), 16f, TextAnchor.UpperLeft);
            var group = section.gameObject.AddComponent<CanvasGroup>();

            var row = LabelRow(section, label, false);
            hint = null;
            if (withHint)
            {
                var hintNode = Node("Hint", row);
                hint = AddText(hintNode, "Tối đa 4", _regular, 24, BugLoggerPalette.TextMuted, TextAnchor.MiddleRight);
                hint.horizontalOverflow = HorizontalWrapMode.Overflow;
            }

            container = Node("Chips", section);
            var flow = container.gameObject.AddComponent<BugLoggerFlowLayout>();
            Wire(flow, ("_spacingX", 14f), ("_spacingY", 14f), ("_rowHeight", CHIP_HEIGHT));
            containerGroup = container.gameObject.AddComponent<CanvasGroup>();
            return group;
        }

        private static CanvasGroup BuildReproduceSection(RectTransform parent, out BugLoggerStepper hits,
            out BugLoggerStepper tries)
        {
            var section = Node("ReproduceSection", parent);
            Column(section, new RectOffset(), 16f, TextAnchor.UpperLeft);
            var group = section.gameObject.AddComponent<CanvasGroup>();
            LabelRow(section, BugLoggerStrings.LABEL_REPRODUCE, false);

            var row = Node("Row", section);
            Row(row, new RectOffset(), 16f, TextAnchor.MiddleLeft);
            hits = BuildStepper(row, "Hits");
            var slash = Node("Slash", row);
            var slashText = AddText(slash, BugLoggerStrings.REPRODUCE_SEPARATOR, _semiBold, 40,
                BugLoggerPalette.TextMuted, TextAnchor.MiddleCenter);
            slashText.horizontalOverflow = HorizontalWrapMode.Overflow;
            tries = BuildStepper(row, "Tries");
            var caption = Node("Caption", row);
            var captionText = AddText(caption,
                BugLoggerStrings.REPRODUCE_HITS + " " + BugLoggerStrings.REPRODUCE_SEPARATOR + " " +
                BugLoggerStrings.REPRODUCE_TRIES, _regular, 24, BugLoggerPalette.TextMuted, TextAnchor.MiddleLeft);
            captionText.horizontalOverflow = HorizontalWrapMode.Overflow;
            Layout(caption, -1f, -1f, 1f);
            return group;
        }

        private static BugLoggerStepper BuildStepper(RectTransform parent, string name)
        {
            var stepper = Node(name, parent);
            Layout(stepper, -1f, STEPPER_HEIGHT);
            AddImage(stepper, "bl_pill", BugLoggerPalette.Surface, false, PILL_SPRITE_RADIUS / (STEPPER_HEIGHT / 2f));
            var stroke = Node("Stroke", stepper);
            Stretch(stroke);
            AddImage(stroke, "bl_pill_stroke", BugLoggerPalette.Stroke, false,
                PILL_SPRITE_RADIUS / (STEPPER_HEIGHT / 2f));
            Ignore(stroke);
            Row(stepper, new RectOffset(8, 8, 8, 8), 6f, TextAnchor.MiddleCenter);

            var minus = CircleButton(stepper, "Minus", STEPPER_BUTTON, BugLoggerPalette.SurfaceRaised, "ic_minus", 26f,
                BugLoggerPalette.TextPrimary);
            Layout((RectTransform)minus.transform, STEPPER_BUTTON, STEPPER_BUTTON);
            var valueNode = Node("Value", stepper);
            var value = AddText(valueNode, "1", _semiBold, 36, BugLoggerPalette.TextPrimary, TextAnchor.MiddleCenter);
            value.horizontalOverflow = HorizontalWrapMode.Overflow;
            Layout(valueNode, 64f, STEPPER_BUTTON);
            var plus = CircleButton(stepper, "Plus", STEPPER_BUTTON, BugLoggerPalette.SurfaceRaised, "ic_plus", 26f,
                BugLoggerPalette.TextPrimary);
            Layout((RectTransform)plus.transform, STEPPER_BUTTON, STEPPER_BUTTON);

            var component = stepper.gameObject.AddComponent<BugLoggerStepper>();
            Wire(component, ("_minusButton", minus), ("_plusButton", plus),
                ("_minusGroup", minus.gameObject.AddComponent<CanvasGroup>()),
                ("_plusGroup", plus.gameObject.AddComponent<CanvasGroup>()), ("_valueText", value));
            return component;
        }

        private static BugLoggerChip BuildChipTemplate(RectTransform parent)
        {
            var chip = Node("Chip", parent);
            Row(chip, new RectOffset(24, 28, 0, 0), 10f, TextAnchor.MiddleCenter);
            Layout(chip, -1f, CHIP_HEIGHT);

            var background = Node("Bg", chip);
            Stretch(background);
            var backgroundImage = AddImage(background, "bl_pill", BugLoggerPalette.Surface, true,
                PILL_SPRITE_RADIUS / (CHIP_HEIGHT / 2f));
            Ignore(background);
            var strokeNode = Node("Stroke", chip);
            Stretch(strokeNode);
            var stroke = AddImage(strokeNode, "bl_pill_stroke", BugLoggerPalette.Stroke, false,
                PILL_SPRITE_RADIUS / (CHIP_HEIGHT / 2f));
            Ignore(strokeNode);

            var iconNode = Node("Icon", chip);
            Layout(iconNode, 26f, 26f);
            var icon = AddImage(iconNode, "ic_plus", BugLoggerPalette.TextMuted);
            var labelNode = Node("Label", chip);
            var label = AddText(labelNode, "Option", _medium, 28, BugLoggerPalette.TextSecondary,
                TextAnchor.MiddleCenter);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;

            var button = chip.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = backgroundImage;
            Pressable(chip, 0.93f);

            var component = chip.gameObject.AddComponent<BugLoggerChip>();
            Wire(component, ("_button", button), ("_background", backgroundImage), ("_stroke", stroke),
                ("_icon", icon), ("_label", label), ("_shakeTarget", chip), ("_iconAdd", LoadSprite("ic_plus")),
                ("_iconCheck", LoadSprite("ic_check")));
            chip.gameObject.SetActive(false);
            return component;
        }

        private static Graphic BuildSkeletonTemplate(RectTransform parent)
        {
            var skeleton = Node("Skeleton", parent);
            Layout(skeleton, 160f, CHIP_HEIGHT);
            var image = AddImage(skeleton, "bl_pill", BugLoggerPalette.SurfaceRaised, false,
                PILL_SPRITE_RADIUS / (CHIP_HEIGHT / 2f));
            skeleton.gameObject.SetActive(false);
            return image;
        }

        #endregion

        #region Viewer / Toast

        private static BugLoggerImageViewer BuildViewer(RectTransform root, out RectTransform viewerRoot)
        {
            viewerRoot = Node("Viewer", root);
            Stretch(viewerRoot);
            var group = viewerRoot.gameObject.AddComponent<CanvasGroup>();

            var backdrop = Node("Backdrop", viewerRoot);
            Stretch(backdrop);
            var backdropImage = AddImage(backdrop, null, BugLoggerPalette.WithAlpha(Color.black, 0.92f), true);
            var closeArea = backdrop.gameObject.AddComponent<Button>();
            closeArea.transition = Selectable.Transition.None;
            closeArea.targetGraphic = backdropImage;

            var frame = Node("Frame", viewerRoot);
            Stretch(frame);
            frame.gameObject.AddComponent<BugLoggerSafeArea>();

            var fit = Node("Fit", frame);
            Stretch(fit, 40f, 40f, 40f, 140f);
            var imageNode = Node("Image", fit);
            Stretch(imageNode);
            var image = imageNode.gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            var fitter = imageNode.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 9f / 16f;

            var close = CircleButton(frame, "Close", 88f, BugLoggerPalette.WithAlpha(Color.white, 0.14f), "ic_close",
                34f, Color.white);
            Place((RectTransform)close.transform, Vector2.one, Vector2.one, new Vector2(-32f, -32f),
                Vector2.one * 88f);

            var component = viewerRoot.gameObject.AddComponent<BugLoggerImageViewer>();
            Wire(component, ("_group", group), ("_frame", fit), ("_image", image), ("_fitter", fitter),
                ("_closeArea", closeArea), ("_closeButton", close));
            viewerRoot.gameObject.SetActive(false);
            return component;
        }

        private static BugLoggerToast BuildToast(RectTransform parent)
        {
            var toast = Node("Toast", parent);
            Place(toast, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -32f), Vector2.zero);
            var group = toast.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var panel = Node("Panel", toast);
            Place(panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(400f, TOAST_HEIGHT));
            var shadow = Node("Shadow", panel);
            Stretch(shadow, -36f, -48f, -36f, -28f);
            AddImage(shadow, "bl_card_shadow", BugLoggerPalette.WithAlpha(Color.black, 0.5f), false, 2f);
            Ignore(shadow);
            var fill = Node("Bg", panel);
            Stretch(fill);
            AddImage(fill, "bl_pill", BugLoggerPalette.SurfaceRaised, false, PILL_SPRITE_RADIUS / (TOAST_HEIGHT / 2f));
            Ignore(fill);
            var stroke = Node("Stroke", panel);
            Stretch(stroke);
            AddImage(stroke, "bl_pill_stroke", BugLoggerPalette.StrokeStrong, false,
                PILL_SPRITE_RADIUS / (TOAST_HEIGHT / 2f));
            Ignore(stroke);
            Row(panel, new RectOffset(20, 36, 20, 20), 18f, TextAnchor.MiddleLeft);
            var panelFitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            panelFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var iconBackgroundNode = Node("IconBg", panel);
            Layout(iconBackgroundNode, 48f, 48f);
            var iconBackground = AddImage(iconBackgroundNode, "bl_circle", BugLoggerPalette.Success);
            var iconNode = Node("Icon", iconBackgroundNode);
            Place(iconNode, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * 26f);
            var icon = AddImage(iconNode, "ic_check", Color.white);

            var messageNode = Node("Message", panel);
            var message = AddText(messageNode, "Toast", _medium, 28, BugLoggerPalette.TextPrimary,
                TextAnchor.MiddleLeft);
            var messageLayout = Layout(messageNode, 300f);

            var component = toast.gameObject.AddComponent<BugLoggerToast>();
            Wire(component, ("_group", group), ("_panel", panel), ("_iconBackground", iconBackground),
                ("_icon", icon), ("_message", message), ("_messageLayout", messageLayout),
                ("_iconInfo", LoadSprite("ic_info")), ("_iconSuccess", LoadSprite("ic_check")),
                ("_iconError", LoadSprite("ic_alert")));
            toast.gameObject.SetActive(false);
            return component;
        }

        #endregion

        #region Helpers

        private static Button CircleButton(RectTransform parent, string name, float size, Color background,
            string icon, float iconSize, Color iconColor)
        {
            var node = Node(name, parent);
            Place(node, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * size);
            var image = AddImage(node, "bl_circle", background, true);
            var iconNode = Node("Icon", node);
            Place(iconNode, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * iconSize);
            AddImage(iconNode, icon, iconColor);
            var button = node.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = image;
            Pressable(node, 0.9f);
            return button;
        }

        private static void Pressable(RectTransform node, float scale)
        {
            var pressable = node.gameObject.AddComponent<BugLoggerPressable>();
            Wire(pressable, ("_target", node), ("_pressedScale", scale));
        }

        private static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = UI_LAYER };
            var rect = (RectTransform)go.transform;
            if (parent != null) rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect, float left = 0f, float bottom = 0f, float right = 0f,
            float top = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private static void Ignore(RectTransform rect)
        {
            if (!rect.TryGetComponent<LayoutElement>(out var layout)) layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
        }

        private static Image AddImage(RectTransform rect, string sprite, Color color, bool raycast = false,
            float pixelsPerUnitMultiplier = 1f)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            if (sprite == null) return image;

            image.sprite = LoadSprite(sprite);
            var sliced = image.sprite.border != Vector4.zero;
            image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            image.pixelsPerUnitMultiplier = pixelsPerUnitMultiplier;
            image.preserveAspect = !sliced;
            return image;
        }

        private static Text AddText(RectTransform rect, string value, Font font, int size, Color color,
            TextAnchor alignment)
        {
            var text = rect.gameObject.AddComponent<Text>();
            text.text = value;
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.lineSpacing = 1f;
            return text;
        }

        private static LayoutElement Layout(RectTransform rect, float preferredWidth = -1f,
            float preferredHeight = -1f, float flexibleWidth = -1f, float flexibleHeight = -1f, float minWidth = -1f)
        {
            if (!rect.TryGetComponent<LayoutElement>(out var layout)) layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = preferredWidth;
            layout.preferredHeight = preferredHeight;
            layout.flexibleWidth = flexibleWidth;
            layout.flexibleHeight = flexibleHeight;
            layout.minWidth = minWidth;
            return layout;
        }

        private static void Row(RectTransform rect, RectOffset padding, float spacing, TextAnchor alignment)
        {
            var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            ConfigureGroup(group, padding, spacing, alignment);
        }

        private static void Column(RectTransform rect, RectOffset padding, float spacing, TextAnchor alignment)
        {
            var group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            ConfigureGroup(group, padding, spacing, alignment);
            group.childForceExpandWidth = true;
        }

        private static void ConfigureGroup(HorizontalOrVerticalLayoutGroup group, RectOffset padding, float spacing,
            TextAnchor alignment)
        {
            group.padding = padding;
            group.spacing = spacing;
            group.childAlignment = alignment;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            group.childScaleWidth = false;
            group.childScaleHeight = false;
        }

        private static Sprite LoadSprite(string name)
        {
            var path = SPRITE_DIR + name + ".png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("Thiếu sprite " + path);
            return sprite;
        }

        private static Font LoadFont(string name)
        {
            var path = FONT_DIR + name + ".ttf";
            var font = AssetDatabase.LoadAssetAtPath<Font>(path);
            if (font == null) throw new InvalidOperationException("Thiếu font " + path);
            return font;
        }

        /// <summary>Gán field private [SerializeField] theo tên — sai tên là ném ngay để builder không ra prefab hở.</summary>
        private static void Wire(Object target, params (string Field, object Value)[] pairs)
        {
            var serialized = new SerializedObject(target);
            foreach (var (field, value) in pairs)
            {
                var property = serialized.FindProperty(field);
                if (property == null)
                    throw new InvalidOperationException(target.GetType().Name + " không có field " + field);

                switch (value)
                {
                    case Object[] array:
                        property.arraySize = array.Length;
                        for (var i = 0; i < array.Length; i++)
                            property.GetArrayElementAtIndex(i).objectReferenceValue = array[i];
                        break;
                    case Object reference:
                        property.objectReferenceValue = reference;
                        break;
                    case float number:
                        property.floatValue = number;
                        break;
                    case bool flag:
                        property.boolValue = flag;
                        break;
                    case int integer:
                        property.intValue = integer;
                        break;
                    default:
                        throw new InvalidOperationException("Kiểu không hỗ trợ cho " + field);
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        #endregion
    }
}
