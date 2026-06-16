using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Ezg.Package.Singleton;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;
#if USE_PAD && UNITY_ANDROID && !UNITY_EDITOR
using Google.Play.AssetDelivery;
#endif

namespace Ezg.Core.Adapter
{
    /// <summary>
    ///     Manages AssetBundle loading and retrieval across different platforms.
    ///     Mode switching is controlled via Scripting Define Symbols:
    ///     USE_PAD    — Play Asset Delivery (Android device, production)
    ///     USE_BUNDLE — Local bundle files (Editor for Android/iOS, iOS device StreamingAssets, Android device without PAD)
    ///     (none)     — Resources.Load (fallback mode)
    /// </summary>
    public class AssetBundleManager : Singleton<AssetBundleManager>
    {
        #region Fields

        /// <summary>
        ///     Dictionary containing loaded asset bundles mapped by their name.
        /// </summary>
        private readonly Dictionary<string, AssetBundle> loadedBundles = new();

        /// <summary>
        ///     Cache for individual loaded assets to speed up retrieval.
        /// </summary>
        private readonly Dictionary<string, Object> assetCache = new();

        /// <summary>
        ///     Cache for loaded sprite atlases.
        /// </summary>
        private readonly Dictionary<string, SpriteAtlas> spriteAtlases = new();

        /// <summary>
        ///     Tracks active load tasks to avoid redundant parallel load calls.
        /// </summary>
        private readonly Dictionary<string, UniTask<bool>> pendingLoads = new();

