using UnityEngine;
using UnityEngine.UI;

namespace Ezg.Core.UI
{
    public class UI_GridLayoutGroup : GridLayoutGroup
    {
        #region Private Methods

        /// <summary>
        ///     Core positioning and layout logic to calculate driven properties and align cells along the given axis.
        /// </summary>
        /// <param name="axis">0 for horizontal axis calculation, 1 for vertical axis calculation.</param>
        private void SetCellsAlongAxis(int axis)
        {
            // Normally a Layout Controller should only set horizontal values when invoked for the horizontal axis
            // and only vertical values when invoked for the vertical axis.
            // However, in this case we set both the horizontal and vertical position when invoked for the vertical axis.
            // Since we only set the horizontal position and not the size, it shouldn't affect children's layout,
            // and thus shouldn't break the rule that all horizontal layout must be calculated before all vertical layout.
            var rectChildrenCount = rectChildren.Count;
            if (axis == 0)
            {
                // Only set the sizes when invoked for horizontal axis, not the positions.

                for (var i = 0; i < rectChildrenCount; i++)
                {
                    var rect = rectChildren[i];

                    m_Tracker.Add(this, rect,
                        DrivenTransformProperties.Anchors |
                        DrivenTransformProperties.AnchoredPosition |
                        DrivenTransformProperties.SizeDelta);

                    rect.anchorMin = Vector2.up;
                    rect.anchorMax = Vector2.up;
                    rect.sizeDelta = cellSize;
                }

                return;
            }

            var width = rectTransform.rect.size.x;
            var height = rectTransform.rect.size.y;

            var cellCountX = 1;
            var cellCountY = 1;
            if (m_Constraint == Constraint.FixedColumnCount)
            {
                cellCountX = m_ConstraintCount;

                if (rectChildrenCount > cellCountX)
                    cellCountY = rectChildrenCount / cellCountX + (rectChildrenCount % cellCountX > 0 ? 1 : 0);
            }
            else if (m_Constraint == Constraint.FixedRowCount)
            {
                cellCountY = m_ConstraintCount;

                if (rectChildrenCount > cellCountY)
                    cellCountX = rectChildrenCount / cellCountY + (rectChildrenCount % cellCountY > 0 ? 1 : 0);
            }
            else
            {
                if (cellSize.x + spacing.x <= 0)
                    cellCountX = int.MaxValue;
                else
                    cellCountX = Mathf.Max(1,
                        Mathf.FloorToInt((width - padding.horizontal + spacing.x + 0.001f) / (cellSize.x + spacing.x)));

                if (cellSize.y + spacing.y <= 0)
                    cellCountY = int.MaxValue;
                else
                    cellCountY = Mathf.Max(1,
                        Mathf.FloorToInt((height - padding.vertical + spacing.y + 0.001f) / (cellSize.y + spacing.y)));
            }

            var cornerX = (int)startCorner % 2;
            var cornerY = (int)startCorner / 2;

            int cellsPerMainAxis, actualCellCountX, actualCellCountY;
            if (startAxis == Axis.Horizontal)
            {
                cellsPerMainAxis = cellCountX;
                actualCellCountX = Mathf.Clamp(cellCountX, 1, rectChildrenCount);
                actualCellCountY = Mathf.Clamp(cellCountY, 1,
                    Mathf.CeilToInt(rectChildrenCount / (float)cellsPerMainAxis));
            }
            else
            {
                cellsPerMainAxis = cellCountY;
                actualCellCountY = Mathf.Clamp(cellCountY, 1, rectChildrenCount);
                actualCellCountX = Mathf.Clamp(cellCountX, 1,
                    Mathf.CeilToInt(rectChildrenCount / (float)cellsPerMainAxis));
            }

            var lastCellsCount = rectChildrenCount % cellsPerMainAxis;

            var requiredSpace = new Vector2(
                actualCellCountX * cellSize.x + (actualCellCountX - 1) * spacing.x,
                actualCellCountY * cellSize.y + (actualCellCountY - 1) * spacing.y
            );
            var startOffset = new Vector2(
                GetStartOffset(0, requiredSpace.x),
                GetStartOffset(1, requiredSpace.y)
            );

            var actualLastCellsCount = lastCellsCount == 0 ? cellsPerMainAxis : lastCellsCount;
            var cellsX = startAxis == Axis.Horizontal ? actualLastCellsCount : actualCellCountX;
            var cellsY = startAxis == Axis.Vertical ? actualLastCellsCount : actualCellCountY;

            var lastCellsRequiredSpace = new Vector2(
                cellsX * cellSize.x + (cellsX - 1) * spacing.x,
                cellsY * cellSize.y + (cellsY - 1) * spacing.y
            );

            var lastCellsStartOffset = new Vector2(
                GetStartOffset(0, lastCellsRequiredSpace.x),
                GetStartOffset(1, lastCellsRequiredSpace.y)
            );

            for (var i = 0; i < rectChildrenCount; i++)
            {
                int positionX;
                int positionY;
                var cellStartOffset = i + 1 > rectChildrenCount - actualLastCellsCount
                    ? lastCellsStartOffset
                    : startOffset;

                if (startAxis == Axis.Horizontal)
                {
                    positionX = i % cellsPerMainAxis;
                    positionY = i / cellsPerMainAxis;
                }
                else
                {
                    positionX = i / cellsPerMainAxis;
                    positionY = i % cellsPerMainAxis;
                }

                if (cornerX == 1)
                    positionX = actualCellCountX - 1 - positionX;
                if (cornerY == 1)
                    positionY = actualCellCountY - 1 - positionY;

                SetChildAlongAxis(rectChildren[i], 0, cellStartOffset.x + (cellSize[0] + spacing[0]) * positionX,
                    cellSize[0]);
                SetChildAlongAxis(rectChildren[i], 1, cellStartOffset.y + (cellSize[1] + spacing[1]) * positionY,
                    cellSize[1]);
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Sets the horizontal layout properties and positions cell sizes for the grid group.
        /// </summary>
        public override void SetLayoutHorizontal()
        {
            SetCellsAlongAxis(0);
        }

        /// <summary>
        ///     Sets the vertical layout properties and positions cell sizes for the grid group.
        /// </summary>
        public override void SetLayoutVertical()
        {
            SetCellsAlongAxis(1);
        }

        #endregion
    }
}