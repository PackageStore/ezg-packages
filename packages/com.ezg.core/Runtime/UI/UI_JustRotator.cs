using DG.Tweening;
using UnityEngine;

namespace Ezg.Core.UI
{
    public class UI_JustRotator : MonoBehaviour
    {
        #region Public Methods

        /// <summary>
        ///     Sets the rotation speed.
        /// </summary>
        /// <param name="speed">The new rotation speed.</param>
        public void SetRotateSpeed(float speed)
        {
            rotateSpeed = speed;
        }

        #endregion

        #region Fields

        public bool canRotate = true;
        public bool isRealTime = true;
        public float rotateSpeed = 10f;

        #endregion

        #region Initialize

        /// <summary>
        ///     Starts rotation animation when enabled.
        /// </summary>
        private void OnEnable()
        {
            transform.DORotate(new Vector3(0, 0, rotateSpeed < 0 ? -360 : 360), 1f / Mathf.Abs(rotateSpeed),
                    RotateMode.FastBeyond360)
                .SetRelative()
                .SetLoops(-1, LoopType.Restart)
                .SetEase(Ease.Linear).SetUpdate(isRealTime);
        }

        /// <summary>
        ///     Kills the rotation animation when disabled.
        /// </summary>
        private void OnDisable()
        {
            transform.DOKill();
        }

        #endregion
    }
}