        /// <summary>
        ///     Global cancellation token source for all active async tasks.
        /// </summary>
        private CancellationTokenSource cts;

#if USE_BUNDLE && UNITY_EDITOR
        /// <summary>
        ///     Path to asset bundles in the Unity Editor environment.
        /// </summary>
        private static readonly string EditorBundlePath =
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "AssetBundles", "Android"));
#endif

        #endregion

        #region Initialize

        /// <summary>
        ///     Initializes the cancellation token source on component wakeup.
        /// </summary>
        private void Awake()
        {
            cts = new CancellationTokenSource();
        }

        /// <summary>
        ///     Cancels active tasks and unloads all loaded asset bundles upon destruction.
        /// </summary>
        private void OnDestroy()
        {
            cts?.Cancel();
            cts?.Dispose();
            foreach (var kvp in loadedBundles) kvp.Value.Unload(true);
            loadedBundles.Clear();
            assetCache.Clear();
            spriteAtlases.Clear();
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Cancels all ongoing loading tasks and resets the global cancellation token.
        /// </summary>
        public void CancelAllTasks()
        {
            cts?.Cancel();
            cts?.Dispose();
            cts = new CancellationTokenSource();
        }

        /// <summary>
        ///     Resets all loaded bundle data, clearing caches and unloading existing bundles.
        /// </summary>
        public void ResetData()
        {
            foreach (var kvp in loadedBundles) kvp.Value?.Unload(false);
            loadedBundles.Clear();
            assetCache.Clear();
            spriteAtlases.Clear();
            pendingLoads.Clear();
        }

        /// <summary>
        ///     Registers an asset bundle in the loaded bundles directory.
        /// </summary>
        /// <param name="bundleName">The name of the bundle.</param>
        /// <param name="bundle">The asset bundle instance.</param>
        public void RegisterBundle(string bundleName, AssetBundle bundle)
        {
            if (bundle == null) return;
            if (loadedBundles.TryGetValue(bundleName, out var existing))
            {
                if (existing == bundle) return;
                existing.Unload(true);
            }

            loadedBundles[bundleName] = bundle;
            Debug.Log($"[AssetBundleManager] Registered: {bundleName}");
        }

        /// <summary>
        ///     Gets a loaded asset bundle by its name.
        /// </summary>
        /// <param name="bundleName">The name of the bundle.</param>
        /// <returns>The loaded <see cref="AssetBundle" /> if found; otherwise, null.</returns>
        public AssetBundle GetBundle(string bundleName)
        {
            return loadedBundles.GetValueOrDefault(bundleName);
        }

        /// <summary>
        ///     Checks if a bundle is currently loaded.
        /// </summary>
        /// <param name="bundleName">The name of the bundle.</param>
        /// <returns>True if loaded; otherwise, false.</returns>
        public bool IsBundleLoaded(string bundleName)
        {
            return loadedBundles.ContainsKey(bundleName);
        }

        /// <summary>
        ///     Backward-compatibility alias for checking if a feature bundle is loaded.
        /// </summary>
        /// <param name="bundleName">The name of the bundle.</param>
        /// <returns>True if loaded; otherwise, false.</returns>
        public bool IsFeatureBundleLoaded(string bundleName)
        {
            return IsBundleLoaded(bundleName);
        }

        /// <summary>
        ///     Unloads a registered asset bundle.
        /// </summary>
        /// <param name="bundleName">The name of the bundle to unload.</param>
        /// <param name="unloadAllObjects">If true, unloads all loaded assets belonging to this bundle as well.</param>
        public void UnloadBundle(string bundleName, bool unloadAllObjects = false)
        {
            if (!loadedBundles.TryGetValue(bundleName, out var bundle)) return;
            bundle.Unload(unloadAllObjects);
            loadedBundles.Remove(bundleName);
            spriteAtlases.Remove(bundleName);
            if (unloadAllObjects) assetCache.Clear();
            Debug.Log($"[AssetBundleManager] Unloaded: {bundleName}");
        }

        /// <summary>
        ///     Loads an asset bundle asynchronously by its name. Supports co-operative task reuse.
        /// </summary>
        /// <param name="bundleName">The name of the bundle.</param>
        /// <param name="onProgress">Optional progress reporter.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A UniTask returning true if successful; otherwise, false.</returns>
        public async UniTask<bool> LoadBundleAsync(string bundleName, IProgress<float> onProgress = null,
            CancellationToken cancellationToken = default)
        {
            var token = ResolveToken(cancellationToken);
            if (loadedBundles.ContainsKey(bundleName)) return true;
            if (pendingLoads.TryGetValue(bundleName, out var pending)) return await pending;

            var tcs = new UniTaskCompletionSource<bool>();
            pendingLoads[bundleName] = tcs.Task;

            var success = false;
            try
            {
#if USE_PAD && UNITY_ANDROID && !UNITY_EDITOR
            success = await LoadBundlePAD(bundleName, onProgress, token);
#elif USE_BUNDLE
                success = await LoadBundleFromFile(bundleName, onProgress, token);
#else
            // Resources fallback mode: no bundle loading needed
            success = true;
#endif
                if (success) await LoadSpriteAtlas(bundleName, token);
            }
            finally
            {
                pendingLoads.Remove(bundleName);
                tcs.TrySetResult(success);
            }

            return success;
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Resolves the cancellation token, using the global token if none is specified.
        /// </summary>
        /// <param name="ct">The caller-specified cancellation token.</param>
        /// <returns>A valid <see cref="CancellationToken" />.</returns>
        private CancellationToken ResolveToken(CancellationToken ct)
        {
            return ct == CancellationToken.None ? cts.Token : ct;
        }

#if USE_BUNDLE
        /// <summary>
        ///     Loads an asset bundle from the local streaming assets or editor bundle file path.
        /// </summary>
        /// <param name="bundleName">The name of the bundle.</param>
        /// <param name="onProgress">Progress callback.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A UniTask returning true if loaded successfully; otherwise, false.</returns>
        private async UniTask<bool> LoadBundleFromFile(string bundleName, IProgress<float> onProgress,
            CancellationToken ct)
        {
#if UNITY_EDITOR
            var path = Path.Combine(EditorBundlePath, bundleName.ToLower());
#else
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, bundleName.ToLower());
#endif
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[AssetBundleManager] Bundle not found: {path}");
                return false;
            }

            var req = AssetBundle.LoadFromFileAsync(path);
            while (!req.isDone)
            {
                ct.ThrowIfCancellationRequested();
                onProgress?.Report(req.progress);
                await UniTask.Yield(ct);
            }

            if (req.assetBundle != null)
            {
                RegisterBundle(bundleName, req.assetBundle);
                return true;
            }

            Debug.LogWarning($"[AssetBundleManager] Failed to load bundle: {bundleName}");
            return false;
        }
#endif

