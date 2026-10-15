using Ezg.BugLogger.UI;
using NUnit.Framework;
using UnityEngine;

namespace Ezg.BugLogger.Tests
{
    public class BugImageDocumentTests
    {
        private static readonly Color32 Red = new Color32(255, 0, 0, 255);

        private static BugAnnotation Box(BugAnnotationKind kind, float x, float y, float size = 40f)
        {
            return new BugAnnotation(kind, Red, 6f, new[] { new Vector2(x, y), new Vector2(x + size, y + size) });
        }

        private static BugAnnotation Marker(int number)
        {
            return new BugAnnotation(BugAnnotationKind.Marker, Red, 20f, new[] { new Vector2(50f, 50f) }, number);
        }

        [Test]
        public void NewDocument_IsPristineFullCrop()
        {
            var document = new BugImageDocument(1080, 2400);

            Assert.IsTrue(document.IsPristine);
            Assert.IsFalse(document.IsCropped);
            Assert.AreEqual(new Rect(0f, 0f, 1080f, 2400f), document.Crop);
            Assert.IsFalse(document.CanUndo);
            Assert.IsFalse(document.CanRedo);
        }

        [Test]
        public void UndoRedo_RestoresItemsAndCrop()
        {
            var document = new BugImageDocument(1000, 2000);
            Assert.AreEqual(BugAddResult.Added, document.TryAdd(Box(BugAnnotationKind.Rect, 10f, 10f)));
            Assert.IsTrue(document.SetCrop(new Rect(100f, 200f, 500f, 600f)));
            Assert.AreEqual(BugAddResult.Added, document.TryAdd(Box(BugAnnotationKind.Ellipse, 150f, 250f)));

            Assert.IsTrue(document.Undo());
            Assert.AreEqual(1, document.Items.Count);
            Assert.IsTrue(document.IsCropped);

            Assert.IsTrue(document.Undo());
            Assert.IsFalse(document.IsCropped);

            Assert.IsTrue(document.Undo());
            Assert.IsTrue(document.IsPristine);
            Assert.AreEqual(0, document.VertexCount);
            Assert.IsFalse(document.Undo());

            Assert.IsTrue(document.Redo());
            Assert.IsTrue(document.Redo());
            Assert.IsTrue(document.Redo());
            Assert.AreEqual(2, document.Items.Count);
            Assert.AreEqual(new Rect(100f, 200f, 500f, 600f), document.Crop);
            Assert.IsFalse(document.Redo());
        }

        [Test]
        public void NewEdit_ClearsRedo()
        {
            var document = new BugImageDocument(500, 500);
            document.TryAdd(Box(BugAnnotationKind.Rect, 10f, 10f));
            document.Undo();
            Assert.IsTrue(document.CanRedo);

            document.TryAdd(Box(BugAnnotationKind.Arrow, 20f, 20f));
            Assert.IsFalse(document.CanRedo);
        }

        [Test]
        public void ClearAll_ResetsAndIsUndoable()
        {
            var document = new BugImageDocument(800, 600);
            document.TryAdd(Box(BugAnnotationKind.Rect, 10f, 10f));
            document.SetCrop(new Rect(0f, 0f, 400f, 300f));

            Assert.IsTrue(document.ClearAll());
            Assert.IsTrue(document.IsPristine);
            Assert.IsFalse(document.ClearAll());

            Assert.IsTrue(document.Undo());
            Assert.AreEqual(1, document.Items.Count);
            Assert.AreEqual(new Rect(0f, 0f, 400f, 300f), document.Crop);
        }

        [Test]
        public void SetCrop_ClampsInsideImageWithMinimumSize()
        {
            var document = new BugImageDocument(1000, 500);

            document.SetCrop(Rect.MinMaxRect(-50f, -20f, 300f, 700f));
            Assert.AreEqual(Rect.MinMaxRect(0f, 0f, 350f, 500f), document.Crop);

            document.SetCrop(new Rect(990f, 490f, 2f, 2f));
            var min = document.MinCropSize;
            Assert.GreaterOrEqual(document.Crop.width, min - 1f);
            Assert.GreaterOrEqual(document.Crop.height, min - 1f);
            Assert.LessOrEqual(document.Crop.xMax, 1000f);
            Assert.LessOrEqual(document.Crop.yMax, 500f);
            Assert.AreEqual(Mathf.Round(document.Crop.x), document.Crop.x);
        }

        [Test]
        public void SetCrop_SameRect_IsNoOp()
        {
            var document = new BugImageDocument(400, 400);
            Assert.IsFalse(document.SetCrop(new Rect(0f, 0f, 400f, 400f)));
            Assert.IsFalse(document.CanUndo);
        }

        [Test]
        public void Clone_IsIndependent()
        {
            var document = new BugImageDocument(400, 400);
            document.TryAdd(Box(BugAnnotationKind.Rect, 10f, 10f));

            var copy = document.Clone();
            copy.TryAdd(Box(BugAnnotationKind.Rect, 50f, 50f));
            copy.SetCrop(new Rect(0f, 0f, 200f, 200f));

            Assert.AreEqual(1, document.Items.Count);
            Assert.IsFalse(document.IsCropped);
            Assert.AreEqual(2, copy.Items.Count);
            Assert.IsTrue(copy.Undo());
            Assert.IsTrue(copy.Undo());
            Assert.IsTrue(copy.Undo());
            Assert.IsTrue(copy.IsPristine);
            Assert.AreEqual(1, document.Items.Count);
        }

        [Test]
        public void NextMarkerNumber_FollowsExistingMarkers()
        {
            var document = new BugImageDocument(400, 400);
            Assert.AreEqual(1, document.NextMarkerNumber);
            document.TryAdd(Marker(1));
            document.TryAdd(Marker(2));
            Assert.AreEqual(3, document.NextMarkerNumber);
            document.Undo();
            Assert.AreEqual(2, document.NextMarkerNumber);
        }

        [Test]
        public void Limits_AreEnforced()
        {
            var document = new BugImageDocument(2000, 2000);
            for (var i = 0; i < BugImageDocument.MAX_SPOTLIGHTS; i++)
                Assert.AreEqual(BugAddResult.Added, document.TryAdd(Box(BugAnnotationKind.Spotlight, i * 60f, 0f)));
            Assert.AreEqual(BugAddResult.SpotlightLimit, document.TryAdd(Box(BugAnnotationKind.Spotlight, 0f, 500f)));

            Assert.AreEqual(BugAddResult.MarkerLimit, document.TryAdd(Marker(BugImageDocument.MAX_MARKER + 1)));

            var result = BugAddResult.Added;
            for (var i = 0; i < 200 && result == BugAddResult.Added; i++)
                result = document.TryAdd(Box(BugAnnotationKind.Ellipse, i, i));
            Assert.AreEqual(BugAddResult.LimitReached, result);
            Assert.LessOrEqual(document.Items.Count, BugImageDocument.MAX_ITEMS);
            Assert.LessOrEqual(document.VertexCount, BugImageDocument.VERTEX_BUDGET);
        }
    }
}
