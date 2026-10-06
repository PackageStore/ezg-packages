using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityFigmaBridge.Editor.Settings
{

    public class UnityFigmaBridgeSettingsProvider : SettingsProvider
    {
        private SerializedObject m_SerializedObject;
        private FigmaConnectionPanel m_Panel;

        public UnityFigmaBridgeSettingsProvider(string path, SettingsScope scopes, IEnumerable<string> keywords = null)
            : base(path, scopes, keywords)
        {
        }

        public static bool IsSettingsAvailable()
        {
            return true;
        }

        private UnityFigmaBridgeSettings unityFigmaBridgeSettingsAsset;

        private void BuildPanel()
        {
            if (unityFigmaBridgeSettingsAsset == null || m_Panel != null) return;
            m_SerializedObject = new SerializedObject(unityFigmaBridgeSettingsAsset);
            m_Panel = new FigmaConnectionPanel(() => UnityEditorInternal.InternalEditorUtility.RepaintAllViews());
        }

        private void TearDown()
        {
            EditorApplication.update -= OnUpdate;
            m_Panel?.Dispose();
            m_Panel = null;
            m_SerializedObject = null;
        }

        private void Attach()
        {
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
            BuildPanel();
        }

        private void OnUpdate()
        {
            if (m_Panel == null || unityFigmaBridgeSettingsAsset == null) return;
            m_Panel.Tick(unityFigmaBridgeSettingsAsset);
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            unityFigmaBridgeSettingsAsset = FindUnityBridgeSettingsAsset();
            if (unityFigmaBridgeSettingsAsset != null) Attach();
        }

        public override void OnDeactivate()
        {
            TearDown();
        }

        /// <summary>
        /// Finds the first (and should be only) matching asset
        /// </summary>
        /// <returns></returns>
        public static UnityFigmaBridgeSettings FindUnityBridgeSettingsAsset()
        {
            var assets = AssetDatabase.FindAssets($"t:{typeof(UnityFigmaBridgeSettings).Name}");
            if (assets == null || assets.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<UnityFigmaBridgeSettings>(AssetDatabase.GUIDToAssetPath(assets[0]));
        }

        public override void OnGUI(string searchContext)
        {
            if (unityFigmaBridgeSettingsAsset == null)
            {
                TearDown();
                unityFigmaBridgeSettingsAsset = FindUnityBridgeSettingsAsset();
                if (unityFigmaBridgeSettingsAsset != null) Attach();
            }

            if (unityFigmaBridgeSettingsAsset == null)
            {
                GUILayout.Label("Create Unity Figma Bridge Settings Asset");
                if (GUILayout.Button("Create..."))
                {
                    unityFigmaBridgeSettingsAsset = GenerateUnityFigmaBridgeSettingsAsset();
                    Attach();
                }

                return;
            }

            if (m_Panel == null) BuildPanel();

            var changed = m_Panel.Draw(m_SerializedObject, unityFigmaBridgeSettingsAsset);

            m_SerializedObject.Update();
            FigmaSettingsDrawer.DrawField(m_SerializedObject, "OnlyImportSelectedPages");
            FigmaSettingsDrawer.DrawField(m_SerializedObject, "ImportSelectionOnly");
            FigmaSettingsDrawer.DrawField(m_SerializedObject, "OnlyImportListedScreens");
            m_SerializedObject.ApplyModifiedProperties();

            FigmaSettingsDrawer.DrawFoldouts(m_SerializedObject);

            GUILayout.Space(6);
            if (GUILayout.Button("Open Figma Bridge window", GUILayout.Height(28)))
                FigmaBridgeWindow.Open();

            if (changed)
            {
                EditorUtility.SetDirty(unityFigmaBridgeSettingsAsset);
                AssetDatabase.SaveAssets();
            }
        }


        // Register the SettingsProvider
        [SettingsProvider]
        public static SettingsProvider CreateMyCustomSettingsProvider()
        {
            if (IsSettingsAvailable())
            {
                var provider =
                    new UnityFigmaBridgeSettingsProvider("Project/Unity Figma Bridge", SettingsScope.Project);
                return provider;
            }

            return null;
        }

        public static UnityFigmaBridgeSettings GenerateUnityFigmaBridgeSettingsAsset()
        {
            // try create a new version asset.
            var newSettingsAsset = UnityFigmaBridgeSettings.CreateInstance<UnityFigmaBridgeSettings>();

            // Save to the project
            AssetDatabase.CreateAsset(newSettingsAsset, "Assets/UnityFigmaBridgeSettings.asset");
            AssetDatabase.SaveAssets();
            Debug.Log("Generating UnityFigmaBridgeSettings asset", newSettingsAsset);

            return newSettingsAsset;
        }
    }
}