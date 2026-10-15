using System.Collections.Generic;
using UnityEngine;

namespace Ezg.BugLogger.UI
{
    /// <summary>
    ///     Dựng hình cho nét vẽ trên ảnh: nét liền đầu tròn (bút), khung, elip, mũi tên, huy hiệu đánh số, vùng tối của
    ///     "làm nổi". Mọi mép có một dải mờ rộng <see cref="BugMeshBuffer.Feather" /> (alpha của đỉnh về 0) ⇒ khử răng
    ///     cưa mà không cần MSAA hay shader riêng — vẽ bằng material UI mặc định ở cả màn hình lẫn ảnh xuất.
    ///     Số đỉnh chỉ phụ thuộc kích thước tính theo pixel ẢNH (không theo tỉ lệ hiển thị) ⇒ đếm một lần, dùng cho
    ///     giới hạn của <see cref="BugImageDocument" />. Chạy trên main thread (dùng list tạm tĩnh).
    /// </summary>
    internal static class BugAnnotationMesh
    {
        #region Fields

        private const float MITER_LIMIT = 2.4f;
        private const int CAP_SEGMENTS = 10;
        private const int ELLIPSE_SEGMENTS = 96;
        private const int MIN_DISC_SEGMENTS = 16;
        private const int MAX_DISC_SEGMENTS = 48;
        private const float DISC_SEGMENTS_PER_PIXEL = 0.5f;
        private const float MIN_POINT_DISTANCE = 0.01f;

        // Mũi tên: đầu dài 4.4 × độ dày nét, nửa đáy = 0.6 × chiều dài đầu, đầu không quá nửa thân.
        private const float ARROW_HEAD_RATIO = 4.4f;
        private const float ARROW_HEAD_SPREAD = 0.6f;
        private const float ARROW_MAX_HEAD_SHARE = 0.5f;
        private const float ARROW_SHAFT_OVERLAP = 0.3f;

        private const float SPOTLIGHT_STROKE_RATIO = 0.5f;

        // Huy hiệu: viền tối mảnh để nổi trên mọi nền, chữ số cao ~ bán kính.
        private const float MARKER_OUTLINE_RATIO = 1.16f;
        private const float MARKER_DIGIT_RATIO = 1.04f;
        private const float MARKER_DIGIT_TRACKING = 0.92f;
        private const float ON_COLOR_LUMINANCE = 0.6f;

        private static readonly Color32 MarkerOutline = new Color32(0, 0, 0, 110);
        private static readonly Color32 DarkInk = new Color32(17, 20, 28, 255);
        private static readonly Color32 SpotlightDim = new Color32(0, 0, 0, 150);

        private static readonly List<Vector2> MappedPoints = new List<Vector2>(256);
        private static readonly List<Vector2> StrokePoints = new List<Vector2>(256);
        private static readonly List<Vector2> StrokeNormals = new List<Vector2>(256);
        private static readonly List<Vector2> PolygonPoints = new List<Vector2>(8);
        private static readonly List<Rect> Holes = new List<Rect>(BugImageDocument.MAX_SPOTLIGHTS + 1);
        private static readonly List<float> GridX = new List<float>(32);
        private static readonly List<float> GridY = new List<float>(32);
        private static readonly BugMeshBuffer CountBuffer = new BugMeshBuffer();

        #endregion

        #region Public Methods

        /// <summary>Số đỉnh mesh của một nét (không phụ thuộc tỉ lệ hiển thị).</summary>
        public static int CountVertices(BugAnnotation item)
        {
            CountBuffer.Clear();
            AppendShape(CountBuffer, item);
            var count = CountBuffer.VertexCount;
            CountBuffer.Clear();
            return count;
        }

        public static void AppendShape(BugMeshBuffer buffer, BugAnnotation item)
        {
            AppendShape(buffer, item.Kind, item.Color, item.Width, item.Points, item.Number);
        }

