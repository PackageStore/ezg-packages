using System.Collections.Generic;
using Ezg.ProceduralAnimation;
using UnityEditor;
using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    public partial class InbetweenAnimationGeneratorWindow
    {
        private void DrawBatchExportPanel()
        {
            EditorGUILayout.Space(8f);
            batchAnimationExportFoldout = EditorGUILayout.Foldout(
                batchAnimationExportFoldout,
                InbetweenGeneratorContent.BatchAnimationExportFoldout,
                true);

            if (!batchAnimationExportFoldout)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                batchBaseName = EditorGUILayout.TextField(new GUIContent("Base Name", "Tên gốc dùng để đặt tên cho các <b>AnimationClip</b> xuất ra. Được sử dụng với token <b>{base}</b> trong <b>Name Template</b>.\n<i>Ví dụ: anim</i>"), batchBaseName);

                EditorGUI.BeginChangeCheck();
                graphAsset.generationOptions.clipNameTemplate = EditorGUILayout.TextField(new GUIContent("Name Template", "Mẫu tên cho clip xuất ra. Hỗ trợ token: <b>{base}</b> (Base Name), <b>{path}</b> (tên path), <b>{index}</b> (số thứ tự từ 1), <b>{index0}</b> (số thứ tự từ 0).\n<i>Ví dụ: {base}_{path}_{index}</i>"), graphAsset.generationOptions.clipNameTemplate);
                if (EditorGUI.EndChangeCheck()) MarkDirty();

                EditorGUILayout.LabelField("Tokens: {base}, {path}, {index}, {index0}", EditorStyles.miniLabel);

                EditorGUI.BeginChangeCheck();
                graphAsset.generationOptions.frameRate = EditorGUILayout.IntField(new GUIContent("Frame Rate", "Tốc độ khung hình (FPS) của <b>AnimationClip</b> xuất ra.\n<i>Ví dụ: 30 (mượt), 60 (rất mượt)</i>"), graphAsset.generationOptions.frameRate);
                graphAsset.generationOptions.generatePosition = EditorGUILayout.Toggle(new GUIContent("Generate Position", "Có tạo keyframe cho <b>vị trí</b> (LocalPosition) của các transform trong clip không. Nên bật nếu animation có di chuyển."), graphAsset.generationOptions.generatePosition);
                graphAsset.generationOptions.generateRotation = EditorGUILayout.Toggle(new GUIContent("Generate Rotation", "Có tạo keyframe cho <b>góc quay</b> (LocalRotation) của các transform trong clip không. Hầu hết animation cần bật mục này."), graphAsset.generationOptions.generateRotation);
                graphAsset.generationOptions.generateScale = EditorGUILayout.Toggle(new GUIContent("Generate Scale", "Có tạo keyframe cho <b>tỉ lệ</b> (LocalScale) của các transform trong clip không. Thường chỉ bật khi nhân vật thay đổi kích thước."), graphAsset.generationOptions.generateScale);
                graphAsset.generationOptions.overwriteExistingClips = EditorGUILayout.Toggle(new GUIContent("Overwrite Existing", "Ghi đè lên các <b>AnimationClip</b> đã tồn tại cùng tên trong thư mục xuất. Nếu <b>tắt</b>, các clip trùng tên sẽ bị bỏ qua."), graphAsset.generationOptions.overwriteExistingClips);
                graphAsset.generationOptions.maxBatchCountGuard = EditorGUILayout.IntField(new GUIContent("Max Batch Guard", "Ngưỡng cảnh báo số lượng path tối đa cho phép khi batch export. Nếu vượt quá sẽ hiện hộp thoại xác nhận. Đặt <b>0</b> để tắt cảnh báo."), graphAsset.generationOptions.maxBatchCountGuard);
                if (EditorGUI.EndChangeCheck()) MarkDirty(false);

                EditorGUILayout.Space(4f);
                DrawGraphOutputFolderField();

                int selectedCount = 0;
                for (int i = 0; i < pathSelection.Length; i++)
                {
                    if (pathSelection[i]) selectedCount++;
                }

                using (new EditorGUI.DisabledScope(selectedCount == 0))
                {
                    if (GUILayout.Button($"Generate {selectedCount} Selected Clips", GUILayout.Height(32f)))
                    {
                        BatchGenerate();
                    }
                }
            }
        }
    }
}
