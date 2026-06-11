using System;

namespace Ezg.Package.Pooling
{
    /// <summary>
    ///     Interface for a pooling module that defines pooling behavior, such as a callback to return to the pool.
    /// </summary>
    public interface IPoolingModule
    {
        #region Public Methods

        /// <summary>
        ///     Gets or sets the action to be invoked when returning to the pool.
        /// </summary>
        public Action ReturnPool { get; set; }

        #endregion
    }
}