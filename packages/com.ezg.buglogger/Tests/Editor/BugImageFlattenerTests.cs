using Ezg.BugLogger.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ezg.BugLogger.Tests
{
    /// <summary>
    ///     Dựng ảnh đã chỉnh trên GPU thật (Blit + vẽ mesh vào RenderTexture): đúng chiều (không lật dọc trên Metal /
    ///     Vulkan / D3D), cắt đúng vùng, nét nằm đúng chỗ.
    /// </summary>
    public class BugImageFlattenerTests
    {
        private const string ATLAS_PATH = "Packages/com.ezg.buglogger/Runtime/UI/Sprites/bl_annotation_atlas.png";
        private const int WIDTH = 256;
        private const int HEIGHT = 128;

        private Texture2D _source;
        private Texture2D _atlas;

        [SetUp]
        public void SetUp()
        {
            Assume.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null),
                "Cần GPU (Editor chạy -nographics thì bỏ qua)");

            // Nửa trên-trái đỏ, còn lại đen (toạ độ texture: gốc trái-dưới).
            _source = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, false);
            var pixels = new Color32[WIDTH * HEIGHT];
            for (var y = 0; y < HEIGHT; y++)
            for (var x = 0; x < WIDTH; x++)
                pixels[y * WIDTH + x] = x < WIDTH / 2 && y >= HEIGHT / 2
                    ? new Color32(255, 0, 0, 255)
                    : new Color32(0, 0, 0, 255);
            _source.SetPixels32(pixels);
            _source.Apply(false, true);
            _atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(ATLAS_PATH);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_source);
        }

        [Test]
        public void Atlas_Exists()
        {
            Assert.IsNotNull(_atlas, "Thiếu " + ATLAS_PATH);
        }

        [Test]
        public void Render_Uncropped_KeepsOrientation()
        {
            var document = new BugImageDocument(WIDTH, HEIGHT);
            document.TryAdd(new BugAnnotation(BugAnnotationKind.Pen, new Color32(255, 255, 255, 255), 30f,
                new[] { new Vector2(200f, 100f) }));

            var image = Decode(BugImageFlattener.Render(_source, document, _atlas));
            Assert.AreEqual(WIDTH, image.width);
            Assert.AreEqual(HEIGHT, image.height);

            AssertColor(image, 30, 110, Color.red, "góc trên-trái phải đỏ");
            AssertColor(image, 30, 15, Color.black, "góc dưới-trái phải đen");
            AssertColor(image, 200, 100, Color.white, "chấm bút phải ở trên-phải");
            AssertColor(image, 200, 28, Color.black, "dưới chấm bút phải đen (không lật dọc)");
            Object.DestroyImmediate(image);
        }

        [Test]
        public void Render_Cropped_KeepsOnlyCropAndMovesStrokes()
        {
            var document = new BugImageDocument(WIDTH, HEIGHT);
            document.SetCrop(new Rect(0f, 64f, 128f, 64f));
            document.TryAdd(new BugAnnotation(BugAnnotationKind.Pen, new Color32(0, 255, 0, 255), 16f,
                new[] { new Vector2(100f, 80f) }));

            var image = Decode(BugImageFlattener.Render(_source, document, _atlas));
            Assert.AreEqual(128, image.width);
            Assert.AreEqual(64, image.height);

            AssertColor(image, 20, 50, Color.red, "vùng cắt toàn đỏ");
            AssertColor(image, 20, 5, Color.red, "vùng cắt toàn đỏ (mép dưới)");
            AssertColor(image, 100, 16, Color.green, "chấm bút dời theo vùng cắt");
            Object.DestroyImmediate(image);
        }

        [Test]
        public void Render_Spotlight_DimsOutside()
        {
            var document = new BugImageDocument(WIDTH, HEIGHT);
            document.TryAdd(new BugAnnotation(BugAnnotationKind.Spotlight, new Color32(255, 255, 255, 255), 2f,
                new[] { new Vector2(0f, 64f), new Vector2(64f, 128f) }));

            var image = Decode(BugImageFlattener.Render(_source, document, _atlas));
            var inside = image.GetPixel(20, 100);
            var outside = image.GetPixel(100, 100);
            Assert.Greater(inside.r, 0.85f, "trong vùng làm nổi giữ nguyên");
            Assert.Less(outside.r, 0.6f, "ngoài vùng làm nổi tối đi");
            Assert.Greater(outside.r, 0.2f, "ngoài vùng vẫn còn thấy ảnh");
            Object.DestroyImmediate(image);
        }

        [Test]
        public void MeshColors_ConvertToLinearOnlyWhenAsked()
        {
            var buffer = new BugMeshBuffer();
            buffer.Vertex(Vector2.zero, new Color32(255, 59, 48, 128), Vector2.zero);
            var mesh = new Mesh();
            try
            {
                buffer.CopyTo(mesh, false);
                Assert.AreEqual(59f / 255f, mesh.colors[0].g, 0.002f);

                buffer.CopyTo(mesh, true);
                Assert.AreEqual(Mathf.GammaToLinearSpace(59f / 255f), mesh.colors[0].g, 0.002f);
                Assert.AreEqual(128f / 255f, mesh.colors[0].a, 0.002f, "alpha giữ nguyên");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        private static Texture2D Decode(byte[] jpg)
        {
            Assert.IsNotNull(jpg, "Render trả null");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.IsTrue(texture.LoadImage(jpg), "JPG hỏng");
            return texture;
        }

        private static void AssertColor(Texture2D image, int x, int y, Color expected, string message)
        {
            var actual = image.GetPixel(x, y);
            var distance = Mathf.Abs(actual.r - expected.r) + Mathf.Abs(actual.g - expected.g) +
                           Mathf.Abs(actual.b - expected.b);
            Assert.Less(distance, 0.35f, $"{message}: ({x},{y}) = {actual}");
        }
    }
}
