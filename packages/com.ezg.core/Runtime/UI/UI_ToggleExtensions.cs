#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif
using UnityEngine;
using UnityEngine.UI;

namespace Ezg.Core.UI
{
    internal class UI_ToggleExtensions : MonoBehaviour
    {
        #region Initialize

        /// <summary>
        ///     Registers a listener for the toggle button state change and initializes the current visual state.
        /// </summary>
        private void Awake()
        {
            _toggleButton.onValueChanged.AddListener(OnChange);
            OnChange(_toggleButton.isOn);
        }

        #endregion

        #region Event Handlers

        /// <summary>
        ///     Triggered when the toggle state changes; activates/deactivates the corresponding On/Off GameObjects.
        /// </summary>
        /// <param name="isOn">The current state of the toggle.</param>
        private void OnChange(bool isOn)
        {
            _onObject.SetActive(isOn);
            _offObject.SetActive(!isOn);
        }

        #endregion

        #region Fields

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")]
#endif
        private Toggle _toggleButton;

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")]
#endif
        private GameObject _onObject;

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")]
#endif
        private GameObject _offObject;

        #endregion
    }
}