#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>NGUON DUY NHAT cua map: doc/ghi file SO <see cref="RoadCanvasSave"/> (.asset git-tracked,
    /// 1 file / Level Prefab, NGOAI Resources). Save/Restore + Write/Read canvas ↔ SO.</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        private const string SaveFolderPrefKey = "VisualRoadBuilder.SaveFolder";
        private bool _saveFolderSearched;

        private string ResolvedSaveDir()
        {
            if (_applyTarget.SaveFolder != null)
            {
                string p = AssetDatabase.GetAssetPath(_applyTarget.SaveFolder);
                if (AssetDatabase.IsValidFolder(p))
                {
                    EditorPrefs.SetString(SaveFolderPrefKey, p);
                    return p;
                }
                _applyTarget.SaveFolder = null;
            }
            string cached = EditorPrefs.GetString(SaveFolderPrefKey, "");
            if (!string.IsNullOrEmpty(cached) && AssetDatabase.IsValidFolder(cached))
            {
                _applyTarget.SaveFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(cached);
                if (_applyTarget.SaveFolder != null) return cached;
            }

            if (_saveFolderSearched) return null;
            _saveFolderSearched = true;

            string discovered = DiscoverCanvasSaveDir();
            if (discovered == null) return null;

            _applyTarget.SaveFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(discovered);
            if (_applyTarget.SaveFolder != null)
            {
                EditorPrefs.SetString(SaveFolderPrefKey, discovered);
                return discovered;
            }
            return null;
        }

        private string DiscoverCanvasSaveDir()
        {
            string prefabPath = LevelPrefabPath();
            if (prefabPath != null)
            {
                string prefabName = System.IO.Path.GetFileNameWithoutExtension(prefabPath);
                string expectedName = prefabName + "_RoadCanvas";
                string[] guids = AssetDatabase.FindAssets($"t:RoadCanvasSave {expectedName}");
                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (System.IO.Path.GetFileNameWithoutExtension(assetPath) == expectedName)
                        return System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');
                }
            }

            string[] allGuids = AssetDatabase.FindAssets("t:RoadCanvasSave");
            if (allGuids.Length == 0) return null;

            string commonDir = null;
            foreach (string guid in allGuids)
            {
                string dir = System.IO.Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(guid))
                    .Replace('\\', '/');
                if (commonDir == null) commonDir = dir;
                else if (commonDir != dir) return null;
            }
            return commonDir;
        }

        private string SaveAssetPath()
        {
            string dir = ResolvedSaveDir();
            if (dir == null) return null;
            string p = LevelPrefabPath();
            if (p == null) return null;
            return $"{dir}/{System.IO.Path.GetFileNameWithoutExtension(p)}_RoadCanvas.asset";
        }

        private void EnsureSaveDir()
        {
            string dir = ResolvedSaveDir();
            if (dir != null) return;
        }

        private void SaveToSo(bool verbose)
        {
            string path = SaveAssetPath();
            if (path == null)
            {
                if (verbose)
                    EditorUtility.DisplayDialog("Road Canvas",
                        "Gan Level Prefab va Save Folder truoc khi luu file SO.", "OK");
                return;
            }

            var so = AssetDatabase.LoadAssetAtPath<RoadCanvasSave>(path);
            bool created = so == null;
            if (created)
            {
                EnsureSaveDir();
                so = CreateInstance<RoadCanvasSave>();
            }

            WriteInto(so);
            if (created) AssetDatabase.CreateAsset(so, path);
            EditorUtility.SetDirty(so);
            AssetDatabase.SaveAssetIfDirty(so);

            _savedSig = ComputeCanvasSignature();
            _dirty = false;
            _saveInfo = $"Da luu {DateTime.Now:HH:mm:ss}  ·  {System.IO.Path.GetFileName(path)}";
            if (verbose)
            {
                Debug.Log($"[VisualRoadBuilder] Luu canvas → {path}");
                EditorGUIUtility.PingObject(so);
            }
            Repaint();
        }

        private void RestoreFromSo()
        {
            string path = SaveAssetPath();
            var so = path != null ? AssetDatabase.LoadAssetAtPath<RoadCanvasSave>(path) : null;
            if (so == null)
            {
                EditorUtility.DisplayDialog("Road Canvas",
                    "Chua co file SO save cho prefab nay (Save de tao).", "OK");
                return;
            }
            if (_pendingChange) CommitHistoryStep();
            ReadFrom(so);
            PruneOutOfRangeEdges();
            _savedSig = ComputeCanvasSignature();
            _saveInfo = $"Da nap {DateTime.Now:HH:mm:ss}  ·  {System.IO.Path.GetFileName(path)}";
            Repaint();
        }

        private void WriteInto(RoadCanvasSave so)
        {
            so.version = 3;
            so.edgeSpanVersion = 1;
            so.width = _doc.GridWidth;
            so.height = _doc.GridHeight;
            so.cellWorldSize = GridConst.CellWorldSize;
            so.edges = new List<int>(_doc.Edges);
            so.highwayEdges = new List<int>(_doc.HighwayEdges);
            so.hwDecorEdges = new List<int>(_doc.HwDecorEdges);
            so.road2Edges = new List<int>(_doc.Road2Edges);
            so.pathEdges = new List<int>(_doc.PathEdges);
            so.stations = new List<int>(_doc.Stations);
            so.parkings = new List<int>(_doc.Parkings);
            so.rampFlips = new List<int>(_doc.RampFlips);
            so.decors = _doc.Decors.ConvertAll(d => new RoadCanvasSave.DecorPlacement
                { entry = d.entry, x2 = d.x2, y2 = d.y2, rot = d.rot, scale = d.scale });
            so.originCell = _doc.OriginCell;
            so.libraryGuid = GuidOf(_library);
            so.decorLibraryGuid = GuidOf(_decorLibrary);
            so.targetPrefabGuid = GuidOf(_applyTarget.LevelPrefab);
            so.savedAt = DateTime.Now.ToString("O");
        }

        private void ReadFrom(RoadCanvasSave so)
        {
            _doc.GridWidth = Mathf.Clamp(so.width, 2, GridConst.MaxGridSize);
            _doc.GridHeight = Mathf.Clamp(so.height, 2, GridConst.MaxGridSize);
            _doc.Edges = new List<int>(so.edges ?? new List<int>());
            _doc.HighwayEdges = new List<int>(so.highwayEdges ?? new List<int>());
            _doc.HwDecorEdges = new List<int>(so.hwDecorEdges ?? new List<int>());
            _doc.Road2Edges = new List<int>(so.road2Edges ?? new List<int>());
            _doc.PathEdges = new List<int>(so.pathEdges ?? new List<int>());
            _doc.Stations = new List<int>(so.stations ?? new List<int>());
            _doc.Parkings = new List<int>(so.parkings ?? new List<int>());
            _doc.RampFlips = new List<int>(so.rampFlips ?? new List<int>());
            _doc.RampFlips.Sort();
            _doc.Decors = (so.decors ?? new List<RoadCanvasSave.DecorPlacement>())
                .ConvertAll(d => new DecorItem { entry = d.entry, x2 = d.x2, y2 = d.y2, rot = d.rot, scale = d.scale });
            _doc.OriginCell = so.originCell;

            if (so.edgeSpanVersion < 1)
            {
                EdgeCodec.SplitEdgeSpan(_doc.Edges);
                EdgeCodec.SplitEdgeSpan(_doc.HighwayEdges);
                EdgeCodec.SplitEdgeSpan(_doc.HwDecorEdges);
                EdgeCodec.SplitEdgeSpan(_doc.Road2Edges);
            }

            RoadPartLibrary lib = LoadByGuid<RoadPartLibrary>(so.libraryGuid);
            if (lib != null) _library = lib;
            DecorLibrary dlib = LoadByGuid<DecorLibrary>(so.decorLibraryGuid);
            if (dlib != null) _decorLibrary = dlib;

            _doc.DataVersion = 4;
            ClearSelection();
        }
    }
}
#endif
