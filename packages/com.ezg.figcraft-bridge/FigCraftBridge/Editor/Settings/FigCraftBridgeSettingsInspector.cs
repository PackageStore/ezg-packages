using UnityEditor;
using UnityEngine;

namespace FigCraftBridge.Editor.Settings
{
    [CustomEditor(typeof(FigCraftBridgeSettings))]
    public sealed class FigCraftBridgeSettingsInspector : UnityEditor.Editor
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
            m_Panel.Tick((FigCraftBridgeSettings)target);
        }

        public override void OnInspectorGUI()
        {
            if (m_Panel == null) return;
            var asset = (FigCraftBridgeSettings)target;
            var changed = m_Panel.Draw(serializedObject, (FigCraftBridgeSettings)target);

            serializedObject.Update();
            FigmaSettingsDrawer.DrawField(serializedObject, "OnlyImportSelectedPages");
            FigmaSettingsDrawer.DrawField(serializedObject, "ImportSelectionOnly");
            FigmaSettingsDrawer.DrawField(serializedObject, "OnlyImportListedScreens");
            serializedObject.ApplyModifiedProperties();

            FigmaSettingsDrawer.DrawFoldouts(serializedObject);

            GUILayout.Space(6);
            if (GUILayout.Button("Open FigCraft Bridge", GUILayout.Height(28)))
                FigCraftBridgeWindow.Open();

            if (changed)
            {
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
