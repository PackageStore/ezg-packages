#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Nạp lại art đường khi _road_plan.psd đổi. Canvas vẽ qua HAI tầng cache
    /// (<see cref="_roadReadable"/> = bản readable của cả atlas, <see cref="_roadPieceTex"/> = piece đã
    /// cắt + xoay); cả hai key theo instance Texture/Sprite mà Unity GIỮ NGUYÊN qua reimport, nên art
    /// mới không bao giờ tự lọt vào — phải xoá tay cả hai.</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        /// <summary>Import lại PSD từ disk (giữ nguyên slice + custom pivot trong .meta) rồi vứt cache.
        /// Dùng khi Unity chưa kịp thấy file đổi, hoặc canvas vẫn lì ra art cũ.</summary>
        private void ForceReloadSprites()
        {
            string path = RoadPlanAtlasPath;
            if (!string.IsNullOrEmpty(path))
                AssetDatabase.ImportAsset(path,
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            ReloadSpriteCaches();
        }

        private void ReloadSpriteCaches()
        {
            ClearRoadPieceCache();
            ClearReadableCache();
            EnsureRoadSprites();
            Repaint();
        }

        /// <summary>Huỷ + xoá bản readable của atlas. Thiếu bước này thì dù cắt lại piece vẫn ra art CŨ.</summary>
        private void ClearReadableCache()
        {
            foreach (Texture2D tex in _roadReadable.Values)
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            _roadReadable.Clear();
        }

        /// <summary>Mọi window đang mở tự nạp lại khi PSD được import (save từ Photoshop, đổi slice…).</summary>
        internal static void OnAssetsImported(string[] importedPaths)
        {
            foreach (VisualRoadBuilderTool window in Resources.FindObjectsOfTypeAll<VisualRoadBuilderTool>())
            {
                string path = window.RoadPlanAtlasPath;
                if (string.IsNullOrEmpty(path) || Array.IndexOf(importedPaths, path) < 0) continue;
                window.ReloadSpriteCaches();
            }
        }
    }

    internal sealed class VisualRoadBuilderSpriteWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
            string[] moved, string[] movedFrom)
        {
            if (imported.Length > 0) VisualRoadBuilderTool.OnAssetsImported(imported);
        }
    }
}
#endif
