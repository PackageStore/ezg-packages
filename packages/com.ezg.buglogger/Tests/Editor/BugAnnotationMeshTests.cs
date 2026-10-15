using System.Collections.Generic;
using Ezg.BugLogger.UI;
using NUnit.Framework;
using UnityEngine;

namespace Ezg.BugLogger.Tests
{
    public class BugAnnotationMeshTests
    {
        private static readonly Color32 Red = new Color32(255, 0, 0, 255);

        private static IEnumerable<BugAnnotation> AllKinds()
        {
            var pen = new Vector2[60];
            for (var i = 0; i < pen.Length; i++) pen[i] = new Vector2(100f + i * 7f, 300f + Mathf.Sin(i * 0.4f) * 80f);

            yield return new BugAnnotation(BugAnnotationKind.Pen, Red, 9f, pen);
            yield return new BugAnnotation(BugAnnotationKind.Pen, Red, 9f, new[] { new Vector2(10f, 10f) });
            yield return new BugAnnotation(BugAnnotationKind.Rect, Red, 9f, new[] { new Vector2(10f, 20f), new Vector2(300f, 200f) });
            yield return new BugAnnotation(BugAnnotationKind.Ellipse, Red, 9f, new[] { new Vector2(10f, 20f), new Vector2(300f, 200f) });
            yield return new BugAnnotation(BugAnnotationKind.Arrow, Red, 9f, new[] { new Vector2(10f, 20f), new Vector2(300f, 200f) });
            yield return new BugAnnotation(BugAnnotationKind.Arrow, Red, 9f, new[] { new Vector2(10f, 20f), new Vector2(14f, 22f) });
            yield return new BugAnnotation(BugAnnotationKind.Spotlight, Red, 9f, new[] { new Vector2(10f, 20f), new Vector2(300f, 200f) });
            yield return new BugAnnotation(BugAnnotationKind.Marker, Red, 34f, new[] { new Vector2(500f, 500f) }, 7);
            yield return new BugAnnotation(BugAnnotationKind.Marker, Red, 34f, new[] { new Vector2(500f, 500f) }, 42);
        }

        [Test]
        public void EveryKind_ProducesValidMesh()
        {
            foreach (var item in AllKinds())
            {
                var buffer = new BugMeshBuffer();
                buffer.SetTransform(new Rect(0f, 0f, 1080f, 2400f), new Rect(-300f, -600f, 600f, 1200f));
                BugAnnotationMesh.AppendShape(buffer, item);

                Assert.Greater(buffer.VertexCount, 0, item.Kind + " không có đỉnh");
                Assert.AreEqual(0, buffer.Indices.Count % 3, item.Kind.ToString());
                foreach (var index in buffer.Indices) Assert.That(index, Is.InRange(0, buffer.VertexCount - 1), item.Kind.ToString());
                foreach (var position in buffer.Positions)
                    Assert.IsFalse(float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsInfinity(position.x),
                        item.Kind + " có toạ độ NaN");
            }
        }

        [Test]
        public void VertexCount_DoesNotDependOnDisplayScale()
        {
            foreach (var item in AllKinds())
            {
                var scaled = new BugMeshBuffer();
                scaled.SetTransform(new Rect(0f, 0f, 1080f, 2400f), new Rect(0f, 0f, 270f, 600f));
                scaled.Feather = 3f;
                BugAnnotationMesh.AppendShape(scaled, item);

                Assert.AreEqual(BugAnnotationMesh.CountVertices(item), scaled.VertexCount, item.Kind.ToString());
            }
        }

        [Test]
        public void SpotlightDim_CoversEverythingExceptHoles()
        {
            var items = new[]
            {
                new BugAnnotation(BugAnnotationKind.Spotlight, Red, 4f, new[] { new Vector2(100f, 100f), new Vector2(300f, 300f) }),
                // Chồng lên lỗ đầu + tràn ra ngoài ảnh.
                new BugAnnotation(BugAnnotationKind.Spotlight, Red, 4f, new[] { new Vector2(200f, 200f), new Vector2(1200f, 400f) })
            };
            var buffer = new BugMeshBuffer();
            BugAnnotationMesh.AppendSpotlightDim(buffer, new Rect(0f, 0f, 1000f, 1000f), items, null);

            // Hợp hai lỗ (đã cắt theo ảnh): 200×200 + 800×200 − 100×100 chồng nhau.
            var holes = 200f * 200f + 800f * 200f - 100f * 100f;
            Assert.AreEqual(1000f * 1000f - holes, TriangleArea(buffer), 1f);
        }

        [Test]
        public void SpotlightDim_NoSpotlight_DrawsNothing()
        {
            var buffer = new BugMeshBuffer();
            BugAnnotationMesh.AppendSpotlightDim(buffer, new Rect(0f, 0f, 100f, 100f), new BugAnnotation[0], null);
            Assert.AreEqual(0, buffer.VertexCount);

            BugAnnotationMesh.AppendSpotlightDim(buffer, new Rect(0f, 0f, 100f, 100f), null, new Rect(10f, 10f, 20f, 20f));
            Assert.AreEqual(100f * 100f - 20f * 20f, TriangleArea(buffer), 0.01f);
        }

        [Test]
        public void GetOnColor_DarkOnLightBackground()
        {
            Assert.AreEqual(255, BugAnnotationMesh.GetOnColor(new Color32(255, 59, 48, 255)).r);
            Assert.Less(BugAnnotationMesh.GetOnColor(new Color32(255, 214, 10, 255)).r, 100);
            Assert.Less(BugAnnotationMesh.GetOnColor(new Color32(255, 255, 255, 255)).r, 100);
        }

        [Test]
        public void Polyline_Simplify_KeepsEndsAndShape()
        {
            var line = new List<Vector2>();
            for (var i = 0; i <= 100; i++) line.Add(new Vector2(i, 0.01f * (i % 2)));
            var simplified = BugPolyline.Simplify(line, 0.5f);
            Assert.AreEqual(2, simplified.Length);
            Assert.AreEqual(line[0], simplified[0]);
            Assert.AreEqual(line[100], simplified[1]);

            var corner = new List<Vector2> { Vector2.zero, new Vector2(50f, 0f), new Vector2(100f, 0f), new Vector2(100f, 100f) };
            Assert.AreEqual(3, BugPolyline.Simplify(corner, 1f).Length);
        }

        private static float TriangleArea(BugMeshBuffer buffer)
        {
            var area = 0f;
            for (var i = 0; i + 2 < buffer.Indices.Count; i += 3)
            {
                Vector2 a = buffer.Positions[buffer.Indices[i]];
                Vector2 b = buffer.Positions[buffer.Indices[i + 1]];
                Vector2 c = buffer.Positions[buffer.Indices[i + 2]];
                area += Mathf.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)) * 0.5f;
            }

            return area;
        }
    }
}
