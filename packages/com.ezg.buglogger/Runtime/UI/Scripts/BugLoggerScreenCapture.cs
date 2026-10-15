using System;
using UnityEngine;
using BugLoggerApi = Ezg.BugLogger.BugLogger;
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

        /// <summary>
        ///     Chụp rồi nén JPG như ảnh form gửi đi (<see cref="BugLoggerApi.EncodeScreenshot" />), huỷ texture chụp. Gọi ở
        ///     cuối frame. Null khi chụp hoặc nén lỗi.
        /// </summary>
        public static byte[] CaptureJpg()
        {
            var shot = Capture();
            if (shot == null) return null;

            try
            {
                return EncodeJpg(shot);
            }
            finally
            {
                Object.Destroy(shot);
            }
        }

        /// <summary>Nén ảnh chụp thành JPG như ảnh form gửi đi; không huỷ <paramref name="shot" />. Null khi lỗi.</summary>
        public static byte[] EncodeJpg(Texture2D shot)
        {
            if (shot == null) return null;

            try
            {
                return BugLoggerApi.EncodeScreenshot(shot);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugLogger] Encode screenshot failed. {ex.Message}");
                return null;
            }
        }

        /// <summary>
        ///     Chép ảnh chụp sang một RenderTexture riêng để giữ lâu (màn chỉnh sửa ảnh). Khác texture chỉ-GPU, RenderTexture
        ///     báo được khi mất nội dung (<see cref="RenderTexture.IsCreated" /> = false — Android GLES mất context lúc app
        ///     vào nền) để caller dựng lại. Null khi lỗi; không huỷ <paramref name="shot" />.
        /// </summary>
        public static RenderTexture CopyToRenderTexture(Texture2D shot)
        {
            if (shot == null) return null;

            var previous = RenderTexture.active;
            RenderTexture target = null;
            try
            {
                target = new RenderTexture(shot.width, shot.height, 0, RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB) { name = "BugLogger.Screenshot", useMipMap = false };
                if (!target.Create()) throw new InvalidOperationException("RenderTexture.Create failed");

                Graphics.Blit(shot, target);
                var result = target;
                target = null;
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugLogger] Keep screenshot as RenderTexture failed. {ex.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) Object.Destroy(target);
            }
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
