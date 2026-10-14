using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ezg.BugLogger.Tests
{
    /// <summary>Settings (endpoint mặc định / override) và mã hoá screenshot của facade.</summary>
    public class BugLoggerFacadeTests
    {
        [Test]
        public void Settings_EmptyOverride_UsesDefaultEndpoint()
        {
            var settings = NewSettings(" SM007 ", "  ");
            try
            {
                Assert.AreEqual("SM007", settings.ProjectCode);
                Assert.AreEqual(BugLoggerSettings.DEFAULT_ENDPOINT, settings.ResolveEndpoint());
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void Settings_Override_WinsWithoutTrailingSlash()
        {
            var settings = NewSettings("SM007", "http://localhost:8787/");
            try
            {
                Assert.AreEqual("http://localhost:8787", settings.ResolveEndpoint());
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [TestCase(1080, 2400, 576, 1280)]
        [TestCase(2400, 1080, 1280, 576)]
        [TestCase(1280, 720, 1280, 720)]
        [TestCase(640, 480, 640, 480)]
        public void ScaledSize_FitsLongEdge(int sourceWidth, int sourceHeight, int expectedWidth, int expectedHeight)
        {
            BugLogger.GetScaledSize(sourceWidth, sourceHeight, out var width, out var height);

            Assert.AreEqual(expectedWidth, width);
            Assert.AreEqual(expectedHeight, height);
        }

        [Test]
        public void EncodeScreenshot_IsJpgWithinLongEdge()
        {
            Assume.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null), "needs a graphics device");

            var source = new Texture2D(2000, 1000, TextureFormat.RGBA32, false);
            var decoded = new Texture2D(2, 2);
            try
            {
                var jpg = BugLogger.EncodeScreenshot(source);

                Assert.IsNotNull(jpg);
                Assert.AreEqual(0xFF, jpg[0]);
                Assert.AreEqual(0xD8, jpg[1]);
                Assert.IsTrue(decoded.LoadImage(jpg));
                Assert.AreEqual(BugLogger.SCREENSHOT_MAX_LONG_EDGE, decoded.width);
                Assert.AreEqual(640, decoded.height);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(decoded);
            }
        }

        [Test]
        public void EncodeScreenshot_Null_ReturnsNull()
        {
            Assert.IsNull(BugLogger.EncodeScreenshot(null));
        }

        private static BugLoggerSettings NewSettings(string projectCode, string endpointOverride)
        {
            var settings = ScriptableObject.CreateInstance<BugLoggerSettings>();
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("projectCode").stringValue = projectCode;
            serialized.FindProperty("endpointOverride").stringValue = endpointOverride;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return settings;
        }
    }
}