#if USE_PAD && UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>
    /// Loads an asset bundle via Google Play Asset Delivery (PAD).
    /// </summary>
    /// <param name="bundleName">The name of the bundle.</param>
    /// <param name="onProgress">Progress callback.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A UniTask returning true if retrieved successfully; otherwise, false.</returns>
    private async UniTask<bool> LoadBundlePAD(string bundleName, IProgress<float> onProgress, CancellationToken ct)
    {
        var req = PlayAssetDelivery.RetrieveAssetBundleAsync(bundleName.ToLower());
        while (!req.IsDone)
        {
            ct.ThrowIfCancellationRequested();

            if (req.Status == AssetDeliveryStatus.WaitingForWifi)
            {
                var confirm = PlayAssetDelivery.ShowCellularDataConfirmation();
                while (!confirm.IsDone) await UniTask.Yield(ct);
                while (req.Status == AssetDeliveryStatus.WaitingForWifi) await UniTask.Yield(ct);
                continue;
            }

            if (req.Status == AssetDeliveryStatus.Failed)
            {
                Debug.LogWarning($"[AssetBundleManager] PAD failed '{bundleName}': {req.Error}");
                return false;
            }

            if (req.Status == AssetDeliveryStatus.Retrieving)
                onProgress?.Report(req.DownloadProgress);

            await UniTask.Yield(ct);
        }

        if (req.Status == AssetDeliveryStatus.Available)
        {
            RegisterBundle(bundleName, req.AssetBundle);
            return true;
        }

        Debug.LogWarning($"[AssetBundleManager] PAD failed '{bundleName}': {req.Error}");
        return false;
    }
