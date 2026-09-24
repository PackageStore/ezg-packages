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
        private string m_SelectedPageId;

        /// <summary>
        ///     The bridge's only menu item: it opens the window. Sync / offline re-import / post-processors
        ///     are buttons in the window; scripts and MCP call <see cref="UnityFigmaBridgeImporter.SyncDocument"/>,
        ///     <see cref="UnityFigmaBridgeImporter.SyncDocumentOffline"/> and
        ///     <see cref="UnityFigmaBridgeImporter.RunPostProcessorsOnly"/> directly.
        /// </summary>
        public const string MENU_PATH = "Tools/EZG Technical Art/Figma Bridge";

        [MenuItem(MENU_PATH)]
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

            using (var scroll = new EditorGUILayout.ScrollViewScope(m_MainScrollPos))
            {
                m_MainScrollPos = scroll.scrollPosition;

                if (m_SelectedTab == 0)
                    DrawSetupTab();
                else
                    DrawTokenTab(prev != 1);
            }

            // Outside the scroll view, so a long screen list never pushes it out of reach
            if (m_SelectedTab == 0)
            {
                GUILayout.Space(4);
                if (GUILayout.Button("Sync Document", GUILayout.Height(32)))
                    UnityFigmaBridgeImporter.SyncDocument();

                using (new EditorGUILayout.HorizontalScope())
                {
                    var hasCache = System.IO.File.Exists(FigmaApiUtils.CachedDocumentPath);
                    using (new EditorGUI.DisabledScope(!hasCache))
                    {
                        if (GUILayout.Button(new GUIContent("Re-import from cache (offline)",
                                hasCache
                                    ? "Rebuild every output from Assets/FigmaOutput.json and the sprites already on disk. No Figma API call."
                                    : "No cached document yet - run Sync Document online once."), GUILayout.Height(24)))
                            UnityFigmaBridgeImporter.SyncDocumentOffline();
                    }

                    if (GUILayout.Button(new GUIContent("Run Post-Processors (no Sync)",
                            "Run every IFigmaImportPostProcessor in the project against the prefabs on disk."), GUILayout.Height(24)))
                        UnityFigmaBridgeImporter.RunPostProcessorsOnly();
                }

                var selectedPrefab = Verify.FigmaVisualCheck.SelectedPrefabPath();
                using (new EditorGUI.DisabledScope(selectedPrefab == null))
                {
                    if (GUILayout.Button(new GUIContent("Visual Check (selected screen prefab)",
                            "Compare the selected screen prefab with Figma's render of its frame, container by container " +
                            $"(SSIM, pass {Verify.FigmaVisualCheck.DefaultPassScore:0.00}, text-only {Verify.FigmaVisualCheck.DefaultTextPassScore:0.00}). " +
                            $"Output: {Verify.FigmaVisualCheck.OutputRoot}."),
                            GUILayout.Height(24)))
                        Debug.Log(Verify.FigmaVisualCheck.Run(selectedPrefab).ToString());
                }
            }
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
                var changed = ListPages("Select Pages to import", m_Settings.PageDataList);
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
            }
            m_SerializedSettings.ApplyModifiedProperties();

            GUILayout.Space(10);
            var (isValid, fileId) = FigmaApiUtils.GetFigmaDocumentIdFromUrl(m_Settings.DocumentUrl);
            EditorGUILayout.HelpBox(
                isValid ? $"Valid Figma Document URL - FileID: {fileId}" : "Invalid Figma Document URL",
                isValid ? MessageType.Info : MessageType.Error);
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

            UnityFigmaBridgeImporter.WarnMissingRequiredPages(figmaFile, settings);
            settings.RefreshForUpdatedPages(figmaFile);
            settings.RefreshForUpdatedScreens(figmaFile);
            settings.RefreshForUpdatedComponents(figmaFile);

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        /// <summary>
        /// List every screen frame and top-level component, grouped by the Figma page it sits on
        /// </summary>
        private bool ListScreens(UnityFigmaBridgeSettings settings)
        {
            var applyChanges = false;
            var screenRows = settings.ScreenNameOverrides;
            var componentRows = settings.ComponentSelections;

            using (new EditorGUILayout.VerticalScope())
            {
                GUILayout.Label("Select Screens and Components to import", EditorStyles.boldLabel);
                GUILayout.Label("Blank prefab name keeps the Figma frame name.", EditorStyles.miniLabel);
                GUILayout.Space(5);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Refresh from Figma", GUILayout.Width(130)))
                        RefreshScreenList(settings);

                    if (GUILayout.Button("Select all", GUILayout.Width(80)))
                    {
                        applyChanges = true;
                        foreach (var data in screenRows) data.ExcludeFromImport = false;
                        foreach (var data in componentRows) data.Include = true;
                    }

                    if (GUILayout.Button("Deselect all", GUILayout.Width(80)))
                    {
                        applyChanges = true;
                        foreach (var data in screenRows) data.ExcludeFromImport = true;
                        foreach (var data in componentRows) data.Include = false;
                    }
                }
                GUILayout.Space(5);

                if (screenRows.Count == 0 && componentRows.Count == 0)
                {
                    EditorGUILayout.HelpBox("No screens or components listed yet - press Refresh from Figma.",
                        MessageType.Info);
                    return applyChanges;
                }

                EditorGUILayout.HelpBox(settings.ImportSelectionOnly
                        ? "Only ticked items are imported, plus every component they use through instances. " +
                          "Everything else keeps its prefabs and sprites from the last import."
                        : "'Import Selection Only' is off: every component is imported, and the component ticks are ignored.",
                    MessageType.Info);

                var tickedScreens = new HashSet<string>(screenRows
                    .Where(row => !row.ExcludeFromImport && IsPageImported(settings, row.PageNodeId))
                    .Select(row => row.FrameName));
                var tickedComponents = new HashSet<string>(componentRows
                    .Where(row => row.Include && IsPageImported(settings, row.PageNodeId))
                    .Select(row => row.NodeId));

                // One tab per page, so a long page does not push the others out of reach
                var pageIds = PageIdsInOrder(settings).ToList();
                if (!pageIds.Contains(m_SelectedPageId)) m_SelectedPageId = pageIds[0];
                var tabLabels = pageIds.Select(pageId => PageTabLabel(settings, pageId)).ToArray();
                m_SelectedPageId = pageIds[GUILayout.Toolbar(pageIds.IndexOf(m_SelectedPageId), tabLabels)];
                GUILayout.Space(4);

                if (ListScreenPageGroup(settings, m_SelectedPageId, tickedScreens, tickedComponents))
                    applyChanges = true;

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

        private static bool IsPageImported(UnityFigmaBridgeSettings settings, string pageId) =>
            !settings.OnlyImportSelectedPages ||
            (settings.PageDataList.FirstOrDefault(p => p.NodeId == pageId)?.Selected ?? false);

        /// <summary>Pages that have rows, in the order the page list holds them</summary>
        private static IEnumerable<string> PageIdsInOrder(UnityFigmaBridgeSettings settings)
        {
            var pageOrder = settings.PageDataList.Select(p => p.NodeId).ToList();
            return settings.ScreenNameOverrides.Select(row => row.PageNodeId ?? "")
                .Concat(settings.ComponentSelections.Select(row => row.PageNodeId ?? ""))
                .Distinct()
                .OrderBy(pageId =>
                {
                    var index = pageOrder.IndexOf(pageId);
                    return index >= 0 ? index : int.MaxValue;
                });
        }

        private static string PageTabLabel(UnityFigmaBridgeSettings settings, string pageId)
        {
            var rowCount = settings.ScreenNameOverrides.Count(row => (row.PageNodeId ?? "") == pageId) +
                           settings.ComponentSelections.Count(row => (row.PageNodeId ?? "") == pageId);
            return $"{PageName(settings, pageId)} ({rowCount})";
        }

        private static string PageName(UnityFigmaBridgeSettings settings, string pageId) =>
            settings.ScreenNameOverrides.Where(row => (row.PageNodeId ?? "") == pageId).Select(row => row.PageName)
                .Concat(settings.ComponentSelections.Where(row => (row.PageNodeId ?? "") == pageId).Select(row => row.PageName))
                .FirstOrDefault(name => !string.IsNullOrEmpty(name)) ?? "Unknown page";

        /// <summary>
        /// Draw one page's rows, sorted by name: its screen rows, then its component rows
        /// </summary>
        private static bool ListScreenPageGroup(UnityFigmaBridgeSettings settings, string pageId,
            HashSet<string> tickedScreens, HashSet<string> tickedComponents)
        {
            var applyChanges = false;
            var screenRows = settings.ScreenNameOverrides.Where(row => (row.PageNodeId ?? "") == pageId)
                .OrderBy(row => row.FrameName, System.StringComparer.OrdinalIgnoreCase).ToList();
            var componentRows = settings.ComponentSelections.Where(row => (row.PageNodeId ?? "") == pageId)
                .OrderBy(row => row.Name, System.StringComparer.OrdinalIgnoreCase).ToList();

            var counts = new List<string>();
            if (screenRows.Count > 0) counts.Add($"{screenRows.Count} screens");
            if (componentRows.Count > 0) counts.Add($"{componentRows.Count} components");
            var header = string.Join(", ", counts);
            if (!IsPageImported(settings, pageId)) header += "  - page not imported";

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(header, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("All", GUILayout.Width(34)))
                {
                    applyChanges = true;
                    foreach (var row in screenRows) row.ExcludeFromImport = false;
                    foreach (var row in componentRows) row.Include = true;
                }

                if (GUILayout.Button("None", GUILayout.Width(44)))
                {
                    applyChanges = true;
                    foreach (var row in screenRows) row.ExcludeFromImport = true;
                    foreach (var row in componentRows) row.Include = false;
                }
            }

            foreach (var data in screenRows)
            {
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

            foreach (var data in componentRows)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(14);

                    var include = EditorGUILayout.Toggle(data.Include, GUILayout.Width(16));
                    if (include != data.Include)
                    {
                        data.Include = include;
                        applyChanges = true;
                    }

                    EditorGUILayout.LabelField(data.Name, GUILayout.MinWidth(60));
                    EditorGUILayout.LabelField(ComponentRowStatus(data, tickedScreens, tickedComponents), EditorStyles.miniLabel);
                }
            }

            return applyChanges;
        }

        /// <summary>Why an unticked component still imports, or how many screens use it</summary>
        private static string ComponentRowStatus(FigmaComponentSelection row, HashSet<string> tickedScreens,
            HashSet<string> tickedComponents)
        {
            var kind = row.IsSet ? "set" : "component";
            var neededBy = row.NeededByScreens.Where(tickedScreens.Contains).ToList();
            if (!row.Include && neededBy.Count > 0)
                return $"{kind} · imported: used by {string.Join(", ", neededBy)}";
            if (!row.Include && row.NeededByComponents.Any(tickedComponents.Contains))
                return $"{kind} · imported: used by a ticked component";
            return row.NeededByScreens.Count > 0 ? $"{kind} · used by {row.NeededByScreens.Count} screen(s)" : kind;
        }

        /// <summary>
        /// List all pages in the settings file
        /// </summary>
        private bool ListPages(string listTitle, IReadOnlyList<FigmaPageData> dataList)
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

                foreach (var data in dataList) {
                    var isChecked = data.Selected;
                    data.Selected = EditorGUILayout.ToggleLeft(data.Name, data.Selected);
                    if (isChecked != data.Selected) {
                        applyChanges = true;
                    }
                }

                return applyChanges;

            }
        }
    }
}
