using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ezg.Package.Pooling
{
    /// <summary>
    ///     Resolver for prefab lookups by path used by the path-based <see cref="PoolService" /> overloads.
    ///     Defaults to Unity's <see cref="Resources" />, keeping the package self-contained. A host project can
    ///     override <see cref="Loader" /> to route prefab resolution through its own asset pipeline
    ///     (e.g. AssetBundles / Addressables) without changing the pooling API.
    /// </summary>
    public static class PoolResources
    {
        #region Fields

        /// <summary>
        ///     The active prefab resolver. Receives the resource path and the requested type, and returns the
        ///     matching asset (or null). Defaults to <see cref="Resources.Load(string, Type)" />.
        /// </summary>
        public static Func<string, Type, Object> Loader = (path, type) => Resources.Load(path, type);

        #endregion

        #region Public Methods

        /// <summary>
        ///     Loads an asset of type <typeparamref name="T" /> from the configured <see cref="Loader" />.
        /// </summary>
        /// <typeparam name="T">The asset type, e.g. <see cref="GameObject" /> or a <see cref="Component" />.</typeparam>
        /// <param name="path">The resource path of the prefab.</param>
        /// <returns>The loaded asset if found; otherwise, null.</returns>
        public static T Load<T>(string path) where T : Object
        {
            return Loader?.Invoke(path, typeof(T)) as T;
        }

        #endregion
    }
}
