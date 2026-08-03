using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace EZG.StatsOverlay
{
    /// <summary>
    /// Panel overlay vẽ số liệu runtime, luôn nằm trên cùng mọi UI game (canvas riêng, sorting order rất cao).
    ///
    /// KHÔNG có <see cref="GraphicRaycaster"/> và mọi Graphic đều <c>raycastTarget = false</c> ⇒ overlay
    /// KHÔNG bao giờ nuốt input của game. Kéo thả tự xử lý bằng cách đọc <c>Input</c> và test điểm chạm
    /// trong rect của panel.
    ///
    /// Đừng tạo tay component này — vào qua <see cref="StatsOverlay.Install"/> (bootstrap tự gọi).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StatsOverlayView : MonoBehaviour
    {
        /// <summary>Ngưỡng di chuyển (pixel) để phân biệt kéo thả với tap thu gọn.</summary>
        private const float DragThresholdPixels = 10f;

        private readonly StringBuilder _sb = new StringBuilder(768);
        private readonly StatsProbe _probe = new StatsProbe();

        private Canvas _canvas;
        private RectTransform _canvasRect;
        private RectTransform _panel;
        private Text _headerText;
        private Text _bodyText;
        private GameObject _bodyGo;
        private Image _background;

        private bool _visible;
        private bool _collapsed;
        private bool _userMoved;
        private float _refreshTimer;
        private Vector2Int _lastScreenSize;

        private bool _pressing;
        private bool _dragged;
        private Vector2 _pressScreenPos;
        private Vector2 _pressAnchoredPos;

        private static StatsOverlayConfig Config => StatsOverlay.Config;

        private void Awake()
        {
            BuildUi();
            _collapsed = Config.StartCollapsed;
            ApplyCollapsed();
            SetVisible(false, force: true);
        }

        private void OnDestroy()
        {
            _probe.Dispose();
        }

        private void Update()
        {
            bool shouldShow = StatsOverlay.ShouldBeVisible();
            if (shouldShow != _visible) SetVisible(shouldShow);
            if (!_visible) return;

            _probe.Tick(Time.unscaledDeltaTime);
            HandleInput();
            HandleScreenSizeChange();

            _refreshTimer += Time.unscaledDeltaTime;
            float interval = Mathf.Max(0.05f, Config.RefreshInterval);
            if (_refreshTimer < interval) return;

            _refreshTimer = 0f;
            _probe.CommitWindow();
            Refresh();
        }

        /// <summary>Dựng lại toàn bộ theo <see cref="StatsOverlay.Config"/> hiện tại (gọi từ ApplyConfig).</summary>
        internal void RebuildFromConfig()
        {
            if (_canvas != null) _canvas.sortingOrder = Config.SortingOrder;

            var scaler = GetComponent<CanvasScaler>();
            if (scaler != null) scaler.referenceResolution = Config.ReferenceResolution;

            if (_background != null) _background.color = Config.BackgroundColor;

            if (_headerText != null)
            {
                _headerText.fontSize = Config.FontSize;
                _headerText.color = Config.HeaderColor;
            }

            if (_bodyText != null)
            {
                _bodyText.fontSize = Config.FontSize;
                _bodyText.color = Config.TextColor;
            }

            _userMoved = false;
            if (_visible) Refresh();
            ApplyDefaultPosition();
        }

        internal void SetCollapsed(bool collapsed)
        {
            if (_collapsed == collapsed) return;
            _collapsed = collapsed;
            ApplyCollapsed();
        }

        internal bool IsCollapsed => _collapsed;

        // ------------------------------------------------------------------ UI

        private void BuildUi()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = Config.SortingOrder;
            _canvasRect = (RectTransform)_canvas.transform;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Config.ReferenceResolution;
            // Khớp theo CHIỀU CAO: game khổ dọc, bám chiều cao thì cỡ chữ ổn định khi đổi tỉ lệ ngang.
            scaler.matchWidthOrHeight = 1f;

            // KHÔNG add GraphicRaycaster: overlay chỉ để nhìn, không nhận sự kiện EventSystem.

            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panelGo.transform.SetParent(_canvas.transform, false);

            _panel = (RectTransform)panelGo.transform;
            // Neo cố định góc trên-trái để toán vị trí/kẹp biên chỉ có 1 hệ quy chiếu duy nhất.
            _panel.anchorMin = new Vector2(0f, 1f);
            _panel.anchorMax = new Vector2(0f, 1f);
            _panel.pivot = new Vector2(0f, 1f);

            _background = panelGo.GetComponent<Image>();
            _background.color = Config.BackgroundColor;
            _background.raycastTarget = false;

            var layout = panelGo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 12, 12);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = panelGo.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Font font = ResolveFont();
            _headerText = CreateText(panelGo.transform, "Header", font, Config.HeaderColor);
            _bodyText = CreateText(panelGo.transform, "Body", font, Config.TextColor);
            _bodyGo = _bodyText.gameObject;

            _headerText.text = "EZG STATS";
            _bodyText.text = string.Empty;
        }

        private static Text CreateText(Transform parent, string name, Font font, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = Config.FontSize;
            text.color = color;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.lineSpacing = 1.05f;
            return text;
        }

        /// <summary>
        /// Font builtin: Unity 6 bỏ "Arial.ttf" (gọi là NÉM exception, không phải trả null) — phải dùng
        /// "LegacyRuntime.ttf". Vẫn thử lần lượt + fallback font hệ điều hành cho bản Unity cũ hơn.
        /// </summary>
        private static Font ResolveFont()
        {
            Font font = TryBuiltinFont("LegacyRuntime.ttf") ?? TryBuiltinFont("Arial.ttf");
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Courier New", 16);
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 16);
            return font;
        }

        private static Font TryBuiltinFont(string path)
        {
            try { return Resources.GetBuiltinResource<Font>(path); }
            catch { return null; }
        }

        // ------------------------------------------------------- hiển thị / vị trí

        private void SetVisible(bool visible, bool force = false)
        {
            if (!force && _visible == visible) return;
            _visible = visible;

            if (_panel != null) _panel.gameObject.SetActive(visible);

            if (visible)
            {
                // Chỉ tạo ProfilerRecorder khi thật sự hiện → tắt cheat là không tốn gì.
                _probe.Start();
                _refreshTimer = 0f;
                _userMoved = false;
                Refresh();
                ApplyDefaultPosition();
            }
            else
            {
                _probe.Stop();
                _pressing = false;
                _dragged = false;
            }
        }

        private void ApplyCollapsed()
        {
            if (_bodyGo != null) _bodyGo.SetActive(!_collapsed);
            if (_visible) ApplyPositionAfterResize();
        }

        private void HandleScreenSizeChange()
        {
            var size = new Vector2Int(Screen.width, Screen.height);
            if (size == _lastScreenSize) return;
            _lastScreenSize = size;
            ApplyPositionAfterResize();
        }

        /// <summary>Panel đổi kích thước/màn hình xoay: chưa kéo thì bám lại góc mặc định, đã kéo thì kẹp trong màn.</summary>
        private void ApplyPositionAfterResize()
        {
            if (_userMoved) ClampIntoScreen();
            else ApplyDefaultPosition();
        }

        private void ApplyDefaultPosition()
        {
            if (_panel == null || _canvasRect == null) return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(_panel);

            Vector2 canvasSize = _canvasRect.rect.size;
            Vector2 panelSize = _panel.rect.size;
            float scale = Mathf.Approximately(_canvas.scaleFactor, 0f) ? 1f : _canvas.scaleFactor;

            // Safe area (pixel) -> đơn vị canvas.
            float safeLeft = 0f, safeRight = 0f, safeTop = 0f, safeBottom = 0f;
            if (Config.RespectSafeArea)
            {
                Rect safe = Screen.safeArea;
                safeLeft = safe.xMin / scale;
                safeRight = (Screen.width - safe.xMax) / scale;
                safeBottom = safe.yMin / scale;
                safeTop = (Screen.height - safe.yMax) / scale;
            }

            float left = Config.Margin.x + safeLeft;
            float right = canvasSize.x - panelSize.x - Config.Margin.x - safeRight;
            float top = -(Config.Margin.y + safeTop);
            float bottom = -(canvasSize.y - panelSize.y - Config.Margin.y - safeBottom);

            Vector2 pos;
            switch (Config.Corner)
            {
                case StatsOverlayCorner.TopRight: pos = new Vector2(right, top); break;
                case StatsOverlayCorner.BottomLeft: pos = new Vector2(left, bottom); break;
                case StatsOverlayCorner.BottomRight: pos = new Vector2(right, bottom); break;
                default: pos = new Vector2(left, top); break;
            }

            _panel.anchoredPosition = pos;
            ClampIntoScreen();
        }

        private void ClampIntoScreen()
        {
            if (_panel == null || _canvasRect == null) return;

            Vector2 canvasSize = _canvasRect.rect.size;
            Vector2 panelSize = _panel.rect.size;
            Vector2 pos = _panel.anchoredPosition;

            float maxX = Mathf.Max(0f, canvasSize.x - panelSize.x);
            float minY = -Mathf.Max(0f, canvasSize.y - panelSize.y);

            pos.x = Mathf.Clamp(pos.x, 0f, maxX);
            pos.y = Mathf.Clamp(pos.y, minY, 0f);
            _panel.anchoredPosition = pos;
        }

        // ------------------------------------------------------------- input

        private void HandleInput()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!Config.Draggable || _panel == null) return;

            if (Input.GetMouseButtonDown(0))
            {
                Vector2 screenPos = Input.mousePosition;
                // Overlay ở ScreenSpaceOverlay ⇒ camera = null.
                if (!RectTransformUtility.RectangleContainsScreenPoint(_panel, screenPos, null)) return;

                _pressing = true;
                _dragged = false;
                _pressScreenPos = screenPos;
                _pressAnchoredPos = _panel.anchoredPosition;
                return;
            }

            if (!_pressing) return;

            if (Input.GetMouseButton(0))
            {
                Vector2 delta = (Vector2)Input.mousePosition - _pressScreenPos;
                if (!_dragged && delta.magnitude < DragThresholdPixels) return;

                _dragged = true;
                _userMoved = true;
                float scale = Mathf.Approximately(_canvas.scaleFactor, 0f) ? 1f : _canvas.scaleFactor;
                _panel.anchoredPosition = _pressAnchoredPos + delta / scale;
                ClampIntoScreen();
                return;
            }

            if (Input.GetMouseButtonUp(0))
            {
                // Chạm rồi nhả mà không kéo = tap → thu gọn/mở rộng.
                if (!_dragged) SetCollapsed(!_collapsed);
                _pressing = false;
                _dragged = false;
            }
