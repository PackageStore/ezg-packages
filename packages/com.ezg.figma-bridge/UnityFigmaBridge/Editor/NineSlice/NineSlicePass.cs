using System;
using UnityEditor;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Utils;

namespace UnityFigmaBridge.Editor.NineSlice
{
    /// <summary>
    /// Runs <see cref="FigmaNineSlice.Apply"/> on every generated prefab, collapsing slice grids
    /// into sliced sprites. Called once per import, after component instantiation.
    /// </summary>
    public static class NineSlicePass
    {
        public static void Run(FigmaImportProcessData figmaImportProcessData)
        {
            var componentPrefabs = figmaImportProcessData.ComponentData.AllComponentPrefabs;
            int prefabsTouched = 0, totalCollapsed = 0, totalSprites = 0;
            var spriteDir = FigmaPaths.FigmaImageFillFolder;

            AssetDatabase.StartAssetEditing();
            try
            {
                var nodes = figmaImportProcessData.NodeLookupDictionary;
                Node Lookup(string id) => id != null && nodes != null && nodes.TryGetValue(id, out var node) ? node : null;
                foreach (var prefab in componentPrefabs)
                    ProcessPrefab(prefab, spriteDir, Lookup, ref prefabsTouched, ref totalCollapsed, ref totalSprites);
                foreach (var prefab in figmaImportProcessData.ScreenPrefabs)
                    ProcessPrefab(prefab, spriteDir, Lookup, ref prefabsTouched, ref totalCollapsed, ref totalSprites);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            Debug.Log($"[NineSlicePass] {prefabsTouched} prefabs touched, {totalCollapsed} grids collapsed, {totalSprites} sprites written");
        }

        private static void ProcessPrefab(GameObject prefab, string spriteDir, Func<string, Node> lookup,
            ref int prefabsTouched, ref int totalCollapsed, ref int totalSprites)
        {
            var assetPath = AssetDatabase.GetAssetPath(prefab);
            var contents = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                var result = FigmaNineSlice.Apply(contents, spriteDir, lookup);
                if (result.collapsed > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
                    prefabsTouched++;
                    totalCollapsed += result.collapsed;
                    totalSprites += result.spritesWritten;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NineSlicePass] Failed to process {assetPath}: {e.Message}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
    }
}
