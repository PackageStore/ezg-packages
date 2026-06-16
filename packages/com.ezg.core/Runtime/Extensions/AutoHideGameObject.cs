using System.Collections;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif
using UnityEngine;

namespace Ezg.Core.Extensions
{
    public class AutoHideGameObject : MonoBehaviour
    {
        #region Initialize

        /// <summary>
        ///     Starts the automatic hiding process when the game object is enabled.
        /// </summary>
        private void OnEnable()
        {
            StartCoroutine(WaitForHideObject());
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Gets the delay time before the game object is hidden or destroyed.
        /// </summary>
        /// <returns>The delay time in seconds.</returns>
        public float GetDelayTime()
        {
            return _delayTime;
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Coroutine that waits for the specified delay time and then hides or destroys the GameObject.
        /// </summary>
        /// <returns>IEnumerator for coroutine execution.</returns>
        protected virtual IEnumerator WaitForHideObject()
        {
            if (_isRealtime)
                yield return new WaitForSecondsRealtime(_delayTime);
            else
                yield return new WaitForSeconds(_delayTime);

            if (_isDestroy)
                Destroy(gameObject);
            else
                gameObject.SetActive(false);
        }

        #endregion

        #region Fields

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")] [Title("Delay time hide")] [Required]
#endif
        protected float _delayTime;

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")] [Title("Is realtime")] [Required]
#endif
        protected bool _isRealtime;

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")] [Title("Destroy game object")] [Required]
#endif
        protected bool _isDestroy;

        #endregion
    }
}