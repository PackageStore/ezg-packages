using System;
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
