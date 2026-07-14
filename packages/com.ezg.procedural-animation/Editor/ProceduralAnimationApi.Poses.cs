using System.Linq;
using System.Text;
using Ezg.ProceduralAnimation;
using UnityEditor;
using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    /// <summary>
    /// Pose inspection and maintenance utilities: enumerate capturable bone paths (feeds
    /// CapturePose's includedBonePathsCsv), inspect captured poses, batch-fix bone paths after
    /// hierarchy renames, and capture preview PNGs (interactive editor only — needs a SceneView).
    /// </summary>
    public static partial class ProceduralAnimationApi
    {
        /// <summary>
        /// Lists every capturable RELATIVE bone path under a scene skeleton root — the exact strings
        /// CapturePose's includedBonePathsCsv expects. The root itself is the EMPTY string (shown as
        /// "(root)"); include it in a CSV via a leading comma or empty segment.
        /// </summary>
        public static string ListSceneBonePaths(string skeletonRootHierarchyPath)
        {
            Transform root = FindSceneTransform(skeletonRootHierarchyPath);
            if (root == null)
            {
                return $"ERROR scene object not found: {skeletonRootHierarchyPath}";
            }

            Transform[] bones = root.GetComponentsInChildren<Transform>(true);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"COUNT {bones.Length}");
            foreach (Transform bone in bones)
            {
                string path = BonePathUtility.GetRelativePath(root, bone);
                sb.AppendLine(string.IsNullOrEmpty(path) ? "(root)" : path);
            }

            return sb.ToString();
        }

        /// <summary>Dumps a captured pose's bone set so a driver can verify a capture before graph wiring.</summary>
        public static string DescribePose(string poseAssetPath)
        {
            PoseAsset pose = AssetDatabase.LoadAssetAtPath<PoseAsset>(poseAssetPath);
            if (pose == null)
            {
                return $"ERROR pose asset not found: {poseAssetPath}";
            }

            StringBuilder sb = new StringBuilder();
            string rootName = pose.skeletonRootReference != null ? pose.skeletonRootReference.name : "<none — scene refs do not persist>";
            sb.AppendLine($"POSE {pose.name} bones={pose.bones.Count} skeletonRootReference={rootName}");
            foreach (BonePoseData bone in pose.bones)
            {
                sb.AppendLine(string.IsNullOrEmpty(bone.bonePath) ? "(root)" : bone.bonePath);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Batch find/replace across bone paths of one or more poses (CSV of asset paths) — recovery
        /// tool after skeleton hierarchy renames break path matching. Mirrors the GUI Pose Renamer.
        /// WARNING: plain substring replace — an over-broad findSubstring corrupts many paths at once.
        /// </summary>
        public static string RenamePoseBonePaths(string poseAssetPathsCsv, string findSubstring, string replaceWith)
        {
            if (string.IsNullOrEmpty(findSubstring))
            {
                return "ERROR findSubstring must be non-empty";
            }

            string[] assetPaths = poseAssetPathsCsv.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            if (assetPaths.Length == 0)
            {
                return "ERROR poseAssetPathsCsv contained no asset paths";
            }

            StringBuilder sb = new StringBuilder();
            int totalChanged = 0;
            foreach (string assetPath in assetPaths)
            {
                PoseAsset pose = AssetDatabase.LoadAssetAtPath<PoseAsset>(assetPath);
                if (pose == null)
                {
                    sb.AppendLine($"ERROR pose asset not found: {assetPath}");
                    continue;
                }

                int changed = 0;
                foreach (BonePoseData bone in pose.bones)
                {
                    if (string.IsNullOrEmpty(bone.bonePath) || !bone.bonePath.Contains(findSubstring))
                    {
                        continue;
                    }

                    bone.bonePath = bone.bonePath.Replace(findSubstring, replaceWith ?? string.Empty);
                    changed++;
                }

                if (changed > 0)
                {
                    EditorUtility.SetDirty(pose);
                    totalChanged += changed;
                }

                sb.AppendLine($"{pose.name}: {changed} bone paths changed");
            }

            if (totalChanged > 0)
            {
                AssetDatabase.SaveAssets();
            }

            sb.AppendLine($"TOTAL {totalChanged} bone paths changed across {assetPaths.Length} poses");
            return sb.ToString();
        }

        /// <summary>
        /// Captures a SceneView screenshot as the pose's preview PNG (saved next to the pose asset).
        /// Requires an ACTIVE SceneView — works in an interactive editor, NOT in -batchmode.
        /// Pass graphPath to also wire the PNG into every variant referencing this pose.
        /// </summary>
        public static string CapturePosePreview(string poseAssetPath, bool overwriteExisting = true, string graphPath = null)
        {
            PoseAsset pose = AssetDatabase.LoadAssetAtPath<PoseAsset>(poseAssetPath);
            if (pose == null)
            {
                return $"ERROR pose asset not found: {poseAssetPath}";
            }

            Texture2D preview;
            try
            {
                preview = PosePreviewCaptureUtility.CaptureSceneViewPreview();
            }
            catch (System.InvalidOperationException e)
            {
                return $"ERROR preview capture needs an active SceneView (interactive editor only): {e.Message}";
            }

            if (preview == null)
            {
                return "ERROR SceneView capture returned null";
            }

            Texture2D saved = PosePreviewCaptureUtility.SavePreviewTextureForPose(preview, pose, overwriteExisting);
            Object.DestroyImmediate(preview);
            if (saved == null)
            {
                return $"SKIPPED preview not saved — existing PNG with overwriteExisting=false, or pose path outside Assets";
            }

            string savedPath = AssetDatabase.GetAssetPath(saved);
            int wiredVariants = 0;
            if (!string.IsNullOrEmpty(graphPath))
            {
                PoseCombinationGraphAsset graph = LoadGraph(graphPath, out string error);
                if (graph == null)
                {
                    return $"SAVED {savedPath} but graph wiring failed: {error}";
                }

                foreach (PoseVariant variant in graph.stages.SelectMany(s => s.variants).Where(v => v.pose == pose))
                {
                    variant.previewImage = saved;
                    wiredVariants++;
                }

                Save(graph);
            }

            return $"SAVED {savedPath}" + (wiredVariants > 0 ? $" (wired into {wiredVariants} variants)" : string.Empty);
        }
    }
}
