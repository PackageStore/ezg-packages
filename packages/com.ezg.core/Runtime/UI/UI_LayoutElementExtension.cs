using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ezg.Core.UI
{
    /// <summary>
    ///     Mở rộng LayoutElement, thêm max height
    /// </summary>
    [RequireComponent(typeof(LayoutElement))]
    internal class UI_LayoutElementExtension : UIBehaviour, ILayoutElement
    {
        #region Fields

        public float MaxHeight = 100;
        public float MaxWidth = 100;
        public RectTransform ContentToGrowWith;

        public int LayoutPriority = 10;

        private LayoutElement m_LayoutElement;

        #endregion

        #region Public Methods

        public float minWidth => m_LayoutElement.minWidth;
        public float preferredWidth { get; private set; }

        public float flexibleWidth => m_LayoutElement.flexibleWidth;
        public float minHeight => m_LayoutElement.minHeight;
        public float preferredHeight { get; private set; }

        public float flexibleHeight => m_LayoutElement.flexibleHeight;
        public int layoutPriority => LayoutPriority;

        /// <summary>
        ///     Calculates the horizontal layout input properties based on content width constrained by MaxWidth.
        /// </summary>
        public void CalculateLayoutInputHorizontal()
        {
            if (m_LayoutElement == null) m_LayoutElement = GetComponent<LayoutElement>();

            var contentWidth = ContentToGrowWith.sizeDelta.x;

            if (contentWidth < MaxWidth)
                preferredWidth = contentWidth;
            else
                preferredWidth = MaxWidth;
        }

        /// <summary>
        ///     Calculates the vertical layout input properties based on content height constrained by MaxHeight.
        /// </summary>
        public void CalculateLayoutInputVertical()
        {
            if (m_LayoutElement == null) m_LayoutElement = GetComponent<LayoutElement>();

            var contentHeight = ContentToGrowWith.sizeDelta.y;

            if (contentHeight < MaxHeight)
                preferredHeight = contentHeight;
            else
                preferredHeight = MaxHeight;
        }

        #endregion
    }
}