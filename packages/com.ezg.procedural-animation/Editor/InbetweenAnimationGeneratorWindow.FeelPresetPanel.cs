using UnityEditor;
using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    public partial class InbetweenAnimationGeneratorWindow
    {
        private void DrawFeelPresetTab()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Feel Presets", EditorStyles.boldLabel);

            DrawFeelPresetFolderField();

            if (GUILayout.Button("Create Default Feel Presets", GUILayout.Height(28f)))
            {
                if (string.IsNullOrEmpty(feelPresetFolder) ||
                    (feelPresetFolder != "Assets" && !feelPresetFolder.StartsWith("Assets/")))
                {
                    EditorUtility.DisplayDialog("Invalid Folder", "Choose a folder inside this project's Assets folder.", "OK");
                    return;
                }

                DefaultFeelPresetFactory.CreateDefaultPresets(feelPresetFolder);
            }
        }

        private void DrawFeelPresetFolderField()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DefaultAsset folderAsset = !string.IsNullOrEmpty(feelPresetFolder)
                    ? AssetDatabase.LoadAssetAtPath<DefaultAsset>(feelPresetFolder)
                    : null;

                EditorGUI.BeginChangeCheck();
                DefaultAsset newFolderAsset = (DefaultAsset)EditorGUILayout.ObjectField(
                    InbetweenGeneratorContent.PresetFolder,
                    folderAsset,
                    typeof(DefaultAsset),
                    false);
                if (EditorGUI.EndChangeCheck())
                {
                    SetFeelPresetFolderFromAsset(newFolderAsset);
                }

                if (GUILayout.Button("Pick", GUILayout.Width(48f)))
                {
                    PickFeelPresetFolder();
                }
            }
        }
    }
}
