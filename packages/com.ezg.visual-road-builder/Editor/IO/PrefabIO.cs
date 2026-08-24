#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Truy xuat Level Prefab: duong dan asset, tim/tao node RoadParent, Load lai canvas tu
    /// file SO map, va tien ich GUID ↔ asset.</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        private string LevelPrefabPath()
        {
            if (_applyTarget.LevelPrefab == null) return null;
            string p = AssetDatabase.GetAssetPath(_applyTarget.LevelPrefab);
            return string.IsNullOrEmpty(p) || !p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ? null : p;
        }

        private Transform FindOrCreateRoadParent(Transform contentsRoot)
        {
            string name = string.IsNullOrEmpty(_applyTarget.RoadParentName)
                ? ApplyTarget.DefaultRoadParentName
                : _applyTarget.RoadParentName;
            Transform found = FindDeep(contentsRoot, name);
            if (found != null) return found;

            var go = new GameObject(name);
            go.transform.SetParent(contentsRoot, false);
            return go.transform;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }

        private void ImportFromPrefab()
        {
            string path = LevelPrefabPath();
            if (path == null)
            {
                EditorUtility.DisplayDialog("Road Grid", "Chua gan Level Prefab (asset .prefab).", "OK");
                return;
            }

            string savePath = SaveAssetPath();
            var so = AssetDatabase.LoadAssetAtPath<RoadCanvasSave>(savePath);
            if (so == null)
            {
                EditorUtility.DisplayDialog("Road Grid",
                    $"Chua co file SO map cho prefab nay:\n{savePath}\n\n" +
                    "Ve map roi Apply (hoac Save) de tao lan dau.", "OK");
                return;
            }

            ReadFrom(so);
            PruneOutOfRangeEdges();
            _savedSig = ComputeCanvasSignature();
            Repaint();
            Debug.Log($"[VisualRoadBuilder] Load '{_applyTarget.LevelPrefab.name}': " +
                      $"luoi {_doc.GridWidth}x{_doc.GridHeight}, " +
                      $"{_doc.Edges.Count} edge, {_doc.Stations.Count} station, " +
                      $"{_doc.Parkings.Count} parking, {_doc.Decors.Count} decor.");
        }

        private static string GuidOf(UnityEngine.Object o)
        {
            if (o == null) return "";
            string p = AssetDatabase.GetAssetPath(o);
            return string.IsNullOrEmpty(p) ? "" : AssetDatabase.AssetPathToGUID(p);
        }

        private static T LoadByGuid<T>(string guid) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(guid)) return null;
            string p = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(p) ? null : AssetDatabase.LoadAssetAtPath<T>(p);
        }
    }
}
#endif