        /// <summary>Dựng một nét (toạ độ ảnh gốc) vào <paramref name="buffer" />.</summary>
        public static void AppendShape(BugMeshBuffer buffer, BugAnnotationKind kind, Color32 color, float width,
            IReadOnlyList<Vector2> points, int number)
        {
            if (points == null || points.Count == 0 || width <= 0f) return;

            var half = width * 0.5f * buffer.LengthScale;
            var discSegments = GetDiscSegments(width * 0.5f);
            switch (kind)
            {
                case BugAnnotationKind.Pen:
                    MappedPoints.Clear();
                    for (var i = 0; i < points.Count; i++) MappedPoints.Add(buffer.Map(points[i]));
                    Stroke(buffer, MappedPoints, half, color, false, discSegments);
                    break;
                case BugAnnotationKind.Rect:
                    AppendBoxOutline(buffer, buffer.Map(GetBox(points)), half, color);
                    break;
                case BugAnnotationKind.Spotlight:
                    AppendBoxOutline(buffer, buffer.Map(GetBox(points)), half * SPOTLIGHT_STROKE_RATIO, color);
                    break;
                case BugAnnotationKind.Ellipse:
                    AppendEllipse(buffer, buffer.Map(GetBox(points)), half, color);
                    break;
                case BugAnnotationKind.Arrow:
                    AppendArrow(buffer, buffer.Map(points[0]), buffer.Map(points[points.Count - 1]), half, color,
                        discSegments);
                    break;
                case BugAnnotationKind.Marker:
                    AppendMarker(buffer, buffer.Map(points[0]), width * buffer.LengthScale, color, number,
                        GetDiscSegments(width * MARKER_OUTLINE_RATIO));
                    break;
            }
        }

        /// <summary>
        ///     Lớp tối của "làm nổi": phủ <paramref name="bounds" /> (px ảnh) trừ các vùng làm nổi trong
        ///     <paramref name="items" /> và <paramref name="extraHole" /> (vùng đang kéo, có thể null). Không có vùng nào thì
        ///     không vẽ gì.
        /// </summary>
        public static void AppendSpotlightDim(BugMeshBuffer buffer, Rect bounds, IReadOnlyList<BugAnnotation> items,
            Rect? extraHole)
        {
            var area = buffer.Map(bounds);
            Holes.Clear();
            if (items != null)
                for (var i = 0; i < items.Count; i++)
                    if (items[i].Kind == BugAnnotationKind.Spotlight)
                        AddHole(buffer.Map(items[i].GetBox()), area);
            if (extraHole.HasValue) AddHole(buffer.Map(extraHole.Value), area);
            if (Holes.Count == 0) return;

            // Chia vùng theo mọi mép của các lỗ thành lưới; ô nào không nằm trong lỗ thì tô (gộp ô liền nhau theo hàng).
            BuildGrid(GridX, area.xMin, area.xMax, true);
            BuildGrid(GridY, area.yMin, area.yMax, false);
            var uv = BugAnnotationAtlas.SolidUv;
            for (var row = 0; row + 1 < GridY.Count; row++)
            {
                var y0 = GridY[row];
                var y1 = GridY[row + 1];
                var runStart = -1;
                for (var column = 0; column <= GridX.Count - 1; column++)
                {
                    var isDim = column < GridX.Count - 1 &&
                                !IsInsideHole(new Vector2((GridX[column] + GridX[column + 1]) * 0.5f, (y0 + y1) * 0.5f));
                    if (isDim)
                    {
                        if (runStart < 0) runStart = column;
                        continue;
                    }

                    if (runStart < 0) continue;

                    AppendSolidRect(buffer, Rect.MinMaxRect(GridX[runStart], y0, GridX[column], y1), SpotlightDim, uv);
                    runStart = -1;
                }
            }
        }

        /// <summary>Tô đặc một hình chữ nhật (toạ độ đích, không viền mờ).</summary>
        public static void AppendSolidRect(BugMeshBuffer buffer, Rect rect, Color32 color)
        {
            AppendSolidRect(buffer, rect, color, BugAnnotationAtlas.SolidUv);
        }

        /// <summary>Nét liền qua các điểm (toạ độ đích) — dùng cho khung cắt của màn chỉnh sửa.</summary>
        public static void StrokeDestination(BugMeshBuffer buffer, IReadOnlyList<Vector2> points, float halfWidth,
            Color32 color, bool closed)
        {
            Stroke(buffer, points, halfWidth, color, closed, MIN_DISC_SEGMENTS);
        }

