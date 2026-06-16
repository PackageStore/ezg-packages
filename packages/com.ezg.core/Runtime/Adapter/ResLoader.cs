using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Ezg.Core.Adapter
{
    /// <summary>
    ///     Static loader class providing synchronous and asynchronous methods to load assets
    ///     from Resources or AssetBundles seamlessly based on active configurations.
    /// </summary>
    public static class ResLoader
    {
#if UNITY_EDITOR

        #region Fields

        /// <summary>
        ///     Caches matching GUID asset paths for fast repeated lookups in editor.
        /// </summary>
        private static readonly Dictionary<string, string> _assetPathCache = new(256);

        /// <summary>
        ///     Caches loaded asset references in editor to avoid redundant asset database loads.
        /// </summary>
        private static readonly Dictionary<string, Object> _assetCache = new(256);

        /// <summary>
        ///     If enabled, bypasses asset bundle retrieval and loads directly from Resources/AssetDatabase.
        /// </summary>
        public static bool ForceResourcesMode = false;

        #endregion

#endif

        #region Public Methods

        /// <summary>
        ///     Synchronously loads an asset of type <typeparamref name="T" /> using an <see cref="AssetRef" />.
        /// </summary>
        /// <typeparam name="T">The type of asset, must inherit from <see cref="UnityEngine.Object" />.</typeparam>
        /// <param name="asset">The asset reference structure.</param>
        /// <returns>The loaded asset if found; otherwise, null.</returns>
        public static T Load<T>(AssetRef asset) where T : Object
        {
            return Load<T>(asset.Folder, asset.Name, asset.bundle);
        }

        /// <summary>
        ///     Synchronously loads an asset of type <typeparamref name="T" /> by its path and name.
        /// </summary>
        /// <typeparam name="T">The type of asset, must inherit from <see cref="UnityEngine.Object" />.</typeparam>
        /// <param name="path">The directory path of the asset.</param>
        /// <param name="name">The name of the asset file.</param>
        /// <param name="bundleName">Optional name of the asset bundle containing the asset.</param>
        /// <returns>The loaded asset if found; otherwise, null.</returns>
        public static T Load<T>(string path, string name, string bundleName = null) where T : Object
        {
#if UNITY_EDITOR && (USE_PAD || USE_BUNDLE)
            if (!ForceResourcesMode)
            {
                var result = !string.IsNullOrEmpty(bundleName)
                    ? AssetBundleManager.Instance.GetAssetFromBundle<T>(bundleName, name)
                    : AssetBundleManager.Instance.GetAssetFromAnyBundle<T>(name);
                if (result != null) return result;
            }
#endif
#if UNITY_EDITOR
            var editorAsset = GetAssetFromBundleEditor<T>(path, name);
            if (editorAsset != null) return editorAsset;
#endif
#if (USE_PAD || USE_BUNDLE) && !UNITY_EDITOR
        {
            var result = !string.IsNullOrEmpty(bundleName)
                ? AssetBundleManager.Instance.GetAssetFromBundle<T>(bundleName, name)
                : AssetBundleManager.Instance.GetAssetFromDownloadedBundle<T>(name);
            if (result != null) return result;
        }
#endif
            return Resources.Load<T>(path + name);
        }

        /// <summary>
        ///     Synchronously loads an asset of type <typeparamref name="T" /> by its relative path.
        /// </summary>
        /// <typeparam name="T">The type of asset, must inherit from <see cref="UnityEngine.Object" />.</typeparam>
        /// <param name="path">The relative resource path of the asset.</param>
        /// <param name="bundleName">Optional name of the asset bundle containing the asset.</param>
        /// <returns>The loaded asset if found; otherwise, null.</returns>
        public static T Load<T>(string path, string bundleName = null) where T : Object
        {
            var name = Path.GetFileName(path);
#if UNITY_EDITOR && (USE_PAD || USE_BUNDLE)
            if (!ForceResourcesMode)
            {
                var result = !string.IsNullOrEmpty(bundleName)
                    ? AssetBundleManager.Instance.GetAssetFromBundle<T>(bundleName, name)
                    : AssetBundleManager.Instance.GetAssetFromAnyBundle<T>(name);
                if (result != null) return result;
            }
#endif
#if UNITY_EDITOR
            return Resources.Load<T>(path);
#endif
#if (USE_PAD || USE_BUNDLE) && !UNITY_EDITOR
        {
            var result = !string.IsNullOrEmpty(bundleName)
                ? AssetBundleManager.Instance.GetAssetFromBundle<T>(bundleName, name)
                : AssetBundleManager.Instance.GetAssetFromDownloadedBundle<T>(name);
            if (result != null) return result;
        }
#endif
            return Resources.Load<T>(path);
        }

        /// <summary>
        ///     Synchronously loads an asset of type <typeparamref name="T" /> by its full path from an <see cref="AssetRef" />.
        /// </summary>
        /// <typeparam name="T">The type of asset, must inherit from <see cref="UnityEngine.Object" />.</typeparam>
        /// <param name="asset">The asset reference structure.</param>
        /// <returns>The loaded asset if found; otherwise, null.</returns>
        public static T LoadPath<T>(AssetRef asset) where T : Object
        {
            return LoadPath<T>(asset.Folder + asset.Name, asset.bundle);
        }

        /// <summary>
        ///     Synchronously loads an asset of type <typeparamref name="T" /> by its full lowercased path.
        /// </summary>
        /// <typeparam name="T">The type of asset, must inherit from <see cref="UnityEngine.Object" />.</typeparam>
        /// <param name="path">The path of the asset.</param>
        /// <param name="bundleName">Optional name of the asset bundle containing the asset.</param>
        /// <returns>The loaded asset if found; otherwise, null.</returns>
        public static T LoadPath<T>(string path, string bundleName = null) where T : Object
        {
            var name = path.ToLower();
#if UNITY_EDITOR && (USE_PAD || USE_BUNDLE)
            if (!ForceResourcesMode)
            {
                var result = !string.IsNullOrEmpty(bundleName)
                    ? AssetBundleManager.Instance.GetAssetFromBundleByPath<T>(bundleName, name)
                    : AssetBundleManager.Instance.GetAssetFromAnyBundle<T>(name);
                if (result != null) return result;
            }
#endif
#if UNITY_EDITOR
            return Resources.Load<T>(path);
#endif
#if (USE_PAD || USE_BUNDLE) && !UNITY_EDITOR
        {
            var result = !string.IsNullOrEmpty(bundleName)
                ? AssetBundleManager.Instance.GetAssetFromBundleByPath<T>(bundleName, name)
                : AssetBundleManager.Instance.GetAssetFromDownloadedBundle<T>(name);
            if (result != null) return result;
        }
#endif
            return Resources.Load<T>(path);
        }

        /// <summary>
        ///     Synchronously loads all assets of type <typeparamref name="T" /> under the specified path.
        /// </summary>
        /// <typeparam name="T">The type of asset, must inherit from <see cref="UnityEngine.Object" />.</typeparam>
        /// <param name="path">The resource or bundle subdirectory path.</param>
        /// <param name="bundleName">Optional name of the asset bundle.</param>
        /// <returns>An array of loaded assets of type <typeparamref name="T" />.</returns>
        public static T[] LoadAll<T>(string path, string bundleName = null) where T : Object
        {
#if UNITY_EDITOR && (USE_PAD || USE_BUNDLE)
            if (!ForceResourcesMode && !string.IsNullOrEmpty(bundleName))
            {
                var bundleAssets = AssetBundleManager.Instance.GetAllAssetsFromBundle<T>(bundleName);
                if (bundleAssets != null && bundleAssets.Length > 0) return bundleAssets;
            }
#endif
#if UNITY_EDITOR
            return Resources.LoadAll<T>(path);
#endif
#if (USE_PAD || USE_BUNDLE) && !UNITY_EDITOR
        if (!string.IsNullOrEmpty(bundleName))
        {
            var bundleAssets = AssetBundleManager.Instance.GetAllAssetsFromBundle<T>(bundleName);
            if (bundleAssets != null && bundleAssets.Length > 0) return bundleAssets;
        }
#endif
            return Resources.LoadAll<T>(path);
        }

        /// <summary>
        ///     Synchronously loads all assets of type <typeparamref name="T" /> under the specified resource path.
        /// </summary>
        /// <typeparam name="T">The type of asset, must inherit from <see cref="UnityEngine.Object" />.</typeparam>
        /// <param name="path">The relative path under Resources.</param>
        /// <returns>An array of loaded assets of type <typeparamref name="T" />.</returns>
        public static T[] LoadAll<T>(string path) where T : Object
        {
            return Resources.LoadAll<T>(path);
        }

        /// <summary>
        ///     Synchronously loads all assets under the specified resource path.
        /// </summary>
        /// <param name="path">The relative path under Resources.</param>
        /// <returns>An array of loaded <see cref="Object" />s.</returns>
        public static Object[] LoadAll(string path)
        {
            return Resources.LoadAll(path);
        }

        /// <summary>
        ///     Synchronously loads all prefab GameObjects under the specified path.
        /// </summary>
        /// <param name="path">The path under Resources or bundle.</param>
        /// <param name="bundleName">Optional name of the asset bundle.</param>
        /// <returns>An array of loaded <see cref="GameObject" /> prefabs.</returns>
        public static GameObject[] LoadAllGameObject(string path, string bundleName = null)
        {
#if UNITY_EDITOR && (USE_PAD || USE_BUNDLE)
            if (!ForceResourcesMode && !string.IsNullOrEmpty(bundleName))
            {
                var result = AssetBundleManager.Instance.LoadAllPrefabsFromBundleByPath(bundleName, path);
                if (result.Length > 0) return result;
            }
#endif
#if UNITY_EDITOR
            return Resources.LoadAll<GameObject>(path);
#endif
#if (USE_PAD || USE_BUNDLE) && !UNITY_EDITOR
        return AssetBundleManager.Instance.LoadAllPrefabsFromBundleByPath(bundleName, path);
#endif
            return Resources.LoadAll<GameObject>(path);
        }

        #endregion

        #region Async

        /// <summary>
        ///     Asynchronously loads an asset of type <typeparamref name="T" /> using an <see cref="AssetRef" />.
        /// </summary>
        /// <typeparam name="T">The type of asset, must inherit from <see cref="UnityEngine.Object" />.</typeparam>
        /// <param name="asset">The asset reference structure.</param>
        /// <returns>A UniTask returning the loaded asset if successful; otherwise, null.</returns>
        public static UniTask<T> LoadAsync<T>(AssetRef asset) where T : Object
        {
            return LoadAsync<T>(asset.Folder, asset.Name, asset.bundle);
        }

        /// <summary>
        ///     Asynchronously loads an asset of type <typeparamref name="T" /> by its relative path.
        /// </summary>
        /// <typeparam name="T">The type of asset, must inherit from <see cref="UnityEngine.Object" />.</typeparam>
        /// <param name="path">The relative resource path of the asset.</param>
        /// <param name="bundleName">Optional name of the asset bundle containing the asset.</param>
        /// <returns>A UniTask returning the loaded asset if successful; otherwise, null.</returns>
        public static async UniTask<T> LoadAsync<T>(string path, string bundleName = null) where T : Object
        {
            var name = Path.GetFileName(path);
#if UNITY_EDITOR && (USE_PAD || USE_BUNDLE)
            if (!ForceResourcesMode)
            {
                var result = !string.IsNullOrEmpty(bundleName)
                    ? AssetBundleManager.Instance.GetAssetFromBundle<T>(bundleName, name)
                    : AssetBundleManager.Instance.GetAssetFromAnyBundle<T>(name);
                if (result != null) return result;
            }
#endif
#if UNITY_EDITOR
            // Force yield to ensure async behavior in Editor matches Player execution flow.
            await UniTask.Yield();
            var resourceRequest = Resources.LoadAsync<T>(path);
            await resourceRequest;
            if (resourceRequest.asset != null) return resourceRequest.asset as T;
            var editorAsset = GetAssetFromBundleEditor<T>(path, name);
            if (editorAsset != null) return editorAsset;
#endif
#if (USE_PAD || USE_BUNDLE) && !UNITY_EDITOR
        {
            var result = !string.IsNullOrEmpty(bundleName)
                ? AssetBundleManager.Instance.GetAssetFromBundle<T>(bundleName, name)
                : AssetBundleManager.Instance.GetAssetFromDownloadedBundle<T>(name);
            if (result != null) return result;
        }
#endif
            return await Resources.LoadAsync<T>(path) as T;
        }

        /// <summary>
        ///     Asynchronously loads an asset of type <typeparamref name="T" /> by its path and name.
        /// </summary>
        /// <typeparam name="T">The type of asset, must inherit from <see cref="UnityEngine.Object" />.</typeparam>
        /// <param name="path">The directory path of the asset.</param>
        /// <param name="name">The name of the asset file.</param>
        /// <param name="bundleName">Optional name of the asset bundle containing the asset.</param>
        /// <returns>A UniTask returning the loaded asset if successful; otherwise, null.</returns>
        public static async UniTask<T> LoadAsync<T>(string path, string name, string bundleName = null)
            where T : Object
        {
#if UNITY_EDITOR && (USE_PAD || USE_BUNDLE)
            if (!ForceResourcesMode)
            {
                var result = !string.IsNullOrEmpty(bundleName)
                    ? AssetBundleManager.Instance.GetAssetFromBundle<T>(bundleName, name)
                    : AssetBundleManager.Instance.GetAssetFromAnyBundle<T>(name);
                if (result != null) return result;
            }
#endif
#if UNITY_EDITOR
            await UniTask.Yield();
            var resourceRequest = Resources.LoadAsync<T>(path + name);
            await resourceRequest;
            if (resourceRequest.asset != null) return resourceRequest.asset as T;
            var editorAsset = GetAssetFromBundleEditor<T>(path, name);
            if (editorAsset != null) return editorAsset;
#endif
#if (USE_PAD || USE_BUNDLE) && !UNITY_EDITOR
        {
            var result = !string.IsNullOrEmpty(bundleName)
                ? AssetBundleManager.Instance.GetAssetFromBundle<T>(bundleName, name)
                : AssetBundleManager.Instance.GetAssetFromDownloadedBundle<T>(name);
            if (result != null) return result;
        }
#endif
            return await Resources.LoadAsync<T>(path + name) as T;
        }

        /// <summary>
        ///     Asynchronously loads an asset of type <typeparamref name="T" /> by its full path from an <see cref="AssetRef" />.
        /// </summary>
        /// <typeparam name="T">The type of asset, must inherit from <see cref="UnityEngine.Object" />.</typeparam>
        /// <param name="asset">The asset reference structure.</param>
        /// <returns>A UniTask returning the loaded asset if successful; otherwise, null.</returns>
        public static UniTask<T> LoadPathAsync<T>(AssetRef asset) where T : Object
        {
            return LoadPathAsync<T>(asset.Folder + asset.Name, asset.bundle);
        }

        /// <summary>
        ///     Asynchronously loads an asset of type <typeparamref name="T" /> by its full lowercased path.
        /// </summary>
        /// <typeparam name="T">The type of asset, must inherit from <see cref="UnityEngine.Object" />.</typeparam>
        /// <param name="path">The path of the asset.</param>
        /// <param name="bundleName">Optional name of the asset bundle containing the asset.</param>
        /// <returns>A UniTask returning the loaded asset if successful; otherwise, null.</returns>
        public static async UniTask<T> LoadPathAsync<T>(string path, string bundleName = null) where T : Object
        {
            var name = path.ToLower();
#if UNITY_EDITOR && (USE_PAD || USE_BUNDLE)
            if (!ForceResourcesMode)
            {
                var result = !string.IsNullOrEmpty(bundleName)
                    ? AssetBundleManager.Instance.GetAssetFromBundleByPath<T>(bundleName, name)
                    : AssetBundleManager.Instance.GetAssetFromAnyBundle<T>(name);
                if (result != null) return result;
            }
#endif
#if UNITY_EDITOR
            await UniTask.Yield();
            return await Resources.LoadAsync<T>(path) as T;
#endif
#if (USE_PAD || USE_BUNDLE) && !UNITY_EDITOR
        {
            var result = !string.IsNullOrEmpty(bundleName)
                ? AssetBundleManager.Instance.GetAssetFromBundleByPath<T>(bundleName, name)
                : AssetBundleManager.Instance.GetAssetFromDownloadedBundle<T>(name);
            if (result != null) return result;
        }
#endif
            return await Resources.LoadAsync<T>(path) as T;
        }

        #endregion

#if UNITY_EDITOR

        #region Public Methods

        /// <summary>
        ///     Retrieves or loads an asset of type <typeparamref name="T" /> in the editor environment.
        ///     Uses GUID-based search and cached paths to optimize load performance.
        /// </summary>
        /// <typeparam name="T">The type of asset, must inherit from <see cref="UnityEngine.Object" />.</typeparam>
        /// <param name="path">The folder path of the asset.</param>
        /// <param name="name">The name of the asset.</param>
        /// <returns>The loaded asset if found; otherwise, null.</returns>
        public static T GetAssetFromBundleEditor<T>(string path, string name) where T : Object
        {
            var cacheKey = BuildCacheKey(path, name, typeof(T).Name);
            if (TryGetCachedAsset(cacheKey, out T cachedAsset)) return cachedAsset;
            if (TryGetCachedPath(cacheKey, out var cachedPath)) return LoadAndCacheAsset<T>(cacheKey, cachedPath);
            var assetPath = FindAssetPath(path, name, typeof(T).Name);
            if (string.IsNullOrEmpty(assetPath)) return null;
            CacheAssetPath(cacheKey, assetPath);
            return LoadAndCacheAsset<T>(cacheKey, assetPath);
        }

        /// <summary>
        ///     Clears all editor-side cached paths and loaded assets.
        /// </summary>
        public static void ClearCache()
        {
            _assetPathCache.Clear();
            _assetCache.Clear();
        }

        /// <summary>
        ///     Removes a specific asset from the editor path and object caches.
        /// </summary>
        /// <param name="path">The folder path of the asset.</param>
        /// <param name="name">The name of the asset.</param>
        /// <param name="typeName">The name of the asset type.</param>
        public static void RemoveFromCache(string path, string name, string typeName)
        {
            var cacheKey = BuildCacheKey(path, name, typeName);
            _assetPathCache.Remove(cacheKey);
            _assetCache.Remove(cacheKey);
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Builds a unique string key used to cache and retrieve editor assets.
        /// </summary>
        private static string BuildCacheKey(string path, string name, string typeName)
        {
            return $"{path}|{name}|{typeName}";
        }

        /// <summary>
        ///     Attempts to retrieve a loaded asset from the editor cache.
        /// </summary>
        private static bool TryGetCachedAsset<T>(string cacheKey, out T asset) where T : Object
        {
            if (_assetCache.TryGetValue(cacheKey, out var cachedObj) && cachedObj != null)
            {
                asset = cachedObj as T;
                return asset != null;
            }

            asset = null;
            return false;
        }

        /// <summary>
        ///     Attempts to retrieve a cached asset path.
        /// </summary>
        private static bool TryGetCachedPath(string cacheKey, out string path)
        {
            return _assetPathCache.TryGetValue(cacheKey, out path) && !string.IsNullOrEmpty(path);
        }

        /// <summary>
        ///     Caches a resolved asset path.
        /// </summary>
        private static void CacheAssetPath(string cacheKey, string assetPath)
        {
            _assetPathCache[cacheKey] = assetPath;
        }

        /// <summary>
        ///     Loads an asset from a given path and caches it.
        /// </summary>
        private static T LoadAndCacheAsset<T>(string cacheKey, string assetPath) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null) _assetCache[cacheKey] = asset;
            return asset;
        }

        /// <summary>
        ///     Resolves the actual asset database path for a given name and type.
        /// </summary>
        private static string FindAssetPath(string path, string name, string typeName)
        {
            var filter = $"{name} t:{typeName}";
            var guids = AssetDatabase.FindAssets(filter);
            var normalizedPath = string.IsNullOrEmpty(path) ? null : path.Replace("\\", "/");
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (normalizedPath != null && !assetPath.Replace("\\", "/").Contains(normalizedPath)) continue;
                return assetPath;
            }

            return null;
        }

        #endregion

#endif
    }
}