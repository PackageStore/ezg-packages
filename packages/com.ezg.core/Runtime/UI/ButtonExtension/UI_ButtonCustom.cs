using UnityEngine;
using UnityEngine.UI;

namespace Ezg.Core.UI
{
    public class UI_ButtonCustom : Button
    {
        #region Fields

        public GameObject DisableObject;

        #endregion

        #region Private Methods

        /// <summary>
        ///     Handles state transitions for the button. Toggles the visibility of the DisableObject and targetGraphic based on
        ///     interactability.
        /// </summary>
        /// <param name="state">The new selection state of the button.</param>
        /// <param name="instant">If true, transitions instantly without animations.</param>
        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            if (DisableObject != null)
            {
                DisableObject?.SetActive(!IsInteractable());
                targetGraphic.gameObject.SetActive(IsInteractable());
            }
        }

        #endregion
    }
}