        /// <summary>Màu chữ trên nền <paramref name="background" />: tối trên nền sáng (vàng, trắng), trắng trên nền đậm.</summary>
        public static Color32 GetOnColor(Color32 background)
        {
            var luminance = (0.2126f * background.r + 0.7152f * background.g + 0.0722f * background.b) / 255f;
            return luminance > ON_COLOR_LUMINANCE ? DarkInk : new Color32(255, 255, 255, 255);
        }

        #endregion

        #region Private Methods — shapes

        private static void AppendBoxOutline(BugMeshBuffer buffer, Rect box, float half, Color32 color)
        {
            PolygonPoints.Clear();
            PolygonPoints.Add(new Vector2(box.xMin, box.yMin));
            PolygonPoints.Add(new Vector2(box.xMax, box.yMin));
            PolygonPoints.Add(new Vector2(box.xMax, box.yMax));
            PolygonPoints.Add(new Vector2(box.xMin, box.yMax));
            Stroke(buffer, PolygonPoints, half, color, true, MIN_DISC_SEGMENTS);
        }

        private static void AppendEllipse(BugMeshBuffer buffer, Rect box, float half, Color32 color)
        {
            var center = box.center;
            var radiusX = Mathf.Max(box.width * 0.5f, 0.001f);
            var radiusY = Mathf.Max(box.height * 0.5f, 0.001f);
            MappedPoints.Clear();
            for (var i = 0; i < ELLIPSE_SEGMENTS; i++)
            {
                var angle = i * Mathf.PI * 2f / ELLIPSE_SEGMENTS;
                MappedPoints.Add(new Vector2(center.x + Mathf.Cos(angle) * radiusX, center.y + Mathf.Sin(angle) * radiusY));
            }

            Stroke(buffer, MappedPoints, half, color, true, MIN_DISC_SEGMENTS);
        }

        private static void AppendArrow(BugMeshBuffer buffer, Vector2 tail, Vector2 head, float half, Color32 color,
            int discSegments)
        {
            var vector = head - tail;
            var length = vector.magnitude;
            if (length < MIN_POINT_DISTANCE)
            {
                Disc(buffer, head, half, color, discSegments);
                return;
            }

            var direction = vector / length;
            var normal = new Vector2(-direction.y, direction.x);
            var headLength = Mathf.Min(half * 2f * ARROW_HEAD_RATIO, length * ARROW_MAX_HEAD_SHARE);
            var headHalf = Mathf.Max(headLength * ARROW_HEAD_SPREAD, half * 1.6f);
            var baseCenter = head - direction * headLength;

            // Thân dừng ngầm bên trong đầu mũi tên — không lòi qua mũi nhọn, không hở khe ở chân.
            PolygonPoints.Clear();
            PolygonPoints.Add(tail);
            PolygonPoints.Add(baseCenter + direction * (headLength * ARROW_SHAFT_OVERLAP));
            Stroke(buffer, PolygonPoints, half, color, false, discSegments);

            PolygonPoints.Clear();
            PolygonPoints.Add(head);
            PolygonPoints.Add(baseCenter + normal * headHalf);
            PolygonPoints.Add(baseCenter - normal * headHalf);
            FillConvex(buffer, PolygonPoints, color);
        }

        private static void AppendMarker(BugMeshBuffer buffer, Vector2 center, float radius, Color32 color, int number,
            int discSegments)
        {
            var outline = MarkerOutline;
            outline.a = (byte)(outline.a * color.a / 255);
            Disc(buffer, center, radius * MARKER_OUTLINE_RATIO, outline, discSegments);
            Disc(buffer, center, radius, color, discSegments);

            number = Mathf.Clamp(number, 0, BugImageDocument.MAX_MARKER);
            var tens = number / 10;
            var ones = number % 10;
            var inkScale = radius * MARKER_DIGIT_RATIO / (BugAnnotationAtlas.INK_BOTTOM - BugAnnotationAtlas.INK_TOP);
            var tracking = tens > 0 ? MARKER_DIGIT_TRACKING : 1f;
            var total = (tens > 0 ? BugAnnotationAtlas.GetAdvance(tens) : 0f) + BugAnnotationAtlas.GetAdvance(ones);
            var cursor = center.x - total * inkScale * tracking * 0.5f;
            var ink = GetOnColor(color);
            ink.a = color.a;
            if (tens > 0) cursor = AppendDigit(buffer, tens, cursor, center.y, inkScale, tracking, ink);
            AppendDigit(buffer, ones, cursor, center.y, inkScale, tracking, ink);
        }

