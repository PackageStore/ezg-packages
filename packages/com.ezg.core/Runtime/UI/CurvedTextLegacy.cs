using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Ezg.Core.UI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Text))]
    public class CurvedTextLegacy : BaseMeshEffect
    {
        #region Public Methods

        /// <summary>
        ///     Modifies the text mesh to align the characters along the configured AnimationCurve.
        /// </summary>
        /// <param name="vh">The VertexHelper providing access to vertex and mesh generation.</param>
        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;

            var verts = new List<UIVertex>();
            vh.GetUIVertexStream(verts);

            // Find global text bounds
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (var i = 0; i < verts.Count; i++)
            {
                var v = verts[i].position;
                if (v.x < minX) minX = v.x;
                if (v.x > maxX) maxX = v.x;
                if (v.y < minY) minY = v.y;
                if (v.y > maxY) maxY = v.y;
            }

            var width = Mathf.Max(0.0001f, maxX - minX);
            var anchorY = Mathf.Lerp(minY, maxY, baselineAnchor);

            // Process each character (6 verts per quad)
            var charCount = verts.Count / VERTS_PER_CHAR;
            for (var c = 0; c < charCount; c++)
            {
                var startIdx = c * VERTS_PER_CHAR;

                // Find character center X
                float charMinX = float.MaxValue, charMaxX = float.MinValue;
                float charMinY = float.MaxValue, charMaxY = float.MinValue;
                for (var j = 0; j < VERTS_PER_CHAR; j++)
                {
                    var p = verts[startIdx + j].position;
                    if (p.x < charMinX) charMinX = p.x;
                    if (p.x > charMaxX) charMaxX = p.x;
                    if (p.y < charMinY) charMinY = p.y;
                    if (p.y > charMaxY) charMaxY = p.y;
                }

                var charCenterX = (charMinX + charMaxX) * 0.5f;
                var charCenterY = (charMinY + charMaxY) * 0.5f;

                // Evaluate curve at character center
                var t = Mathf.Clamp01((charCenterX - minX) / width);
                var yOffset = curve.Evaluate(t) * curveScale;

                // Calculate rotation angle from tangent
                var angle = 0f;
                if (rotateWithTangent)
                {
                    var sampleDelta = 0.001f;
                    var t0 = Mathf.Clamp01(t - sampleDelta);
                    var t1 = Mathf.Clamp01(t + sampleDelta);
                    var dy = (curve.Evaluate(t1) - curve.Evaluate(t0)) * curveScale;
                    var dx = Mathf.Max(0.0001f, (t1 - t0) * width);
                    angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg * rotateAmount;
                }

                var rad = angle * Mathf.Deg2Rad;
                var cosA = Mathf.Cos(rad);
                var sinA = Mathf.Sin(rad);

                // Apply offset + rotation to each vertex of this character
                for (var j = 0; j < VERTS_PER_CHAR; j++)
                {
                    var uiV = verts[startIdx + j];
                    var p = uiV.position;

                    // Translate to character center
                    var lx = p.x - charCenterX;
                    var ly = p.y - charCenterY;

                    // Rotate around character center
                    var rx = lx * cosA - ly * sinA;
                    var ry = lx * sinA + ly * cosA;

                    // Translate back + apply curve offset
                    p.x = charCenterX + rx;
                    p.y = charCenterY + ry + yOffset;

                    uiV.position = p;
                    verts[startIdx + j] = uiV;
                }
            }

            vh.Clear();
            vh.AddUIVertexTriangleStream(verts);
        }

        #endregion

        #region Fields

        [Header("Curve (x: 0..1 across text, y: offset)")]
        public AnimationCurve curve = new(new Keyframe(0, 0), new Keyframe(1, 0));

        public float curveScale = 20f;

        [Header("Advanced")] [Range(0f, 1f)] public float baselineAnchor = 0.5f;

        [Tooltip("Rotate each character to follow the curve tangent (like Photoshop Arc)")]
        public bool rotateWithTangent = true;

        [Range(0f, 1f)] public float rotateAmount = 1f;

        private const int VERTS_PER_CHAR = 6; // 2 triangles per quad in triangle stream

        private RectTransform _rt;

        #endregion

        #region Initialize

        /// <summary>
        ///     Initializes references and marks the graphic vertices as dirty to trigger a rebuild on enable.
        /// </summary>
        private void OnEnable()
        {
            _rt = GetComponent<RectTransform>();
            graphic.SetVerticesDirty();
        }

#if UNITY_EDITOR
        /// <summary>
        ///     Validates the AnimationCurve wrap modes and marks the graphic vertices as dirty in the editor.
        /// </summary>
        private void OnValidate()
        {
            curve.postWrapMode = WrapMode.ClampForever;
            curve.preWrapMode = WrapMode.ClampForever;
            if (graphic) graphic.SetVerticesDirty();
        }
#endif

        #endregion
    }
}