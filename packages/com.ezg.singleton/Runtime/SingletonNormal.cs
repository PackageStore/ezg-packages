using System;

namespace Ezg.Package.Singleton
{
    /// <summary>
    ///     A standard generic thread-safe Singleton pattern implementation using Lazy initialization.
    /// </summary>
    /// <typeparam name="T">The type of the singleton class.</typeparam>
    [Serializable]
    public class SingletonNormal<T>
    {
        #region Fields

        private static readonly Lazy<T> _lazy = new(() => (T)Activator.CreateInstance(typeof(T)));

        #endregion

        #region Public Methods

        /// <summary>
        ///     Gets the single instance of type T.
        /// </summary>
        public static T Instance => _lazy.Value;

        #endregion
    }
}