using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ezg.BugLogger.UI
{
    /// <summary>
    ///     UI dùng chung của Bug Logger: nút nổi + form báo lỗi + toast, toàn bộ sprite/font nằm trong package nên
    ///     trông giống hệt nhau ở mọi project. Game chỉ quyết định LÚC NÀO hiện (ví dụ khi bật cheat cho máy QA):
    ///     <code>BugLoggerUI.LauncherVisible = isQaDevice;</code>
    ///     Overlay chỉ được dựng (Resources.Load) ở lần đầu cần hiện — bản build người chơi không bao giờ bật thì
    ///     không tốn gì.
    /// </summary>
    public static class BugLoggerUI
    {
        #region Fields

        private const string OVERLAY_RESOURCE = "EzgBugLogger/BugLoggerOverlay";

        private static BugLoggerOverlay _overlay;
        private static bool _launcherVisible;

        #endregion

        #region Properties

        /// <summary>Hiện/ẩn nút nổi mở form. Bật lần đầu sẽ dựng overlay (DontDestroyOnLoad).</summary>
        public static bool LauncherVisible
        {
            get => _launcherVisible;
            set
            {
                _launcherVisible = value;
                var overlay = value ? EnsureOverlay() : _overlay;
                if (overlay != null) overlay.SetLauncherVisible(value);
            }
        }

        /// <summary>Form đang mở (kể cả lúc đang chụp ảnh / đang chạy animation đóng).</summary>
        public static bool IsFormOpen => _overlay != null && _overlay.IsFormOpen;

        /// <summary>
        ///     Tuỳ chọn: tên màn hình / tính năng điền sẵn vào form (QA sửa được). Null hoặc trả rỗng → tên scene.
        ///     Gọi lúc QA bấm nút, trước khi chụp ảnh.
        /// </summary>
        public static Func<string> FeatureKeyProvider { get; set; }

        /// <summary>Dừng game (<c>Time.timeScale = 0</c>) trong lúc form mở. Mặc định bật.</summary>
        public static bool PauseGameWhileOpen { get; set; } = true;

        /// <summary>
        ///     Tuỳ chọn: cách game resume khi form đóng (ví dụ trả về tốc độ x2 của cheat). Null → trả
        ///     <c>Time.timeScale</c> về giá trị trước khi mở form.
        /// </summary>
        public static Action ResumeGame { get; set; }

        /// <summary>Form vừa mở (đã chụp ảnh, game đã dừng).</summary>
        public static event Action FormOpened;

        /// <summary>Form vừa đóng hẳn (gửi xong hoặc QA đóng).</summary>
        public static event Action FormClosed;

        #endregion

        #region Public Methods

        /// <summary>Chụp màn hình rồi mở form — dùng khi game có nút/cử chỉ báo lỗi riêng.</summary>
        public static void OpenForm()
        {
            var overlay = EnsureOverlay();
            if (overlay != null) overlay.OpenForm();
        }

        /// <summary>Đóng form nếu đang mở (nội dung đang gõ được giữ cho lần mở sau).</summary>
        public static void CloseForm()
        {
            if (_overlay != null) _overlay.CloseForm();
        }

        /// <summary>
        ///     Chụp màn hình KHÔNG kèm UI của Bug Logger (nút nổi, toast, form): tắt overlay đúng frame chụp rồi bật lại,
        ///     trả JPG đã nén giống ảnh form gửi đi. Công cụ tự gửi report (QA tự động, cheat) dùng hàm này thay vì
        ///     <c>ScreenCapture</c> để ảnh trên BugHub không dính nút báo lỗi. Ảnh là frame kế tiếp sau lúc gọi.
        /// </summary>
        /// <returns>Byte JPG, hoặc null khi chụp/nén lỗi.</returns>
        public static UniTask<byte[]> CaptureScreenshotAsync(CancellationToken cancellationToken = default)
        {
            return _overlay != null
                ? _overlay.CaptureScreenshotAsync(cancellationToken)
                : CaptureWithoutOverlayAsync(cancellationToken);
        }

        /// <summary>
        ///     <see cref="CaptureScreenshotAsync" /> dạng callback — gọi qua reflection được, không cần reference UniTask.
        ///     <paramref name="onCaptured" /> nhận null khi chụp lỗi.
        /// </summary>
        public static void CaptureScreenshot(Action<byte[]> onCaptured)
        {
            if (onCaptured == null) throw new ArgumentNullException(nameof(onCaptured));

            CaptureAndNotifyAsync(onCaptured).Forget();
        }

        /// <summary>
        ///     Ẩn toàn bộ UI của Bug Logger trong <paramref name="seconds" /> giây thực, cho công cụ chụp màn từ ngoài app
        ///     (adb screencap, simctl io screenshot): gọi, chờ 1–2 frame, chụp, rồi gọi lại với 0. Hết hạn tự hiện lại
        ///     (công cụ chết giữa chừng không để nút mất luôn); gọi lại để gia hạn, ≤ 0 để hiện ngay.
        /// </summary>
        public static void HideForCapture(float seconds)
        {
            if (_overlay != null) _overlay.HideForCapture(seconds);
        }

        #endregion

        #region Internal

        internal static void RaiseFormOpened()
        {
            FormOpened?.Invoke();
        }

        internal static void RaiseFormClosed()
        {
            FormClosed?.Invoke();
        }

        #endregion

        #region Private Methods

        // Chưa dựng overlay ⇒ không có UI nào của package để ẩn, chụp luôn ở cuối frame kế.
        private static async UniTask<byte[]> CaptureWithoutOverlayAsync(CancellationToken cancellationToken)
        {
            await UniTask.NextFrame(PlayerLoopTiming.Update, cancellationToken);
            await UniTask.WaitForEndOfFrame(cancellationToken);
            return BugLoggerScreenCapture.CaptureJpg();
        }

        private static async UniTaskVoid CaptureAndNotifyAsync(Action<byte[]> onCaptured)
        {
            byte[] jpg = null;
            try
            {
                jpg = await CaptureScreenshotAsync();
            }
            catch (OperationCanceledException)
            {
                // Overlay bị huỷ giữa chừng (thoát app / thoát Play) — báo null như chụp lỗi.
            }

            onCaptured(jpg);
        }

        private static BugLoggerOverlay EnsureOverlay()
        {
            if (_overlay != null) return _overlay;

            var prefab = Resources.Load<BugLoggerOverlay>(OVERLAY_RESOURCE);
            if (prefab == null)
            {
                Debug.LogError($"[BugLogger] Thiếu prefab Resources/{OVERLAY_RESOURCE} trong package com.ezg.buglogger.");
                return null;
            }

            _overlay = Object.Instantiate(prefab);
            _overlay.name = prefab.name;
            Object.DontDestroyOnLoad(_overlay.gameObject);
            return _overlay;
        }

        // Editor tắt Reload Domain thì static sống qua các lần Play.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _overlay = null;
            _launcherVisible = false;
            FeatureKeyProvider = null;
            PauseGameWhileOpen = true;
            ResumeGame = null;
            FormOpened = null;
            FormClosed = null;
        }

        #endregion
    }
}
