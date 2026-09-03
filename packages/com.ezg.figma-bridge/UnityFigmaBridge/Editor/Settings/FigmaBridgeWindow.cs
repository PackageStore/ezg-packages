using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Utils;

namespace UnityFigmaBridge.Editor.Settings
{
    /// <summary>
    /// Dedicated editor window for driving the Figma bridge - settings, token and sync
    /// </summary>
    public sealed class FigmaBridgeWindow : EditorWindow
    {
        private static readonly string[] s_TabLabels = { "Setup", "Token" };

        private UnityFigmaBridgeSettings m_Settings;
        private SerializedObject m_SerializedSettings;

        private int m_SelectedTab;
        private string m_TokenDraft;

        private Vector2 m_MainScrollPos;
        private Vector2 m_PageScrollPos;
        private Vector2 m_ScreenScrollPos;
        private readonly Dictionary<string, bool> m_PageFoldouts = new();

        [MenuItem("Tools/EZG Technical Art/Figma Bridge")]
        public static void Open()
        {
            var window = GetWindow<FigmaBridgeWindow>("Figma Bridge");
            window.minSize = new Vector2(360, 480);
            window.Show();
        }

        private void OnEnable()
        {
            m_Settings = UnityFigmaBridgeSettingsProvider.FindUnityBridgeSettingsAsset();
        }

        private void OnGUI()
        {
            if (m_Settings == null)
            {
                m_Settings = UnityFigmaBridgeSettingsProvider.FindUnityBridgeSettingsAsset();
                m_SerializedSettings = null;
            }

            if (m_Settings == null)
            {
                GUILayout.Space(8);
                GUILayout.Label("No Unity Figma Bridge settings asset found.");
                if (GUILayout.Button("Create Settings Asset"))
                    m_Settings = UnityFigmaBridgeSettingsProvider.GenerateUnityFigmaBridgeSettingsAsset();
                return;
            }

            var prev = m_SelectedTab;
            m_SelectedTab = GUILayout.Toolbar(m_SelectedTab, s_TabLabels);
            GUILayout.Space(8);

            using var scroll = new EditorGUILayout.ScrollViewScope(m_MainScrollPos);
            m_MainScrollPos = scroll.scrollPosition;

            if (m_SelectedTab == 0)
                DrawSetupTab();
            else
                DrawTokenTab(prev != 1);
        }

        private void DrawSetupTab()
        {
            var onlyImportPages = m_Settings.OnlyImportSelectedPages;
            var preEditUrl = m_Settings.DocumentUrl;

            DrawSettingsFields();

            // If the URL has changed, we want to reset the select pages to off and clear
            if (m_Settings.DocumentUrl != preEditUrl)
            {
                if (m_Settings.OnlyImportSelectedPages)
                {
                    m_Settings.OnlyImportSelectedPages = false;
                    m_Settings.PageDataList.Clear();
                }
            }
            else if (m_Settings.OnlyImportSelectedPages != onlyImportPages)
            {
                if (m_Settings.OnlyImportSelectedPages)
                    RefreshPageList(m_Settings);
                else
                    m_Settings.PageDataList.Clear();
            }

            if (m_Settings.OnlyImportSelectedPages)
            {
                GUILayout.Space(20);
                var changed = ListPages("Select Pages to import", m_Settings.PageDataList, ref m_PageScrollPos);
                if (changed)
                {
                    EditorUtility.SetDirty(m_Settings);
                    AssetDatabase.SaveAssetIfDirty(m_Settings);
                }
            }

            GUILayout.Space(20);
            if (ListScreens(m_Settings))
            {
                EditorUtility.SetDirty(m_Settings);
                AssetDatabase.SaveAssetIfDirty(m_Settings);
            }

            GUILayout.Space(20);
            if (GUILayout.Button("Sync Document", GUILayout.Height(32)))
                UnityFigmaBridgeImporter.SyncDocument();
        }

        private void DrawSettingsFields()
        {
            if (m_SerializedSettings == null || m_SerializedSettings.targetObject != m_Settings)
                m_SerializedSettings = new SerializedObject(m_Settings);

            m_SerializedSettings.Update();
            var prop = m_SerializedSettings.GetIterator();
            var enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (prop.propertyPath == "m_Script") continue;
                EditorGUILayout.PropertyField(prop, true);
                if (prop.propertyPath == nameof(UnityFigmaBridgeSettings.ImageFillFolder))
                    DrawResolvedOutputFolders();
            }
            m_SerializedSettings.ApplyModifiedProperties();