        /// <summary>Một chữ số, căn phần mực theo <paramref name="centerY" />. Trả vị trí bắt đầu chữ kế.</summary>
        private static float AppendDigit(BugMeshBuffer buffer, int digit, float cursor, float centerY, float scale,
            float tracking, Color32 color)
        {
            var advance = BugAnnotationAtlas.GetAdvance(digit) * scale * tracking;
            var cellCenterX = cursor + advance * 0.5f;
            var halfWidth = BugAnnotationAtlas.CELL_WIDTH * scale * 0.5f;
            var top = centerY + (BugAnnotationAtlas.INK_TOP + BugAnnotationAtlas.INK_BOTTOM) * 0.5f * scale;
            var bottom = top - BugAnnotationAtlas.CELL_HEIGHT * scale;
            var uv = BugAnnotationAtlas.GetDigitUv(digit);

            var a = buffer.Vertex(new Vector2(cellCenterX - halfWidth, bottom), color, new Vector2(uv.xMin, uv.yMin));
            var b = buffer.Vertex(new Vector2(cellCenterX - halfWidth, top), color, new Vector2(uv.xMin, uv.yMax));
            var c = buffer.Vertex(new Vector2(cellCenterX + halfWidth, top), color, new Vector2(uv.xMax, uv.yMax));
            var d = buffer.Vertex(new Vector2(cellCenterX + halfWidth, bottom), color, new Vector2(uv.xMax, uv.yMin));
            buffer.Quad(a, b, c, d);
            return cursor + advance;
        }

        #endregion

        #region Private Methods — primitives

        /// <summary>
        ///     Nét liền bề rộng 2 × <paramref name="half" /> qua các điểm (toạ độ đích): khớp vát nhọn có giới hạn, đầu tròn
        ///     khi hở. Mỗi điểm 4 đỉnh: mép mờ trái, lõi trái, lõi phải, mép mờ phải.
        /// </summary>
        private static void Stroke(BugMeshBuffer buffer, IReadOnlyList<Vector2> points, float half, Color32 color,
            bool closed, int discSegments)
        {
            StrokePoints.Clear();
            for (var i = 0; i < points.Count; i++)
                if (StrokePoints.Count == 0 || (points[i] - StrokePoints[StrokePoints.Count - 1]).sqrMagnitude >
                    MIN_POINT_DISTANCE * MIN_POINT_DISTANCE)
                    StrokePoints.Add(points[i]);
            if (closed && StrokePoints.Count > 2 &&
                (StrokePoints[0] - StrokePoints[StrokePoints.Count - 1]).sqrMagnitude <= MIN_POINT_DISTANCE * MIN_POINT_DISTANCE)
                StrokePoints.RemoveAt(StrokePoints.Count - 1);

            var count = StrokePoints.Count;
            if (count == 0) return;
            if (count == 1)
            {
                Disc(buffer, StrokePoints[0], half, color, discSegments);
                return;
            }

            if (closed && count < 3) closed = false;

            ComputeNormals(closed);
            var feather = buffer.Feather * 0.5f;
            var inner = Mathf.Max(half - feather, 0f);
            var outer = half + feather;
            var clear = WithAlpha(color, 0);
            var uv = BugAnnotationAtlas.SolidUv;

            var start = buffer.VertexCount;
            for (var i = 0; i < count; i++)
            {
                var point = StrokePoints[i];
                var normal = StrokeNormals[i];
                buffer.Vertex(point + normal * outer, clear, uv);
                buffer.Vertex(point + normal * inner, color, uv);
                buffer.Vertex(point - normal * inner, color, uv);
                buffer.Vertex(point - normal * outer, clear, uv);
            }

            var segments = closed ? count : count - 1;
            for (var i = 0; i < segments; i++)
            {
                var a = start + i * 4;
                var b = start + (i + 1) % count * 4;
                buffer.Quad(a, b, b + 1, a + 1);
                buffer.Quad(a + 1, b + 1, b + 2, a + 2);
                buffer.Quad(a + 2, b + 2, b + 3, a + 3);
            }

            if (closed) return;

            Cap(buffer, StrokePoints[0], (StrokePoints[0] - StrokePoints[1]).normalized, inner, outer, color);
            Cap(buffer, StrokePoints[count - 1], (StrokePoints[count - 1] - StrokePoints[count - 2]).normalized, inner,
                outer, color);
        }

