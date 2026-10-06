using UnityEditor;
using UnityEngine;

namespace UnityFigmaBridge.Editor.Settings
{
    [CustomEditor(typeof(UnityFigmaBridgeSettings))]
    public sealed class UnityFigmaBridgeSettingsInspector : UnityEditor.Editor
    {
        private FigmaConnectionPanel m_Panel;

        private void OnEnable()
        {
            m_Panel = new FigmaConnectionPanel(Repaint);
            EditorApplication.update += OnUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnUpdate;
            m_Panel?.Dispose();
            m_Panel = null;
        }

        private void OnUpdate()
        {
            if (m_Panel == null || target == null) return;
            m_Panel.Tick((UnityFigmaBridgeSettings)target);
        }

        public override void OnInspectorGUI()
        {
            if (m_Panel == null) return;
            var asset = (UnityFigmaBridgeSettings)target;
            var changed = m_Panel.Draw(serializedObject, (UnityFigmaBridgeSettings)target);

            serializedObject.Update();
            FigmaSettingsDrawer.DrawField(serializedObject, "OnlyImportSelectedPages");
            FigmaSettingsDrawer.DrawField(serializedObject, "ImportSelectionOnly");
            FigmaSettingsDrawer.DrawField(serializedObject, "OnlyImportListedScreens");
            serializedObject.ApplyModifiedProperties();

            FigmaSettingsDrawer.DrawFoldouts(serializedObject);

            GUILayout.Space(6);
            if (GUILayout.Button("Open Figma Bridge", GUILayout.Height(28)))
                FigmaBridgeWindow.Open();

            if (changed)
            {
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
