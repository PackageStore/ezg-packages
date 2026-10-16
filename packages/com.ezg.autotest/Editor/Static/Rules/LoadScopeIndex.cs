using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Phạm vi LOAD của asset trong bản build — để chấm severity theo "lỗi này có chạm tới người chơi không":
    ///     <list type="bullet">
    ///         <item><see cref="LoadScope.Resources" />: nằm trong thư mục Resources/ (Resources.Load được).</item>
    ///         <item><see cref="LoadScope.Bundle" />: có AssetBundle (meta gán hoặc kế thừa từ thư mục).</item>
    ///         <item><see cref="LoadScope.SceneClosure" />: nằm trong closure phụ thuộc của scene trong Build Settings,
    ///         của asset Resources hoặc của asset trong bundle (được build kéo theo).</item>
    ///         <item><see cref="LoadScope.Referenced" />: có asset khác trong phạm vi quét tham chiếu tới, nhưng cả cụm không
    ///         load được trong build (prefab nháp, biến thể cũ…).</item>
    ///         <item><see cref="LoadScope.Unreferenced" />: không ai tham chiếu, không load được — asset mồ côi.</item>
    ///     </list>
    ///     Dựng một lần mỗi luật (GetDependencies đệ quy trên hàng nghìn root mất vài giây).
    /// </summary>
    internal sealed class LoadScopeIndex
    {
        const string RESOURCES_SEGMENT = "/Resources/";

        readonly HashSet<string> _reachable;
        readonly HashSet<string> _bundleAssets;

        public int RootCount { get; }
        public int ReachableCount => _reachable.Count;

        LoadScopeIndex(HashSet<string> reachable, HashSet<string> bundleAssets, int rootCount)
        {
            _reachable = reachable;
            _bundleAssets = bundleAssets;
            RootCount = rootCount;
        }

        /// <summary>Dựng index từ scene Build Settings (đang bật) + mọi asset Resources/ + mọi asset có bundle.</summary>
        public static LoadScopeIndex Build()
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in EditorBuildSettings.scenes)
                if (s != null && s.enabled && !string.IsNullOrEmpty(s.path))
                    roots.Add(s.path);

            foreach (var guid in AssetDatabase.FindAssets("", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsResources(path) && !AssetDatabase.IsValidFolder(path)) roots.Add(path);
            }

            var bundleAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var bundle in AssetDatabase.GetAllAssetBundleNames())
            foreach (var path in AssetDatabase.GetAssetPathsFromAssetBundle(bundle))
            {
                bundleAssets.Add(path);
                roots.Add(path);
            }

            var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (roots.Count > 0)
                foreach (var dep in AssetDatabase.GetDependencies(roots.ToArray(), true))
                    reachable.Add(dep);
            foreach (var r in roots) reachable.Add(r);
            return new LoadScopeIndex(reachable, bundleAssets, roots.Count);
        }

        /// <summary>Phạm vi load của <paramref name="assetPath" />; <paramref name="referencedGuids" /> = GUID được asset khác trong phạm vi quét trỏ tới.</summary>
        public LoadScope Classify(string assetPath, HashSet<string> referencedGuids)
        {
            if (string.IsNullOrEmpty(assetPath)) return LoadScope.Unreferenced;
            if (IsResources(assetPath)) return LoadScope.Resources;
            if (_bundleAssets.Contains(assetPath) || !string.IsNullOrEmpty(AssetDatabase.GetImplicitAssetBundleName(assetPath)))
                return LoadScope.Bundle;
            if (_reachable.Contains(assetPath)) return LoadScope.SceneClosure;
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (!string.IsNullOrEmpty(guid) && referencedGuids != null && referencedGuids.Contains(guid))
                return LoadScope.Referenced;
            return LoadScope.Unreferenced;
        }

        /// <summary>Asset ở phạm vi này có load được trong bản build không.</summary>
        public static bool IsLoadable(LoadScope scope)
        {
            return scope == LoadScope.Resources || scope == LoadScope.Bundle || scope == LoadScope.SceneClosure;
        }

        public static string Label(LoadScope scope)
        {
            switch (scope)
            {
                case LoadScope.Resources: return "trong Resources/";
                case LoadScope.Bundle: return "trong AssetBundle";
                case LoadScope.SceneClosure: return "được scene build / Resources / bundle kéo theo";
                case LoadScope.Referenced: return "có asset khác tham chiếu nhưng cả cụm không load được trong build";
                default: return "không ai tham chiếu, không load được trong build";
            }
        }

        static bool IsResources(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                   path.Replace('\\', '/').IndexOf(RESOURCES_SEGMENT, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    internal enum LoadScope
    {
        Resources,
        Bundle,
        SceneClosure,
        Referenced,
        Unreferenced
    }
}
