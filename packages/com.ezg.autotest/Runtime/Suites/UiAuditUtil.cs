using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Màn hình mà một case Play đang kiểm tra: một feature đã mở qua adapter, hoặc "màn hiện tại" (mọi Canvas
    ///     gốc đang active) khi adapter không điều khiển được màn hình (project không theo template).
    /// </summary>
    public sealed class ScreenScope
    {
        /// <summary>Feature đang test; null = màn hiện tại.</summary>
        public AutoTestFeature Feature;

        /// <summary>Tên hiển thị (tên feature, hoặc "Scene &lt;tên&gt;") — dùng làm location của issue.</summary>
        public string Name;

        /// <summary>GameObject gốc của feature (null ở chế độ màn hiện tại).</summary>
        public GameObject Root;

        /// <summary>Feature đã mở sẵn trước khi case mở nó (màn nền / HUD).</summary>
        public bool WasAlreadyOpen;

        public bool IsCurrentScreen => Feature == null;

        /// <summary>Bước tái hiện đầu tiên cho QA.</summary>
        public string ReproOpenStep => IsCurrentScreen ? $"Ở màn hình đang hiển thị ({Name})" : $"Mở màn {Name}";

        /// <summary>Các gốc cần quét: root của feature, hoặc mọi Canvas gốc đang active.</summary>
        public List<GameObject> GetRoots()
        {
            var list = new List<GameObject>();
            if (!IsCurrentScreen)
            {
                if (Root != null) list.Add(Root);
                return list;
            }

            foreach (var canvas in UiAuditUtil.ActiveRootCanvases()) list.Add(canvas.gameObject);
            return list;
        }
    }

    /// <summary>
    ///     Tiện ích dùng chung cho các suite Play soi UI (UI Audit, Button Sweep, Monkey, Visual Regression): dựng
    ///     case theo màn hình, mở/đóng màn an toàn, blacklist nút, hình học màn hình, bắt log lỗi mới, gom issue.
    /// </summary>
    public static class UiAuditUtil
    {
        /// <summary>Id case khi adapter không mở được màn hình — soi UI đang hiển thị.</summary>
        public const string CURRENT_SCREEN_CASE_ID = "current-screen";

        /// <summary>Tiền tố id case theo màn: "screen-&lt;FeatureName&gt;" (giữ ổn định giữa các lượt).</summary>
        public const string SCREEN_CASE_PREFIX = "screen-";

        public const string CATEGORY_SCREEN = "Màn hình";

        /// <summary>Thời gian dành cho phần việc của một case màn hình (ngoài thời gian boot).</summary>
        const float SCREEN_CASE_WORK_SECONDS = 60f;

        const float MIN_VISIBLE_ALPHA = 0.02f;
        const float MIN_RECT_SIZE = 0.5f;

        /// <summary>1 dp = 1 px ở màn 160 dpi (chuẩn Android).</summary>
        const float DP_BASE_DPI = 160f;

        /// <summary>
        ///     Cạnh ngắn tham chiếu (dp) của điện thoại phổ thông — dùng quy đổi px→dp khi dpi không tin được (Editor:
        ///     Screen.dpi là dpi màn máy tính, không phải thiết bị).
        /// </summary>
        const float REFERENCE_SHORT_SIDE_DP = 400f;

        const int DEFAULT_EXCERPT_LENGTH = 48;
        const int DEFAULT_SHORT_PATH_SEGMENTS = 3;

        static readonly Regex RICH_TEXT_TAG = new(@"<[^<>]{1,80}>", RegexOptions.CultureInvariant);
        static readonly Regex WHITESPACE = new(@"\s+", RegexOptions.CultureInvariant);

        /// <summary>Ký hiệu tiền tệ — nút có text này gần như chắc là mua bằng tiền thật.</summary>
        static readonly Regex PRICE_SYMBOL = new(@"[$€£¥₫₩₹]|\d[\d.,]*\s?đ(?!\p{L})", RegexOptions.CultureInvariant);

        /// <summary>Mã tiền tệ ISO (phân biệt hoa thường để không khớp nhầm từ thường).</summary>
        static readonly Regex PRICE_CODE = new(@"\b(USD|VND|EUR|GBP|JPY|KRW|INR|THB|IDR|BRL|RUB|CNY|PHP|MYR|SGD)\b",
            RegexOptions.CultureInvariant);

        static readonly List<MonoBehaviour> _behaviourBuffer = new();
        static readonly List<Graphic> _graphicBuffer = new();
        static FieldInfo _callsField;
        static bool _callsFieldResolved;
        static Type _callListType;
        static FieldInfo _runtimeCallsField;
        static PropertyInfo _callCountProperty;

        #region Dựng case theo màn hình

        /// <summary>Adapter có mở/đóng được màn hình không.</summary>
        public static bool SupportsFeatures(IGameAdapter game)
        {
            return game != null && (game.Capabilities & AdapterCapabilities.Features) != 0;
        }

        /// <summary>
        ///     Một case mỗi màn hình test được ("screen-&lt;Feature&gt;"); adapter không hỗ trợ màn hình ⇒ một case
        ///     "current-screen" soi UI đang hiển thị.
        /// </summary>
        /// <param name="describe">Mô tả case theo feature (null = màn hiện tại).</param>
        /// <param name="body">Nội dung case, chạy sau khi màn đã mở + settle.</param>
        /// <param name="timeoutSeconds">0 = tự tính theo thời gian boot + phần việc màn hình.</param>
        /// <param name="extraSettleSeconds">Chờ thêm sau settle mặc định (vd chụp ảnh cần UI đứng yên hẳn).</param>
        /// <param name="capOpenErrors">
        ///     Exception/error log lúc mở màn chỉ tính Minor (Smoke đã báo nặng) — dùng cho suite phụ (UI Audit, Visual).
        /// </param>
        public static List<AutoTestCase> BuildScreenCases(AutoTestBuildContext bctx, Func<AutoTestFeature, string> describe,
            Func<AutoTestContext, ScreenScope, Task> body, float timeoutSeconds = 0, float extraSettleSeconds = 0,
            bool capOpenErrors = false)
        {
            if (capOpenErrors)
            {
                var inner = body;
                body = (ctx, scope) =>
                {
                    ctx.LogSeverityCap = Severity.Minor;
                    return inner(ctx, scope);
                };
            }

            var config = bctx?.Config ?? new AutoTestConfig();
            var timeout = timeoutSeconds > 0 ? timeoutSeconds : ScreenCaseTimeout(config);
            var cases = new List<AutoTestCase>();
            if (!SupportsFeatures(bctx?.Game))
            {
                cases.Add(new AutoTestCase(CURRENT_SCREEN_CASE_ID, "Màn hình hiện tại", describe(null),
                    ctx => RunOnScreen(ctx, null, body, extraSettleSeconds), CATEGORY_SCREEN, timeout));
                return cases;
            }

            foreach (var f in GameFlow.TestableFeatures(config, bctx.Game, SafeCreateHooks()))
            {
                var feature = f;
                cases.Add(new AutoTestCase(SCREEN_CASE_PREFIX + feature.Name, feature.Name, describe(feature),
                    ctx => RunOnScreen(ctx, feature, body, extraSettleSeconds), CATEGORY_SCREEN, timeout));
            }

            return cases;
        }

        /// <summary>Timeout mặc định của case màn hình: đủ cho boot (case đầu phiên) + phần việc.</summary>
        public static float ScreenCaseTimeout(AutoTestConfig config, float workSeconds = SCREEN_CASE_WORK_SECONDS)
        {
            var general = config.general;
            return Mathf.Max(general.caseTimeoutSeconds,
                general.bootTimeoutSeconds + general.settleSecondsAfterBoot + workSeconds);
        }

        /// <summary>
        ///     Khung chuẩn của case màn hình: EnsureReady → mở màn (mở hỏng ⇒ Skip, Smoke đã báo) → body → luôn đóng màn
        ///     + về màn nền.
        /// </summary>
        public static async Task RunOnScreen(AutoTestContext ctx, AutoTestFeature feature,
            Func<AutoTestContext, ScreenScope, Task> body, float extraSettleSeconds)
        {
            await GameFlow.EnsureReady(ctx);
            var scope = new ScreenScope();
            try
            {
                if (feature != null)
                {
                    if (!SupportsFeatures(ctx.Game)) ctx.Skip("Adapter hiện tại không mở được màn hình.");
                    // Feature lấy lúc liệt kê có thể thuộc adapter/domain cũ → resolve lại theo tên.
                    var live = ResolveFeature(ctx.Game, feature.Name) ?? feature;
                    scope.Feature = live;
                    scope.Name = live.Name;
                    GameFlow.OpenResult open;
                    using (ctx.Step($"Mở màn {live.Name}"))
                    {
                        open = await GameFlow.OpenFeature(ctx, live,
                            ctx.Config.smoke.screenSettleSeconds + Mathf.Max(0f, extraSettleSeconds));
                    }

                    if (open.Error != null) ctx.Skip($"Không mở được: {open.Error}");
                    scope.Root = open.Root;
                    scope.WasAlreadyOpen = open.WasAlreadyOpen;
                }
                else
                {
                    scope.Name = CurrentScreenName();
                    if (extraSettleSeconds > 0) await ctx.WaitSeconds(extraSettleSeconds);
                }

                await body(ctx, scope);
            }
            finally
            {
                if (scope.Feature != null) await GameFlow.CloseFeature(ctx, scope.Feature);
                await GameFlow.ReturnToBaseline(ctx);
            }
        }

        /// <summary>Tìm feature theo tên trong adapter đang chạy.</summary>
        public static AutoTestFeature ResolveFeature(IGameAdapter game, string name)
        {
            if (game == null || string.IsNullOrEmpty(name)) return null;
            try
            {
                foreach (var f in game.GetFeatures())
                    if (f != null && string.Equals(f.Name, name, StringComparison.Ordinal))
                        return f;
            }
            catch (Exception e)
            {
                AutoTestLog.Warn("Không đọc được danh sách feature: " + e.Message);
            }

            return null;
        }

        /// <summary>Tên "màn hiện tại" = scene đang active.</summary>
        public static string CurrentScreenName()
        {
            return "Scene " + SceneManager.GetActiveScene().name;
        }

        static IAutoTestProjectHooks SafeCreateHooks()
        {
            try
            {
                return AutoTestRegistry.CreateHooks();
            }
            catch (Exception e)
            {
                AutoTestLog.Warn("Không tạo được hooks khi liệt kê case: " + e.Message);
                return null;
            }
        }

        #endregion

        #region Trạng thái game / UI

        /// <summary>Mọi Canvas gốc đang active, sắp theo sortingOrder rồi tên (thứ tự ổn định).</summary>
        public static List<Canvas> ActiveRootCanvases()
        {
            var list = new List<Canvas>();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (c != null && c.isRootCanvas && c.isActiveAndEnabled)
                    list.Add(c);
            list.Sort((a, b) =>
            {
                var order = a.sortingOrder.CompareTo(b.sortingOrder);
                return order != 0 ? order : string.CompareOrdinal(a.name, b.name);
            });
            return list;
        }

        /// <summary>Game sẵn sàng chưa (hooks ưu tiên, không thì adapter). Không ném exception.</summary>
        public static bool IsGameReady(AutoTestContext ctx)
        {
            try
            {
                var hook = ctx.Hooks?.IsGameReady();
                return hook ?? ctx.Game.IsGameReady();
            }
            catch (Exception e)
            {
                AutoTestLog.Warn("Kiểm tra ready lỗi: " + e.Message);
                return false;
            }
        }

        /// <summary>Tên các feature đang mở (rỗng nếu adapter không hỗ trợ màn hình).</summary>
        public static List<AutoTestFeature> OpenFeatures(AutoTestContext ctx)
        {
            var list = new List<AutoTestFeature>();
            if (!SupportsFeatures(ctx.Game)) return list;
            try
            {
                foreach (var f in ctx.Game.GetOpenFeatures())
                    if (f != null)
                        list.Add(f);
            }
            catch (Exception e)
            {
                AutoTestLog.Warn("Không đọc được màn đang mở: " + e.Message);
            }

            return list;
        }

        /// <summary>Graphic đang thực sự hiện: active, alpha (màu × CanvasRenderer × CanvasGroup) đủ lớn, không bị cull.</summary>
        public static bool IsGraphicVisible(UiDriver ui, Graphic g)
        {
            if (g == null || !g.isActiveAndEnabled) return false;
            var cr = g.canvasRenderer;
            if (cr == null || cr.cull) return false;
            if (g.color.a * cr.GetAlpha() < MIN_VISIBLE_ALPHA) return false;
            return ui.IsVisible(g.gameObject);
        }

        /// <summary>Có Graphic nào (chính nó hoặc con) nhận raycast không.</summary>
        public static bool HasRaycastTarget(GameObject go)
        {
            if (go == null) return false;
            go.GetComponentsInChildren(false, _graphicBuffer);
            try
            {
                foreach (var g in _graphicBuffer)
                    if (g != null && g.raycastTarget && g.isActiveAndEnabled)
                        return true;
                return false;
            }
            finally
            {
                _graphicBuffer.Clear();
            }
        }

        /// <summary>
        ///     Số px mỗi dp. Device: Screen.dpi / 160. Editor (dpi là của màn máy tính) hoặc dpi = 0: quy theo cạnh ngắn
        ///     màn hình ≈ <see cref="REFERENCE_SHORT_SIDE_DP" /> dp.
        /// </summary>
        public static float PixelsPerDp()
        {
            var dpi = Screen.dpi;
            if (!Application.isEditor && dpi > 0) return dpi / DP_BASE_DPI;
            var shortSide = Mathf.Min(Screen.width, Screen.height);
            return shortSide > 0 ? shortSide / REFERENCE_SHORT_SIDE_DP : 1f;
        }

        #endregion

        #region Nút

        /// <summary>
        ///     Nút KHÔNG được bấm: tên hoặc text khớp blacklist, hoặc text mang giá tiền thật (ký hiệu/mã tiền tệ).
        /// </summary>
        public static bool IsBlacklisted(UiDriver ui, GameObject go, IEnumerable<string> patterns, out string reason)
        {
            reason = null;
            if (go == null) return false;
            if (AutoTestFilters.MatchesAny(go.name, patterns))
            {
                reason = $"tên '{go.name}' khớp blacklist";
                return true;
            }

            var text = ui.GetText(go);
            if (string.IsNullOrWhiteSpace(text)) return false;
            var plain = StripRichText(text).Trim();
            if (AutoTestFilters.MatchesAny(plain, patterns))
            {
                reason = $"text '{Excerpt(plain)}' khớp blacklist";
                return true;
            }

            if (PRICE_SYMBOL.IsMatch(plain) || PRICE_CODE.IsMatch(plain))
            {
                reason = $"text '{Excerpt(plain)}' giống giá tiền thật";
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Số listener thêm bằng code (AddListener) của UnityEvent. -1 = không đọc được (reflection đổi theo bản
        ///     Unity) ⇒ người gọi bỏ qua kiểm tra.
        /// </summary>
        public static int RuntimeListenerCount(UnityEventBase evt)
        {
            if (evt == null) return -1;
            try
            {
                if (!_callsFieldResolved)
                {
                    _callsFieldResolved = true;
                    _callsField = typeof(UnityEventBase).GetField("m_Calls",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                }

                if (_callsField == null) return -1;
                var calls = _callsField.GetValue(evt);
                if (calls == null) return 0;
                var type = calls.GetType();
                if (_callListType != type)
                {
                    _callListType = type;
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                    _runtimeCallsField = type.GetField("m_RuntimeCalls", flags);
                    _callCountProperty = type.GetProperty("Count", flags);
                }

                if (_runtimeCallsField != null && _runtimeCallsField.GetValue(calls) is ICollection runtime)
                    return runtime.Count;
                if (_callCountProperty != null && _callCountProperty.GetValue(calls) is int count) return count;
            }
            catch (Exception)
            {
                // Reflection hỏng → coi như không biết, không báo nhầm.
            }

            return -1;
        }

        /// <summary>
        ///     GameObject có component xử lý chạm khác ngoài chính Selectable (EventTrigger, script nhấn giữ, hiệu ứng
        ///     bấm…) — khi đó nút không "chết" dù onClick rỗng.
        /// </summary>
        public static bool HasOtherPointerHandlers(GameObject go, Component self)
        {
            if (go == null) return false;
            go.GetComponents(_behaviourBuffer);
            try
            {
                foreach (var mb in _behaviourBuffer)
                {
                    if (mb == null || mb == self || !mb.enabled) continue;
                    if (mb is IPointerClickHandler || mb is IPointerDownHandler || mb is IPointerUpHandler ||
                        mb is ISubmitHandler)
                        return true;
                }

                return false;
            }
            finally
            {
                _behaviourBuffer.Clear();
            }
        }

        #endregion

        #region Log lỗi

        /// <summary>
        ///     Log Error/Exception mới từ <paramref name="cursor" /> (bỏ log trong ignoredLogPatterns + log policy
        ///     Ignore); cập nhật cursor tới sau dòng cuối đã đọc.
        /// </summary>
        public static List<AutoTestLogCapture.Entry> NewErrors(AutoTestConfig config, ref long cursor)
        {
            var result = new List<AutoTestLogCapture.Entry>();
            foreach (var e in AutoTestLogCapture.Since(cursor))
            {
                if (e.Index >= cursor) cursor = e.Index + 1;
                if (!AutoTestLogCapture.IsErrorType(e.Type)) continue;
                if (AutoTestLogCapture.IsIgnored(e.Message ?? "")) continue;
                if (ErrorSeverity(config, e.Type) == null) continue;
                result.Add(e);
            }

            return result;
        }

        /// <summary>Severity của một log lỗi theo policy trong settings; null = policy Ignore.</summary>
        public static Severity? ErrorSeverity(AutoTestConfig config, LogType type)
        {
            var isException = type == LogType.Exception;
            var policy = isException ? config.general.exceptionPolicy : config.general.errorLogPolicy;
            switch (policy)
            {
                case LogErrorPolicy.Ignore: return null;
                case LogErrorPolicy.WarnCase: return Severity.Minor;
                default: return isException ? Severity.Critical : Severity.Major;
            }
        }

        #endregion

        #region Hình học

        /// <summary>Giao của hai rect (rỗng ⇒ rect kích thước 0).</summary>
        public static Rect Intersect(Rect a, Rect b)
        {
            var xMin = Mathf.Max(a.xMin, b.xMin);
            var yMin = Mathf.Max(a.yMin, b.yMin);
            var xMax = Mathf.Min(a.xMax, b.xMax);
            var yMax = Mathf.Min(a.yMax, b.yMax);
            if (xMax <= xMin || yMax <= yMin) return new Rect(xMin, yMin, 0f, 0f);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        public static float Area(Rect r)
        {
            return Mathf.Max(0f, r.width) * Mathf.Max(0f, r.height);
        }

        public static bool IsEmpty(Rect r)
        {
            return r.width <= MIN_RECT_SIZE || r.height <= MIN_RECT_SIZE;
        }

        /// <summary>Tỉ lệ diện tích (0–1) của <paramref name="r" /> nằm ngoài <paramref name="bounds" />.</summary>
        public static float OutsideFraction(Rect r, Rect bounds)
        {
            var area = Area(r);
            if (area <= 0f) return 0f;
            return 1f - Area(Intersect(r, bounds)) / area;
        }

        /// <summary>Khoảng lấn (px) lớn nhất của <paramref name="r" /> ra ngoài <paramref name="bounds" />.</summary>
        public static float OverflowDistance(Rect r, Rect bounds)
        {
            return Mathf.Max(Mathf.Max(bounds.xMin - r.xMin, r.xMax - bounds.xMax),
                Mathf.Max(bounds.yMin - r.yMin, r.yMax - bounds.yMax));
        }

        #endregion

        #region Chuỗi

        /// <summary>Bỏ thẻ rich text (&lt;color&gt;, &lt;b&gt;, &lt;sprite&gt;…).</summary>
        public static string StripRichText(string s)
        {
            return string.IsNullOrEmpty(s) ? s ?? "" : RICH_TEXT_TAG.Replace(s, "");
        }

        /// <summary>Trích ngắn một dòng để đưa vào title/message.</summary>
        public static string Excerpt(string s, int max = DEFAULT_EXCERPT_LENGTH)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var one = WHITESPACE.Replace(s, " ").Trim();
            return one.Length <= max ? one : one.Substring(0, max) + "…";
        }

        public static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var i = s.IndexOf('\n');
            return (i < 0 ? s : s.Substring(0, i)).TrimEnd('\r');
        }

        /// <summary>Vài đoạn cuối của đường dẫn hierarchy ("…/Panel/btn_ok").</summary>
        public static string ShortPath(string path, int segments = DEFAULT_SHORT_PATH_SEGMENTS)
        {
            if (string.IsNullOrEmpty(path)) return "";
            var parts = path.Split('/');
            if (parts.Length <= segments) return path;
            return "…/" + string.Join("/", parts, parts.Length - segments, segments);
        }

        /// <summary>Số dạng invariant (dấu chấm thập phân) — report đọc giống nhau trên mọi máy.</summary>
        public static string Num(double value, string format = "0.##")
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        #endregion

        #region Report

        /// <summary>Ghi số đo KHÔNG tự sinh issue khi vượt ngưỡng (suite tự báo issue có ngữ cảnh rõ hơn).</summary>
        public static TestMetric AddMetric(AutoTestContext ctx, string name, double value, string unit = "",
            double? max = null)
        {
            var metric = new TestMetric
            {
                name = name,
                value = Math.Round(value, 3),
                unit = unit,
                hasMax = max.HasValue,
                max = max ?? 0,
                passed = !max.HasValue || value <= max.Value
            };
            ctx.Result.metrics.Add(metric);
            return metric;
        }

        /// <summary>Đính kèm ảnh đã lưu trong thư mục report (đường dẫn tương đối).</summary>
        public static void AttachImage(AutoTestContext ctx, string label, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return;
            ctx.Result.attachments.Add(new TestAttachment { label = label, path = relativePath, kind = "image" });
        }

        #endregion
    }

    /// <summary>So tên object + các cha (tới gốc quét) với danh sách regex, có cache theo Transform.</summary>
    public sealed class AncestorNameMatcher
    {
        readonly List<string> _patterns;
        readonly Dictionary<Transform, bool> _cache = new();

        public AncestorNameMatcher(IEnumerable<string> patterns)
        {
            _patterns = new List<string>();
            if (patterns == null) return;
            foreach (var p in patterns)
                if (!string.IsNullOrWhiteSpace(p))
                    _patterns.Add(p);
        }

        public bool IsEmpty => _patterns.Count == 0;

        /// <summary>True nếu <paramref name="t" /> hoặc một cha (dừng ở <paramref name="stopAt" />, tính cả nó) khớp.</summary>
        public bool Matches(Transform t, Transform stopAt)
        {
            if (t == null || _patterns.Count == 0) return false;
            if (_cache.TryGetValue(t, out var cached)) return cached;
            var result = AutoTestFilters.MatchesAny(t.name, _patterns) ||
                         (t != stopAt && t.parent != null && Matches(t.parent, stopAt));
            _cache[t] = result;
            return result;
        }
    }

    /// <summary>
    ///     Vùng cắt (RectMask2D / Mask) áp lên một object, tính bằng pixel màn hình, có cache — để không báo nhầm
    ///     phần tử trong list cuộn bị che bởi viewport.
    /// </summary>
    public sealed class ClipCache
    {
        struct ClipInfo
        {
            public bool Has;
            public Rect Rect;
        }

        readonly UiDriver _ui;
        readonly Dictionary<Transform, ClipInfo> _cache = new();

        public ClipCache(UiDriver ui)
        {
            _ui = ui;
        }

        /// <summary>Vùng cắt của các cha của <paramref name="t" />; false nếu không có cha nào cắt.</summary>
        public bool TryGetClip(Transform t, out Rect clip)
        {
            var info = t != null ? ClipOf(t.parent) : default;
            clip = info.Rect;
            return info.Has;
        }

        ClipInfo ClipOf(Transform t)
        {
            if (t == null) return default;
            if (_cache.TryGetValue(t, out var cached)) return cached;
            var canvas = t.GetComponent<Canvas>();
            var result = canvas != null && canvas.isRootCanvas ? default : ClipOf(t.parent);
            if (t is RectTransform rt && IsClipper(t))
            {
                var r = _ui.GetScreenRect(rt);
                result = new ClipInfo { Has = true, Rect = result.Has ? UiAuditUtil.Intersect(result.Rect, r) : r };
            }

            _cache[t] = result;
            return result;
        }

        static bool IsClipper(Transform t)
        {
            var rectMask = t.GetComponent<RectMask2D>();
            if (rectMask != null && rectMask.isActiveAndEnabled) return true;
            var mask = t.GetComponent<Mask>();
            return mask != null && mask.MaskEnabled();
        }
    }

    /// <summary>
    ///     Gom phát hiện UI của một màn: chống trùng theo (loại lỗi, object), sắp theo severity, giới hạn số issue
    ///     mỗi màn (phần dư gộp thành một dòng Info) và gắn chung một ảnh chụp màn.
    /// </summary>
    public sealed class UiIssueCollector
    {
        const int MAX_SUMMARY_GROUPS = 10;

        sealed class Finding
        {
            public int Order;
            public Severity Severity;
            public string Category;
            public string Title;
            public string Message;
            public string ObjectPath;
            public string Expected;
            public string Actual;
        }

        readonly List<Finding> _findings = new();
        readonly HashSet<string> _keys = new();

        public int Count => _findings.Count;

        /// <summary>Thêm một phát hiện; false nếu object đã có lỗi cùng loại.</summary>
        public bool Add(Severity severity, string category, string title, string message, string objectPath,
            string expected = null, string actual = null)
        {
            if (!_keys.Add(category + "|" + objectPath)) return false;
            _findings.Add(new Finding
            {
                Order = _findings.Count,
                Severity = severity,
                Category = category,
                Title = title,
                Message = message,
                ObjectPath = objectPath,
                Expected = expected,
                Actual = actual
            });
            return true;
        }

        /// <summary>Ghi các phát hiện thành issue của case (nặng trước). Trả tổng số phát hiện.</summary>
        public int Flush(AutoTestContext ctx, ScreenScope scope, string screenshot, int maxIssues)
        {
            _findings.Sort((a, b) =>
            {
                var bySeverity = b.Severity.CompareTo(a.Severity);
                return bySeverity != 0 ? bySeverity : a.Order.CompareTo(b.Order);
            });

            var reported = 0;
            foreach (var f in _findings)
            {
                if (reported >= maxIssues) break;
                var steps = $"1. {scope.ReproOpenStep}\n2. Chờ màn hiển thị xong\n3. Xem object '{UiAuditUtil.ShortPath(f.ObjectPath)}'";
                var issue = ctx.Report(f.Severity, f.Category, f.Title, f.Message, scope.Name, f.ObjectPath,
                    f.Expected, f.Actual, steps);
                issue.screenshot = screenshot;
                reported++;
            }

            var remaining = _findings.Count - reported;
            if (remaining > 0)
            {
                var byCategory = new Dictionary<string, int>();
                for (var i = reported; i < _findings.Count; i++)
                {
                    var c = _findings[i].Category;
                    byCategory[c] = byCategory.TryGetValue(c, out var n) ? n + 1 : 1;
                }

                var sb = new StringBuilder();
                var groups = 0;
                foreach (var pair in byCategory)
                {
                    if (groups++ >= MAX_SUMMARY_GROUPS) break;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(pair.Key).Append(" ×").Append(pair.Value);
                }

                var summary = ctx.Report(Severity.Info, "UI.Summary",
                    $"Còn {remaining} vấn đề khác bị lược bớt",
                    $"Chỉ hiện {maxIssues} vấn đề nặng nhất mỗi màn. Phần còn lại: {sb}.", scope.Name);
                summary.screenshot = screenshot;
            }

            return _findings.Count;
        }
    }
}
