using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ezg.BugLogger.UI
{
    /// <summary>
    ///     Chụp framebuffer thành texture CHỈ nằm trên GPU (bỏ bản CPU ~10 MB full-res — EncodeScreenshot chỉ blit từ
    ///     GPU). Gọi ở cuối frame. ReadPixels lỗi trên vài emulator ⇒ thử lại qua RenderTexture.
    /// </summary>
    internal static class BugLoggerScreenCapture
    {
        #region Public Methods

        /// <summary>Texture chụp được (caller huỷ) hoặc null khi cả hai cách đều thất bại.</summary>
        public static Texture2D Capture()
        {
            try
            {
                var screenshot = CaptureByReadPixels();
                if (screenshot != null) return screenshot;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugLogger] ReadPixels capture failed, fallback to RenderTexture. {ex.Message}");
            }

            try
            {
                return CaptureByRenderTexture();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugLogger] RenderTexture capture failed. {ex.Message}");
            }

            return null;
        }

        /// <summary>Giải mã JPG đã nén thành texture chỉ-GPU để làm thumbnail (đúng ảnh sẽ gửi đi).</summary>
        public static Texture2D DecodePreview(byte[] jpg)
        {
            if (jpg == null || jpg.Length == 0) return null;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (texture.LoadImage(jpg, true)) return texture;

            Object.Destroy(texture);
            return null;
        }

        #endregion

        #region Private Methods

        private static Texture2D CaptureByReadPixels()
        {
            GetResolution(out var width, out var height);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            try
            {
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                texture.Apply(false, true);
                var result = texture;
                texture = null;
                return result;
            }
            finally
            {
                if (texture != null) Object.Destroy(texture);
            }
        }

        private static Texture2D CaptureByRenderTexture()
        {
            GetResolution(out var width, out var height);
            var activeBefore = RenderTexture.active;
            // ARGB32 tránh format nén lạ của driver emulator.
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Texture2D texture = null;
            try
            {
                ScreenCapture.CaptureScreenshotIntoRenderTexture(target);
                RenderTexture.active = target;
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                texture.Apply(false, true);
                var result = texture;
                texture = null;
                return result;
            }
            finally
            {
                RenderTexture.active = activeBefore;
                RenderTexture.ReleaseTemporary(target);
                if (texture != null) Object.Destroy(texture);
            }
        }

        private static void GetResolution(out int width, out int height)
        {
            width = Screen.width;
            height = Screen.height;
            if (width > 0 && height > 0) return;

            var display = Display.main;
            width = Mathf.Max(1, display != null ? display.renderingWidth : width);
            height = Mathf.Max(1, display != null ? display.renderingHeight : height);
        }

        #endregion
    }
}
