#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Destructive prefab write: LoadPrefabContents → wipe RoadParent → BuildInto →
    /// SaveAsPrefabAsset + SaveToSo. Extracted from Apply.cs:103-275.</summary>
    internal static class RoadBaker
    {
        /// <summary>Validate missing parts, confirm, then bake mesh into prefab.</summary>
        internal static void Bake(
            CollectResult result, RoadCanvasDoc doc, RoadPartLibrary library,
            string prefabPath, string roadParentName,
            System.Action<Transform> applyDecors,
            System.Action saveToSo,
            System.Func<int, Vector2> blockFacingStep,
            System.Func<bool, float> blockPivotInsetFor)
        {
            // D4: chỉ "Station"/"Parking" (thiếu hẳn prefab, không có gì để đặt) mới CHẶN Apply; mọi
            // tile modular thiếu (Road/Highway/Road2 part) chỉ CẢNH BÁO — không return (09-bake-wiring.md).
            if (doc.Stations.Count > 0 && library.stationPrefab == null)
                result.Missing.Add("Station");
            if (doc.Parkings.Count > 0 && library.parkingPrefab == null)
                result.Missing.Add("Parking");
            if (doc.Stations2.Count > 0 && library.station2Prefab == null)
                result.Missing.Add("Station 2");

            var blockingMissing = new HashSet<string>(result.Missing);
            blockingMissing.IntersectWith(new[] { "Station", "Parking", "Station 2" });
            if (blockingMissing.Count > 0)
            {
                EditorUtility.DisplayDialog("Road Grid",
                    $"Part Library thiếu prefab: {string.Join(", ", blockingMissing)}.", "OK");
                return;
            }
            if (result.Missing.Count > 0)
                Debug.LogWarning($"[VisualRoadBuilder] Part Library thiếu prefab (không chặn bake): {string.Join(", ", result.Missing)}.");

            if (result.Road.Count == 0 && result.Road2.Count == 0 && result.Path.Count == 0
                && result.Highway.Count == 0 && result.HwDecor.Count == 0
                && doc.Stations.Count == 0 && doc.Stations2.Count == 0 && doc.Parkings.Count == 0 && doc.Decors.Count == 0)
            {
                EditorUtility.DisplayDialog("Road Grid", "Chưa vẽ gì.", "OK");
                return;
            }

            if (prefabPath == null)
            {
                EditorUtility.DisplayDialog("Road Grid", "Chưa gán Level Prefab (asset .prefab).", "OK");
                return;
            }

            string displayName = System.IO.Path.GetFileName(prefabPath);
            if (string.IsNullOrEmpty(roadParentName))
                roadParentName = ApplyTarget.DefaultRoadParentName;

            if (!EditorUtility.DisplayDialog("Road Grid",
                    $"Ghi map vào '{displayName}' → node '{roadParentName}':\n" +
                    "dựng lại TOÀN BỘ mesh trong node đó + ghi file SO map.\n" +
                    "Các object gameplay khác trong prefab GIỮ NGUYÊN. Tiếp tục?",
                    "Ghi", "Huỷ"))
                return;

            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Transform root = FindOrCreateGroup(contents.transform, roadParentName);

                for (int i = root.childCount - 1; i >= 0; i--)
                    Object.DestroyImmediate(root.GetChild(i).gameObject);

                BuildInto(root, result, doc, library, blockFacingStep, blockPivotInsetFor, applyDecors);

                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            saveToSo();
        }

        /// <summary>Dựng toàn bộ mesh xuống root (node RoadParent trong prefab contents). KHÔNG Undo —
        /// gom theo loại vào empty con: Roads / Highways / Stations / Parking / Decor.</summary>
        private static void BuildInto(
            Transform root, CollectResult r, RoadCanvasDoc doc, RoadPartLibrary library,
            System.Func<int, Vector2> blockFacingStep,
            System.Func<bool, float> blockPivotInsetFor,
            System.Action<Transform> applyDecors)
        {
            Vector2 origin = doc.OriginCell;
            float cellSize = GridConst.CellWorldSize;

            if (r.Road.Count > 0)
                PlacePieces(r.Road, FindOrCreateGroup(root, "Roads"), origin, cellSize);
            if (r.Road2.Count > 0)
                PlacePieces(r.Road2, FindOrCreateGroup(root, "Road2"), origin, cellSize);
            if (r.Path.Count > 0)
                PlacePieces(r.Path, FindOrCreateGroup(root, "Path"), origin, cellSize);

            if (r.Highway.Count > 0 || r.HwDecor.Count > 0)
            {
                Transform hwGroup = FindOrCreateGroup(root, "Highways");
                PlacePieces(r.Highway, hwGroup, origin, cellSize);
                PlacePieces(r.HwDecor, hwGroup, origin, cellSize);
            }

            // Station: prefab có pivot nằm TRÊN dải đường trước mặt (StationArea.prefab), cách tâm
            // khối (size/2 + 1) ô về hướng mặt → đặt transform tại pivot đó (StationPivotCell), lùi
            // thêm BlockPivotInsetFor nếu khối nối Road 2 (mặt cắt Road 2 tự lát dải filler).
            if (doc.Stations.Count > 0 && library.stationPrefab != null)
            {
                Transform stGroup = FindOrCreateGroup(root, "Stations");
                int stSize = GridConst.StationSize;
                foreach (int id in doc.Stations)
                {
                    BlockCodec.DecodeStation(id, out int sx2, out int sy2, out int rot);
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(library.stationPrefab, stGroup);
                    Vector2 piv = BlockCodec.StationPivotCell(sx2, sy2, stSize, rot)
                                  + blockFacingStep(rot) * blockPivotInsetFor(r.Road2Blocks?.Contains(id) == true);
                    go.transform.localPosition = new Vector3(
                        (piv.x + origin.x) * cellSize, 0f,
                        (piv.y + origin.y) * cellSize);
                    go.transform.localRotation =
                        Quaternion.Euler(0f, rot * 90f, 0f)
                        * library.stationPrefab.transform.localRotation;
                    go.name = $"{library.stationPrefab.name}_{sx2}_{sy2}";
                }
            }

            if (doc.Stations2.Count > 0 && library.station2Prefab != null)
            {
                Transform st2Group = FindOrCreateGroup(root, "Stations2");
                int st2Size = GridConst.StationSize;
                foreach (int id in doc.Stations2)
                {
                    BlockCodec.DecodeStation(id, out int sx2, out int sy2, out int rot);
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(library.station2Prefab, st2Group);
                    Vector2 piv = BlockCodec.StationPivotCell(sx2, sy2, st2Size, rot);
                    go.transform.localPosition = new Vector3(
                        (piv.x + origin.x) * cellSize, 0f,
                        (piv.y + origin.y) * cellSize);
                    go.transform.localRotation =
                        Quaternion.Euler(0f, rot * 90f, 0f)
                        * library.station2Prefab.transform.localRotation;
                    go.name = $"{library.station2Prefab.name}_{sx2}_{sy2}";
                }
            }

            // Parking: prefab có pivot nằm TRÊN dải đường trước mặt như StationArea → đặt transform
            // tại ParkingPivotCell (giữa mép mặt của khối), KHÔNG phải tâm khối.
            if (doc.Parkings.Count > 0 && library.parkingPrefab != null)
            {
                Transform pkGroup = FindOrCreateGroup(root, "Parking");
                foreach (int id in doc.Parkings)
                {
                    BlockCodec.DecodeParking(id, out int px2, out int py2, out int orient);
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(library.parkingPrefab, pkGroup);
                    Vector2 piv = BlockCodec.ParkingPivotCell(px2, py2, orient, GridConst.ParkingCells(orient))
                                  + blockFacingStep(orient) * blockPivotInsetFor(r.Road2Blocks?.Contains(id) == true);
                    go.transform.localPosition = new Vector3(
                        (piv.x + origin.x) * cellSize, 0f,
                        (piv.y + origin.y) * cellSize);
                    go.transform.localRotation =
                        Quaternion.Euler(0f, orient * 90f, 0f)
                        * library.parkingPrefab.transform.localRotation;
                    go.name = $"{library.parkingPrefab.name}_{px2}_{py2}";
                }
            }

            applyDecors?.Invoke(root);
        }

        private static void PlacePieces(
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> list,
            Transform parent, Vector2 origin, float cellSize)
        {
            foreach ((float x, float y, GameObject prefab, float yaw, Vector3 scaleMul) in list)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                go.transform.localPosition = new Vector3(
                    (x + origin.x) * cellSize, 0f, (y + origin.y) * cellSize);
                // Giữ nguyên rotation gốc của prefab (vd model Blender import có X=90),
                // chỉ xoay THÊM yaw quanh trục Y của root.
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * prefab.transform.localRotation;
                if (scaleMul != Vector3.one)
                    go.transform.localScale = Vector3.Scale(prefab.transform.localScale, scaleMul);
                go.name = $"{prefab.name}_{x}_{y}";
            }
        }

        /// <summary>Empty con của root gom theo loại (tạo mới nếu chưa có). KHÔNG Undo (prefab contents).</summary>
        private static Transform FindOrCreateGroup(Transform root, string name)
        {
            Transform existing = root.Find(name);
            if (existing != null) return existing;

            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            return go.transform;
        }
    }
}
#endif
