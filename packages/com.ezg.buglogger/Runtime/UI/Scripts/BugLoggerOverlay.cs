using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using BugLoggerApi = Ezg.BugLogger.BugLogger;

namespace Ezg.BugLogger.UI
{
    /// <summary>
    ///     Canvas overlay riêng của Bug Logger (sort order rất cao, sống qua đổi scene): nút nổi, form, toast, màn chỉnh
    ///     sửa ảnh.
    ///     Tự co theo hướng màn hình, tự lo chụp ảnh — không dựa vào UI framework của game. Dựng bởi
    ///     <see cref="BugLoggerUI" />, game không tạo trực tiếp.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    internal sealed class BugLoggerOverlay : MonoBehaviour
    {
        #region Fields

        // Trên mọi canvas của game (kể cả overlay debug, toast) ở mọi project.
        private const int SORTING_ORDER = short.MaxValue;

        private static readonly Vector2 PortraitReference = new Vector2(1080f, 1920f);
        private static readonly Vector2 LandscapeReference = new Vector2(1920f, 1080f);

        [SerializeField] private Canvas _canvas;
        [SerializeField] private CanvasScaler _scaler;
        [SerializeField] private BugLoggerLauncher _launcher;
        [SerializeField] private BugLoggerForm _form;
        [SerializeField] private BugLoggerToast _toast;
        [SerializeField] private BugLoggerImageEditor _editor;

        private Vector2Int _appliedScreen;
        private bool _launcherWanted;
        private bool _isCapturing;
        private bool _isPaused;
        private bool _ownsNotify;
        private float _savedTimeScale = 1f;

        // Số lượt chụp đang chờ cuối frame — còn lượt nào thì canvas còn tắt.
        private int _captureHolds;

        // HideForCapture: ẩn overlay tới mốc thời gian thực này (0 = không ẩn).
        private float _hiddenUntil;

        #endregion

        #region Properties

        public bool IsFormOpen => _isCapturing || _form.IsOpen;

        #endregion

        #region Initialize

        private void Awake()
        {
            _form.Bind(this, _canvas);
            _form.gameObject.SetActive(false);
            _editor.Bind(this);
            _editor.Close();
            _toast.gameObject.SetActive(false);
            _launcher.gameObject.SetActive(false);
            _launcher.Clicked += OnLauncherClicked;

            BugLoggerApi.ReportQueued += OnReportQueued;
            BugLoggerApi.ReportSent += OnReportSent;
            BugLoggerApi.ReportFailed += OnReportFailed;

            // Host chưa gắn toast riêng ⇒ thông báo của package hiện bằng toast của Bug Logger.
            if (BugLoggerConfig.Notify == null)
            {
                BugLoggerConfig.Notify = OnNotify;
                _ownsNotify = true;
            }

            ApplyScaler();
            ApplySorting();
        }

        private void Start()
        {
            if (EventSystem.current == null)
                Debug.LogWarning("[BugLogger] Không có EventSystem trong scene — nút báo lỗi sẽ không bấm được.");
        }

        private void OnDestroy()
        {
            _launcher.Clicked -= OnLauncherClicked;
            BugLoggerApi.ReportQueued -= OnReportQueued;
            BugLoggerApi.ReportSent -= OnReportSent;
            BugLoggerApi.ReportFailed -= OnReportFailed;
            if (_ownsNotify) BugLoggerConfig.Notify = null;

            ResumeGame();
        }

        private void Update()
        {
            if (_appliedScreen.x != Screen.width || _appliedScreen.y != Screen.height) ApplyScaler();
            if (_hiddenUntil > 0f && Time.realtimeSinceStartup >= _hiddenUntil)
            {
                _hiddenUntil = 0f;
                ApplyCanvasVisibility();
            }

            HandleBackKey();
        }

        #endregion

        #region Public Methods

        public void SetLauncherVisible(bool visible)
        {
            _launcherWanted = visible;
            if (!visible)
            {
                _launcher.Hide(true);
                return;
            }

            BugLoggerForm.PrefetchOptions();
            if (IsFormOpen) return;

            _launcher.SetPendingCount(BugLoggerApi.PendingCount);
            _launcher.Show();
        }

        public void OpenForm()
        {
            if (IsFormOpen) return;

            CaptureAndOpenAsync().Forget();
        }

        public void CloseForm()
        {
            if (_editor.IsOpen) _editor.Close();
            _form.Close();
        }

        public void ShowToast(string message, BugToastKind kind)
        {
            _toast.Show(message, kind);
        }

        /// <summary>Chụp màn hình không có overlay (nút nổi, toast, form) — xem <see cref="BugLoggerUI.CaptureScreenshotAsync" />.</summary>
        public async UniTask<byte[]> CaptureScreenshotAsync(CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled) return await CaptureJpgAsync(destroyCancellationToken);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken, cancellationToken);
            return await CaptureJpgAsync(linked.Token);
        }

        /// <summary>Ẩn overlay trong <paramref name="seconds" /> giây thực (≤ 0: hiện lại ngay) — xem <see cref="BugLoggerUI.HideForCapture" />.</summary>
        public void HideForCapture(float seconds)
        {
            _hiddenUntil = seconds > 0f ? Time.realtimeSinceStartup + seconds : 0f;
            ApplyCanvasVisibility();
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Ẩn cả overlay đúng frame chụp (nút nổi / toast không lọt vào ảnh), chụp ở cuối frame, nén JPG qua package,
        ///     rồi mới mở form và dừng game. Ảnh chụp gốc (chỉ trên GPU, đủ độ phân giải màn hình) giao cho form giữ tới khi
        ///     đóng — màn chỉnh sửa cắt / vẽ trên ảnh này cho nét.
        /// </summary>
        private async UniTaskVoid CaptureAndOpenAsync()
        {
            _isCapturing = true;
            var featureKey = ResolveFeatureKey();
            _launcher.Hide(false);

            Texture2D shot;
            try
            {
                shot = await CaptureTextureAsync(false, destroyCancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var jpg = BugLoggerScreenCapture.EncodeJpg(shot);
            Texture source = null;
            if (jpg != null && shot != null)
            {
                // Giữ bản RenderTexture (biết khi bị mất nội dung); không tạo được thì giữ luôn ảnh chụp.
                source = BugLoggerScreenCapture.CopyToRenderTexture(shot);
                if (source == null) source = shot;
            }

            if (shot != null && source != shot) Destroy(shot);

            PauseGame();
            _form.Open(jpg, BugLoggerScreenCapture.DecodePreview(jpg), source, featureKey, OnFormClosed);
            _isCapturing = false;
            BugLoggerUI.RaiseFormOpened();
        }

        /// <summary>Chụp (bỏ qua một frame) rồi nén JPG, huỷ ảnh chụp — xem <see cref="CaptureTextureAsync" />.</summary>
        private async UniTask<byte[]> CaptureJpgAsync(CancellationToken cancellationToken)
        {
            var shot = await CaptureTextureAsync(true, cancellationToken);
            if (shot == null) return null;

            try
            {
                return BugLoggerScreenCapture.EncodeJpg(shot);
            }
            finally
            {
                Destroy(shot);
            }
        }

        /// <summary>
        ///     Tắt canvas overlay tới hết frame chụp, chụp ở cuối frame, rồi bật lại (trừ khi còn lượt chụp khác / đang
        ///     <see cref="HideForCapture" />). Caller sở hữu (và phải huỷ) texture trả về; null khi chụp lỗi.
        /// </summary>
        /// <param name="skipFrame">
        ///     Chờ qua một frame trước khi chụp: caller ngoài có thể gọi lúc canvas của frame hiện tại đã vẽ xong.
        /// </param>
        private async UniTask<Texture2D> CaptureTextureAsync(bool skipFrame, CancellationToken cancellationToken)
        {
            _captureHolds++;
            ApplyCanvasVisibility();
            try
            {
                if (skipFrame) await UniTask.NextFrame(PlayerLoopTiming.Update, cancellationToken);
                await UniTask.WaitForEndOfFrame(this, cancellationToken);
                return BugLoggerScreenCapture.Capture();
            }
            finally
            {
                _captureHolds--;
                if (this != null) ApplyCanvasVisibility();
            }
        }

        private void ApplyCanvasVisibility()
        {
            _canvas.enabled = _captureHolds == 0 && _hiddenUntil <= 0f;
        }

        private static string ResolveFeatureKey()
        {
            var provider = BugLoggerUI.FeatureKeyProvider;
            if (provider != null)
                try
                {
                    var key = provider();
                    if (!string.IsNullOrWhiteSpace(key)) return key;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[BugLogger] FeatureKeyProvider threw. {ex.Message}");
                }

            return SceneManager.GetActiveScene().name;
        }

        private void PauseGame()
        {
            if (!BugLoggerUI.PauseGameWhileOpen || _isPaused) return;

            _isPaused = true;
            _savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        private void ResumeGame()
        {
            if (!_isPaused) return;

            _isPaused = false;
            var resume = BugLoggerUI.ResumeGame;
            // Game vốn đang dừng (popup của game pause trước khi QA mở form) ⇒ giữ nguyên dừng, không gọi hook resume.
            if (resume == null || _savedTimeScale <= 0f)
            {
                Time.timeScale = _savedTimeScale;
                return;
            }

            try
            {
                resume();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugLogger] ResumeGame threw, restore timeScale. {ex.Message}");
                Time.timeScale = _savedTimeScale;
            }
        }

        /// <summary>
        ///     Canvas tối thiểu 1080 × 1920 (dọc) hoặc 1920 × 1080 (ngang) đơn vị — mọi chữ/nút giữ cùng tỉ lệ so
        ///     với cạnh ngắn của màn hình ở mọi project, mọi máy.
        /// </summary>
        private void ApplyScaler()
        {
            _appliedScreen = new Vector2Int(Screen.width, Screen.height);
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            _scaler.referenceResolution = Screen.width > Screen.height ? LandscapeReference : PortraitReference;
        }

        /// <summary>
        ///     Overlay canvas xếp theo sorting layer trước rồi mới tới order ⇒ đặt lên layer cuối cùng của project
        ///     (project nào cũng khác) với order lớn nhất.
        /// </summary>
        private void ApplySorting()
        {
            var layers = SortingLayer.layers;
            if (layers.Length > 0) _canvas.sortingLayerID = layers[layers.Length - 1].id;
            _canvas.sortingOrder = SORTING_ORDER;
        }

        private void HandleBackKey()
        {
            if (!IsBackPressed()) return;

            if (_editor.IsOpen) _editor.HandleBack();
            else if (_form.IsOpen) _form.Close();
        }

        // Nút Back Android = phím Escape ở cả hai hệ input; project chỉ bật một trong hai cũng chạy.
        private static bool IsBackPressed()
        {
#if ENABLE_INPUT_SYSTEM && EZG_BUGLOGGER_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) return true;
#endif
            return false;
        }

        private void RefreshPending()
        {
            var pending = BugLoggerApi.PendingCount;
            _launcher.SetPendingCount(pending);
            if (_form.IsOpen) _form.RefreshStatus();
        }

        #endregion

        #region Events

        private void OnLauncherClicked()
        {
            OpenForm();
        }

        private void OnFormClosed()
        {
            ResumeGame();
            if (_launcherWanted)
            {
                _launcher.SetPendingCount(BugLoggerApi.PendingCount);
                _launcher.Show();
            }

            BugLoggerUI.RaiseFormClosed();
        }

        private void OnNotify(string message)
        {
            switch (message)
            {
                case BugLoggerConfig.NOTIFY_SAVED_SENDING:
                    ShowToast(BugLoggerStrings.TOAST_SAVED_SENDING, BugToastKind.Success);
                    break;
                case BugLoggerConfig.NOTIFY_SAVED_OFFLINE:
                    ShowToast(BugLoggerStrings.TOAST_SAVED_OFFLINE, BugToastKind.Info);
                    break;
                case BugLoggerConfig.NOTIFY_SAVE_FAILED:
                    ShowToast(BugLoggerStrings.TOAST_SAVE_FAILED, BugToastKind.Error);
                    break;
                default:
                    ShowToast(message, BugToastKind.Info);
                    break;
            }
        }

        private void OnReportQueued()
        {
            RefreshPending();
        }

        private void OnReportSent(int number)
        {
            RefreshPending();
            if (!_launcherWanted && !_form.IsOpen) return;

            ShowToast(number > 0 ? string.Format(BugLoggerStrings.TOAST_SENT_FORMAT, number) : BugLoggerStrings.TOAST_SENT,
                BugToastKind.Success);
        }

        private void OnReportFailed(string id, string reason)
        {
            RefreshPending();
            if (!_launcherWanted && !_form.IsOpen) return;

            ShowToast(string.Format(BugLoggerStrings.TOAST_FAILED_FORMAT, reason), BugToastKind.Error);
        }

        #endregion
    }
}
