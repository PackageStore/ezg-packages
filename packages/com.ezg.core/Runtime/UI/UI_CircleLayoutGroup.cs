using System.Collections.Generic;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif
using UnityEngine;

namespace Ezg.Core.UI
{
    /// <summary>
    ///     Arranges child transforms evenly on a circle.
    /// </summary>
    public class UI_CircleLayoutGroup : MonoBehaviour
    {
        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")] [Title("Đường kính")]
#endif
        private float _diameter = 10f;

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")] [Title("Tự cộng thêm nửa góc")]
#endif
        private bool _autoBonusAngle;

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")] [Title("Góc cộng thêm")]
#endif
        private float _angleBonus;

        private List<Transform> items;

        /// <summary>
        ///     Rebuilds the layout when the component awakens.
        /// </summary>
        private void Awake()
        {
            OnValidate();
        }

#if ODIN_INSPECTOR
        [Button("Refresh")]
#endif
        /// <summary>
        /// Rebuilds the cached child list and reapplies the layout.
        /// </summary>
        public void OnValidate()
        {
            items = new List<Transform>();
            foreach (Transform t in transform) items.Add(t);

            if (items.Count > 0) ArrangeItems();
        }

        /// <summary>
        ///     Adds an item to the circular layout.
        /// </summary>
        /// <param name="item">The item to add.</param>
        public void InitData(GameObject item)
        {
            item.transform.SetParent(transform);
            items.Add(item.transform);
        }

        /// <summary>
        ///     Positions all items around the circle.
        /// </summary>
        public void ArrangeItems()
        {
            var angleDelta = 360f / items.Count;
            var startAngle = 0f;
            var autoBonusAngle = 0f;
            if (_autoBonusAngle) autoBonusAngle = angleDelta / 2;

            for (var i = 0; i < items.Count; i++)
            {
                var angle = startAngle - i * angleDelta + _angleBonus + (_autoBonusAngle ? autoBonusAngle : 0);
                var x = _diameter * Mathf.Cos(angle * Mathf.Deg2Rad) / 2f;
                var y = _diameter * Mathf.Sin(angle * Mathf.Deg2Rad) / 2f;
                items[i].transform.localPosition = new Vector3(x, y, 0f);
            }
        }

        /// <summary>
        ///     Updates the extra angle offset and reapplies the layout.
        /// </summary>
        /// <param name="value">The new angle bonus.</param>
        public void SetAngleBonus(float value)
        {
            _angleBonus = value;
            ArrangeItems();
        }

        /// <summary>
        ///     Updates the circle diameter.
        /// </summary>
        /// <param name="value">The new diameter.</param>
        public void SetDiameter(float value)
        {
            _diameter = value;
        }

        /// <summary>
        ///     Gets the current diameter.
        /// </summary>
        /// <returns>The diameter value.</returns>
        public float GetDiameter()
        {
            return _diameter;
        }

        /// <summary>
        ///     Gets the current angle bonus.
        /// </summary>
        /// <returns>The angle bonus value.</returns>
        public float GetAngleBonus()
        {
            return _angleBonus;
        }

        /// <summary>
        ///     Enables or disables auto angle bonus and reapplies the layout.
        /// </summary>
        /// <param name="isBonus">True to enable auto bonus angle.</param>
        public void SetAutoBonusAngle(bool isBonus)
        {
            _autoBonusAngle = isBonus;
            ArrangeItems();
        }

        /// <summary>
        ///     Gets the number of tracked items.
        /// </summary>
        /// <returns>The item count.</returns>
        public int GetItemCount()
        {
            return items.Count;
        }
    }
}