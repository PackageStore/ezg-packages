#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Debug tab UI: các tùy chọn kiểm tra / gỡ lỗi cho road builder (boundary lớp đường
    /// road/highway và boundary khối station/parking, bật tắt riêng).</summary>
    internal sealed class DebugPanel
    {
        private const string DebugBoundaryAlphaPrefKey = "VisualRoadBuilder.DebugBoundaryAlpha";

        private readonly ViewState _view;
        private readonly EditorWindow _host;

        internal DebugPanel(ViewState view, EditorWindow host)
        {
            _view = view;
            _host = host;
        }

        /// <summary>Boundary mặc định BẬT. Field initializer chỉ ăn với window tạo mới, nên window đã
        /// serialize từ bản cũ (2 cờ = false) được bật đúng MỘT lần ở đây — tắt tay sau đó vẫn giữ.</summary>
        internal void ApplyDebugBoundaryDefault()
        {
            if (_view.DebugBoundaryDefaultApplied) return;
            _view.DebugBoundaryDefaultApplied = true;
            _view.ShowDebugBoundary = true;
            _view.ShowDebugBlockBoundary = true;
        }

        /// <summary>Alpha nhớ qua EditorPrefs (mặc định 100% khi chưa có pref). [SerializeField] KHÔNG đủ:
        /// đóng/mở lại window hay restart Editor đều dựng instance mới với mọi field về default nên slider
        /// luôn nhảy về 100% — pref sống ngoài window nên giữ được mức người dùng đã chọn.</summary>
        internal void LoadDebugBoundaryAlpha()
        {
            _view.DebugBoundaryAlpha = Mathf.Clamp01(EditorPrefs.GetFloat(DebugBoundaryAlphaPrefKey, 1f));
        }

        internal void DrawDebugTab()
        {
            EditorGUILayout.LabelField("Debug Controls", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUI.BeginChangeCheck();
            _view.ShowDebugBoundary = EditorGUILayout.ToggleLeft("Show boundary (road, highway, road 2)", _view.ShowDebugBoundary);
            _view.ShowDebugBlockBoundary = EditorGUILayout.ToggleLeft("Show boundary (station, parking)", _view.ShowDebugBlockBoundary);

            using (new EditorGUI.DisabledScope(!AnyDebugBoundary))
            {
                int pct = Mathf.RoundToInt(_view.DebugBoundaryAlpha * 100f);
                pct = EditorGUILayout.IntSlider(
                    new GUIContent("Boundary alpha (%)",
                        "Độ mờ của MỌI box boundary (road, highway, station, parking). 0% = chỉ còn box " +
                        "dưới con trỏ (highlight + tooltip luôn giữ nguyên độ đậm)."),
                    pct, 0, 100);
                float alpha = pct / 100f;
                if (!Mathf.Approximately(alpha, _view.DebugBoundaryAlpha))
                {
                    _view.DebugBoundaryAlpha = alpha;
                    EditorPrefs.SetFloat(DebugBoundaryAlphaPrefKey, alpha);
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                _host.Repaint();
            }

            EditorGUILayout.EndVertical();
        }

        internal bool AnyDebugBoundary => _view.ShowDebugBoundary || _view.ShowDebugBlockBoundary;
    }
}
#endif
