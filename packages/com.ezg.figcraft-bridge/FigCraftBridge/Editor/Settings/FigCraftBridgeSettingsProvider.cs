using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FigCraftBridge.Editor.Settings
{

    public class FigCraftBridgeSettingsProvider : SettingsProvider
    {
        private SerializedObject m_SerializedObject;
        private FigmaConnectionPanel m_Panel;

        public FigCraftBridgeSettingsProvider(string path, SettingsScope scopes, IEnumerable<string> keywords = null)
            : base(path, scopes, keywords)
        {
        }

        public static bool IsSettingsAvailable()
        {
            return true;
        }

        private FigCraftBridgeSettings unityFigmaBridgeSettingsAsset;

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
        public static FigCraftBridgeSettings FindUnityBridgeSettingsAsset()
        {
            var assets = AssetDatabase.FindAssets($"t:{typeof(FigCraftBridgeSettings).Name}");
            if (assets == null || assets.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<FigCraftBridgeSettings>(AssetDatabase.GUIDToAssetPath(assets[0]));
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
                GUILayout.Label("Create FigCraft Bridge Settings Asset");
                if (GUILayout.Button("Create..."))
                {
                    unityFigmaBridgeSettingsAsset = GenerateFigCraftBridgeSettingsAsset();
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
            if (GUILayout.Button("Open FigCraft Bridge window", GUILayout.Height(28)))
                FigCraftBridgeWindow.Open();

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
                    new FigCraftBridgeSettingsProvider("Project/FigCraft Bridge", SettingsScope.Project);
                return provider;
            }

            return null;
        }

        public static FigCraftBridgeSettings GenerateFigCraftBridgeSettingsAsset()
        {
            // try create a new version asset.
            var newSettingsAsset = FigCraftBridgeSettings.CreateInstance<FigCraftBridgeSettings>();

            // Save to the project
            AssetDatabase.CreateAsset(newSettingsAsset, "Assets/FigCraftBridgeSettings.asset");
            AssetDatabase.SaveAssets();
            Debug.Log("Generating FigCraftBridgeSettings asset", newSettingsAsset);

            return newSettingsAsset;
        }
    }
}