#endif
        }

        // ------------------------------------------------------------ nội dung

        private void Refresh()
        {
            if (_headerText == null || _bodyText == null) return;

            _sb.Clear();
            _sb.Append("EZG STATS   ")
               .Append(_probe.Fps.ToString("0.0", CultureInfo.InvariantCulture)).Append(" FPS   ")
               .Append(_probe.FrameMs.ToString("0.0", CultureInfo.InvariantCulture)).Append(" ms   ")
               .Append(_collapsed ? "[+]" : "[-]");
            _headerText.text = _sb.ToString();

            if (_collapsed)
            {
                _bodyText.text = string.Empty;
                ApplyPositionAfterResize();
                return;
            }

            _sb.Clear();

            if (Config.ShowGraphics)
            {
                _sb.Append("GRAPHICS").AppendLine();
                _sb.Append("Frame  avg ").Append(Ms(_probe.FrameMs))
                   .Append("   worst ").Append(Ms(_probe.WorstFrameMs))
                   .Append("   best ").Append(Ms(_probe.BestFrameMs)).AppendLine();
                _sb.Append("CPU  main ").Append(Ms(_probe.CpuMainMs))
                   .Append("   render ").Append(Ms(_probe.CpuRenderMs))
                   .Append("   GPU ").Append(Ms(_probe.GpuMs)).AppendLine();
                _sb.Append("Batches ").Append(Count(_probe.Batches))
                   .Append("   saved by batching ").Append(Count(_probe.SavedByBatching)).AppendLine();
                _sb.Append("SetPass ").Append(Count(_probe.SetPassCalls))
                   .Append("   draw calls ").Append(Count(_probe.DrawCalls)).AppendLine();
                _sb.Append("Tris ").Append(Short(_probe.Triangles))
                   .Append("   verts ").Append(Short(_probe.Vertices)).AppendLine();
                _sb.Append("Shadow casters ").Append(Count(_probe.ShadowCasters))
                   .Append("   skinned ").Append(Count(_probe.SkinnedMeshes)).AppendLine();
                _sb.Append("Screen ").Append(Screen.width).Append('x').Append(Screen.height)
                   .Append(" - ").Append(Mb(StatsProbe.ScreenBytes)).AppendLine();
            }

            if (Config.ShowMemory)
            {
                if (Config.ShowGraphics) _sb.AppendLine();
                _sb.Append("MEMORY").AppendLine();
                _sb.Append("System used ").Append(Mb(_probe.SystemUsedMemory)).AppendLine();
                _sb.Append("Total used ").Append(Mb(_probe.TotalUsedMemory))
                   .Append(" / reserved ").Append(Mb(_probe.TotalReservedMemory)).AppendLine();
                _sb.Append("GC used ").Append(Mb(_probe.GcUsedMemory))
                   .Append(" / reserved ").Append(Mb(_probe.GcReservedMemory)).AppendLine();
                _sb.Append("GC alloc/frame ").Append(Kb(_probe.GcAllocInFrame)).AppendLine();
                _sb.Append("Textures ").Append(Mb(_probe.TextureMemory))
                   .Append(" (").Append(Count(_probe.TextureCount)).Append(')')
                   .Append("   meshes ").Append(Mb(_probe.MeshMemory)).AppendLine();
            }

            if (!_probe.HasProfilerCounters)
            {
                _sb.AppendLine();
                _sb.Append("(profiler counters n/a - build Release da strip;").AppendLine();
                _sb.Append(" dung Development Build de co so day du)");
            }

            _bodyText.text = _sb.ToString();
            ApplyPositionAfterResize();
        }

        // --------------------------------------------------------- format số

        private const string NotAvailable = "n/a";

        private static string Ms(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("0.0", CultureInfo.InvariantCulture) + " ms"
                : NotAvailable;
        }

        private static string Ms(float value)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture) + " ms";
        }

        private static string Count(long? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : NotAvailable;
        }

        /// <summary>Rút gọn kiểu cửa sổ Statistics: 78.4k / 1.2M.</summary>
        private static string Short(long? value)
        {
            if (!value.HasValue) return NotAvailable;

            long v = value.Value;
            if (v < 1000L) return v.ToString(CultureInfo.InvariantCulture);
            if (v < 1000000L) return (v / 1000d).ToString("0.0", CultureInfo.InvariantCulture) + "k";
            return (v / 1000000d).ToString("0.0", CultureInfo.InvariantCulture) + "M";
        }

        private static string Mb(long? bytes)
        {
            return bytes.HasValue
                ? (bytes.Value / 1048576d).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
                : NotAvailable;
        }

        private static string Kb(long? bytes)
        {
            if (!bytes.HasValue) return NotAvailable;
            double kb = bytes.Value / 1024d;
            return kb >= 1024d
                ? (kb / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
                : kb.ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        }
    }
}
