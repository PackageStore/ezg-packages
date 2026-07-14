using System.Collections.Generic;
using Ezg.ProceduralAnimation;
using UnityEditor;
using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    public partial class InbetweenAnimationGeneratorWindow
    {
        private void DrawPreviewControls()
        {
            EditorGUILayout.Space(8f);
            previewFoldout = EditorGUILayout.Foldout(
                previewFoldout,
                InbetweenGeneratorContent.PreviewFoldout,
                true);

            if (!previewFoldout)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUI.BeginChangeCheck();
                useSkeletonRootAsPreviewTarget = EditorGUILayout.Toggle(
                    InbetweenGeneratorContent.UseSkeletonRoot,
                    useSkeletonRootAsPreviewTarget);
                if (EditorGUI.EndChangeCheck())
                {
                    previewTarget = useSkeletonRootAsPreviewTarget ? skeletonRoot : null;
                    StopPreview();
                }

                if (useSkeletonRootAsPreviewTarget)
                {
                    previewTarget = skeletonRoot;
                }

                using (new EditorGUI.DisabledScope(useSkeletonRootAsPreviewTarget))
                {
                    EditorGUI.BeginChangeCheck();
                    Transform newPreviewTarget = (Transform)EditorGUILayout.ObjectField(
                        InbetweenGeneratorContent.PreviewTarget,
                        previewTarget,
                        typeof(Transform),
                        true);
                    if (EditorGUI.EndChangeCheck())
                    {
                        previewTarget = newPreviewTarget;
                        StopPreview();
                    }
                }

                using (new EditorGUI.DisabledScope(selectedPathIndex < 0 || selectedPathIndex >= finalPaths.Count || previewTarget == null))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (!isPlayingPreview)
                        {
                            if (GUILayout.Button(InbetweenGeneratorContent.PlayPreview, GUILayout.Height(26f)))
                            {
                                StartPreview();
                            }
                        }
                        else
                        {
                            if (GUILayout.Button("■ Stop", GUILayout.Height(26f)))
                            {
                                StopPreview();
                            }
                        }

                        if (GUILayout.Button("⏸ Pause", GUILayout.Height(26f)))
                        {
                            StopPreview();
                        }
                    }

                    if (previewClip != null)
                    {
                        float newTime = EditorGUILayout.Slider(new GUIContent("Time", "Vị trí thời gian (giây) trên <b>AnimationClip</b> preview. Kéo để scrub thủ công đến frame mong muốn."), previewScrubTime, 0f, previewClip.length);
                        if (Mathf.Abs(newTime - previewScrubTime) > 0.001f)
                        {
                            previewScrubTime = newTime;
                            if (!isPlayingPreview)
                            {
                                SamplePreview(previewScrubTime);
                            }
                        }

                        EditorGUILayout.LabelField($"Duration: {previewClip.length:F2}s", EditorStyles.miniLabel);
                    }
                }
            }
        }
    }
}
