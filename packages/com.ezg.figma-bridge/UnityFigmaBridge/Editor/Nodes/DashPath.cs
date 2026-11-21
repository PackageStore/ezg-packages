using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityFigmaBridge.Editor.Nodes
{
    /// <summary>The centre line of a rounded box, clockwise from the top edge, as edges and quarter arcs with a dash pattern each.</summary>
    internal sealed class DashPath
    {
        private struct Segment
        {
            public Vector2 Start, Direction, Centre;
            public float Radius, StartAngle, Length, Scale;
        }

        private readonly List<Segment> m_Segments = new();
        private readonly float[] m_Dashes;
        private readonly float m_Period, m_Extend;
        private readonly bool m_RoundCap;

        /// <param name="cap">Figma <c>strokeCap</c>: ROUND ends each dash with a half disc, SQUARE extends it by half the weight, anything else is flat.</param>
        public DashPath(Rect box, Vector4 radii, float[] dashes, string cap, float halfWeight)
        {
            m_Dashes = dashes;
            m_Period = dashes.Sum();
            m_RoundCap = cap == "ROUND";
            m_Extend = cap == "SQUARE" ? halfWeight : 0f;
            Line(new Vector2(box.xMin + radii.x, box.yMin), new Vector2(box.xMax - radii.y, box.yMin));
            Arc(new Vector2(box.xMax - radii.y, box.yMin + radii.y), radii.y, -Mathf.PI * 0.5f);
            Line(new Vector2(box.xMax, box.yMin + radii.y), new Vector2(box.xMax, box.yMax - radii.z));
            Arc(new Vector2(box.xMax - radii.z, box.yMax - radii.z), radii.z, 0f);
            Line(new Vector2(box.xMax - radii.z, box.yMax), new Vector2(box.xMin + radii.w, box.yMax));
            Arc(new Vector2(box.xMin + radii.w, box.yMax - radii.w), radii.w, Mathf.PI * 0.5f);
            Line(new Vector2(box.xMin, box.yMax - radii.w), new Vector2(box.xMin, box.yMin + radii.x));
            Arc(new Vector2(box.xMin + radii.x, box.yMin + radii.x), radii.x, Mathf.PI);
        }

        private void Line(Vector2 a, Vector2 b) => Add(new Segment { Start = a, Direction = (b - a).normalized, Length = Vector2.Distance(a, b) });

        private void Arc(Vector2 centre, float radius, float startAngle)
        {
            if (radius > 0.01f) Add(new Segment { Centre = centre, Radius = radius, StartAngle = startAngle, Length = radius * Mathf.PI * 0.5f });
        }

        private void Add(Segment segment)
        {
            if (segment.Length < 0.01f) return;
            // A whole number of cycles per segment, the cycle stretched to fit
            segment.Scale = segment.Length / (Mathf.Max(1f, Mathf.Round(segment.Length / m_Period)) * m_Period);
            m_Segments.Add(segment);
        }

        private static Vector2 At(Segment s, float t) => s.Radius > 0f
            ? s.Centre + s.Radius * new Vector2(Mathf.Cos(s.StartAngle + t / s.Radius), Mathf.Sin(s.StartAngle + t / s.Radius))
            : s.Start + s.Direction * t;

        /// <summary>Distance from p to the nearest dash: to the centre line along a dash, to its end point past it (round cap).</summary>
        public float Distance(Vector2 p)
        {
            var best = float.MaxValue;
            foreach (var s in m_Segments)
            {
                float t;
                if (s.Radius > 0f)
                {
                    var v = p - s.Centre;
                    var angle = Mathf.Repeat(Mathf.Atan2(v.y, v.x) - s.StartAngle, Mathf.PI * 2f);
                    t = angle * s.Radius;
                    if (t > s.Length) t = Vector2.Distance(p, At(s, 0f)) < Vector2.Distance(p, At(s, s.Length)) ? 0f : s.Length;
                }
                else t = Mathf.Clamp(Vector2.Dot(p - s.Start, s.Direction), 0f, s.Length);

                var onLine = Vector2.Distance(p, At(s, t));
                // Pattern position: the first dash is centred on the segment's start joint
                var u = Mathf.Repeat(t / s.Scale + m_Dashes[0] * 0.5f, m_Period);
                var cycleStart = t - u * s.Scale;
                var offset = 0f;
                for (var i = 0; i < m_Dashes.Length; i += 2)
                {
                    float dashStart = cycleStart + offset * s.Scale, dashEnd = dashStart + m_Dashes[i] * s.Scale;
                    if (t >= dashStart - m_Extend && t <= dashEnd + m_Extend) best = Mathf.Min(best, onLine);
                    else if (m_RoundCap)
                    {
                        if (dashStart >= 0f && dashStart <= s.Length) best = Mathf.Min(best, Vector2.Distance(p, At(s, dashStart)));
                        if (dashEnd >= 0f && dashEnd <= s.Length) best = Mathf.Min(best, Vector2.Distance(p, At(s, dashEnd)));
                    }
                    offset += m_Dashes[i] + m_Dashes[i + 1];
                }
                if (!m_RoundCap) continue;
                // Caps of the dashes either side of this cycle: the previous cycle's last end and the next cycle's first start
                var previousEnd = cycleStart - m_Dashes[m_Dashes.Length - 1] * s.Scale;
                var nextStart = cycleStart + m_Period * s.Scale;
                if (previousEnd >= 0f && previousEnd <= s.Length) best = Mathf.Min(best, Vector2.Distance(p, At(s, previousEnd)));
                if (nextStart >= 0f && nextStart <= s.Length) best = Mathf.Min(best, Vector2.Distance(p, At(s, nextStart)));
            }
            return best;
        }
    }
}
