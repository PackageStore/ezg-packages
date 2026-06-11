using UnityEngine;

namespace Ezg.Package.Pooling
{
    /// <summary>
    ///     Data model representing a pooled object and its associated controller.
    /// </summary>
    /// <typeparam name="T">The type of the controller associated with the pooled game object.</typeparam>
    public class PoolingDataModel<T>
    {
        #region Fields

        /// <summary>
        ///     The GameObject of the pooled object.
        /// </summary>
        public GameObject GObject;

        /// <summary>
        ///     The controller component associated with the pooled game object.
        /// </summary>
        public T Controller;

        /// <summary>
        ///     Indicates whether this instance was newly created instead of being retrieved from the pool.
        /// </summary>
        public bool IsNewCreate;

        #endregion
    }
}