        /// <summary>Pháp tuyến trái tại mỗi điểm của <see cref="StrokePoints" />, đã nhân hệ số vát (có giới hạn).</summary>
        private static void ComputeNormals(bool closed)
        {
            StrokeNormals.Clear();
            var count = StrokePoints.Count;
            for (var i = 0; i < count; i++)
            {
                var hasPrevious = closed || i > 0;
                var hasNext = closed || i < count - 1;
                var previous = hasPrevious
                    ? (StrokePoints[i] - StrokePoints[(i - 1 + count) % count]).normalized
                    : Vector2.zero;
                var next = hasNext ? (StrokePoints[(i + 1) % count] - StrokePoints[i]).normalized : Vector2.zero;

                if (!hasPrevious)
                {
                    StrokeNormals.Add(Perpendicular(next));
                    continue;
                }

                if (!hasNext)
                {
                    StrokeNormals.Add(Perpendicular(previous));
                    continue;
                }

                var tangent = previous + next;
                var previousNormal = Perpendicular(previous);
                if (tangent.sqrMagnitude < 1e-6f)
                {
                    // Quay đầu 180°: không có khớp vát hợp lý, giữ pháp tuyến của đoạn trước.
                    StrokeNormals.Add(previousNormal);
                    continue;
                }

                var normal = Perpendicular(tangent.normalized);
                var miter = 1f / Mathf.Max(Vector2.Dot(normal, previousNormal), 1f / MITER_LIMIT);
                StrokeNormals.Add(normal * miter);
            }
        }

        /// <summary>Đầu tròn: nửa hình tròn hướng ra ngoài theo <paramref name="outward" />.</summary>
        private static void Cap(BugMeshBuffer buffer, Vector2 center, Vector2 outward, float inner, float outer,
            Color32 color)
        {
            var normal = Perpendicular(outward);
            var clear = WithAlpha(color, 0);
            var uv = BugAnnotationAtlas.SolidUv;
            var centerIndex = buffer.Vertex(center, color, uv);
            var first = buffer.VertexCount;
            for (var k = 0; k <= CAP_SEGMENTS; k++)
            {
                var angle = k * Mathf.PI / CAP_SEGMENTS;
                var direction = normal * Mathf.Cos(angle) + outward * Mathf.Sin(angle);
                buffer.Vertex(center + direction * inner, color, uv);
                buffer.Vertex(center + direction * outer, clear, uv);
            }

            for (var k = 0; k < CAP_SEGMENTS; k++)
            {
                var core = first + k * 2;
                var nextCore = core + 2;
                buffer.Triangle(centerIndex, core, nextCore);
                buffer.Quad(core, core + 1, nextCore + 1, nextCore);
            }
        }

        /// <summary>Hình tròn đặc có mép mờ.</summary>
        private static void Disc(BugMeshBuffer buffer, Vector2 center, float radius, Color32 color, int segments)
        {
            var feather = buffer.Feather * 0.5f;
            var inner = Mathf.Max(radius - feather, 0f);
            var outer = radius + feather;
            var clear = WithAlpha(color, 0);
            var uv = BugAnnotationAtlas.SolidUv;
            var centerIndex = buffer.Vertex(center, color, uv);
            var first = buffer.VertexCount;
            for (var k = 0; k < segments; k++)
            {
                var angle = k * Mathf.PI * 2f / segments;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                buffer.Vertex(center + direction * inner, color, uv);
                buffer.Vertex(center + direction * outer, clear, uv);
            }

            for (var k = 0; k < segments; k++)
            {
                var core = first + k * 2;
                var nextCore = first + (k + 1) % segments * 2;
                buffer.Triangle(centerIndex, core, nextCore);
                buffer.Quad(core, core + 1, nextCore + 1, nextCore);
            }
        }

