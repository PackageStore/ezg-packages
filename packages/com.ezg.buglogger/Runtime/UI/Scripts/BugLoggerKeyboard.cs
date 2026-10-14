using System;
using UnityEngine;

namespace Ezg.BugLogger.UI
{
    /// <summary>
    ///     Chiều cao bàn phím ảo (pixel màn hình Unity) để form né bàn phím. <see cref="TouchScreenKeyboard.area" />
    ///     luôn trả 0 khi app chạy bằng Android GameActivity (bàn phím vẫn hiện) ⇒ ô ở nửa dưới form bị bàn phím che,
    ///     QA gõ mà không thấy chữ. Khi đó đọc inset IME của cửa sổ Android qua JNI.
    /// </summary>
    internal static class BugLoggerKeyboard
    {
        #region Fields

#if UNITY_ANDROID && !UNITY_EDITOR
        // JNI vài lời gọi/lần ⇒ không đọc mỗi frame; 0.1 s vẫn kịp theo animation bàn phím.
        private const float POLL_INTERVAL = 0.1f;
        private const int API_WINDOW_INSETS_TYPE = 30;

        private static AndroidJavaObject _decorView;
        private static int _sdkInt;
        private static int _imeType;
        private static bool _isJniBroken;
        private static float _cachedHeight;
        private static float _nextPollTime;
#endif

        #endregion

        #region Public Methods

        /// <summary>Chiều cao bàn phím ảo theo pixel của <see cref="Screen" />; 0 khi bàn phím ẩn.</summary>
        public static float GetHeight()
        {
            // Android gõ qua ô sửa native: form đứng hình sau ô sửa, không né (xem BugLoggerNativeEditor).
            if (BugLoggerNativeEditor.IsActive) return 0f;

            if (!TouchScreenKeyboard.isSupported || !TouchScreenKeyboard.visible) return 0f;

            var area = TouchScreenKeyboard.area.height;
            if (area > 0f) return area;

#if UNITY_ANDROID && !UNITY_EDITOR
            if (Time.unscaledTime < _nextPollTime) return _cachedHeight;

            _nextPollTime = Time.unscaledTime + POLL_INTERVAL;
            _cachedHeight = ReadAndroidImeHeight();
            return _cachedHeight;
#else
            return 0f;
#endif
        }

        #endregion

        #region Private Methods

#if UNITY_ANDROID && !UNITY_EDITOR
        private static float ReadAndroidImeHeight()
        {
            if (_isJniBroken) return 0f;

            try
            {
                if (_decorView == null) BindDecorView();

                var viewHeight = _decorView.Call<int>("getHeight");
                if (viewHeight <= 0) return 0f;

                var imeBottom = _sdkInt >= API_WINDOW_INSETS_TYPE ? ReadImeInset() : ReadCoveredBottom(viewHeight);
                return Mathf.Max(0, imeBottom) * (float)Screen.height / viewHeight;
            }
            catch (Exception ex)
            {
                // Không làm hỏng form vì JNI: báo một lần rồi thôi né bàn phím.
                _isJniBroken = true;
                Debug.LogWarning($"[BugLogger] Không đọc được chiều cao bàn phím Android. {ex.Message}");
                return 0f;
            }
        }

        private static void BindDecorView()
        {
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                _sdkInt = version.GetStatic<int>("SDK_INT");

            if (_sdkInt >= API_WINDOW_INSETS_TYPE)
                using (var type = new AndroidJavaClass("android.view.WindowInsets$Type"))
                    _imeType = type.CallStatic<int>("ime");

            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var window = activity.Call<AndroidJavaObject>("getWindow"))
                _decorView = window.Call<AndroidJavaObject>("getDecorView");
        }

        // Android 11+: inset IME đo từ mép dưới cửa sổ, không phụ thuộc chế độ adjustResize/adjustPan.
        private static int ReadImeInset()
        {
            using (var insets = _decorView.Call<AndroidJavaObject>("getRootWindowInsets"))
            {
                if (insets == null) return 0;

                using (var ime = insets.Call<AndroidJavaObject>("getInsets", _imeType))
                    return ime.Get<int>("bottom");
            }
        }

        // Android 10 trở xuống: phần cửa sổ bị che phía dưới (bàn phím + có thể cả thanh điều hướng).
        private static int ReadCoveredBottom(int viewHeight)
        {
            using (var rect = new AndroidJavaObject("android.graphics.Rect"))
            {
                _decorView.Call("getWindowVisibleDisplayFrame", rect);
                return viewHeight - rect.Get<int>("bottom");
            }
        }
#endif

        #endregion
    }
}
