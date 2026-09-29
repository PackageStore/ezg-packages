using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityFigmaBridge.Editor.Utils
{
    /// <summary>
    ///     Saves a freshly built node tree over the prefab the last import wrote, keeping the object ids
    ///     of that prefab on the same nodes.
    ///
    ///     When a prefab is replaced, Unity carries its object ids over by GameObject name across the
    ///     whole tree: the first new object called <c>Rectangle</c> takes the id of one old
    ///     <c>Rectangle</c> and every other one gets a new id. The nine cells of a slice plate, or the
    ///     <c>Text</c> under each repeated row, came back with ids moved between nodes, and an override
    ///     that another prefab held on one of those ids (an instance override an earlier import wrote,
    ///     or one set by hand) landed on a different node: the bottom-right cell's position moved the
    ///     top-left cell.
    ///
    ///     For the save only, every name that is not unique in the old or the new tree gets a suffix on
    ///     both sides - its path of <c>name#occurrence</c> from the root - so each node meets the node at
    ///     the same place in the old prefab, and then the Figma names are written back.
    /// </summary>
    public static class StablePrefabSave
    {
        /// <summary>Invisible separator before the path suffix: never part of a Figma layer name.</summary>
        private const string MARK = "⁣#";

        /// <summary>
        ///     <see cref="PrefabUtility.SaveAsPrefabAssetAndConnect(GameObject,string,InteractionMode)"/>
        ///     that keeps the object ids of an existing prefab at <paramref name="assetPath"/> on the
        ///     nodes at the same place in the tree.
        /// </summary>
        public static GameObject SaveAsPrefabAssetAndConnect(GameObject root, string assetPath, InteractionMode mode)
        {
            var duplicateNames = OwnDuplicateNames(root);
            MarkExistingPrefab(assetPath, duplicateNames);
            if (duplicateNames.Count == 0) return PrefabUtility.SaveAsPrefabAssetAndConnect(root, assetPath, mode);

            var renamed = Mark(root, duplicateNames);
            GameObject prefab = null;
            try
            {
                prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(root, assetPath, mode);
            }
            finally
            {
                foreach (var (node, name) in renamed) node.name = name;
                // Connected instance: the restored names are its only overrides, so applying writes
                // them to the prefab without touching an id
                if (prefab != null) PrefabUtility.ApplyPrefabInstance(root, InteractionMode.AutomatedAction);
                else StripMarks(assetPath);
            }
            return prefab;
        }

        /// <summary>
        ///     Adds the names repeated in the prefab already at <paramref name="assetPath"/> to
        ///     <paramref name="duplicateNames"/> and marks them there, so the new tree meets marked names
        ///     on both sides. A name repeated only in the old prefab is marked too: its single new node
        ///     then takes the id of the old node at its place, not of the first one in the file.
        /// </summary>
        private static void MarkExistingPrefab(string assetPath, HashSet<string> duplicateNames)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) == null) return;

            GameObject contents;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(assetPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FigmaBridge] Could not open '{assetPath}' to keep its object ids; it is replaced as a new prefab - {e.Message}");
                return;
            }

            try
            {
                duplicateNames.UnionWith(OwnDuplicateNames(contents));
                if (duplicateNames.Count > 0 && Mark(contents, duplicateNames).Count > 0)
                    PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        ///     Names used by more than one GameObject the tree owns. The root counts (a component
        ///     <c>PopUp</c> often holds a frame <c>PopUp</c>) but is never renamed. Objects of a nested
        ///     prefab instance belong to that prefab and are left out.
        /// </summary>
        private static HashSet<string> OwnDuplicateNames(GameObject root)
        {
            var seen = new HashSet<string>();
            var duplicates = new HashSet<string>();
            foreach (var node in root.GetComponentsInChildren<Transform>(true))
            {
                if (node != root.transform && PrefabUtility.IsPartOfPrefabInstance(node.gameObject)) continue;
                if (!seen.Add(node.name)) duplicates.Add(node.name);
            }
            return duplicates;
        }

        /// <summary>
        ///     Suffixes every owned node whose name is in <paramref name="names"/> with its path from the
        ///     root. Paths are all taken before the first rename, from the Figma names.
        /// </summary>
        private static List<(Transform node, string name)> Mark(GameObject root, HashSet<string> names)
        {
            var targets = new List<(Transform node, string name, string path)>();
            foreach (var node in root.GetComponentsInChildren<Transform>(true))
            {
                if (node == root.transform || PrefabUtility.IsPartOfPrefabInstance(node.gameObject)) continue;
                if (names.Contains(node.name)) targets.Add((node, node.name, PathKey(node, root.transform)));
            }

            var renamed = new List<(Transform node, string name)>(targets.Count);
            foreach (var (node, name, path) in targets)
            {
                node.name = name + MARK + path;
                renamed.Add((node, name));
            }
            return renamed;
        }

        /// <summary>
        ///     <c>/Frame#0/Rectangle#3</c>: every level's name and how many earlier siblings share it.
        ///     Unique in the tree, and the same for a node that kept its place across two imports.
        /// </summary>
        private static string PathKey(Transform node, Transform root)
        {
            var path = "";
            for (var current = node; current != root && current != null; current = current.parent)
            {
                var occurrence = 0;
                var parent = current.parent;
                for (var i = 0; i < current.GetSiblingIndex(); i++)
                    if (parent.GetChild(i).name == current.name) occurrence++;
                path = $"/{current.name}#{occurrence}{path}";
            }
            return path;
        }

        /// <summary>The replacing save failed: take the marks off the prefab that stays on disk.</summary>
        private static void StripMarks(string assetPath)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) == null) return;
            var contents = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                var changed = false;
                foreach (var node in contents.GetComponentsInChildren<Transform>(true))
                {
                    var markIndex = node.name.IndexOf(MARK, StringComparison.Ordinal);
                    if (markIndex < 0) continue;
                    node.name = node.name.Substring(0, markIndex);
                    changed = true;
                }
                if (changed) PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
    }
}