        /// <summary>Đa giác lồi tô đặc (toạ độ đích) kèm dải mờ quanh mép.</summary>
        private static void FillConvex(BugMeshBuffer buffer, List<Vector2> polygon, Color32 color)
        {
            var count = polygon.Count;
            if (count < 3) return;

            // Chuẩn về ngược chiều kim đồng hồ ⇒ pháp tuyến phải của mỗi cạnh hướng ra ngoài.
            var area = 0f;
            for (var i = 0; i < count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % count];
                area += a.x * b.y - b.x * a.y;
            }

            if (Mathf.Abs(area) < 1e-6f) return;
            if (area < 0f) polygon.Reverse();

            var clear = WithAlpha(color, 0);
            var uv = BugAnnotationAtlas.SolidUv;
            var feather = buffer.Feather;
            var first = buffer.VertexCount;
            for (var i = 0; i < count; i++)
            {
                var point = polygon[i];
                var previousEdge = (point - polygon[(i - 1 + count) % count]).normalized;
                var nextEdge = (polygon[(i + 1) % count] - point).normalized;
                var previousOutward = new Vector2(previousEdge.y, -previousEdge.x);
                var nextOutward = new Vector2(nextEdge.y, -nextEdge.x);
                var outward = (previousOutward + nextOutward).normalized;
                var miter = 1f / Mathf.Max(Vector2.Dot(outward, previousOutward), 1f / MITER_LIMIT);
                buffer.Vertex(point, color, uv);
                buffer.Vertex(point + outward * (feather * miter), clear, uv);
            }

            for (var i = 1; i + 1 < count; i++) buffer.Triangle(first, first + i * 2, first + (i + 1) * 2);
            for (var i = 0; i < count; i++)
            {
                var core = first + i * 2;
                var nextCore = first + (i + 1) % count * 2;
                buffer.Quad(core, core + 1, nextCore + 1, nextCore);
            }
        }

        private static void AppendSolidRect(BugMeshBuffer buffer, Rect rect, Color32 color, Vector2 uv)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;

            var a = buffer.Vertex(new Vector2(rect.xMin, rect.yMin), color, uv);
            var b = buffer.Vertex(new Vector2(rect.xMin, rect.yMax), color, uv);
            var c = buffer.Vertex(new Vector2(rect.xMax, rect.yMax), color, uv);
            var d = buffer.Vertex(new Vector2(rect.xMax, rect.yMin), color, uv);
            buffer.Quad(a, b, c, d);
        }

        #endregion

        #region Private Methods — helpers

        private static int GetDiscSegments(float imageRadius)
        {
            return Mathf.Clamp(Mathf.CeilToInt(imageRadius * DISC_SEGMENTS_PER_PIXEL), MIN_DISC_SEGMENTS,
                MAX_DISC_SEGMENTS);
        }

        private static Rect GetBox(IReadOnlyList<Vector2> points)
        {
            var a = points[0];
            var b = points[points.Count - 1];
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private static void AddHole(Rect hole, Rect area)
        {
            var clipped = Rect.MinMaxRect(Mathf.Max(hole.xMin, area.xMin), Mathf.Max(hole.yMin, area.yMin),
                Mathf.Min(hole.xMax, area.xMax), Mathf.Min(hole.yMax, area.yMax));
            if (clipped.width > 0f && clipped.height > 0f) Holes.Add(clipped);
        }

        private static void BuildGrid(List<float> grid, float min, float max, bool horizontal)
        {
            grid.Clear();
            grid.Add(min);
            grid.Add(max);
            for (var i = 0; i < Holes.Count; i++)
            {
                grid.Add(horizontal ? Holes[i].xMin : Holes[i].yMin);
                grid.Add(horizontal ? Holes[i].xMax : Holes[i].yMax);
            }

            grid.Sort();
            for (var i = grid.Count - 1; i > 0; i--)
                if (grid[i] - grid[i - 1] < 0.001f)
                    grid.RemoveAt(i);
        }

        private static bool IsInsideHole(Vector2 point)
        {
            for (var i = 0; i < Holes.Count; i++)
                if (Holes[i].Contains(point))
                    return true;
            return false;
        }

        private static Vector2 Perpendicular(Vector2 direction)
        {
            return new Vector2(-direction.y, direction.x);
        }

        private static Color32 WithAlpha(Color32 color, byte alpha)
        {
            color.a = alpha;
            return color;
        }

        #endregion
    }
}
