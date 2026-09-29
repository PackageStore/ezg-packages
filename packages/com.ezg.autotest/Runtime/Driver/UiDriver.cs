using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Điều khiển UI như người chơi thật: tìm object, kiểm tra hiển thị/bấm được, bấm (qua EventSystem —
    ///     đi đúng đường pointer down/up/click của UGUI), kéo, nhập text. Không phụ thuộc Input System hay
    ///     legacy Input nên chạy được trong Editor lẫn device.
    /// </summary>
    public sealed class UiDriver
    {
        const float DEFAULT_WAIT_SECONDS = 5f;
        const float MIN_VISIBLE_ALPHA = 0.02f;
        const int DRAG_STEPS = 12;
        const int PRESS_HOLD_FRAMES = 2;

        readonly AutoTestSession _session;
        readonly List<RaycastResult> _raycastBuffer = new();

        public UiDriver(AutoTestSession session)
        {
            _session = session;
        }

        CancellationToken Token => _session.CurrentToken.CanBeCanceled ? _session.CurrentToken : _session.RunToken;

        #region Tìm

        /// <summary>
        ///     Tìm GameObject theo đường dẫn hierarchy (khớp phần đuôi: "Panel/btn_buy") hoặc tên. Tìm trong
        ///     <paramref name="root" /> nếu có, không thì mọi scene đang load (kể cả DontDestroyOnLoad).
        /// </summary>
        public GameObject Find(string pathOrName, GameObject root = null, bool includeInactive = false)
        {
            if (string.IsNullOrEmpty(pathOrName)) return null;
            var parts = pathOrName.Split('/');
            var leaf = parts[parts.Length - 1];
            foreach (var t in EnumerateTransforms(root, includeInactive))
            {
                if (t.name != leaf) continue;
                if (parts.Length == 1 || PathEndsWith(t, parts)) return t.gameObject;
            }

            return null;
        }

        /// <summary>Tìm theo text hiển thị (Text / TMP), trả về object chứa Selectable gần nhất nếu có.</summary>
        public GameObject FindByText(string text, bool contains = true, GameObject root = null)
        {
            foreach (var t in EnumerateTransforms(root, false))
            {
                var s = GetOwnText(t.gameObject);
                if (s == null) continue;
                var match = contains
                    ? s.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                    : string.Equals(s.Trim(), text, StringComparison.OrdinalIgnoreCase);
                if (!match) continue;
                var sel = t.GetComponentInParent<Selectable>();
                return sel != null ? sel.gameObject : t.gameObject;
            }

            return null;
        }

        /// <summary>Mọi component T (đang active) trong root hoặc toàn bộ scene.</summary>
        public List<T> FindAll<T>(GameObject root = null, bool includeInactive = false) where T : Component
        {
            var list = new List<T>();
            if (root != null)
            {
                root.GetComponentsInChildren(includeInactive, list);
                return list;
            }

            list.AddRange(Object.FindObjectsByType<T>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            return list;
        }

        /// <summary>Chờ tới khi object xuất hiện (và hiển thị nếu <paramref name="mustBeVisible" />).</summary>
        public async Task<GameObject> WaitFor(string pathOrName, float timeoutSeconds = DEFAULT_WAIT_SECONDS,
            GameObject root = null, bool mustBeVisible = true)
        {
            GameObject found = null;
            await AutoTestClock.Until(() =>
            {
                found = Find(pathOrName, root);
                return found != null && (!mustBeVisible || IsVisible(found));
            }, timeoutSeconds, Token);
            return found;
        }

        #endregion

        #region Trạng thái

        /// <summary>Đang hiển thị: active, CanvasGroup alpha &gt; 0, Graphic không trong suốt, nằm trong màn hình.</summary>
        public bool IsVisible(GameObject go)
        {
            if (go == null || !go.activeInHierarchy) return false;
            if (EffectiveAlpha(go.transform) < MIN_VISIBLE_ALPHA) return false;
            var graphic = go.GetComponent<Graphic>();
            if (graphic != null && (!graphic.enabled || graphic.color.a < MIN_VISIBLE_ALPHA) &&
                go.transform.childCount == 0)
                return false;
            if (go.transform is RectTransform rt)
            {
                var r = GetScreenRect(rt);
                if (r.width <= 0.5f || r.height <= 0.5f) return false;
                return r.Overlaps(new Rect(0, 0, Screen.width, Screen.height));
            }

            return true;
        }

        /// <summary>Selectable interactable + CanvasGroup cho phép tương tác.</summary>
        public bool IsInteractable(GameObject go)
        {
            if (go == null || !go.activeInHierarchy) return false;
            var sel = go.GetComponent<Selectable>();
            if (sel != null && !sel.IsInteractable()) return false;
            for (var t = go.transform; t != null; t = t.parent)
            {
                var cg = t.GetComponent<CanvasGroup>();
                if (cg == null) continue;
                if (!cg.interactable || !cg.blocksRaycasts) return false;
                if (cg.ignoreParentGroups) break;
            }

            return true;
        }

        /// <summary>
        ///     Bấm được thật không: hiển thị + interactable + raycast tại tâm trúng chính nó (không bị che).
        ///     <paramref name="reason" /> mô tả lý do khi false.
        /// </summary>
        public bool IsClickable(GameObject go, out string reason)
        {
            reason = null;
            if (go == null)
            {
                reason = "object null";
                return false;
            }

            if (!IsVisible(go))
            {
                reason = "không hiển thị";
                return false;
            }

            if (!IsInteractable(go))
            {
                reason = "không tương tác được (interactable/CanvasGroup)";
                return false;
            }

            var top = RaycastTop(GetScreenCenter(go));
            if (top == null)
            {
                reason = "raycast không trúng gì (thiếu Raycast Target/EventSystem?)";
                return false;
            }

            if (top != go && !top.transform.IsChildOf(go.transform))
            {
                var handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(top);
                if (handler != go)
                {
                    reason = $"bị che bởi '{PathOf(top.transform)}'";
                    return false;
                }
            }

            return true;
        }

        /// <summary>Text hiển thị của object (Text/TMP trên chính nó hoặc con đầu tiên).</summary>
        public string GetText(GameObject go)
        {
            if (go == null) return null;
            var own = GetOwnText(go);
            if (own != null) return own;
            var tmp = go.GetComponentInChildren<TMP_Text>();
            if (tmp != null) return tmp.text;
            var txt = go.GetComponentInChildren<Text>();
            return txt != null ? txt.text : null;
        }

        static string GetOwnText(GameObject go)
        {
            var tmp = go.GetComponent<TMP_Text>();
            if (tmp != null) return tmp.text;
            var txt = go.GetComponent<Text>();
            return txt != null ? txt.text : null;
        }

        /// <summary>Hình chữ nhật trên màn hình (pixel, gốc dưới-trái) của RectTransform.</summary>
        public Rect GetScreenRect(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var canvas = rt.GetComponentInParent<Canvas>();
            Camera cam = null;
            if (canvas != null)
            {
                var root = canvas.rootCanvas;
                if (root.renderMode != RenderMode.ScreenSpaceOverlay) cam = root.worldCamera != null ? root.worldCamera : Camera.main;
            }

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var c in corners)
            {
                Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, c);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        public Vector2 GetScreenCenter(GameObject go)
        {
            if (go.transform is RectTransform rt) return GetScreenRect(rt).center;
            var cam = Camera.main;
            return cam != null ? (Vector2)cam.WorldToScreenPoint(go.transform.position) : Vector2.zero;
        }

        /// <summary>Object trên cùng nhận raycast UI (và physics raycaster nếu có) tại điểm màn hình.</summary>
        public GameObject RaycastTop(Vector2 screenPos)
        {
            var es = EventSystem.current;
            if (es == null) return null;
            var data = new PointerEventData(es) { position = screenPos };
            _raycastBuffer.Clear();
            es.RaycastAll(data, _raycastBuffer);
            return _raycastBuffer.Count > 0 ? _raycastBuffer[0].gameObject : null;
        }

        /// <summary>Các nút đang bấm được trong root (theo thứ tự hierarchy).</summary>
        public List<Selectable> GetClickables(GameObject root, bool onlyClickable = true)
        {
            var list = new List<Selectable>();
            foreach (var sel in FindAll<Selectable>(root))
            {
                if (!(sel is Button) && !(sel is Toggle) && sel.GetComponent<IPointerClickHandler>() == null) continue;
                if (onlyClickable && !IsClickable(sel.gameObject, out _)) continue;
                list.Add(sel);
            }

            return list;
        }

        #endregion

        #region Thao tác

        /// <summary>
        ///     Bấm như ngón tay: pointer enter → down → (giữ vài frame) → up → click tại tâm object. Trả false (và
        ///     không bấm) nếu object không bấm được khi <paramref name="requireClickable" />.
        /// </summary>
        public async Task<bool> Click(GameObject go, bool requireClickable = true)
        {
            if (go == null) return false;
            if (requireClickable && !IsClickable(go, out var reason))
            {
                AutoTestLog.Info($"Bỏ qua bấm '{PathOf(go.transform)}': {reason}");
                return false;
            }

            var pos = GetScreenCenter(go);
            var target = RaycastTop(pos);
            // Không raycast được (vd không requireClickable) → bắn thẳng vào object.
            if (target == null || (!requireClickable && !target.transform.IsChildOf(go.transform))) target = go;
            await PressAndRelease(target, pos, PRESS_HOLD_FRAMES);
            return true;
        }

        /// <summary>Tìm theo path/tên (chờ tối đa <paramref name="timeoutSeconds" />) rồi bấm.</summary>
        public async Task<bool> Click(string pathOrName, float timeoutSeconds = DEFAULT_WAIT_SECONDS,
            GameObject root = null)
        {
            var go = await WaitFor(pathOrName, timeoutSeconds, root);
            return go != null && await Click(go);
        }

        /// <summary>Bấm vào object trên cùng tại điểm màn hình (kiểu monkey). Trả object nhận bấm.</summary>
        public async Task<GameObject> Tap(Vector2 screenPos)
        {
            var target = RaycastTop(screenPos);
            if (target == null) return null;
            await PressAndRelease(target, screenPos, PRESS_HOLD_FRAMES);
            return target;
        }

        /// <summary>Nhấn giữ (vd nút giữ để nâng cấp liên tục).</summary>
        public async Task<bool> Hold(GameObject go, float seconds)
        {
            if (go == null || !IsClickable(go, out _)) return false;
            var pos = GetScreenCenter(go);
            var target = RaycastTop(pos) ?? go;
            var es = EventSystem.current;
            var data = CreatePointer(es, pos, target);
            var pressed = ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerDownHandler) ??
                          ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
            data.pointerPress = pressed;
            data.rawPointerPress = target;
            await AutoTestClock.Seconds(seconds, Token);
            if (pressed != null) ExecuteEvents.Execute(pressed, data, ExecuteEvents.pointerUpHandler);
            return true;
        }

        /// <summary>Kéo từ điểm A tới B trong <paramref name="seconds" /> (scroll list, vuốt camera UI…).</summary>
        public async Task Drag(Vector2 from, Vector2 to, float seconds = 0.3f)
        {
            var es = EventSystem.current;
            if (es == null) return;
            var target = RaycastTop(from);
            var data = CreatePointer(es, from, target);
            if (target != null)
            {
                data.pointerPress = ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerDownHandler);
                data.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(target);
            }

            if (data.pointerDrag != null)
            {
                ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.initializePotentialDrag);
                ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.beginDragHandler);
                data.dragging = true;
            }

            var last = from;
            for (var i = 1; i <= DRAG_STEPS; i++)
            {
                await AutoTestClock.Seconds(seconds / DRAG_STEPS, Token);
                var p = Vector2.Lerp(from, to, i / (float)DRAG_STEPS);
                data.position = p;
                data.delta = p - last;
                last = p;
                if (data.pointerDrag != null) ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.dragHandler);
            }

            if (data.pointerPress != null) ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
            if (data.pointerDrag != null)
            {
                ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.endDragHandler);
                var dropTarget = RaycastTop(to);
                if (dropTarget != null) ExecuteEvents.ExecuteHierarchy(dropTarget, data, ExecuteEvents.dropHandler);
            }
        }

        /// <summary>Nhập text vào InputField / TMP_InputField (kích hoạt onValueChanged + onEndEdit).</summary>
        public bool SetText(GameObject go, string text)
        {
            if (go == null) return false;
            var tmp = go.GetComponentInChildren<TMP_InputField>();
            if (tmp != null)
            {
                tmp.text = text;
                tmp.onEndEdit?.Invoke(text);
                return true;
            }

            var input = go.GetComponentInChildren<InputField>();
            if (input == null) return false;
            input.text = text;
            input.onEndEdit?.Invoke(text);
            return true;
        }

        public bool SetToggle(GameObject go, bool isOn)
        {
            var toggle = go != null ? go.GetComponent<Toggle>() : null;
            if (toggle == null) return false;
            toggle.isOn = isOn;
            return true;
        }

        public bool SetSlider(GameObject go, float normalizedValue)
        {
            var slider = go != null ? go.GetComponent<Slider>() : null;
            if (slider == null) return false;
            slider.normalizedValue = Mathf.Clamp01(normalizedValue);
            return true;
        }

        async Task PressAndRelease(GameObject target, Vector2 pos, int holdFrames)
        {
            var es = EventSystem.current;
            if (es == null)
            {
                // Không có EventSystem: gọi thẳng onClick nếu là Button (vẫn test được logic).
                var btn = target.GetComponentInParent<Button>();
                btn?.onClick.Invoke();
                return;
            }

            var data = CreatePointer(es, pos, target);
            ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerEnterHandler);
            var pressed = ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerDownHandler) ??
                          ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
            data.pointerPress = pressed;
            data.rawPointerPress = target;
            data.eligibleForClick = true;
            data.pressPosition = pos;
            data.clickTime = Time.unscaledTime;
            data.clickCount = 1;

            await AutoTestClock.Frames(holdFrames, Token);
            if (pressed == null || !pressed) return;

            ExecuteEvents.Execute(pressed, data, ExecuteEvents.pointerUpHandler);
            var clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
            if (clickHandler != null && clickHandler == pressed)
                ExecuteEvents.Execute(clickHandler, data, ExecuteEvents.pointerClickHandler);
            if (target) ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerExitHandler);
        }

        static PointerEventData CreatePointer(EventSystem es, Vector2 pos, GameObject target)
        {
            var data = new PointerEventData(es)
            {
                position = pos,
                button = PointerEventData.InputButton.Left,
                pointerId = -1
            };
            if (target != null)
                data.pointerCurrentRaycast = new RaycastResult { gameObject = target, screenPosition = pos };
            data.pointerPressRaycast = data.pointerCurrentRaycast;
            return data;
        }

        #endregion

        #region Helpers

        /// <summary>Đường dẫn hierarchy đầy đủ "Root/…/Leaf".</summary>
        public static string PathOf(Transform t)
        {
            if (t == null) return "";
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        /// <summary>Alpha hiệu dụng qua các CanvasGroup cha.</summary>
        public static float EffectiveAlpha(Transform t)
        {
            var alpha = 1f;
            for (; t != null; t = t.parent)
            {
                var cg = t.GetComponent<CanvasGroup>();
                if (cg == null) continue;
                alpha *= cg.alpha;
                if (cg.ignoreParentGroups) break;
            }

            return alpha;
        }

        static bool PathEndsWith(Transform t, string[] parts)
        {
            var cur = t;
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                if (cur == null || cur.name != parts[i]) return false;
                cur = cur.parent;
            }

            return true;
        }

        static IEnumerable<Transform> EnumerateTransforms(GameObject root, bool includeInactive)
        {
            if (root != null)
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive)) yield return t;
                yield break;
            }

            foreach (var t in Object.FindObjectsByType<Transform>(
                         includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                         FindObjectsSortMode.None))
                yield return t;
        }

        #endregion
    }
}
