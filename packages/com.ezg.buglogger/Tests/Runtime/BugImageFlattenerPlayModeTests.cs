using System.Collections;
using Ezg.BugLogger.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Ezg.BugLogger.Tests
{
    /// <summary>
    ///     Ảnh đã chỉnh phải có nét vẽ cả khi game đang chạy (giữa vòng lặp người chơi, render pipeline của project đang
    ///     vẽ camera) — <c>Graphics.DrawMeshNow</c> từng chỉ chạy ở Edit mode, Play với URP thì ảnh ra mất hết nét.
    /// </summary>
    public class BugImageFlattenerPlayModeTests
    {
        private const int WIDTH = 200;
        private const int HEIGHT = 120;

        [UnityTest]
        public IEnumerator Render_WhilePlaying_DrawsStrokesInPlace()
        {
            Assume.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null),
                "Cần GPU (Editor chạy -nographics thì bỏ qua)");
            yield return null;

            var source = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, false);
            var pixels = new Color32[WIDTH * HEIGHT];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color32(0, 0, 0, 255);
            source.SetPixels32(pixels);
            source.Apply(false, true);

            var document = new BugImageDocument(WIDTH, HEIGHT);
            // Chấm trắng ở trên-trái: sai chiều dọc thì chấm rơi xuống dưới-trái.
            document.TryAdd(new BugAnnotation(BugAnnotationKind.Pen, new Color32(255, 255, 255, 255), 24f,
                new[] { new Vector2(40f, 90f) }));

            var jpg = BugImageFlattener.Render(source, document, null);
            Object.Destroy(source);
            Assert.IsNotNull(jpg, "Render trả null");

            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.IsTrue(image.LoadImage(jpg), "JPG hỏng");
            var stroke = image.GetPixel(40, 90);
            var mirrored = image.GetPixel(40, HEIGHT - 90);
            Object.Destroy(image);

            Assert.Greater(stroke.r, 0.8f, "Không thấy nét vẽ trong ảnh xuất: " + stroke);
            Assert.Less(mirrored.r, 0.2f, "Ảnh xuất bị lật dọc: " + mirrored);
        }
    }
}
