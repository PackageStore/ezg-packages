using UnityEngine;

namespace Ezg.BugLogger.UI
{
    /// <summary>
    ///     Co RectTransform (kéo giãn full canvas) vào <see cref="Screen.safeArea" /> — tai thỏ, bo góc, thanh home.
    ///     Tự cập nhật khi xoay màn hình / đổi độ phân giải.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    internal sealed class BugLoggerSafeArea : MonoBehaviour
    {
        #region Fields

        private RectTransform _rect;
        private Rect _appliedArea;
        private Vector2Int _appliedScreen;

        #endregion

        #region Initialize

        private void Awake()
        {
            _rect = (RectTransform)transform;
        }

        private void OnEnable()
        {
            Apply();
        }

        private void Update()
        {
            if (_appliedArea != Screen.safeArea || _appliedScreen.x != Screen.width ||
                _appliedScreen.y != Screen.height)
                Apply();
        }

        #endregion

        #region Private Methods

        private void Apply()
        {
            var area = Screen.safeArea;
            var width = Mathf.Max(1, Screen.width);
            var height = Mathf.Max(1, Screen.height);

            _appliedArea = area;
            _appliedScreen = new Vector2Int(Screen.width, Screen.height);

            _rect.anchorMin = new Vector2(area.xMin / width, area.yMin / height);
            _rect.anchorMax = new Vector2(area.xMax / width, area.yMax / height);
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }

        #endregion
    }
}