            GUILayout.Space(10);
            var (isValid, fileId) = FigmaApiUtils.GetFigmaDocumentIdFromUrl(m_Settings.DocumentUrl);
            EditorGUILayout.HelpBox(
                isValid ? $"Valid Figma Document URL - FileID: {fileId}" : "Invalid Figma Document URL",
                isValid ? MessageType.Info : MessageType.Error);
        }

        /// <summary>
        /// Blank folder fields fall back to defaults, so show where the output actually goes
        /// </summary>
        private void DrawResolvedOutputFolders()
        {
            var folders = FigmaPaths.Resolve(m_Settings, "<document name>", warnOnInvalid: false);
            EditorGUILayout.HelpBox(
                $"Screens: {folders.Screens}\n" +
                $"Components: {folders.Components}\n" +
                $"Pages: {folders.Pages}\n" +
                $"Image fills: {folders.ImageFills}",
                MessageType.None);
        }

        private void DrawTokenTab(bool justSwitched)
        {
            if (m_TokenDraft == null || justSwitched)
                m_TokenDraft = FigmaAccessToken.Read() ?? "";

            GUILayout.Label("Figma Personal Access Token", EditorStyles.boldLabel);
            GUILayout.Label("Stored in Unity PlayerPrefs on this machine - never written into the settings asset.",
                EditorStyles.miniLabel);
            GUILayout.Space(6);

            m_TokenDraft = EditorGUILayout.TextField("Token", m_TokenDraft);

            if (GUILayout.Button("Save Token"))
            {
                FigmaAccessToken.Write(m_TokenDraft);
                m_TokenDraft = FigmaAccessToken.Read() ?? "";
                GUI.FocusControl(null);
            }

            var hasToken = !string.IsNullOrEmpty(FigmaAccessToken.Read());
            EditorGUILayout.LabelField("Status", hasToken ? "Token set" : "No token set", EditorStyles.miniLabel);
        }

