using UnityEngine;
using UnityEngine.UI;

namespace Ezg.BugLogger.UI
{
    /// <summary>
    ///     Layout xếp chip theo dòng, hết chỗ thì xuống dòng (uGUI không có sẵn). Con tự quyết bề rộng (preferred
    ///     width), cao theo <see cref="_rowHeight" />; chiều cao của group = tổng các dòng.
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class BugLoggerFlowLayout : LayoutGroup
    {
        #region Fields

        [SerializeField] private float _spacingX = 16f;
        [SerializeField] private float _spacingY = 16f;
        [SerializeField] private float _rowHeight = 72f;

        #endregion

        #region Layout

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            SetLayoutInputForAxis(padding.horizontal, padding.horizontal, -1f, 0);
        }

        public override void CalculateLayoutInputVertical()
        {
            var height = Arrange(rectTransform.rect.width, false);
            SetLayoutInputForAxis(height, height, -1f, 1);
        }

        public override void SetLayoutHorizontal()
        {
            Arrange(rectTransform.rect.width, true);
        }

        public override void SetLayoutVertical()
        {
            Arrange(rectTransform.rect.width, true);
        }

        #endregion

        #region Private Methods

        private float Arrange(float width, bool apply)
        {
            var maxRowWidth = Mathf.Max(0f, width - padding.horizontal);
            var x = 0f;
            var row = 0;

            for (var i = 0; i < rectChildren.Count; i++)
            {
                var child = rectChildren[i];
                var childWidth = Mathf.Min(LayoutUtility.GetPreferredWidth(child), maxRowWidth);

                if (x > 0f && x + childWidth > maxRowWidth)
                {
                    row++;
                    x = 0f;
                }

                if (apply)
                {
                    var y = padding.top + row * (_rowHeight + _spacingY);
                    SetChildAlongAxis(child, 0, padding.left + x, childWidth);
                    SetChildAlongAxis(child, 1, y, _rowHeight);
                }

                x += childWidth + _spacingX;
            }

            var rows = rectChildren.Count == 0 ? 0 : row + 1;
            return padding.vertical + rows * _rowHeight + Mathf.Max(0, rows - 1) * _spacingY;
        }

        #endregion
    }
}
