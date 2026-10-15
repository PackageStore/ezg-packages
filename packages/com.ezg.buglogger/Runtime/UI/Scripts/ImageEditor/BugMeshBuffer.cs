using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Ezg.BugLogger.UI
{
    /// <summary>
    ///     Bộ đệm mesh dùng chung cho hai nơi vẽ nét: màn chỉnh sửa (đổ vào <see cref="VertexHelper" /> của Graphic) và
    ///     ảnh xuất (đổ vào <see cref="Mesh" /> rồi vẽ lên RenderTexture). Cùng một hàm dựng hình ⇒ ảnh gửi đi giống hệt
    ///     cái QA thấy. Toạ độ vào là pixel ảnh gốc; <see cref="Scale" /> / <see cref="Offset" /> đổi sang toạ độ đích.
    /// </summary>
    internal sealed class BugMeshBuffer
    {
        #region Fields

        /// <summary>Trần đỉnh của một mesh uGUI (VertexHelper ném khi tới 65 000).</summary>
        public const int MAX_UI_VERTICES = 64000;

        public readonly List<Vector3> Positions = new List<Vector3>(512);
        public readonly List<Color32> Colors = new List<Color32>(512);
        public readonly List<Vector2> Uvs = new List<Vector2>(512);
        public readonly List<int> Indices = new List<int>(1536);

        /// <summary>Đổi toạ độ ảnh gốc → đích: <c>đích = ảnh × Scale + Offset</c>.</summary>
        public Vector2 Scale = Vector2.one;

        public Vector2 Offset;

        /// <summary>Bề rộng viền mờ khử răng cưa, tính theo đơn vị đích (≈ 1 pixel màn hình / ảnh xuất).</summary>
        public float Feather = 1f;

        private static readonly List<Color> LinearColors = new List<Color>(512);
        private static bool _warnedOverflow;

        #endregion

        #region Properties

        public int VertexCount => Positions.Count;

        /// <summary>Hệ số đổi độ dài (độ dày nét, bán kính) từ ảnh gốc sang đích.</summary>
        public float LengthScale => (Mathf.Abs(Scale.x) + Mathf.Abs(Scale.y)) * 0.5f;

        #endregion

        #region Public Methods

        public void Clear()
        {
            Positions.Clear();
            Colors.Clear();
            Uvs.Clear();
            Indices.Clear();
        }

        /// <summary>Ánh xạ vùng <paramref name="source" /> của ảnh gốc lên vùng <paramref name="destination" />.</summary>
        public void SetTransform(Rect source, Rect destination)
        {
            Scale = new Vector2(destination.width / Mathf.Max(source.width, 0.0001f),
                destination.height / Mathf.Max(source.height, 0.0001f));
            Offset = destination.min - Vector2.Scale(source.min, Scale);
        }

        public Vector2 Map(Vector2 imagePoint)
        {
            return new Vector2(imagePoint.x * Scale.x + Offset.x, imagePoint.y * Scale.y + Offset.y);
        }

        public Rect Map(Rect imageRect)
        {
            var a = Map(imageRect.min);
            var b = Map(imageRect.max);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        public int Vertex(Vector2 position, Color32 color, Vector2 uv)
        {
            Positions.Add(position);
            Colors.Add(color);
            Uvs.Add(uv);
            return Positions.Count - 1;
        }

        public void Triangle(int a, int b, int c)
        {
            Indices.Add(a);
            Indices.Add(b);
            Indices.Add(c);
        }

        /// <summary>Tứ giác a-b-c-d (theo thứ tự vòng).</summary>
        public void Quad(int a, int b, int c, int d)
        {
            Triangle(a, b, c);
            Triangle(c, d, a);
        }

        /// <summary>Đổ vào mesh của Graphic. Vượt trần đỉnh uGUI thì để trống (không để VertexHelper ném) và cảnh báo.</summary>
        public void CopyTo(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (Positions.Count > MAX_UI_VERTICES)
            {
                if (!_warnedOverflow)
                {
                    _warnedOverflow = true;
                    Debug.LogWarning($"[BugLogger] Nét vẽ vượt {MAX_UI_VERTICES} đỉnh — bỏ qua lớp hiển thị này.");
                }

                return;
            }

            for (var i = 0; i < Positions.Count; i++) vertexHelper.AddVert(Positions[i], Colors[i], Uvs[i]);
            for (var i = 0; i + 2 < Indices.Count; i += 3) vertexHelper.AddTriangle(Indices[i], Indices[i + 1], Indices[i + 2]);
        }

        /// <summary>
        ///     Đổ vào mesh vẽ ngoài canvas. <paramref name="linearColors" />: project Linear — canvas đổi màu đỉnh gamma →
        ///     linear trước khi tới shader UI, mesh tự vẽ phải đổi giống vậy thì màu ảnh xuất mới khớp màn hình.
        /// </summary>
        public void CopyTo(Mesh mesh, bool linearColors)
        {
            mesh.Clear();
            mesh.indexFormat = Positions.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(Positions);
            if (linearColors)
            {
                LinearColors.Clear();
                for (var i = 0; i < Colors.Count; i++) LinearColors.Add(((Color)Colors[i]).linear);
                mesh.SetColors(LinearColors);
                LinearColors.Clear();
            }
            else
            {
                mesh.SetColors(Colors);
            }

            mesh.SetUVs(0, Uvs);
            mesh.SetTriangles(Indices, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
        }

        #endregion
    }
}