        /// <summary>
        /// Download the document and refresh the page list
        /// </summary>
        private async void RefreshPageList(UnityFigmaBridgeSettings settings)
        {
            // Only refresh pages if we have a valid file
            var requirementsMet = UnityFigmaBridgeImporter.CheckRequirements();
            if (!requirementsMet) return;

            // Retrieve the Figma document
            var figmaFile = await UnityFigmaBridgeImporter.DownloadFigmaDocument(settings.FileId);
            if (figmaFile == null) return;

            settings.RefreshForUpdatedPages(figmaFile);

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        /// <summary>
        /// Download the document and refresh both the page list and the screen list
        /// </summary>
        private async void RefreshScreenList(UnityFigmaBridgeSettings settings)
        {
            var requirementsMet = UnityFigmaBridgeImporter.CheckRequirements();
            if (!requirementsMet) return;

            var figmaFile = await UnityFigmaBridgeImporter.DownloadFigmaDocument(settings.FileId);
            if (figmaFile == null) return;

            settings.RefreshForUpdatedPages(figmaFile);
            settings.RefreshForUpdatedScreens(figmaFile);

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        /// <summary>
        /// List every screen frame, grouped by the Figma page it sits on
        /// </summary>
        private bool ListScreens(UnityFigmaBridgeSettings settings)
        {
            var applyChanges = false;
            var overrideList = settings.ScreenNameOverrides;

            using (new EditorGUILayout.VerticalScope())
            {
                GUILayout.Label("Select Screens to import", EditorStyles.boldLabel);
                GUILayout.Label("Blank prefab name keeps the Figma frame name.", EditorStyles.miniLabel);
                GUILayout.Space(5);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Refresh from Figma", GUILayout.Width(130)))
                        RefreshScreenList(settings);

                    if (GUILayout.Button("Select all", GUILayout.Width(80)))
                    {
                        applyChanges = true;
                        foreach (var data in overrideList) data.ExcludeFromImport = false;
                    }

                    if (GUILayout.Button("Deselect all", GUILayout.Width(80)))
                    {
                        applyChanges = true;
                        foreach (var data in overrideList) data.ExcludeFromImport = true;
                    }
                }
                GUILayout.Space(5);

                if (overrideList.Count == 0)
                {
                    EditorGUILayout.HelpBox("No screens listed yet - press Refresh from Figma.",
                        MessageType.Info);
                    return applyChanges;
                }

                using (var scrollViewScope = new EditorGUILayout.ScrollViewScope(m_ScreenScrollPos,
                           GUILayout.MaxHeight(320)))
                {
                    // Refresh writes rows in document order, so a run of rows sharing a page id is
                    // exactly one page and needs no sorting
                    var rowIndex = 0;
                    while (rowIndex < overrideList.Count)
                    {
                        var pageId = overrideList[rowIndex].PageNodeId ?? "";
                        var groupEnd = rowIndex;
                        while (groupEnd < overrideList.Count &&
                               (overrideList[groupEnd].PageNodeId ?? "") == pageId) groupEnd++;

                        if (ListScreenPageGroup(settings, overrideList, rowIndex, groupEnd, pageId))
                            applyChanges = true;

                        rowIndex = groupEnd;
                    }
                    m_ScreenScrollPos = scrollViewScope.scrollPosition;
                }

                if (!settings.OnlyImportListedScreens)
                {
                    EditorGUILayout.HelpBox(
                        "'Only Import Listed Screens' is off, so a frame missing from this list is " +
                        "still imported under its Figma name. Refresh after adding frames in Figma.",
                        MessageType.Info);
                }

                return applyChanges;
            }
        }

        /// <summary>
        /// Draw one page's foldout and the screen rows below it
        /// </summary>
        private bool ListScreenPageGroup(UnityFigmaBridgeSettings settings,
            IReadOnlyList<FigmaScreenNameOverride> overrideList, int firstRow, int endRow, string pageId)
        {
            var applyChanges = false;
            var pageName = overrideList[firstRow].PageName;
            if (string.IsNullOrEmpty(pageName)) pageName = "Unknown page";

            var pageData = settings.PageDataList.FirstOrDefault(p => p.NodeId == pageId);
            var pageIsImported = !settings.OnlyImportSelectedPages || (pageData?.Selected ?? false);

            var header = $"{pageName}  ({endRow - firstRow})";
            if (!pageIsImported) header += "  - page not imported";

            if (!m_PageFoldouts.TryGetValue(pageId, out var expanded)) expanded = pageIsImported;

            using (new EditorGUILayout.HorizontalScope())
            {
                expanded = EditorGUILayout.Foldout(expanded, header, true);

                if (GUILayout.Button("All", GUILayout.Width(34)))
                {
                    applyChanges = true;
                    for (var i = firstRow; i < endRow; i++) overrideList[i].ExcludeFromImport = false;
                }

                if (GUILayout.Button("None", GUILayout.Width(44)))
                {
                    applyChanges = true;
                    for (var i = firstRow; i < endRow; i++) overrideList[i].ExcludeFromImport = true;
                }
            }
            m_PageFoldouts[pageId] = expanded;

            if (!expanded) return applyChanges;

            for (var i = firstRow; i < endRow; i++)
            {
                var data = overrideList[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(14);

                    var shouldImport = EditorGUILayout.Toggle(!data.ExcludeFromImport, GUILayout.Width(16));
                    if (shouldImport == data.ExcludeFromImport)
                    {
                        data.ExcludeFromImport = !shouldImport;
                        applyChanges = true;
                    }

                    EditorGUILayout.LabelField(data.FrameName, GUILayout.MinWidth(60));

                    var prefabName = EditorGUILayout.TextField(data.PrefabName);
                    if (prefabName != data.PrefabName)
                    {
                        data.PrefabName = prefabName;
                        applyChanges = true;
                    }
                }
            }

            return applyChanges;
        }

        /// <summary>
        /// List all pages in the settings file
        /// </summary>
        private bool ListPages(string listTitle, IReadOnlyList<FigmaPageData> dataList, ref Vector2 scrollPos)
        {
            var applyChanges = false;
            using (new EditorGUILayout.VerticalScope()) {
                GUILayout.Label(listTitle, EditorStyles.boldLabel);
                GUILayout.Space(5);
                using (new EditorGUILayout.HorizontalScope()) {
                    if (GUILayout.Button("Select all", GUILayout.Width(80))) {
                        applyChanges = true;
                        foreach (var data in dataList) {
                            data.Selected = true;
                        }
                    }

                    if (GUILayout.Button("Deselect all", GUILayout.Width(80))) {
                        applyChanges = true;
                        foreach (var data in dataList) {
                            data.Selected = false;
                        }
                    }
                }
                GUILayout.Space(5);

                using (var scrollViewScope = new EditorGUILayout.ScrollViewScope(scrollPos))
                {
                    foreach (var data in dataList) {
                        var isChecked = data.Selected;
                        data.Selected = EditorGUILayout.ToggleLeft(data.Name, data.Selected);
                        if (isChecked != data.Selected) {
                            applyChanges = true;
                        }

                    }
                    scrollPos = scrollViewScope.scrollPosition;
                }

                return applyChanges;

            }
        }
    }
}