#endif

        /// <summary>
        ///     Automatically loads a sprite atlas from an asset bundle if present.
        /// </summary>
        /// <param name="bundleName">The name of the bundle containing the atlas.</param>
        /// <param name="ct">Cancellation token.</param>
        private async UniTask LoadSpriteAtlas(string bundleName, CancellationToken ct)
        {
            var bundle = GetBundle(bundleName);
            if (bundle == null) return;

            var atlasName = $"{bundleName}_atlas";
            var req = bundle.LoadAssetAsync<SpriteAtlas>(atlasName);
            await req.ToUniTask(cancellationToken: ct);
            if (req.asset is SpriteAtlas atlas)
            {
                spriteAtlases[bundleName] = atlas;
                return;
            }

            var fallback = Array.Find(bundle.GetAllAssetNames(),
                n => n.IndexOf("atlas", StringComparison.OrdinalIgnoreCase) >= 0);
            if (fallback == null) return;

            var fr = bundle.LoadAssetAsync<SpriteAtlas>(fallback);
            await fr.ToUniTask(cancellationToken: ct);
            if (fr.asset is SpriteAtlas fa) spriteAtlases[bundleName] = fa;
        }

        #endregion

        #region Asset Retrieval

        /// <summary>
        ///     Retrieves an asset of type <typeparamref name="T" /> from a specific bundle by its asset name.
        ///     If no bundle name is provided, searches all loaded bundles.
        /// </summary>
        /// <typeparam name="T">The type of asset to load, must inherit from <see cref="UnityEngine.Object" />.</typeparam>
        /// <param name="bundleName">The name of the bundle to retrieve from.</param>
        /// <param name="assetName">The name of the asset.</param>
        /// <returns>The retrieved asset if found; otherwise, null.</returns>
        public T GetAssetFromBundle<T>(string bundleName, string assetName) where T : Object
        {
            if (string.IsNullOrEmpty(bundleName)) return GetAssetFromAnyBundle<T>(assetName);

            var cacheKey = $"{bundleName}|{assetName}|{typeof(T).Name}";
            if (assetCache.TryGetValue(cacheKey, out var cached)) return cached as T;

            var bundle = GetBundle(bundleName);
            if (bundle == null) return null;

            T asset = null;
            if (typeof(Component).IsAssignableFrom(typeof(T)))
            {
                var go = bundle.LoadAsset<GameObject>(assetName);
                if (go != null) asset = go.GetComponent<T>();
            }

            if (asset == null) asset = bundle.LoadAsset<T>(assetName);
            if (asset == null && typeof(T) == typeof(Sprite))
                asset = FindSpriteInBundle(bundle, bundleName, assetName) as T;

            if (asset != null)
            {
                assetCache[cacheKey] = asset;
                return asset;
            }

            Debug.LogWarning($"[AssetBundleManager] {typeof(T).Name} '{assetName}' not found in '{bundleName}'");
            return null;
        }

        /// <summary>
        ///     Retrieves an asset of type <typeparamref name="T" /> from a specific bundle by its full path.
        /// </summary>
        /// <typeparam name="T">The type of asset to load, must inherit from <see cref="UnityEngine.Object" />.</typeparam>
        /// <param name="bundleName">The name of the bundle to retrieve from.</param>
        /// <param name="assetPath">The path of the asset within the bundle.</param>
        /// <returns>The retrieved asset if found; otherwise, null.</returns>
        public T GetAssetFromBundleByPath<T>(string bundleName, string assetPath) where T : Object
        {
            if (string.IsNullOrEmpty(bundleName) || string.IsNullOrEmpty(assetPath)) return null;

            var cacheKey = $"{bundleName}_{assetPath}";
            if (assetCache.TryGetValue(cacheKey, out var cached)) return cached as T;

            var bundle = GetBundle(bundleName);
            if (bundle == null) return null;

            var path = assetPath.ToLowerInvariant();
            var asset = bundle.LoadAsset<T>(path);

            if (asset == null && typeof(T) == typeof(Sprite))
            {
                var subs = bundle.LoadAssetWithSubAssets<Object>(path);
                if (subs != null)
                    foreach (var o in subs)
                        if (o is Sprite sp)
                        {
                            asset = sp as T;
                            break;
                        }
            }

            if (asset != null)
            {
                assetCache[cacheKey] = asset;
                return asset;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning(
                $"[AssetBundleManager] \"{assetPath}\" not found in bundle '{bundleName}'\n" +
                $"Assets:\n{string.Join("\n", bundle.GetAllAssetNames())}");
#endif
            return null;
        }

        /// <summary>
        ///     Searches for and retrieves an asset of type <typeparamref name="T" /> from any loaded bundle by name.
        /// </summary>
        /// <typeparam name="T">The type of asset to load, must inherit from <see cref="UnityEngine.Object" />.</typeparam>
        /// <param name="assetName">The name of the asset.</param>
        /// <returns>The retrieved asset if found; otherwise, null.</returns>
        public T GetAssetFromAnyBundle<T>(string assetName) where T : Object
        {
            if (assetCache.TryGetValue(assetName, out var cached)) return cached as T;

            foreach (var kvp in loadedBundles)
            {
                var asset = kvp.Value.LoadAsset<T>(assetName);
                if (asset == null && typeof(T) == typeof(Sprite))
                    asset = FindSpriteInBundle(kvp.Value, kvp.Key, assetName) as T;

                if (asset != null)
                {
                    assetCache[assetName] = asset;
                    return asset;
                }
            }

            return null;
        }

        /// <summary>
        ///     Backward-compatibility alias for retrieving an asset from any downloaded bundle.
        /// </summary>
        /// <typeparam name="T">The type of asset to load, must inherit from <see cref="UnityEngine.Object" />.</typeparam>
        /// <param name="assetName">The name of the asset.</param>
        /// <returns>The retrieved asset if found; otherwise, null.</returns>
        public T GetAssetFromDownloadedBundle<T>(string assetName) where T : Object
        {
            return GetAssetFromAnyBundle<T>(assetName);
        }

        /// <summary>
        ///     Helper method to find a sprite within a given asset bundle or its atlases.
        /// </summary>
        /// <param name="bundle">The asset bundle instance to search.</param>
        /// <param name="bundleName">The name of the asset bundle.</param>
        /// <param name="spriteName">The name of the sprite.</param>
        /// <returns>The found <see cref="Sprite" /> if successful; otherwise, null.</returns>
        private Sprite FindSpriteInBundle(AssetBundle bundle, string bundleName, string spriteName)
        {
            var nameNoExt = Path.GetFileNameWithoutExtension(spriteName);

            var subs = bundle.LoadAssetWithSubAssets<Sprite>(nameNoExt);
            if (subs != null && subs.Length > 0)
                foreach (var s in subs)
                    if (s != null && s.name == spriteName)
                        return s;

            foreach (var sprite in bundle.LoadAllAssets<Sprite>())
                if (sprite != null && sprite.name == spriteName)
                    return sprite;

            if (spriteAtlases.TryGetValue(bundleName, out var atlas))
                return atlas.GetSprite(nameNoExt);

            return null;
        }

        #endregion

        #region Batch Retrieval

        /// <summary>
        ///     Loads all assets of type <typeparamref name="T" /> from a specific bundle.
        /// </summary>
        /// <typeparam name="T">The type of asset, must inherit from <see cref="UnityEngine.Object" />.</typeparam>
        /// <param name="bundleName">The name of the bundle.</param>
        /// <param name="assetName">Optional parameter (not currently utilized).</param>
        /// <returns>An array of loaded assets of type <typeparamref name="T" />.</returns>
        public T[] GetAllAssetsFromBundle<T>(string bundleName, string assetName = null) where T : Object
        {
            var bundle = GetBundle(bundleName);
            if (bundle == null)
            {
                Debug.LogWarning($"[AssetBundleManager] Bundle not loaded: {bundleName}");
                return Array.Empty<T>();
            }

            return bundle.LoadAllAssets<T>();
        }

        /// <summary>
        ///     Loads all assets of type <typeparamref name="T" /> from all loaded bundles that match a name prefix.
        /// </summary>
        /// <typeparam name="T">The type of asset, must inherit from <see cref="UnityEngine.Object" />.</typeparam>
        /// <param name="namePrefix">The prefix of the asset name to match.</param>
        /// <returns>An array of loaded assets of type <typeparamref name="T" />.</returns>
        public T[] GetAllAssetsByNamePrefix<T>(string namePrefix) where T : Object
        {
            var results = new List<T>();
            var lower = namePrefix.ToLowerInvariant();
            foreach (var kvp in loadedBundles)
            foreach (var asset in kvp.Value.LoadAllAssets<T>())
                if (asset != null && asset.name.ToLowerInvariant().StartsWith(lower))
                    results.Add(asset);
            return results.ToArray();
        }

        /// <summary>
        ///     Loads all assets of type <typeparamref name="T" /> from a specific loaded bundle that match a name prefix.
        /// </summary>
        /// <typeparam name="T">The type of asset, must inherit from <see cref="UnityEngine.Object" />.</typeparam>
        /// <param name="bundleName">The name of the bundle.</param>
        /// <param name="namePrefix">The prefix of the asset name to match.</param>
        /// <returns>An array of loaded assets of type <typeparamref name="T" />.</returns>
        public T[] GetAllAssetsByNamePrefix<T>(string bundleName, string namePrefix) where T : Object
        {
            var bundle = GetBundle(bundleName);
            if (bundle == null)
            {
                Debug.LogWarning($"[AssetBundleManager] Bundle not loaded: {bundleName}");
                return Array.Empty<T>();
            }

            var results = new List<T>();
            var lower = namePrefix.ToLowerInvariant();
            foreach (var asset in bundle.LoadAllAssets<T>())
                if (asset != null && asset.name.ToLowerInvariant().StartsWith(lower))
                    results.Add(asset);
            return results.ToArray();
        }

        /// <summary>
        ///     Loads all prefab GameObjects from a specific bundle under a given lowercased path prefix.
        /// </summary>
        /// <param name="bundleName">The name of the bundle.</param>
        /// <param name="pathPrefixLowercase">The lowercased prefix path to match.</param>
        /// <returns>An array of matched prefab <see cref="GameObject" />s.</returns>
        public GameObject[] LoadAllPrefabsFromBundleByPath(string bundleName, string pathPrefixLowercase)
        {
            var bundle = GetBundle(bundleName);
            if (bundle == null)
            {
                Debug.LogWarning($"[AssetBundleManager] Bundle not loaded: {bundleName}");
                return Array.Empty<GameObject>();
            }

            var result = new List<GameObject>();
            foreach (var name in bundle.GetAllAssetNames())
            {
                if (!name.StartsWith(pathPrefixLowercase) || !name.EndsWith(".prefab")) continue;
                var prefab = bundle.LoadAsset<GameObject>(name);
                if (prefab != null) result.Add(prefab);
            }

            return result.ToArray();
        }

        #endregion

        #region Sprite Atlas

        /// <summary>
        ///     Retrieves a sprite from the cached sprite atlas of a specific bundle.
        /// </summary>
        /// <param name="bundleName">The name of the bundle containing the atlas.</param>
        /// <param name="spriteName">The name of the sprite within the atlas.</param>
        /// <returns>The retrieved <see cref="Sprite" /> if found; otherwise, null.</returns>
        public Sprite GetSprite(string bundleName, string spriteName)
        {
            if (!spriteAtlases.TryGetValue(bundleName, out var atlas)) return null;
            return atlas.GetSprite(spriteName);
        }

        #endregion

        #region Cache

        /// <summary>
        ///     Clears all stored asset and sprite atlas caches and calls GC/Resources cleanup.
        /// </summary>
        public void ClearAllCaches()
        {
            assetCache.Clear();
            spriteAtlases.Clear();
            Resources.UnloadUnusedAssets();
            GC.Collect();
        }

        #endregion

#if UNITY_EDITOR
        /// <summary>
        ///     Loads an asset bundle synchronously from the local Streaming Assets folder in the Unity Editor.
        /// </summary>
        /// <param name="bundleName">The name of the bundle.</param>
        /// <returns>True if loaded successfully; otherwise, false.</returns>
        public bool LoadBundleFromStreamingAssets(string bundleName)
        {
            if (loadedBundles.ContainsKey(bundleName)) return true;
            var path = Path.Combine(Application.streamingAssetsPath, bundleName);
            if (!File.Exists(path)) return false;
            var bundle = AssetBundle.LoadFromFile(path);
            if (bundle != null)
            {
                RegisterBundle(bundleName, bundle);
                return true;
            }

            return false;
        }
#endif
    }
}