using System;
using UnityEngine;

namespace Ezg.Package.Pooling
{
    /// <summary>
    ///     Component that implements <see cref="IPoolingModule" /> to handle pooling behavior automatically
    ///     via Unity's lifecycle events like <c>OnDisable</c> and <c>OnDestroy</c>.
    /// </summary>
    public class PoolingComponent : MonoBehaviour, IPoolingModule
    {
        #region Initialize

        /// <summary>
        ///     Called when the behaviour becomes disabled or inactive. Invokes the <see cref="ReturnPool" /> action.
        /// </summary>
        private void OnDisable()
        {
            ReturnPool?.Invoke();
        }

        /// <summary>
        ///     Called when the MonoBehaviour will be destroyed. Invokes the <see cref="Remove" /> action.
        /// </summary>
        private void OnDestroy()
        {
            Remove?.Invoke();
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Gets or sets the action to be invoked when returning this component to the pool.
        /// </summary>
        public Action ReturnPool { get; set; }

        /// <summary>
        ///     Gets or sets the action to be invoked when this component is removed from the pool.
        /// </summary>
        public Action Remove { get; set; }

        #endregion
    }
}