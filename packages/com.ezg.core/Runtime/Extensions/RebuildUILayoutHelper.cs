using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Ezg.Core.Extensions
{
    /// <summary>
    ///     Làm mới lại layout khi sử dụng content fitter....
    /// </summary>
    public class RebuildUILayoutHelper : MonoBehaviour
    {
        #region Public Methods

        /// <summary>
        ///     Initiates a layout rebuild on the next end of frame.
        /// </summary>
        public void RebuildLayout()
        {
            StartCoroutine(WaitOneFrameThenRebuild());
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Coroutine that waits for the end of the frame before forcing an immediate rebuild of this object's layout.
        /// </summary>
        /// <returns>IEnumerator for coroutine execution.</returns>
        private IEnumerator WaitOneFrameThenRebuild()
        {
            yield return new WaitForEndOfFrame();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
        }

        #endregion
    }
}