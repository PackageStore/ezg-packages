using System.Collections.Generic;
using Ezg.ProceduralAnimation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    /// <summary>
    /// AnimatorController integration: collect generated clips into a .controller so
    /// runtime players (e.g. ProceduralAnimPlayer, which iterates
    /// animator.runtimeAnimatorController.animationClips) can discover and chain them.
    /// </summary>
    public static partial class ProceduralAnimationApi
    {
        /// <summary>
        /// Creates (or updates) an AnimatorController and adds every AnimationClip found under
        /// clipsFolder as a state on layer 0. clipNamePrefix optionally filters which clips are
        /// added (e.g. "Idle_" for one generated set). Already-added clips are skipped, so the
        /// call is idempotent and safe to re-run after each regeneration.
        /// </summary>
        public static string BuildAnimatorController(string controllerPath, string clipsFolder, string clipNamePrefix = null)
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            bool created = false;
            if (controller == null)
            {
                AnimationClipWriter.EnsureAssetFolder(GetFolder(controllerPath));
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                created = true;
            }

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            var existingMotions = new HashSet<AnimationClip>();
            foreach (ChildAnimatorState child in stateMachine.states)
            {
                if (child.state.motion is AnimationClip existingClip)
                {
                    existingMotions.Add(existingClip);
                }
            }

            int added = 0;
            int skipped = 0;
            string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { clipsFolder });
            foreach (string guid in guids)
            {
                string clipPath = AssetDatabase.GUIDToAssetPath(guid);
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip == null || (!string.IsNullOrEmpty(clipNamePrefix) && !clip.name.StartsWith(clipNamePrefix)))
                {
                    continue;
                }

                if (existingMotions.Contains(clip))
                {
                    skipped++;
                    continue;
                }

                controller.AddMotion(clip);
                added++;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return $"{(created ? "CREATED" : "UPDATED")} {controllerPath}: {added} clips added, {skipped} already present";
        }

        /// <summary>Lists the clips currently referenced by a controller (what ProceduralAnimPlayer will cycle).</summary>
        public static string DescribeAnimatorController(string controllerPath)
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                return $"ERROR controller not found: {controllerPath}";
            }

            var sb = new System.Text.StringBuilder();
            AnimationClip[] clips = controller.animationClips;
            sb.AppendLine($"CONTROLLER {controller.name} clips={clips.Length}");
            foreach (AnimationClip clip in clips)
            {
                sb.AppendLine(clip.name);
            }

            return sb.ToString();
        }
    }
}
