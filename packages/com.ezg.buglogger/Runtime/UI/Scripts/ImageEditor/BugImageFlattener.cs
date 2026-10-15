using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using BugLoggerApi = Ezg.BugLogger.BugLogger;
using Object = UnityEngine.Object;

namespace Ezg.BugLogger.UI
{
    /// <summary>
    ///     Dựng ảnh gửi đi từ ảnh chụp gốc + <see cref="BugImageDocument" />: cắt, vẽ nét, nén JPG — toàn bộ trên GPU
    ///     (ảnh gốc không cần readable). Nét vẽ qua <see cref="CommandBuffer" /> thực thi ngay
    ///     (<see cref="Graphics.ExecuteCommandBuffer" />): chạy với mọi render pipeline, trong cả Edit lẫn Play mode —
    ///     <c>Graphics.DrawMeshNow</c> không vẽ gì khi đang Play với URP. Cùng hàm dựng hình + material UI mặc định như
    ///     trên màn chỉnh sửa ⇒ ảnh ra giống cái QA thấy. Main thread.
    /// </summary>
    internal static class BugImageFlattener
    {
        #region Fields

        private const string COMMAND_NAME = "BugLogger.FlattenScreenshot";
        private static readonly int GuiZTestMode = Shader.PropertyToID("unity_GUIZTestMode");

        private static readonly BugMeshBuffer Buffer = new BugMeshBuffer();

        #endregion

        #region Public Methods

        /// <summary>
        ///     JPG của ảnh đã chỉnh (cạnh dài ≤ <see cref="BugLoggerApi.SCREENSHOT_MAX_LONG_EDGE" />, như ảnh chụp thường),
        ///     hoặc null khi lỗi.
        /// </summary>
        public static byte[] Render(Texture source, BugImageDocument document, Texture atlas)
        {
            if (source == null || document == null) return null;

            var full = document.FullRect;
            var crop = document.Crop;
            BugLoggerApi.GetScaledSize(Mathf.RoundToInt(crop.width), Mathf.RoundToInt(crop.height), out var width,
                out var height);

            var previous = RenderTexture.active;
            RenderTexture target = null;
            Mesh mesh = null;
            Material material = null;
            try
            {
                target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB);
                Graphics.Blit(source, target, new Vector2(crop.width / full.width, crop.height / full.height),
                    new Vector2(crop.x / full.width, crop.y / full.height));

                if (document.Items.Count > 0)
                {
                    Buffer.Clear();
                    Buffer.SetTransform(crop, new Rect(0f, 0f, width, height));
                    Buffer.Feather = 1f;
                    BugAnnotationMesh.AppendSpotlightDim(Buffer, full, document.Items, null);
                    for (var i = 0; i < document.Items.Count; i++) BugAnnotationMesh.AppendShape(Buffer, document.Items[i]);

                    mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                    Buffer.CopyTo(mesh, QualitySettings.activeColorSpace == ColorSpace.Linear);
                    material = new Material(Canvas.GetDefaultCanvasMaterial())
                        { hideFlags = HideFlags.HideAndDontSave, mainTexture = atlas };
                    // RenderTexture không có depth — vẫn ép ZTest Always cho khỏi phụ thuộc giá trị global canvas để lại.
                    material.SetFloat(GuiZTestMode, (float)CompareFunction.Always);

                    using var command = new CommandBuffer { name = COMMAND_NAME };
                    command.SetRenderTarget(target);
                    // Gốc trái-dưới như toạ độ ảnh; Unity tự lật khi đích là RenderTexture trên Metal / Vulkan / D3D.
                    command.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0f, width, 0f, height, -1f, 1f));
                    command.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
                    Graphics.ExecuteCommandBuffer(command);
                }

                return BugLoggerApi.EncodeTexture(target);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugLogger] Render edited screenshot failed. {ex.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                DestroyTemporary(mesh);
                DestroyTemporary(material);
                Buffer.Clear();
            }
        }

        #endregion

        #region Private Methods

        private static void DestroyTemporary(Object temporary)
        {
            if (temporary == null) return;

            if (Application.isPlaying) Object.Destroy(temporary);
            else Object.DestroyImmediate(temporary);
        }

        #endregion
    }
}
