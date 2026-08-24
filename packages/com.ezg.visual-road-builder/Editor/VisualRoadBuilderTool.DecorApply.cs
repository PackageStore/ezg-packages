#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Bake decor: "Apply Decor (chỉ decor)" dựng lại riêng cụm Decor trong prefab, và
    /// <see cref="ApplyDecors"/> spawn prefab decor dưới root (dùng chung với Apply full).</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        /// <summary>Chỉ dựng lại cụm Decor trong RoadParent của Level Prefab: xoá child "Decor" cũ
        /// rồi spawn lại + đồng bộ decor vào file SO — giữ nguyên đường/station/parking.</summary>
        private void ApplyDecorsOnly()
        {
            // Chỉ bắt buộc library khi có chấm cần spawn; 0 chấm vẫn chạy để DỌN cụm Decor cũ.
            if (_decors.Count > 0 && _decorLibrary == null)
            {
                EditorUtility.DisplayDialog("Road Grid", "Chưa gán Decor Library.", "OK");
                return;
            }

            string path = LevelPrefabPath();
            if (path == null)
            {
                EditorUtility.DisplayDialog("Road Grid", "Chưa gán Level Prefab (asset .prefab).", "OK");
                return;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform root = FindOrCreateRoadParent(contents.transform);

                Transform old = root.Find("Decor");
                if (old != null) DestroyImmediate(old.gameObject);

                ApplyDecors(root);

                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            SaveToSo(false); // đồng bộ decor -> file SO (nguồn duy nhất)
            EditorGUIUtility.PingObject(_levelPrefab);
        }

        /// <summary>Spawn toàn bộ decor dưới root (gọi trong Apply, sau các part khác).</summary>
        private void ApplyDecors(Transform root)
        {
            if (_decors.Count == 0) return;
            if (_decorLibrary == null)
            {
                Debug.LogWarning("[VisualRoadBuilder] Có decor trên lưới nhưng chưa gán Decor Library — bỏ qua.");
                return;
            }

            // Gom toàn bộ decor vào 1 empty con "Decor" cho gọn hierarchy.
            var decorParent = new GameObject("Decor");
            decorParent.transform.SetParent(root, false);

            int spawned = 0;
            foreach (DecorItem item in _decors)
            {
                if (item.entry < 0 || item.entry >= _decorLibrary.entries.Count) continue;
                DecorLibrary.DecorEntry entry = _decorLibrary.entries[item.entry];
                if (entry.prefab == null)
                {
                    Debug.LogWarning($"[VisualRoadBuilder] Decor entry '{entry.name}' chưa gán prefab — bỏ qua.");
                    continue;
                }

                var go = (GameObject)PrefabUtility.InstantiatePrefab(entry.prefab, decorParent.transform);
                go.transform.localPosition = new Vector3(
                    (item.x2 * 0.5f + _originCell.x) * CellWorldSize, 0f,
                    (item.y2 * 0.5f + _originCell.y) * CellWorldSize);
                go.transform.localRotation =
                    Quaternion.Euler(0f, item.rot * 90f, 0f)
                    * entry.prefab.transform.localRotation;
                // scale <= 0 = data cũ chưa có field → giữ nguyên scale prefab.
                if (item.scale > 0.01f)
                    go.transform.localScale = entry.prefab.transform.localScale * item.scale;
                go.name = $"{entry.prefab.name}_{item.x2}_{item.y2}";
                spawned++;
            }

            Debug.Log($"[VisualRoadBuilder] Decor: spawn {spawned}/{_decors.Count} item.");
        }
    }
}
#endif
