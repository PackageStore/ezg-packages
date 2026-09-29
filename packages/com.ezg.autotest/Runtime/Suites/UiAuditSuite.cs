using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Suite "UI Audit": mở từng màn hình rồi soi lỗi giao diện QA hay bắt — ô trắng thiếu sprite, chữ tràn/bị cắt,
    ///     lộ key localize, nút ngoài màn hình / ngoài safe area, vùng bấm quá nhỏ, nút bị che, nút không gắn hành
    ///     động, nút chồng nhau. Mỗi màn chụp một ảnh làm bằng chứng chung cho mọi issue của màn đó.
    /// </summary>
    public sealed class UiAuditSuite : AutoTestSuite
    {
        const int MAX_ISSUES_PER_SCREEN = 60;

        public override string Id => "ui-audit";
        public override string DisplayName => "UI Audit";

        public override string Description =>
            "Mở từng màn hình và soi lỗi giao diện: ô trắng thiếu sprite, chữ tràn/bị cắt, lộ key localize/placeholder, " +
            "nút nằm ngoài màn hình hoặc ngoài vùng an toàn, vùng bấm quá nhỏ, nút bị che, nút không gắn hành động, " +
            "nút chồng lên nhau. Mỗi issue kèm ảnh chụp màn + đường dẫn object.";

        public override ExecutionMode Mode => ExecutionMode.Play;
        public override int Order => 30;
        public override string Icon => "d_RectTransformBlueprint";
        public override bool MutatesPlayerData => true;

        public override IEnumerable<AutoTestCase> BuildCases(AutoTestBuildContext ctx)
        {
            return UiAuditUtil.BuildScreenCases(ctx, Describe, AuditScreen, 0, 0, true);
        }

        static string Describe(AutoTestFeature feature)
        {
            return feature == null
                ? "Soi lỗi giao diện trên mọi Canvas đang hiển thị (adapter không mở được từng màn)."
                : $"Mở màn {feature.Name} và soi lỗi giao diện.";
        }

        static async Task AuditScreen(AutoTestContext ctx, ScreenScope scope)
        {
            var roots = scope.GetRoots();
            if (roots.Count == 0) ctx.Skip("Không tìm thấy UI đang hiển thị (không có Canvas active / màn không có root).");

            string screenshot;
            using (ctx.Step("Chụp màn hình"))
            {
                screenshot = await ctx.Screenshot("screen");
            }

            var audit = new ScreenAudit(ctx);
            using (ctx.Step("Soi giao diện"))
            {
                await audit.Run(roots);
            }

            ctx.Metric("Graphic hiển thị", audit.GraphicCount);
            ctx.Metric("Selectable hiển thị", audit.SelectableCount);
            ctx.Metric("Vấn đề UI", audit.Collector.Count);
            var total = audit.Collector.Flush(ctx, scope, screenshot, MAX_ISSUES_PER_SCREEN);
            ctx.Log($"{scope.Name}: {audit.GraphicCount} graphic, {audit.TextCount} text, " +
                    $"{audit.SelectableCount} selectable — {total} vấn đề.");
        }

        /// <summary>Một lượt soi UI của một màn (state riêng, không dùng lại giữa các màn).</summary>
        sealed class ScreenAudit
        {
            const int YIELD_EVERY = 200;
            const float MIN_VISIBLE_ALPHA = 0.02f;

            /// <summary>Kênh màu ≥ ngưỡng này coi là trắng.</summary>
            const float WHITE_CHANNEL_MIN = 0.92f;

            /// <summary>Ô trắng phải đủ đục mới coi là lỗi (lớp trắng mờ thường là highlight cố ý).</summary>
            const float WHITE_BOX_MIN_ALPHA = 0.5f;

            /// <summary>Cạnh nhỏ hơn ngưỡng (px) coi là đường kẻ, không phải "ô".</summary>
            const float THIN_LINE_PX = 6f;

            const float OFFSCREEN_MAX_FRACTION = 0.2f;
            const float SAFE_AREA_TOLERANCE_PX = 2f;
            const float SAFE_AREA_DIFF_PX = 1f;

            /// <summary>Text Overflow mode: tràn quá 15% + 2px mới báo (lệch nhỏ vẫn đọc được).</summary>
            const float OVERFLOW_TOLERANCE_RATIO = 0.15f;

            const float OVERFLOW_TOLERANCE_PX = 2f;
            const float LEGACY_OVERFLOW_TOLERANCE_PX = 1f;
            const float OVERLAP_MIN_RATIO = 0.5f;
            const int MAX_OVERLAP_GROUP = 100;

            /// <summary>Kiểm lại nút bị che sau khoảng này để bỏ qua che tạm thời (animation, toast).</summary>
            const float BLOCKED_RECHECK_SECONDS = 0.4f;

            /// <summary>Nút phủ từ ngần này diện tích màn trở lên là lớp nền (chạm ra ngoài để đóng), không phải nút thật.</summary>
            const float BACKDROP_SCREEN_FRACTION = 0.5f;

            const int MAX_SMALL_TARGETS_LISTED = 15;

            static readonly System.Text.RegularExpressions.Regex BACKDROP_NAME = new(
                "(?i)(background|backdrop|^bg_|_bg$|dim|overlay|blocker|close_?area|outside|fullscreen)");

            /// <summary>Từ bấy nhiêu nút cùng bị một object che ⇒ gộp thành một issue.</summary>
            const int BLOCKED_GROUP_MIN = 3;

            const int GROUP_LIST_MAX = 8;
            const int PERCENT = 100;

            static readonly Regex DECORATIVE_NAME = new("bg|background|overlay|dim|mask|fade|blocker|line|divider",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            static readonly Regex HASH_KEY = new(@"^#[A-Za-z_]", RegexOptions.CultureInvariant);
            static readonly Regex PLACEHOLDER = new(@"\{\d+(:[^{}]*)?\}", RegexOptions.CultureInvariant);
            static readonly Regex HAS_LETTER = new(@"[A-Za-z]", RegexOptions.CultureInvariant);

            readonly AutoTestContext _ctx;
            readonly UiAuditConfig _cfg;
            readonly UiDriver _ui;
            readonly AncestorNameMatcher _ignore;
            readonly ClipCache _clip;
            readonly Regex _keyRegex;
            readonly Rect _screen;
            readonly Rect _safe;
            readonly bool _checkSafeArea;
            readonly float _pxPerDp;
            readonly List<Graphic> _graphics = new();
            readonly List<Selectable> _selectables = new();
            readonly List<Selectable> _blockCandidates = new();
            readonly List<string> _smallTargets = new();
            float _smallestDp = float.MaxValue;
            readonly List<Button> _overlapCandidates = new();
            int _index;

            public ScreenAudit(AutoTestContext ctx)
            {
                _ctx = ctx;
                _cfg = ctx.Config.uiAudit;
                _ui = ctx.Ui;
                _ignore = new AncestorNameMatcher(_cfg.ignoredObjectPatterns);
                _clip = new ClipCache(_ui);
                _keyRegex = CompileKeyRegex(_cfg.localizationKeyPattern);
                _screen = new Rect(0f, 0f, Screen.width, Screen.height);
                _safe = Screen.safeArea;
                _checkSafeArea = _cfg.checkSafeArea && UiAuditUtil.OverflowDistance(_screen, _safe) > SAFE_AREA_DIFF_PX;
                _pxPerDp = UiAuditUtil.PixelsPerDp();
            }

            public UiIssueCollector Collector { get; } = new();
            public int GraphicCount { get; private set; }
            public int TextCount { get; private set; }
            public int SelectableCount { get; private set; }

            public async Task Run(List<GameObject> roots)
            {
                foreach (var root in roots)
                {
                    if (root == null) continue;
                    var stopAt = root.transform;

                    root.GetComponentsInChildren(false, _graphics);
                    foreach (var g in _graphics)
                    {
                        await MaybeYield();
                        if (g == null || _ignore.Matches(g.transform, stopAt)) continue;
                        if (!UiAuditUtil.IsGraphicVisible(_ui, g)) continue;
                        GraphicCount++;
                        AuditGraphic(g);
                    }

                    if (root == null) continue;
                    root.GetComponentsInChildren(false, _selectables);
                    foreach (var s in _selectables)
                    {
                        await MaybeYield();
                        if (s == null || _ignore.Matches(s.transform, stopAt)) continue;
                        AuditSelectable(s);
                    }
                }

                _graphics.Clear();
                _selectables.Clear();
                if (_cfg.checkRaycastBlocked) await CheckBlocked();
                CheckOverlaps();
                FlushSmallTargets();
            }

            async Task MaybeYield()
            {
                if (++_index % YIELD_EVERY != 0) return;
                _ctx.ThrowIfCancelled();
                await _ctx.NextFrame();
            }

            static Regex CompileKeyRegex(string pattern)
            {
                if (string.IsNullOrWhiteSpace(pattern)) return null;
                try
                {
                    // Phân biệt hoa thường: key localize thường viết thường, câu chữ thật có chữ hoa.
                    return new Regex(pattern, RegexOptions.CultureInvariant);
                }
                catch (ArgumentException e)
                {
                    AutoTestLog.Warn($"localizationKeyPattern sai cú pháp ({e.Message}) — bỏ kiểm tra theo pattern.");
                    return null;
                }
            }

            #region Graphic

            void AuditGraphic(Graphic g)
            {
                var path = UiDriver.PathOf(g.transform);
                if (g is Image image)
                {
                    if (_cfg.checkMissingSprite) CheckMissingSprite(image, path);
                    return;
                }

                var tmp = g as TMP_Text;
                var legacy = tmp == null ? g as Text : null;
                if (tmp == null && legacy == null) return;
                var raw = tmp != null ? tmp.text : legacy.text;
                if (string.IsNullOrWhiteSpace(raw)) return;
                TextCount++;
                var plain = UiAuditUtil.StripRichText(raw).Trim();

                if (_cfg.checkLocalizationLeak) CheckLocalizationLeak(g, plain, path);
                if (_cfg.checkTextOverflow)
                {
                    if (tmp != null) CheckTmpOverflow(tmp, plain, path);
                    else CheckLegacyOverflow(legacy, plain, path);
                }

                if (_checkSafeArea) CheckSafeArea(g.rectTransform, g.name, path, "Chữ");
            }

            void CheckMissingSprite(Image image, string path)
            {
                // Subclass của Image thường tự vẽ hình (bo góc, gradient…) không cần sprite.
                if (image.GetType() != typeof(Image)) return;
                if (image.overrideSprite != null) return;
                var color = image.color;
                var alpha = color.a * image.canvasRenderer.GetAlpha() * UiDriver.EffectiveAlpha(image.transform);
                if (alpha <= MIN_VISIBLE_ALPHA) return;
                // Image làm vùng mask / dùng material riêng (shader tự vẽ) là cố ý.
                if (image.GetComponent<Mask>() != null || image.GetComponent<RectMask2D>() != null) return;
                var material = image.material;
                if (material != null && material != image.defaultMaterial) return;

                var rect = _ui.GetScreenRect(image.rectTransform);
                var minSide = Mathf.Min(rect.width, rect.height);
                var isWhite = color.r >= WHITE_CHANNEL_MIN && color.g >= WHITE_CHANNEL_MIN &&
                              color.b >= WHITE_CHANNEL_MIN;
                var size = $"{rect.width:0}×{rect.height:0}px";

                // Nền phủ nửa màn trở lên: nền màu phẳng cố ý (hoặc lỗi lộ liễu tới mức QA thấy ngay) → chỉ ghi Info.
                var screenArea = Mathf.Max(1f, _screen.width * _screen.height);
                if (rect.width * rect.height >= screenArea * BACKDROP_SCREEN_FRACTION)
                {
                    Collector.Add(Severity.Info, "UI.SolidBackground", $"Nền toàn màn không sprite — {image.name}",
                        $"Image '{image.name}' phủ {size} bằng màu phẳng #{ColorUtility.ToHtmlStringRGBA(color)} — " +
                        "kiểm tra lại nếu thiết kế cần ảnh nền.", path);
                    return;
                }

                if (isWhite && alpha >= WHITE_BOX_MIN_ALPHA && minSide >= THIN_LINE_PX)
                {
                    Collector.Add(Severity.Major, "UI.WhiteBox", $"Ô trắng: Image không có sprite — {image.name}",
                        $"Image '{image.name}' không gán sprite nên hiện thành khối trắng {size}.", path,
                        "Có sprite", "sprite = null, màu trắng");
                    return;
                }

                // Khối trắng mảnh / mờ = đường kẻ, highlight; nền/overlay tô màu phẳng là cố ý.
                if (isWhite || DECORATIVE_NAME.IsMatch(image.name)) return;
                var hex = "#" + ColorUtility.ToHtmlStringRGBA(color);
                Collector.Add(Severity.Minor, "UI.MissingSprite", $"Image không có sprite — {image.name}",
                    $"Image '{image.name}' không gán sprite, hiện thành khối màu phẳng {hex} ({size}). " +
                    "Bỏ qua nếu đây là khối màu cố ý.", path, "Có sprite (hoặc cố ý là khối màu)",
                    $"sprite = null, màu {hex}");
            }

            void CheckLocalizationLeak(Graphic g, string plain, string path)
            {
                if (plain.Length == 0) return;
                // Chữ người chơi tự nhập (tên, gift code…) không phải key.
                if (g.GetComponentInParent<TMP_InputField>() != null || g.GetComponentInParent<InputField>() != null)
                    return;

                string kind = null;
                if (PLACEHOLDER.IsMatch(plain)) kind = "placeholder {n} chưa được thay";
                else if (HASH_KEY.IsMatch(plain)) kind = "key localize bị thiếu (dạng #Nhóm_key)";
                else if (_keyRegex != null && HAS_LETTER.IsMatch(plain) && _keyRegex.IsMatch(plain))
                    kind = "chuỗi trông như key localize";
                if (kind == null) return;

                var excerpt = UiAuditUtil.Excerpt(plain);
                Collector.Add(Severity.Major, "UI.LocalizationLeak", $"Lộ key localize / placeholder chưa thay — {g.name}",
                    $"Text đang hiện '{excerpt}' — {kind}.", path, "Chữ đã dịch đầy đủ", excerpt);
            }

            void CheckTmpOverflow(TMP_Text tmp, string plain, string path)
            {
                var mode = tmp.overflowMode;
                // Masking / ScrollRect / Page / Linked: tràn là cố ý (cuộn, phân trang, nối khung).
                if (mode != TextOverflowModes.Overflow && mode != TextOverflowModes.Truncate &&
                    mode != TextOverflowModes.Ellipsis)
                    return;

                var rect = tmp.rectTransform.rect;
                var margin = tmp.margin;
                var availableWidth = rect.width - margin.x - margin.z;
                var availableHeight = rect.height - margin.y - margin.w;
                if (availableWidth <= 0f || availableHeight <= 0f) return;

                tmp.ForceMeshUpdate();
                string detail = null;
                if (mode != TextOverflowModes.Overflow)
                {
                    if (tmp.isTextOverflowing || tmp.isTextTruncated)
                        detail = mode == TextOverflowModes.Ellipsis ? "chữ bị cắt thành '…'" : "chữ bị cắt mất phần cuối";
                }
                else if (tmp.enableAutoSizing)
                {
                    if (tmp.isTextOverflowing)
                        detail = $"đã thu nhỏ tới cỡ tối thiểu {UiAuditUtil.Num(tmp.fontSizeMin, "0.#")} vẫn tràn khung";
                }
                else
                {
                    var preferredHeight = tmp.preferredHeight;
                    if (preferredHeight > availableHeight * (1f + OVERFLOW_TOLERANCE_RATIO) + OVERFLOW_TOLERANCE_PX)
                    {
                        detail = $"chữ cao {preferredHeight:0} > khung {availableHeight:0}";
                    }
                    else if (tmp.textWrappingMode == TextWrappingModes.NoWrap ||
                             tmp.textWrappingMode == TextWrappingModes.PreserveWhitespaceNoWrap)
                    {
                        var preferredWidth = tmp.preferredWidth;
                        if (preferredWidth > availableWidth * (1f + OVERFLOW_TOLERANCE_RATIO) + OVERFLOW_TOLERANCE_PX)
                            detail = $"chữ rộng {preferredWidth:0} > khung {availableWidth:0}";
                    }
                }

                if (detail == null) return;
                Collector.Add(Severity.Minor, "UI.TextOverflow", $"Chữ tràn/bị cắt — {tmp.name}",
                    $"'{UiAuditUtil.Excerpt(plain)}': {detail} (overflow mode = {mode}).", path,
                    "Chữ nằm gọn trong khung", detail);
            }

            void CheckLegacyOverflow(Text text, string plain, string path)
            {
                if (text.resizeTextForBestFit) return;
                if (text.horizontalOverflow != HorizontalWrapMode.Wrap ||
                    text.verticalOverflow != VerticalWrapMode.Truncate)
                    return;
                var height = text.rectTransform.rect.height;
                var preferred = text.preferredHeight;
                if (preferred <= height + LEGACY_OVERFLOW_TOLERANCE_PX) return;
                var detail = $"chữ cao {preferred:0} > khung {height:0}";
                Collector.Add(Severity.Minor, "UI.TextOverflow", $"Chữ tràn/bị cắt — {text.name}",
                    $"'{UiAuditUtil.Excerpt(plain)}': {detail}, dòng thừa bị cắt (Vertical Overflow = Truncate).", path,
                    "Chữ nằm gọn trong khung", detail);
            }

            #endregion

            #region Selectable

            void AuditSelectable(Selectable s)
            {
                var go = s.gameObject;
                if (!_ui.IsVisible(go)) return;
                SelectableCount++;
                if (!_ui.IsInteractable(go)) return;
                if (!(s.transform is RectTransform rt)) return;

                var path = UiDriver.PathOf(s.transform);
                var rect = _ui.GetScreenRect(rt);
                var visibleRect = rect;
                var hasClip = _clip.TryGetClip(s.transform, out var clip);
                if (hasClip)
                {
                    visibleRect = UiAuditUtil.Intersect(rect, clip);
                    if (UiAuditUtil.IsEmpty(visibleRect)) return; // bị viewport cắt hết (item list đã cuộn qua)
                }

                var center = rect.center;
                var centerVisible = (!hasClip || clip.Contains(center)) && _screen.Contains(center);

                var offScreen = false;
                if (_cfg.checkOffScreen)
                {
                    var fraction = UiAuditUtil.OutsideFraction(visibleRect, _screen);
                    if (fraction > OFFSCREEN_MAX_FRACTION)
                    {
                        var percent = UiAuditUtil.Num(fraction * PERCENT, "0");
                        Collector.Add(Severity.Minor, "UI.OffScreen", $"Nút nằm ngoài màn hình — {go.name}",
                            $"{percent}% diện tích nút nằm ngoài màn hình {Screen.width}×{Screen.height}.", path,
                            "Nằm trọn trong màn hình", $"{percent}% ngoài màn hình");
                        offScreen = true;
                    }
                }

                if (_checkSafeArea && !offScreen) CheckSafeAreaRect(visibleRect, go.name, path, "Nút");
                if (_cfg.minTouchTargetDp > 0f && centerVisible && !(s is Scrollbar)) CheckTouchTarget(s, rt, rect, path);
                if (centerVisible && (s is Button || s is Toggle) && !IsBackdrop(rect, go.name)) _blockCandidates.Add(s);
                if (s is Button button)
                {
                    if (_cfg.checkDeadButtons) CheckDeadButton(button, path);
                    if (centerVisible) _overlapCandidates.Add(button);
                }
            }

            void CheckSafeArea(RectTransform rt, string name, string path, string what)
            {
                var rect = _ui.GetScreenRect(rt);
                if (_clip.TryGetClip(rt, out var clip))
                {
                    rect = UiAuditUtil.Intersect(rect, clip);
                    if (UiAuditUtil.IsEmpty(rect)) return;
                }

                CheckSafeAreaRect(rect, name, path, what);
            }

            void CheckSafeAreaRect(Rect rect, string name, string path, string what)
            {
                var overflow = UiAuditUtil.OverflowDistance(rect, _safe);
                if (overflow <= SAFE_AREA_TOLERANCE_PX) return;
                var px = UiAuditUtil.Num(overflow, "0");
                Collector.Add(Severity.Minor, "UI.SafeArea", $"{what} nằm ngoài vùng an toàn — {name}",
                    $"'{name}' lấn {px}px ra ngoài Screen.safeArea — tai thỏ / thanh điều hướng có thể che.", path,
                    "Nằm trong safe area", $"lấn {px}px");
            }

            void CheckTouchTarget(Selectable s, RectTransform rt, Rect screenRect, string path)
            {
                var width = screenRect.width;
                var height = screenRect.height;
                // raycastPadding dương thu nhỏ, âm nới rộng vùng nhận chạm (đơn vị local → đổi ra px màn hình).
                var graphic = s.targetGraphic != null && s.targetGraphic.gameObject == s.gameObject
                    ? s.targetGraphic
                    : s.GetComponent<Graphic>();
                var local = rt.rect;
                if (graphic != null && local.width > 0f && local.height > 0f)
                {
                    var pad = graphic.raycastPadding;
                    width -= (pad.x + pad.z) * (screenRect.width / local.width);
                    height -= (pad.y + pad.w) * (screenRect.height / local.height);
                }

                var minDp = Mathf.Min(width, height) / _pxPerDp;
                if (minDp >= _cfg.minTouchTargetDp) return;
                // Gộp thành một issue mỗi màn (liệt kê từng nút) — tránh hàng chục issue giống nhau.
                _smallestDp = Mathf.Min(_smallestDp, minDp);
                _smallTargets.Add($"{s.name} ≈ {UiAuditUtil.Num(minDp, "0")} dp ({width:0}×{height:0}px) — {path}");
            }

            void FlushSmallTargets()
            {
                if (_smallTargets.Count == 0) return;
                var min = UiAuditUtil.Num(_cfg.minTouchTargetDp, "0");
                var shown = _smallTargets.Take(MAX_SMALL_TARGETS_LISTED).ToList();
                var more = _smallTargets.Count - shown.Count;
                Collector.Add(Severity.Minor, "UI.TouchTarget",
                    $"{_smallTargets.Count} vùng bấm nhỏ hơn {min} dp",
                    "Dễ bấm trượt trên điện thoại:\n- " + string.Join("\n- ", shown) +
                    (more > 0 ? $"\n… và {more} nút khác" : ""), null, $"≥ {min} dp",
                    $"nhỏ nhất ≈ {UiAuditUtil.Num(_smallestDp, "0")} dp");
            }

            bool IsBackdrop(Rect rect, string objectName)
            {
                var screenArea = Mathf.Max(1f, _screen.width * _screen.height);
                return rect.width * rect.height >= screenArea * BACKDROP_SCREEN_FRACTION ||
                       BACKDROP_NAME.IsMatch(objectName ?? "");
            }

            void CheckDeadButton(Button button, string path)
            {
                if (button.onClick.GetPersistentEventCount() > 0) return;
                var runtime = UiAuditUtil.RuntimeListenerCount(button.onClick);
                if (runtime != 0) return; // > 0 có listener; < 0 không đọc được → không báo nhầm
                if (UiAuditUtil.HasOtherPointerHandlers(button.gameObject, button)) return;
                Collector.Add(Severity.Minor, "UI.DeadButton", $"Nút không gắn hành động — {button.name}",
                    "Button không có listener onClick (cả Inspector lẫn code) và không có component xử lý chạm nào khác " +
                    "— bấm vào sẽ không có gì xảy ra.", path, "Có hành động khi bấm", "onClick rỗng");
            }

            #endregion

            #region Nút bị che / chồng nhau

            async Task CheckBlocked()
            {
                if (_blockCandidates.Count == 0) return;
                if (EventSystem.current == null)
                {
                    Collector.Add(Severity.Info, "UI.NoEventSystem", "Không có EventSystem — bỏ qua kiểm tra nút bị che",
                        "Scene không có EventSystem nên không raycast được; người chơi cũng không bấm được nút nào.",
                        null);
                    return;
                }

                var pending = new List<Selectable>();
                foreach (var s in _blockCandidates)
                    if (EvaluateBlocked(s, out _, out _))
                        pending.Add(s);
                if (pending.Count == 0) return;

                // Kiểm lại sau một nhịp: bỏ qua che tạm thời (animation mở màn, toast…).
                await _ctx.WaitSeconds(BLOCKED_RECHECK_SECONDS);
                var byBlocker = new Dictionary<string, List<Selectable>>();
                var blockerNames = new Dictionary<string, string>();
                foreach (var s in pending)
                {
                    if (!EvaluateBlocked(s, out var blocker, out var noRaycastTarget)) continue;
                    var path = UiDriver.PathOf(s.transform);
                    if (noRaycastTarget)
                    {
                        Collector.Add(Severity.Major, "UI.NoRaycastTarget", $"Nút không nhận chạm — {s.name}",
                            "Không Graphic nào của nút (và con) bật Raycast Target, hoặc Canvas thiếu GraphicRaycaster " +
                            "— bấm vào không có tác dụng.", path, "Bấm được", "raycast không trúng nút");
                        continue;
                    }

                    var blockerPath = UiDriver.PathOf(blocker.transform);
                    if (!byBlocker.TryGetValue(blockerPath, out var list))
                    {
                        list = new List<Selectable>();
                        byBlocker[blockerPath] = list;
                        blockerNames[blockerPath] = blocker.name;
                    }

                    list.Add(s);
                }

                foreach (var pair in byBlocker)
                {
                    var blockerPath = pair.Key;
                    var blockerName = blockerNames[blockerPath];
                    var list = pair.Value;
                    if (list.Count >= BLOCKED_GROUP_MIN)
                    {
                        Collector.Add(Severity.Major, "UI.Blocked", $"{list.Count} nút bị che bởi '{blockerName}'",
                            $"'{blockerPath}' nằm đè lên và chặn chạm của: {ListNames(list)}.", blockerPath,
                            "Các nút bấm được", $"{list.Count} nút bị che");
                        continue;
                    }

                    foreach (var s in list)
                    {
                        if (s == null) continue;
                        Collector.Add(Severity.Major, "UI.Blocked", $"Nút bị che, không bấm được — {s.name}",
                            $"Tâm nút bị '{blockerPath}' nằm đè lên nên chạm không tới nút.", UiDriver.PathOf(s.transform),
                            "Bấm được", $"bị che bởi '{blockerName}'");
                    }
                }
            }

            bool EvaluateBlocked(Selectable s, out GameObject blocker, out bool noRaycastTarget)
            {
                blocker = null;
                noRaycastTarget = false;
                if (s == null) return false;
                var go = s.gameObject;
                if (!_ui.IsVisible(go) || !_ui.IsInteractable(go)) return false;
                if (_ui.IsClickable(go, out _)) return false;
                if (!UiAuditUtil.HasRaycastTarget(go))
                {
                    noRaycastTarget = true;
                    return true;
                }

                var top = _ui.RaycastTop(_ui.GetScreenCenter(go));
                if (top == null)
                {
                    noRaycastTarget = true;
                    return true;
                }

                // Lớp debug / cheat (stats overlay, nút tốc độ cheat…) chỉ có ở bản dev → không tính là che nút.
                for (var t = top.transform; t != null; t = t.parent)
                    if (AutoTestFilters.MatchesAny(t.name, _cfg.ignoredObjectPatterns))
                        return false;
                blocker = top;
                return true;
            }

            void CheckOverlaps()
            {
                var groups = new Dictionary<Transform, List<Button>>();
                foreach (var b in _overlapCandidates)
                {
                    if (b == null || b.transform.parent == null) continue;
                    var parent = b.transform.parent;
                    if (!groups.TryGetValue(parent, out var list))
                    {
                        list = new List<Button>();
                        groups[parent] = list;
                    }

                    if (list.Count < MAX_OVERLAP_GROUP) list.Add(b);
                }

                foreach (var list in groups.Values)
                {
                    if (list.Count < 2) continue;
                    var rects = new Rect[list.Count];
                    for (var i = 0; i < list.Count; i++)
                        rects[i] = list[i] != null && list[i].transform is RectTransform rt
                            ? _ui.GetScreenRect(rt)
                            : default;

                    for (var i = 0; i < list.Count; i++)
                    for (var j = i + 1; j < list.Count; j++)
                    {
                        var smaller = Mathf.Min(UiAuditUtil.Area(rects[i]), UiAuditUtil.Area(rects[j]));
                        if (smaller <= 0f) continue;
                        var ratio = UiAuditUtil.Area(UiAuditUtil.Intersect(rects[i], rects[j])) / smaller;
                        if (ratio <= OVERLAP_MIN_RATIO) continue;
                        // Nút vẽ trước (sibling index nhỏ hơn) bị nút sau đè lên.
                        var under = list[i].transform.GetSiblingIndex() < list[j].transform.GetSiblingIndex()
                            ? list[i]
                            : list[j];
                        var over = under == list[i] ? list[j] : list[i];
                        var percent = UiAuditUtil.Num(ratio * PERCENT, "0");
                        Collector.Add(Severity.Minor, "UI.OverlapButtons", $"Hai nút chồng lên nhau — {under.name} / {over.name}",
                            $"'{under.name}' bị '{over.name}' đè {percent}% diện tích — người chơi dễ bấm nhầm.",
                            UiDriver.PathOf(under.transform), "Các nút tách rời", $"chồng {percent}%");
                    }
                }
            }

            static string ListNames(List<Selectable> list)
            {
                var sb = new StringBuilder();
                var shown = 0;
                foreach (var s in list)
                {
                    if (s == null) continue;
                    if (shown >= GROUP_LIST_MAX)
                    {
                        sb.Append(", …");
                        break;
                    }

                    if (shown > 0) sb.Append(", ");
                    sb.Append(s.name);
                    shown++;
                }

                return sb.ToString();
            }

            #endregion
        }
    }
}
