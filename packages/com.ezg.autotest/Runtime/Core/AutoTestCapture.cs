using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Ezg.AutoTest
{
    /// <summary>Chụp màn hình Play mode (cuối frame) + lưu PNG có thu nhỏ.</summary>
    public static class AutoTestCapture
    {
        const int MIN_SIDE = 64;

        /// <summary>
        ///     Chụp frame hiện tại (gồm cả UI overlay). Trả null nếu không chụp được (batchmode -nographics…).
        ///     Người gọi chịu trách nhiệm Destroy texture.
        /// </summary>
        public static async Task<Texture2D> CaptureScreen(CancellationToken token)
        {
            if (!Application.isPlaying || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                return null;
            try
            {
                await Awaitable.EndOfFrameAsync(token);
                return ScreenCapture.CaptureScreenshotAsTexture();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                AutoTestLog.Warn("Không chụp được màn hình: " + e.Message);
                return null;
            }
        }

        /// <summary>Lưu PNG, thu nhỏ theo <paramref name="scale" /> (0–1].</summary>
        public static void SavePng(Texture2D source, string absolutePath, float scale = 1f)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            if (scale >= 0.999f || scale <= 0f)
            {
                File.WriteAllBytes(absolutePath, source.EncodeToPNG());
                return;
            }

            var w = Mathf.Max(MIN_SIDE, Mathf.RoundToInt(source.width * scale));
            var h = Mathf.Max(MIN_SIDE, Mathf.RoundToInt(source.height * scale));
            var scaled = Resize(source, w, h);
            try
            {
                File.WriteAllBytes(absolutePath, scaled.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.Destroy(scaled);
            }
        }

        /// <summary>Resize bằng GPU (Blit) — nhanh, chất lượng bilinear.</summary>
        public static Texture2D Resize(Texture2D source, int width, int height)
        {
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                result.Apply();
                return result;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
