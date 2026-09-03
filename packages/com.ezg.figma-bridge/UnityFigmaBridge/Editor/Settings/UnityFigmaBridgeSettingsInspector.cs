using UnityEditor;
using UnityEngine;

namespace UnityFigmaBridge.Editor.Settings
{
    /// <summary>
    /// The settings asset is edited from the Figma Bridge window, not the inspector
    /// </summary>
    [CustomEditor(typeof(UnityFigmaBridgeSettings))]
    public sealed class UnityFigmaBridgeSettingsInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Edit these settings from the Figma Bridge window:\nTools > EZG Technical Art > Figma Bridge",
                MessageType.Info);
            GUILayout.Space(6);
            if (GUILayout.Button("Open Figma Bridge", GUILayout.Height(28)))
                FigmaBridgeWindow.Open();
        }
    }
}
