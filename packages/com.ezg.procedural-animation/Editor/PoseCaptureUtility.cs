using System.Collections.Generic;
using Ezg.ProceduralAnimation;
using UnityEditor;
using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    public static class PoseCaptureUtility
    {
        public static void CaptureIntoPose(Transform skeletonRoot, PoseAsset poseAsset, ICollection<string> includedBonePaths = null)
        {
            if (skeletonRoot == null)
            {
                throw new System.ArgumentNullException(nameof(skeletonRoot));
            }

            if (poseAsset == null)
            {
                throw new System.ArgumentNullException(nameof(poseAsset));
            }

            poseAsset.skeletonRootReference = skeletonRoot;
            poseAsset.bones.Clear();

            Transform[] bones = skeletonRoot.GetComponentsInChildren<Transform>(true);
            foreach (Transform bone in bones)
            {
                string bonePath = BonePathUtility.GetRelativePath(skeletonRoot, bone);
                if (includedBonePaths != null && !includedBonePaths.Contains(bonePath))
                {
                    continue;
                }

                poseAsset.bones.Add(new BonePoseData
                {
                    bonePath = bonePath,
                    localPosition = bone.localPosition,
                    localRotation = bone.localRotation,
                    localScale = bone.localScale
                });
            }

            EditorUtility.SetDirty(poseAsset);
            AssetDatabase.SaveAssets();
            Debug.Log($"Captured pose '{poseAsset.name}' from '{skeletonRoot.name}' with {poseAsset.bones.Count} bones.");
        }

        public static void ApplyPoseToSkeleton(Transform skeletonRoot, PoseAsset poseAsset)
        {
            if (skeletonRoot == null)
            {
                throw new System.ArgumentNullException(nameof(skeletonRoot));
            }

            if (poseAsset == null)
            {
                throw new System.ArgumentNullException(nameof(poseAsset));
            }

            int appliedCount = 0;
            for (int i = 0; i < poseAsset.bones.Count; i++)
            {
                BonePoseData bone = poseAsset.bones[i];
                Transform target = string.IsNullOrEmpty(bone.bonePath)
                    ? skeletonRoot
                    : skeletonRoot.Find(bone.bonePath);

                if (target == null)
                {
                    continue;
                }

                Undo.RecordObject(target, $"Load Pose {poseAsset.name}");
                target.localPosition = bone.localPosition;
                target.localRotation = bone.localRotation;
                target.localScale = bone.localScale;
                EditorUtility.SetDirty(target);
                appliedCount++;
            }

            Debug.Log($"Loaded pose '{poseAsset.name}' onto '{skeletonRoot.name}' with {appliedCount}/{poseAsset.bones.Count} bones applied.");
        }
    }
}
