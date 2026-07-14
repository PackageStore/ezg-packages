#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    using System.Collections;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    // ---------------------------------------------------------------------------
    // Main EditorWindow for the Ezg Icon CSV Generator tool.
    // Menu: Tools > EZG Technical Art > Icon CSV Generator
    //
    // Layout (OnGUI):
    //   1. ProcessPendingActions()           ← FIRST, before any drawing
    //   2. DrawAutoSaveBar()                 ← global top bar (settings ObjectField here)
    //   3. DrawGenerationSettingsSection()   ← AR / size / concurrency
    //   4. DrawApiKeySection()               ← Gemini key
    //   5. Global "Load Rows" status line
    //   6. ONE outer BeginScrollView
    //   7. For each group index: DrawGroupBlock()
    //      - Foldout header (items count)
    //      - Group config fields (name, CSV, filter, pattern, prompt, ref-images)
    //      - Per-group toolbar (Generate selected / Save selected / Select all/none)
    //      - Per-item rows: preview box + select + name + status + extra prompt + buttons
    //   8. Add Group button
    //   9. EndScrollView
    //
    // GUILayout desync prevention (MANDATORY — risk score 16):
    //   - Structurally-stable layout: every control drawn every frame (vary text/enable only).
    //   - Deferred-action queue: all state-mutating buttons set a pending field, then Repaint().
    //     ProcessPendingActions() runs at the very top of OnGUI (before any layout group).
    //     No ExitGUI() inside scroll/foldout groups.
    //   - Group add/remove MUST route through pendingAddGroup / pendingRemoveGroupIndex.
    //     These are processed in ProcessPendingActions() — NEVER mutate groups list mid-draw.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Orchestrates the full icon-generation pipeline with per-group dynamic layout.
    /// </summary>
    internal sealed class IconGeneratorWindow : EditorWindow
    {
        // ── Constants ─────────────────────────────────────────────────────────────

        private const string MENU_PATH               = "Tools/EZG Technical Art/Icon CSV Generator";
        private const string WINDOW_TITLE            = "Icon Generator";
        private const float  SECTION_SPACING         = 8f;
        private const float  PREVIEW_SIZE            = 80f;
        private const float  GENERATE_PROGRESS_WIDTH = 200f;

        /// <summary>Per-group reference-image cap (artist-requested). Gemini 3.1 Flash uses up to ~14.</summary>
        private const int MAX_REFERENCE_IMAGES = 15;

        /// <summary>Serialized field name on <see cref="IconCsvGroup"/> holding the reference images array.</summary>
        private const string REFERENCE_IMAGES_FIELD = "referenceImages";

        /// <summary>Serialized field name on <see cref="IconCsvGroup"/> holding the prompt template string.</summary>
        private const string PROMPT_TEMPLATE_FIELD = "promptTemplate";

        // ── Reference-image import / auto-save ────────────────────────────────────

        private const string EDITOR_PREFS_REF_AUTO_SAVE = "Ezg.IconCsvGenerator.RefAutoSave";
        private static string REF_IMAGES_FOLDER_ROOT => IconGenPaths.ReferenceImagesRoot;

        private static readonly string[] SUPPORTED_IMAGE_EXTENSIONS = { ".png", ".jpg", ".jpeg", ".webp" };

        // ── Persistent state ──────────────────────────────────────────────────────

        private string apiKeyInput    = string.Empty;
        private bool   apiKeyLoaded;
        private bool   apiKeyExpanded = true;

        private bool refAutoSave;
        private bool hasUnsavedRefChanges;

        // ── Generation settings ────────────────────────────────────────────────────

        private IconGeneratorSettings? generationSettings;
        private SerializedObject?      settingsSerializedObject;
        private int arDropdownIndex;
        private int sizeDropdownIndex;
        private int concurrencyDropdownIndex;

        // ── Runtime state ─────────────────────────────────────────────────────────

        private List<IconRowModel>? loadedRows;
        private string              csvLoadStatus = string.Empty;

        private List<IconReviewStateItem> reviewItems = new();

        private bool    forceRegenerateAll;
        private Vector2 outerScrollPosition;

        // Per-group-index foldout and search state (keyed by group index).
        private Dictionary<int, bool>   groupFoldouts = new();
        private Dictionary<int, string> groupSearches = new();
        // Per-group-index config-section foldout (for the group config block).
        private Dictionary<int, bool>   groupConfigFoldouts = new();

        // Generation progress tracking
        private int generationTotal;
        private int generationCompleted;

        // Bounded concurrency pool
        private readonly Queue<IconReviewStateItem> genQueue = new();
        private int activeWorkers;

        private bool isGenerating => this.genQueue.Count > 0 || this.activeWorkers > 0;

        // Write results
        private List<IconWriter.WriteResult>? lastWriteResults;

        // ── Deferred-action queue ─────────────────────────────────────────────────
        // ALL state mutations happen in ProcessPendingActions() at the top of OnGUI.
        // NEVER mutate the groups list or reviewItems mid-draw (GUILayout desync risk).

        private bool   pendingLoadRows;
        private int?   pendingGenerateGroupIndex;
        private int?   pendingWriteGroupIndex;
        private IconReviewStateItem? pendingGenerateItem;
        private IconReviewStateItem? pendingWriteItem;
        // Import path deferred actions
        private int      pendingImportGroupIndex;
        private string[]? pendingImportPaths;
        private int?     pendingClipboardGroupIndex;

        // Group structural changes — processed in ProcessPendingActions() BEFORE any layout.
        private bool pendingAddGroup;
        private int? pendingRemoveGroupIndex;

        // ── Menu ──────────────────────────────────────────────────────────────────

        [MenuItem(MENU_PATH)]
        public static void Open()
        {
            GetWindow<IconGeneratorWindow>(WINDOW_TITLE);
        }

        // ── Unity lifecycle ───────────────────────────────────────────────────────

        private void OnEnable()
        {
            this.apiKeyInput  = IconGeneratorPrefs.GetApiKey();
            this.apiKeyLoaded = true;

            this.generationSettings = IconSettingsLoader.Load();
            IconGenPaths.ApplyFrom(this.generationSettings);
            this.RebindSerializedObject();
            this.arDropdownIndex          = this.generationSettings.AspectRatioIndex();
            this.sizeDropdownIndex        = this.generationSettings.ImageSizeIndex();
            this.concurrencyDropdownIndex = this.generationSettings.ConcurrencyIndex();

            this.refAutoSave          = EditorPrefs.GetBool(EDITOR_PREFS_REF_AUTO_SAVE, defaultValue: true);
            this.hasUnsavedRefChanges = false;

            this.RecoverFromDomainReload();
        }

        private void OnDisable()
        {
            this.DestroyAllPreviews();
        }

        private void OnGUI()
        {
            // FIRST: process deferred mutations before any layout group opens.
            this.ProcessPendingActions();

            // Global top bars.
            this.DrawAutoSaveBar();
            this.DrawGenerationSettingsSection();
            EditorGUILayout.Space(SECTION_SPACING);
            this.DrawApiKeySection();
            EditorGUILayout.Space(SECTION_SPACING);

            // Global load status line + force-regenerate toggle.
            this.DrawGlobalLoadBar();
            EditorGUILayout.Space(SECTION_SPACING);

            // Generation progress (always drawn, disabled when not generating).
            this.DrawGlobalProgressBar();

            // ONE outer scroll view wrapping all group blocks + add-group button.
            this.outerScrollPosition = EditorGUILayout.BeginScrollView(this.outerScrollPosition);
            {
                if (this.generationSettings != null)
                {
                    for (var i = 0; i < this.generationSettings.groups.Count; i++)
                    {
                        this.DrawGroupBlock(i);
                        EditorGUILayout.Space(SECTION_SPACING);
                    }
                }

                // Add Group button.
                if (GUILayout.Button(LucideButtonIcons.Content("plus", "Add Group"), GUILayout.Width(130)))
                {
                    this.pendingAddGroup = true;
                    this.Repaint();
                }

                // Write results at the bottom of the scroll area.
                this.DrawWriteResults();
            }
            EditorGUILayout.EndScrollView();
        }

        // ── Deferred actions ──────────────────────────────────────────────────────

        /// <summary>
        /// Processes all pending mutations at the very top of OnGUI, before any layout group.
        /// Order: structural (add/remove group) → load → import → generate → write.
        /// Each action clears its own pending field after executing.
        /// </summary>
        private void ProcessPendingActions()
        {
            // Group structural changes FIRST (before any loop that iterates groups.Count).
            if (this.pendingAddGroup)
            {
                this.pendingAddGroup = false;
                if (this.generationSettings != null)
                {
                    this.generationSettings.groups.Add(new IconCsvGroup { groupName = "New Group" });
                    this.settingsSerializedObject?.Update();
                    EditorUtility.SetDirty(this.generationSettings);
                    if (this.refAutoSave) AssetDatabase.SaveAssets();
                }
            }

            if (this.pendingRemoveGroupIndex.HasValue)
            {
                var removeIdx = this.pendingRemoveGroupIndex.Value;
                this.pendingRemoveGroupIndex = null;
                if (this.generationSettings != null &&
                    removeIdx >= 0 &&
                    removeIdx < this.generationSettings.groups.Count)
                {
                    this.generationSettings.groups.RemoveAt(removeIdx);
                    this.settingsSerializedObject?.Update();
                    EditorUtility.SetDirty(this.generationSettings);
                    if (this.refAutoSave) AssetDatabase.SaveAssets();
                    // Remove per-index state for indices >= removeIdx.
                    this.ShiftGroupStateAfterRemove(removeIdx);
                    // Reload rows to remove orphaned items.
                    this.pendingLoadRows = true;
                }
            }

            if (this.pendingLoadRows)
            {
                this.pendingLoadRows = false;
                this.ReloadCsvRows();
            }

            // Clipboard import.
            if (this.pendingClipboardGroupIndex.HasValue)
            {
                var clipIdx = this.pendingClipboardGroupIndex.Value;
                this.pendingClipboardGroupIndex = null;
                if (TryGrabClipboardImage(out var clipPath, out var clipError))
                {
                    this.pendingImportGroupIndex = clipIdx;
                    this.pendingImportPaths      = new[] { clipPath };
                }
                else
                {
                    Debug.LogWarning($"[IconGenerator] Copy from Clipboard: {clipError}");
                    var clipMsg = clipError;
                    EditorApplication.delayCall += () =>
                        EditorUtility.DisplayDialog(WINDOW_TITLE, $"Copy from Clipboard failed:\n{clipMsg}", "OK");
                }
            }

            if (this.pendingImportPaths != null)
            {
                var paths     = this.pendingImportPaths;
                var groupIdx  = this.pendingImportGroupIndex;
                this.pendingImportPaths = null;

                if (this.generationSettings != null &&
                    groupIdx >= 0 &&
                    groupIdx < this.generationSettings.groups.Count &&
                    this.settingsSerializedObject != null)
                {
                    this.settingsSerializedObject.Update();
                    var groupsProp = this.settingsSerializedObject.FindProperty("groups");
                    if (groupsProp != null && groupIdx < groupsProp.arraySize)
                    {
                        var refProp = groupsProp.GetArrayElementAtIndex(groupIdx)
                                                .FindPropertyRelative(REFERENCE_IMAGES_FIELD);
                        if (refProp != null)
                        {
                            var anyImported = false;
                            foreach (var p in paths)
                            {
                                this.ImportReferenceImage(p, groupIdx, refProp);
                                anyImported = true;
                            }
                            if (anyImported)
                            {
                                this.settingsSerializedObject.ApplyModifiedProperties();
                                this.ApplyRefChange();
                            }
                        }
                    }
                }
            }

            if (this.pendingGenerateItem != null)
            {
                var item = this.pendingGenerateItem;
                this.pendingGenerateItem = null;
                if (this.IsGroupReady(item.Row.GroupIndex))
                {
                    this.EnqueueGeneration(new List<IconReviewStateItem> { item });
                }
            }

            if (this.pendingWriteItem != null)
            {
                var item = this.pendingWriteItem;
                this.pendingWriteItem = null;
                EditorApplication.delayCall += () => this.WriteSingleItem(item);
            }

            if (this.pendingGenerateGroupIndex.HasValue)
            {
                var idx = this.pendingGenerateGroupIndex.Value;
                this.pendingGenerateGroupIndex = null;
                EditorApplication.delayCall += () => this.StartGenerationForGroup(idx);
            }

            if (this.pendingWriteGroupIndex.HasValue)
            {
                var idx = this.pendingWriteGroupIndex.Value;
                this.pendingWriteGroupIndex = null;
                EditorApplication.delayCall += () => this.WriteGroupSelected(idx);
            }
        }

        // ── Section: auto-save bar (with settings ObjectField) ───────────────────

        private void DrawAutoSaveBar()
        {
            using var toolbar = new EditorGUILayout.HorizontalScope();
            {
                var newAutoSave = EditorGUILayout.ToggleLeft("Auto Save", this.refAutoSave, GUILayout.Width(85));
                if (newAutoSave != this.refAutoSave)
                {
                    this.refAutoSave = newAutoSave;
                    EditorPrefs.SetBool(EDITOR_PREFS_REF_AUTO_SAVE, this.refAutoSave);
                }

                EditorGUI.BeginDisabledGroup(this.refAutoSave || !this.hasUnsavedRefChanges);
                if (GUILayout.Button(LucideButtonIcons.Content("save", "Save"), GUILayout.Width(74)))
                {
                    AssetDatabase.SaveAssets();
                    this.hasUnsavedRefChanges = false;
                }
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.LabelField(
                    (!this.refAutoSave && this.hasUnsavedRefChanges) ? "● unsaved" : " ",
                    EditorStyles.miniLabel);

                GUILayout.Space(12f);

                // Settings ObjectField — lets the user pick a different profile.
                EditorGUILayout.LabelField("Settings", GUILayout.Width(52));
                var newSettings = (IconGeneratorSettings)EditorGUILayout.ObjectField(
                    this.generationSettings, typeof(IconGeneratorSettings), allowSceneObjects: false,
                    GUILayout.ExpandWidth(true));
                if (newSettings != this.generationSettings)
                {
                    this.generationSettings = newSettings;
                    if (newSettings != null)
                    {
                        IconSettingsLoader.PersistSelection(newSettings);
                        IconGenPaths.ApplyFrom(newSettings);
                        this.RebindSerializedObject();
                        this.arDropdownIndex          = newSettings.AspectRatioIndex();
                        this.sizeDropdownIndex        = newSettings.ImageSizeIndex();
                        this.concurrencyDropdownIndex = newSettings.ConcurrencyIndex();
                    }
                    else
                    {
                        this.settingsSerializedObject = null;
                    }
                    this.pendingLoadRows = true;
                    this.Repaint();
                }

                GUILayout.FlexibleSpace();
            }
        }

        // ── Section: generation settings ─────────────────────────────────────────

        private void DrawGenerationSettingsSection()
        {
            EditorGUILayout.LabelField("Generation Settings", EditorStyles.boldLabel);

            if (this.generationSettings == null)
            {
                EditorGUILayout.HelpBox(
                    "IconGeneratorSettings asset not loaded. Check Console for details.",
                    MessageType.Warning);
                return;
            }

            using var changeCheck = new EditorGUI.ChangeCheckScope();

            using (var h = new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("AR", GUILayout.Width(20));
                this.arDropdownIndex = EditorGUILayout.Popup(
                    this.arDropdownIndex,
                    IconGeneratorSettings.ASPECT_RATIO_OPTIONS,
                    GUILayout.Width(80));

                GUILayout.Space(24f);

                EditorGUILayout.LabelField("Size", GUILayout.Width(30));
                this.sizeDropdownIndex = EditorGUILayout.Popup(
                    this.sizeDropdownIndex,
                    IconGeneratorSettings.IMAGE_SIZE_OPTIONS,
                    GUILayout.Width(80));

                GUILayout.Space(24f);

                EditorGUILayout.LabelField("Concurrent", GUILayout.Width(72));
                EditorGUI.BeginDisabledGroup(this.isGenerating);
                this.concurrencyDropdownIndex = EditorGUILayout.Popup(
                    this.concurrencyDropdownIndex,
                    IconGeneratorSettings.CONCURRENCY_OPTIONS,
                    GUILayout.Width(50));
                EditorGUI.EndDisabledGroup();

                GUILayout.Space(24f);

                var newForce = EditorGUILayout.ToggleLeft(
                    "Force regen all", this.forceRegenerateAll, GUILayout.Width(120));
                if (newForce != this.forceRegenerateAll)
                {
                    this.forceRegenerateAll = newForce;
                    this.ApplyForceRegenerateToggle();
                }

                GUILayout.FlexibleSpace();
            }

            if (changeCheck.changed)
            {
                this.generationSettings.ApplyDropdownSelection(
                    this.arDropdownIndex, this.sizeDropdownIndex, this.concurrencyDropdownIndex);
            }
        }

        // ── Section: API key ──────────────────────────────────────────────────────

        private void DrawApiKeySection()
        {
            this.apiKeyExpanded = EditorGUILayout.Foldout(this.apiKeyExpanded, "Gemini API Key", true);
            if (!this.apiKeyExpanded) return;

            EditorGUILayout.HelpBox(
                "Key is stored in EditorPrefs only — never committed to any asset or version-controlled file.",
                MessageType.Info);

            this.apiKeyInput = EditorGUILayout.PasswordField("Gemini API Key", this.apiKeyInput);

            using var horizontal = new EditorGUILayout.HorizontalScope();
            {
                if (GUILayout.Button(LucideButtonIcons.Content("key", "Save Key"), GUILayout.Width(112)))
                {
                    IconGeneratorPrefs.SetApiKey(this.apiKeyInput);
                    EditorUtility.DisplayDialog(WINDOW_TITLE, "API key saved to EditorPrefs.", "OK");
                }

                var hasKey = IconGeneratorPrefs.HasApiKey();
                EditorGUILayout.LabelField(
                    hasKey ? "Key stored." : "No key stored.",
                    EditorStyles.miniLabel);
            }
        }

        // ── Section: global load bar ──────────────────────────────────────────────

        private void DrawGlobalLoadBar()
        {
            using var h = new EditorGUILayout.HorizontalScope();
            {
                if (GUILayout.Button(LucideButtonIcons.Content("download", "Load Rows from CSVs"), GUILayout.Width(182)))
                {
                    this.pendingLoadRows = true;
                    this.Repaint();
                }

                var statusText = string.IsNullOrEmpty(this.csvLoadStatus)
                    ? "Not loaded."
                    : this.csvLoadStatus;
                EditorGUILayout.LabelField(statusText, EditorStyles.miniLabel);
            }
        }

        // ── Section: global progress bar ──────────────────────────────────────────

        private void DrawGlobalProgressBar()
        {
            var progress = (this.generationTotal > 0)
                ? (float)this.generationCompleted / this.generationTotal
                : 0f;
            var label = this.isGenerating
                ? $"Generating {this.generationCompleted}/{this.generationTotal}  ({this.activeWorkers} active)…"
                : (this.generationTotal > 0 ? $"Done {this.generationCompleted}/{this.generationTotal}" : " ");

            var rect = GUILayoutUtility.GetRect(GENERATE_PROGRESS_WIDTH, 16f, GUILayout.ExpandWidth(true));
            if (this.isGenerating || this.generationTotal > 0)
            {
                EditorGUI.ProgressBar(rect, progress, label);
            }
            else
            {
                EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f, 0.1f));
            }
        }

        // ── Per-group block ───────────────────────────────────────────────────────

        private void DrawGroupBlock(int groupIndex)
        {
            if (this.generationSettings == null) return;
            if (groupIndex < 0 || groupIndex >= this.generationSettings.groups.Count) return;

            var group = this.generationSettings.groups[groupIndex];

            // Collect items for this group.
            var groupItems = new List<IconReviewStateItem>();
            foreach (var item in this.reviewItems)
            {
                if (item.Row.GroupIndex == groupIndex) groupItems.Add(item);
            }

            // Foldout header.
            if (!this.groupFoldouts.ContainsKey(groupIndex)) this.groupFoldouts[groupIndex] = true;
            var foldout = this.groupFoldouts[groupIndex];
            var header  = $"{group.groupName}  ({groupItems.Count} items)";
            this.groupFoldouts[groupIndex] = EditorGUILayout.Foldout(
                foldout, header, true, EditorStyles.foldoutHeader);

            if (!this.groupFoldouts[groupIndex]) return;

            EditorGUI.indentLevel++;

            // ── Group config (name / CSV / filter / pattern / prompt / ref-images) ──
            if (!this.groupConfigFoldouts.ContainsKey(groupIndex)) this.groupConfigFoldouts[groupIndex] = true;
            this.groupConfigFoldouts[groupIndex] = EditorGUILayout.Foldout(
                this.groupConfigFoldouts[groupIndex], "Config", true);

            if (this.groupConfigFoldouts[groupIndex])
            {
                EditorGUI.indentLevel++;
                this.DrawGroupConfig(groupIndex, group);
                EditorGUI.indentLevel--;
            }

            // ── CSV/load info + Reload ────────────────────────────────────────────
            using (var h = new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    groupItems.Count > 0 ? $"Loaded {groupItems.Count} rows" : "No rows (load above)",
                    EditorStyles.miniLabel);

                if (GUILayout.Button(LucideButtonIcons.Content("refresh-cw", "Reload"), GUILayout.Width(84)))
                {
                    this.pendingLoadRows = true;
                    this.Repaint();
                }
            }

            // Per-group readiness warning.
            this.DrawGroupReadinessWarning(groupIndex, group);

            // ── Per-group toolbar ─────────────────────────────────────────────────
            this.DrawGroupToolbar(groupIndex, groupItems, group);

            // ── Per-group search ──────────────────────────────────────────────────
            if (!this.groupSearches.ContainsKey(groupIndex)) this.groupSearches[groupIndex] = string.Empty;

            using (var sh = new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Search", GUILayout.Width(50));
                this.groupSearches[groupIndex] = EditorGUILayout.TextField(this.groupSearches[groupIndex]);
                var search = this.groupSearches[groupIndex];
                EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(search));
                if (GUILayout.Button(LucideButtonIcons.Content("x", "Clear"), GUILayout.Width(70)))
                {
                    this.groupSearches[groupIndex] = string.Empty;
                    GUI.FocusControl(null);
                }
                EditorGUI.EndDisabledGroup();
            }

            var query    = this.groupSearches[groupIndex].Trim();
            var hasQuery = query.Length > 0;

            // ── Item rows (filtered by search query) ──────────────────────────────
            var shown = 0;
            foreach (var item in groupItems)
            {
                if (hasQuery && !MatchesSearch(item, query)) continue;
                this.DrawItemRow(item);
                shown++;
            }

            EditorGUILayout.LabelField(
                hasQuery
                    ? (shown == 0 ? $"No items match \"{query}\"" : $"Showing {shown} of {groupItems.Count}")
                    : " ",
                EditorStyles.miniLabel);

            EditorGUI.indentLevel--;
        }

        // ── Group config block ────────────────────────────────────────────────────

        private void DrawGroupConfig(int groupIndex, IconCsvGroup group)
        {
            if (this.settingsSerializedObject == null) return;
            this.settingsSerializedObject.Update();
            var groupsProp = this.settingsSerializedObject.FindProperty("groups");
            if (groupsProp == null || groupIndex >= groupsProp.arraySize) return;
            var elem = groupsProp.GetArrayElementAtIndex(groupIndex);

            using var cc = new EditorGUI.ChangeCheckScope();

            // Group name.
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("groupName"), new GUIContent("Name"));

            // CSV ObjectField.
            var csvPathProp = elem.FindPropertyRelative("csvPath");
            if (csvPathProp != null)
            {
                var currentPath  = csvPathProp.stringValue;
                var currentAsset = string.IsNullOrEmpty(currentPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<TextAsset>(currentPath);
                var newAsset = (TextAsset)EditorGUILayout.ObjectField(
                    "CSV File", currentAsset, typeof(TextAsset), allowSceneObjects: false);
                if (newAsset != currentAsset)
                {
                    // Clearing (null) keeps existing path — prevents accidental wipe.
                    csvPathProp.stringValue = newAsset != null
                        ? AssetDatabase.GetAssetPath(newAsset)
                        : currentPath;
                }
            }

            // Id column.
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("idColumn"), new GUIContent("Id Column"));

            // Filter row.
            using (var fh = new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Filter", GUILayout.Width(45));
                EditorGUILayout.PropertyField(
                    elem.FindPropertyRelative("filterColumn"), GUIContent.none, GUILayout.Width(100));
                EditorGUILayout.PropertyField(
                    elem.FindPropertyRelative("filterMode"), GUIContent.none, GUILayout.Width(90));
                EditorGUILayout.PropertyField(
                    elem.FindPropertyRelative("filterValue"), GUIContent.none, GUILayout.ExpandWidth(true));
            }

            // Filename pattern.
            EditorGUILayout.PropertyField(
                elem.FindPropertyRelative("filenamePattern"), new GUIContent("Filename Pattern"));

            // Prompt template (TextArea bound via SerializedProperty — risk-9 mitigation).
            EditorGUILayout.LabelField("Prompt Template", EditorStyles.miniBoldLabel);
            var promptProp = elem.FindPropertyRelative(PROMPT_TEMPLATE_FIELD);
            if (promptProp != null)
            {
                promptProp.stringValue = EditorGUILayout.TextArea(
                    promptProp.stringValue, EditorStyles.textArea,
                    GUILayout.MinHeight(54f), GUILayout.ExpandWidth(true));
            }

            // Reference images (bound via SerializedProperty — risk-9 mitigation).
            var refProp = elem.FindPropertyRelative(REFERENCE_IMAGES_FIELD);
            if (refProp != null)
            {
                this.DrawGroupReferenceImages(groupIndex, group, refProp);
            }

            if (cc.changed)
            {
                this.settingsSerializedObject.ApplyModifiedProperties();
                this.ApplyRefChange();
            }
            else
            {
                this.settingsSerializedObject.ApplyModifiedProperties();
            }

            // Remove group button.
            EditorGUILayout.Space(4f);
            using var rh = new EditorGUILayout.HorizontalScope();
            {
                GUILayout.FlexibleSpace();
                var prevColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button(LucideButtonIcons.Content("trash-2", $"Remove '{group.groupName}'"), GUILayout.Width(180)))
                {
                    this.pendingRemoveGroupIndex = groupIndex;
                    this.Repaint();
                }
                GUI.backgroundColor = prevColor;
            }
        }

        // ── Group reference-images sub-section ────────────────────────────────────

        private void DrawGroupReferenceImages(int groupIndex, IconCsvGroup group, SerializedProperty refProp)
        {
            EditorGUILayout.LabelField(
                $"Reference Images  ({refProp.arraySize}/{MAX_REFERENCE_IMAGES})",
                EditorStyles.miniBoldLabel);

            using (var h = new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(LucideButtonIcons.Content("image-plus", "Import image…"), GUILayout.Width(134)))
                {
                    var path = EditorUtility.OpenFilePanel("Import reference image", "", "png,jpg,jpeg,webp");
                    if (!string.IsNullOrEmpty(path))
                    {
                        this.pendingImportGroupIndex = groupIndex;
                        this.pendingImportPaths      = new[] { path };
                        this.Repaint();
                    }
                }

                if (GUILayout.Button(LucideButtonIcons.Content("clipboard", "Copy from Clipboard"), GUILayout.Width(174)))
                {
                    this.pendingClipboardGroupIndex = groupIndex;
                    this.Repaint();
                }
            }

            // OS file drop zone.
            var dropRect = GUILayoutUtility.GetRect(0f, 32f, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "Drop image files here", EditorStyles.helpBox);

            var currentEvent = Event.current;
            if (dropRect.Contains(currentEvent.mousePosition))
            {
                if (currentEvent.type == EventType.DragUpdated)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    currentEvent.Use();
                }
                else if (currentEvent.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    var validPaths = new List<string>();
                    foreach (var dragPath in DragAndDrop.paths)
                    {
                        var ext       = System.IO.Path.GetExtension(dragPath).ToLowerInvariant();
                        var supported = false;
                        foreach (var se in SUPPORTED_IMAGE_EXTENSIONS)
                        {
                            if (ext == se) { supported = true; break; }
                        }
                        if (supported)
                        {
                            validPaths.Add(dragPath);
                        }
                        else
                        {
                            Debug.LogWarning(
                                $"[IconGenerator] Dropped file '{dragPath}' has unsupported extension '{ext}'. Skipping.");
                        }
                    }
                    if (validPaths.Count > 0)
                    {
                        this.pendingImportGroupIndex = groupIndex;
                        this.pendingImportPaths      = validPaths.ToArray();
                        this.Repaint();
                    }
                    currentEvent.Use();
                }
            }

            // PropertyField + cap clamp (already inside a ChangeCheckScope from the caller).
            EditorGUILayout.PropertyField(refProp, new GUIContent("Images"), includeChildren: true);
            if (refProp.arraySize > MAX_REFERENCE_IMAGES)
            {
                refProp.arraySize = MAX_REFERENCE_IMAGES;
            }
        }

        // ── Per-group readiness warning ───────────────────────────────────────────

        private void DrawGroupReadinessWarning(int groupIndex, IconCsvGroup group)
        {
            if (this.IsGroupReady(groupIndex)) return;

            var missing = new System.Text.StringBuilder();
            if (string.IsNullOrWhiteSpace(group.csvPath)) missing.Append("CSV not assigned. ");
            if (string.IsNullOrWhiteSpace(group.filenamePattern)) missing.Append("Filename pattern empty. ");
            if (string.IsNullOrWhiteSpace(group.promptTemplate)) missing.Append("Prompt template empty. ");

            if (missing.Length > 0)
            {
                EditorGUILayout.HelpBox(
                    $"Group '{group.groupName}' not ready: {missing}Generation disabled for this group.",
                    MessageType.Warning);
            }
        }

        /// <summary>
        /// A group is generate-ready iff CSV assigned, filenamePattern non-empty, promptTemplate non-empty.
        /// A not-ready group disables only itself — other groups still generate.
        /// </summary>
        private bool IsGroupReady(int groupIndex)
        {
            if (this.generationSettings == null) return false;
            if (groupIndex < 0 || groupIndex >= this.generationSettings.groups.Count) return false;
            var g = this.generationSettings.groups[groupIndex];
            return !string.IsNullOrWhiteSpace(g.csvPath)
                && !string.IsNullOrWhiteSpace(g.filenamePattern)
                && !string.IsNullOrWhiteSpace(g.promptTemplate);
        }

        // ── Per-group toolbar ──────────────────────────────────────────────────────

        private void DrawGroupToolbar(int groupIndex, List<IconReviewStateItem> groupItems, IconCsvGroup group)
        {
            var selectedCount = 0;
            var savableCount  = 0;
            foreach (var item in groupItems)
            {
                if (item.IsSelected &&
                    !(item.Status == ReviewStatus.Skipped && !this.forceRegenerateAll)) selectedCount++;
                if (item.IsSelected && item.PngBytes != null && item.PngBytes.Length > 0) savableCount++;
            }

            using var h = new EditorGUILayout.HorizontalScope();
            {
                var groupReady = this.IsGroupReady(groupIndex);

                // A run already in flight does NOT disable this — new work merges
                // into the shared worker pool (EnqueueGeneration skips in-flight items).
                EditorGUI.BeginDisabledGroup(
                    !IconGeneratorPrefs.HasApiKey() ||
                    selectedCount == 0 ||
                    !groupReady);
                if (GUILayout.Button(LucideButtonIcons.Content("sparkles", $"Generate selected ({selectedCount})"), GUILayout.Width(206)))
                {
                    this.pendingGenerateGroupIndex = groupIndex;
                    this.Repaint();
                }
                EditorGUI.EndDisabledGroup();

                GUILayout.Space(8f);

                // Saving never touches the generation pool — only gate on having something to save.
                EditorGUI.BeginDisabledGroup(savableCount == 0);
                if (GUILayout.Button(LucideButtonIcons.Content("save", $"Save Selected ({savableCount})"), GUILayout.Width(181)))
                {
                    this.pendingWriteGroupIndex = groupIndex;
                    this.Repaint();
                }
                EditorGUI.EndDisabledGroup();

                GUILayout.Space(8f);

                if (GUILayout.Button("All", GUILayout.Width(36)))
                {
                    foreach (var item in groupItems) item.IsSelected = true;
                }
                if (GUILayout.Button("None", GUILayout.Width(40)))
                {
                    foreach (var item in groupItems) item.IsSelected = false;
                }

                GUILayout.FlexibleSpace();
            }
        }

        // ── Per-item row ──────────────────────────────────────────────────────────

        private void DrawItemRow(IconReviewStateItem item)
        {
            using var rowScope = new EditorGUILayout.HorizontalScope(EditorStyles.helpBox);
            {
                var previewRect = GUILayoutUtility.GetRect(
                    PREVIEW_SIZE, PREVIEW_SIZE,
                    GUILayout.Width(PREVIEW_SIZE),
                    GUILayout.Height(PREVIEW_SIZE));

                if (item.Preview != null)
                {
                    GUI.DrawTexture(previewRect, item.Preview, ScaleMode.ScaleToFit);
                }
                else
                {
                    EditorGUI.DrawRect(previewRect, new Color(0.15f, 0.15f, 0.15f));
                    var statusText = item.Status switch
                    {
                        ReviewStatus.Queued     => "Queued",
                        ReviewStatus.Generating => "Generating…",
                        ReviewStatus.Failed     => "Failed",
                        ReviewStatus.Skipped    => "Skipped",
                        ReviewStatus.Pending    => "…",
                        ReviewStatus.Approved   => "Approved",
                        ReviewStatus.Rejected   => "Rejected",
                        _                       => item.Status.ToString(),
                    };
                    GUI.Label(previewRect, statusText, this.GetCenteredMiniLabelStyle());
                }

                GUILayout.Space(4f);

                using var vertScope = new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true));
                {
                    // Row 1: select toggle + filename + status label.
                    using (var h = new EditorGUILayout.HorizontalScope())
                    {
                        item.IsSelected = EditorGUILayout.ToggleLeft(
                            item.Row.OutputFileName, item.IsSelected,
                            EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
                        EditorGUILayout.LabelField(item.Status.ToString(), EditorStyles.miniLabel, GUILayout.Width(80));
                    }

                    // Row 2: per-item extra prompt.
                    EditorGUILayout.LabelField(
                        "Extra (optional — appended to the system prompt)", EditorStyles.miniLabel);
                    var newExtra = EditorGUILayout.TextArea(
                        item.PromptOverride ?? string.Empty,
                        EditorStyles.textArea,
                        GUILayout.MinHeight(32f),
                        GUILayout.ExpandWidth(true));
                    item.PromptOverride = string.IsNullOrEmpty(newExtra) ? null : newExtra;

                    // Error line — always drawn for layout stability.
                    if (!string.IsNullOrEmpty(item.Error))
                    {
                        var prevColor = GUI.contentColor;
                        GUI.contentColor = new Color(1f, 0.4f, 0.4f);
                        EditorGUILayout.LabelField(item.Error, EditorStyles.miniLabel);
                        GUI.contentColor = prevColor;
                    }
                    else
                    {
                        EditorGUILayout.LabelField(" ", EditorStyles.miniLabel);
                    }

                    // Row 3: action buttons.
                    using (var h = new EditorGUILayout.HorizontalScope())
                    {
                        var isGenerated = item.Preview != null || item.Status == ReviewStatus.Failed;
                        var itemBusy    = item.Status == ReviewStatus.Generating || item.Status == ReviewStatus.Queued;
                        var hasResult   = item.PngBytes != null && item.PngBytes.Length > 0;

                        // Per-item actions gate ONLY on this item's own state — a run
                        // active on OTHER items must not disable this item's buttons.
                        var isRegen   = item.Preview != null || item.Status == ReviewStatus.Failed;
                        var genContent = isRegen
                            ? LucideButtonIcons.Content("rotate-cw", "Regenerate")
                            : LucideButtonIcons.Content("sparkles", "Generate");
                        EditorGUI.BeginDisabledGroup(
                            itemBusy ||
                            !IconGeneratorPrefs.HasApiKey() ||
                            !this.IsGroupReady(item.Row.GroupIndex));
                        if (GUILayout.Button(genContent, GUILayout.Width(116)))
                        {
                            this.pendingGenerateItem = item;
                            this.Repaint();
                        }
                        EditorGUI.EndDisabledGroup();

                        EditorGUI.BeginDisabledGroup(!isGenerated || itemBusy);
                        if (GUILayout.Button(LucideButtonIcons.Content("x", "Reset"), GUILayout.Width(78)))
                        {
                            item.ResetForRegenerate();
                            if (!this.forceRegenerateAll &&
                                IconExistenceChecker.ExistsFast(item.Row, IconExistenceChecker.BuildStemSet()))
                            {
                                item.Status = ReviewStatus.Skipped;
                            }
                        }
                        EditorGUI.EndDisabledGroup();

                        EditorGUI.BeginDisabledGroup(!hasResult || itemBusy);
                        if (GUILayout.Button(LucideButtonIcons.Content("save", "Save"), GUILayout.Width(74)))
                        {
                            this.pendingWriteItem = item;
                            this.Repaint();
                        }
                        EditorGUI.EndDisabledGroup();

                        EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(item.PromptOverride));
                        if (GUILayout.Button(LucideButtonIcons.Content("eraser", "Clear extra"), GUILayout.Width(110)))
                        {
                            item.PromptOverride = null;
                        }
                        EditorGUI.EndDisabledGroup();

                        GUILayout.FlexibleSpace();
                    }
                }
            }
        }

        // ── Write results section ─────────────────────────────────────────────────

        private void DrawWriteResults()
        {
            if (this.lastWriteResults == null || this.lastWriteResults.Count == 0) return;

            var successCount = 0;
            var failCount    = 0;
            foreach (var r in this.lastWriteResults) { if (r.Success) successCount++; else failCount++; }

            var msgType = failCount > 0 ? MessageType.Warning : MessageType.Info;
            EditorGUILayout.HelpBox(
                $"Write complete: {successCount} written, {failCount} failed/skipped.\n" +
                "Run t1k:unity:base:asset-import to validate + promote S_* files to Sprites/.",
                msgType);

            foreach (var r in this.lastWriteResults)
            {
                if (!r.Success)
                    EditorGUILayout.HelpBox($"FAIL: {r.Row.OutputFileName} — {r.Error}", MessageType.Error);
            }
        }

        // ── Lazy GUI style ────────────────────────────────────────────────────────

        private GUIStyle? centeredMiniLabelStyle;

        private GUIStyle GetCenteredMiniLabelStyle()
        {
            if (this.centeredMiniLabelStyle != null) return this.centeredMiniLabelStyle;
            this.centeredMiniLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap  = true,
            };
            return this.centeredMiniLabelStyle;
        }

        private static bool MatchesSearch(IconReviewStateItem item, string query)
        {
            var row = item.Row;
            return Contains(row.OutputFileName) || Contains(row.Id) || Contains(row.Slot) || Contains(row.Rarity);

            bool Contains(string value) =>
                !string.IsNullOrEmpty(value) &&
                value.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ── Reference-image import helpers ────────────────────────────────────────

        private void ApplyRefChange()
        {
            if (this.refAutoSave)
            {
                AssetDatabase.SaveAssets();
            }
            else
            {
                this.hasUnsavedRefChanges = true;
            }
        }

        private static bool TryGrabClipboardImage(out string tempPngPath, out string error)
        {
            tempPngPath = string.Empty;
            error       = string.Empty;

            var dest = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"icongen_clipboard_{System.Guid.NewGuid():N}.png");

#if UNITY_EDITOR_OSX
            var script =
                "set destPath to \"" + dest + "\"\n" +
                "try\n" +
                "  set pngData to (the clipboard as «class PNGf»)\n" +
                "on error\n" +
                "  return \"NOIMAGE\"\n" +
                "end try\n" +
                "set fh to open for access (POSIX file destPath) with write permission\n" +
                "set eof fh to 0\n" +
                "write pngData to fh\n" +
                "close access fh\n" +
                "return \"OK\"";
            var psi = new System.Diagnostics.ProcessStartInfo("/usr/bin/osascript");
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(script);
#elif UNITY_EDITOR_WIN
            var script =
                "Add-Type -AssemblyName System.Windows.Forms,System.Drawing; " +
                "$img=[System.Windows.Forms.Clipboard]::GetImage(); " +
                "if($img){ $img.Save('" + dest.Replace("\\", "\\\\") + "', " +
                "[System.Drawing.Imaging.ImageFormat]::Png); 'OK' } else { 'NOIMAGE' }";
            var psi = new System.Diagnostics.ProcessStartInfo("powershell");
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(script);
#else
            error = "Copy from Clipboard is only supported on macOS and Windows.";
            return false;
#endif

#if UNITY_EDITOR_OSX || UNITY_EDITOR_WIN
            psi.UseShellExecute        = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError  = true;
            psi.CreateNoWindow         = true;
            try
            {
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) { error = "Failed to start clipboard helper process."; return false; }

                var stdout = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(10000);

                if (stdout.Contains("NOIMAGE") ||
                    !System.IO.File.Exists(dest) ||
                    new System.IO.FileInfo(dest).Length == 0)
                {
                    error = "No image found in the clipboard.";
                    return false;
                }

                tempPngPath = dest;
                return true;
            }
            catch (System.Exception ex)
            {
                error = $"Clipboard read failed: {ex.Message}";
                return false;
            }
#endif
        }

        private void ImportReferenceImage(
            string sourceAbsolutePath,
            int groupIndex,
            SerializedProperty referenceImagesProp)
        {
            var ext   = System.IO.Path.GetExtension(sourceAbsolutePath).ToLowerInvariant();
            var extOk = false;
            foreach (var supported in SUPPORTED_IMAGE_EXTENSIONS)
            {
                if (ext == supported) { extOk = true; break; }
            }
            if (!extOk)
            {
                Debug.LogWarning(
                    $"[IconGenerator] ImportReferenceImage: '{sourceAbsolutePath}' has unsupported extension '{ext}'. Skipping.");
                return;
            }

            if (referenceImagesProp.arraySize >= MAX_REFERENCE_IMAGES)
            {
                var groupName = this.generationSettings != null && groupIndex < this.generationSettings.groups.Count
                    ? this.generationSettings.groups[groupIndex].groupName
                    : groupIndex.ToString();
                Debug.LogWarning(
                    $"[IconGenerator] Group '{groupName}' already has {MAX_REFERENCE_IMAGES} reference images (maximum). " +
                    "Remove one before importing another.");
                return;
            }

            var groupFolder = this.GetGroupRefImagesFolder(groupIndex);
            this.EnsureFolderChain(groupFolder);

            var fileName      = System.IO.Path.GetFileName(sourceAbsolutePath);
            var destAssetPath = AssetDatabase.GenerateUniqueAssetPath($"{groupFolder}/{fileName}");

            var projectRoot = Application.dataPath[..^"Assets".Length];
            var destAbsPath = System.IO.Path.Combine(projectRoot, destAssetPath);

            try
            {
                System.IO.File.Copy(sourceAbsolutePath, destAbsPath, overwrite: false);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"[IconGenerator] ImportReferenceImage: failed to copy '{sourceAbsolutePath}' → '{destAbsPath}': {ex.Message}");
                return;
            }

            AssetDatabase.ImportAsset(destAssetPath);

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(destAssetPath);
            if (tex == null)
            {
                Debug.LogWarning(
                    $"[IconGenerator] ImportReferenceImage: imported '{destAssetPath}' but could not load it as Texture2D.");
                return;
            }

            var lastIndex = referenceImagesProp.arraySize;
            referenceImagesProp.arraySize = lastIndex + 1;
            referenceImagesProp.GetArrayElementAtIndex(lastIndex).objectReferenceValue = tex;

            Debug.Log($"[IconGenerator] Imported reference image → {destAssetPath} (added to group {groupIndex}).");
        }

        private string GetGroupRefImagesFolder(int groupIndex)
        {
            if (this.generationSettings != null &&
                groupIndex >= 0 &&
                groupIndex < this.generationSettings.groups.Count)
            {
                var sanitizedName = IconExistenceChecker.SubfolderForGroup(
                    this.generationSettings.groups[groupIndex].groupName);
                return $"{REF_IMAGES_FOLDER_ROOT}/{sanitizedName}";
            }
            return $"{REF_IMAGES_FOLDER_ROOT}/Group{groupIndex}";
        }

        private void EnsureFolderChain(string assetFolderPath)
        {
            var parts   = assetFolderPath.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        // ── CSV + row loading ─────────────────────────────────────────────────────

        private void ReloadCsvRows()
        {
            this.DestroyAllPreviews();
            this.reviewItems.Clear();
            this.lastWriteResults = null;

            if (this.generationSettings == null || this.generationSettings.groups.Count == 0)
            {
                this.csvLoadStatus = "No groups configured. Add groups and set CSV paths.";
                this.Repaint();
                return;
            }

            try
            {
                this.loadedRows = IconCsvLoader.LoadAll(this.generationSettings.groups);

                // Build per-group counts for the status line.
                var groupCounts = new Dictionary<int, int>();
                foreach (var row in this.loadedRows)
                {
                    if (!groupCounts.ContainsKey(row.GroupIndex)) groupCounts[row.GroupIndex] = 0;
                    groupCounts[row.GroupIndex]++;
                }

                var parts = new System.Text.StringBuilder();
                parts.Append($"Loaded {this.loadedRows.Count} rows — ");
                for (var i = 0; i < this.generationSettings.groups.Count; i++)
                {
                    var count = groupCounts.GetValueOrDefault(i, 0);
                    parts.Append($"{this.generationSettings.groups[i].groupName}:{count}");
                    if (i < this.generationSettings.groups.Count - 1) parts.Append(", ");
                }
                this.csvLoadStatus = parts.ToString();
            }
            catch (System.Exception ex)
            {
                this.loadedRows    = null;
                this.csvLoadStatus = $"Error loading CSVs: {ex.Message}";
                Debug.LogError($"[IconGenerator] CSV load failed: {ex}");
                this.Repaint();
                return;
            }

            var stemSet = IconExistenceChecker.BuildStemSet();

            var restoredFromCache = 0;
            foreach (var row in this.loadedRows)
            {
                var item = new IconReviewStateItem(row);

                if (!this.forceRegenerateAll && IconExistenceChecker.ExistsFast(row, stemSet))
                {
                    item.Status = ReviewStatus.Skipped;
                }
                else if (this.TryRestoreFromCache(item))
                {
                    restoredFromCache++;
                }
                this.reviewItems.Add(item);
            }

            if (restoredFromCache > 0)
            {
                this.csvLoadStatus += $"  •  restored {restoredFromCache} unsaved preview(s) from cache";
            }

            this.Repaint();
        }

        private bool TryRestoreFromCache(IconReviewStateItem item)
        {
            if (!IconGenCache.TryLoadLatest(item.Row, out var cachedPng, out var cachePath) ||
                cachedPng == null)
            {
                return false;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: false);
            if (!tex.LoadImage(cachedPng, markNonReadable: false))
            {
                UnityEngine.Object.DestroyImmediate(tex);
                return false;
            }

            item.Preview   = tex;
            item.PngBytes  = cachedPng;
            item.CachePath = cachePath;
            item.Status    = ReviewStatus.Pending;
            return true;
        }

        // ── Generation ────────────────────────────────────────────────────────────

        private void StartGenerationForGroup(int groupIndex)
        {
            if (!this.IsGroupReady(groupIndex)) return;

            var toGenerate = new List<IconReviewStateItem>();
            foreach (var item in this.reviewItems)
            {
                if (item.Row.GroupIndex != groupIndex) continue;
                if (!item.IsSelected) continue;
                if (!this.forceRegenerateAll && item.Status == ReviewStatus.Skipped) continue;
                // Already queued / mid-generation from a prior trigger — leave it running.
                if (item.Status == ReviewStatus.Queued || item.Status == ReviewStatus.Generating) continue;
                toGenerate.Add(item);
            }

            if (toGenerate.Count == 0)
            {
                EditorUtility.DisplayDialog(WINDOW_TITLE, "No rows selected for this group.", "OK");
                return;
            }

            if (!IconCostEstimator.ConfirmGeneration(toGenerate.Count)) return;

            this.EnqueueGeneration(toGenerate);
        }

        private void EnqueueGeneration(List<IconReviewStateItem> items)
        {
            // Snapshot BEFORE enqueuing: idle => fresh session, busy => join the
            // in-flight session so the progress bar accumulates rather than resets.
            var wasIdle = !this.isGenerating;

            var enqueued = 0;
            foreach (var item in items)
            {
                // Skip items already in the pool so a group/item trigger fired during
                // an active run never double-adds the same row.
                if (item.Status == ReviewStatus.Queued || item.Status == ReviewStatus.Generating)
                    continue;

                item.ResetForRegenerate();
                item.Status = ReviewStatus.Queued;
                this.genQueue.Enqueue(item);
                enqueued++;
            }

            if (enqueued == 0)
            {
                this.Repaint();
                return;
            }

            if (wasIdle)
            {
                this.generationTotal     = enqueued;
                this.generationCompleted = 0;
                this.lastWriteResults    = null;
            }
            else
            {
                this.generationTotal += enqueued;
            }

            // Spin up only enough NEW workers to reach the cap, counting the ones
            // already draining the queue — otherwise concurrent enqueues would
            // exceed maxConcurrent (active + newly-started).
            var maxConcurrent  = Mathf.Clamp(this.generationSettings?.maxConcurrent ?? 2, 2, 10);
            var desiredWorkers = Mathf.Min(maxConcurrent, this.activeWorkers + this.genQueue.Count);
            for (var i = this.activeWorkers; i < desiredWorkers; i++)
            {
                this.activeWorkers++;
                EditorCoroutineRunner.StartCoroutine(this.GenWorker());
            }

            this.Repaint();
        }

        private IEnumerator GenWorker()
        {
            while (this.genQueue.Count > 0)
            {
                var item = this.genQueue.Dequeue();
                item.Status = ReviewStatus.Generating;
                this.Repaint();

                yield return this.GenerateOne(item);

                this.generationCompleted++;
                this.Repaint();
            }

            this.activeWorkers--;
            this.Repaint();
        }

        private IEnumerator GenerateOne(IconReviewStateItem item)
        {
            if (this.generationSettings == null ||
                item.Row.GroupIndex < 0 ||
                item.Row.GroupIndex >= this.generationSettings.groups.Count)
            {
                item.Status = ReviewStatus.Failed;
                item.Error  = $"Settings or group index {item.Row.GroupIndex} invalid.";
                yield break;
            }

            var group = this.generationSettings.groups[item.Row.GroupIndex];

            string resolvedPrompt;
            try
            {
                resolvedPrompt = IconPromptResolver.Resolve(group.promptTemplate, item.Row);
            }
            catch (System.Exception ex)
            {
                item.Status = ReviewStatus.Failed;
                item.Error  = $"Prompt resolution failed: {ex.Message}";
                Debug.LogError($"[IconGenerator] Prompt resolve failed for '{item.Row.OutputFileName}': {ex}");
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(item.PromptOverride))
            {
                resolvedPrompt += "\n" + item.PromptOverride!.Trim();
            }

            var referenceImages = ReferenceImageReader.Read(group.referenceImages);
            var aspectRatio     = this.generationSettings.aspectRatio;
            var imageSize       = this.generationSettings.imageSize;

            Debug.Log(
                $"[IconGenerator] Prompt for '{item.Row.OutputFileName}' " +
                $"(ar={aspectRatio}, size={imageSize}, refs={referenceImages.Count}):\n{resolvedPrompt}");

            GeminiImageResult? result = null;
            yield return GeminiImageClient.RequestImage(
                resolvedPrompt, aspectRatio, imageSize, referenceImages, r => result = r);

            if (result == null)
            {
                item.Status = ReviewStatus.Failed;
                item.Error  = "Gemini callback returned null result (unexpected).";
                yield break;
            }

            if (!result.IsSuccess)
            {
                item.Status = ReviewStatus.Failed;
                item.Error  = result.Error;
                Debug.LogError($"[IconGenerator] Gemini failed for '{item.Row.OutputFileName}': {result.Error}");
                yield break;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: false);
            if (!tex.LoadImage(result.PngBytes!, markNonReadable: false))
            {
                UnityEngine.Object.DestroyImmediate(tex);
                item.Status = ReviewStatus.Failed;
                item.Error  = "Failed to decode Gemini PNG bytes into a Texture2D.";
                yield break;
            }

            item.Preview  = tex;
            item.PngBytes = result.PngBytes;
            item.CachePath = IconGenCache.Save(item.Row, result.PngBytes!);
            item.Status   = ReviewStatus.Pending;
        }

        // ── Write ─────────────────────────────────────────────────────────────────

        private void WriteGroupSelected(int groupIndex)
        {
            var toWrite = new List<IconReviewStateItem>();
            foreach (var item in this.reviewItems)
            {
                if (item.Row.GroupIndex == groupIndex &&
                    item.IsSelected &&
                    item.PngBytes != null && item.PngBytes.Length > 0)
                {
                    toWrite.Add(item);
                }
            }

            if (toWrite.Count == 0)
            {
                EditorUtility.DisplayDialog(WINDOW_TITLE, "No selected icons with a generated result in this group.", "OK");
                return;
            }

            var results = IconWriter.WriteItems(toWrite);
            if (this.lastWriteResults == null) this.lastWriteResults = results;
            else this.lastWriteResults.AddRange(results);
            this.Repaint();
        }

        private void WriteSingleItem(IconReviewStateItem item)
        {
            if (item.PngBytes == null || item.PngBytes.Length == 0)
            {
                EditorUtility.DisplayDialog(WINDOW_TITLE, "Nothing to save — generate this icon first.", "OK");
                return;
            }

            var results = IconWriter.WriteItems(new[] { item });
            if (this.lastWriteResults == null) this.lastWriteResults = results;
            else this.lastWriteResults.AddRange(results);
            this.Repaint();
        }

        // ── Domain-reload recovery ────────────────────────────────────────────────

        private void RecoverFromDomainReload()
        {
            this.genQueue.Clear();
            this.activeWorkers = 0;

            var recovered = 0;
            foreach (var item in this.reviewItems)
            {
                if (item.Status == ReviewStatus.Generating || item.Status == ReviewStatus.Queued)
                {
                    item.Status = ReviewStatus.Failed;
                    item.Error  = "Interrupted by a script recompile / domain reload — regenerate.";
                    recovered++;
                }
            }

            if (recovered > 0)
            {
                Debug.LogWarning(
                    $"[IconGenerator] {recovered} row(s) were mid-generation after a domain reload and have been reset to Failed.");
            }
        }

        private void ApplyForceRegenerateToggle()
        {
            if (this.reviewItems.Count == 0) return;

            var stemSet = IconExistenceChecker.BuildStemSet();

            foreach (var item in this.reviewItems)
            {
                if (item.Status != ReviewStatus.Skipped && item.Status != ReviewStatus.Pending)
                    continue;

                var exists = IconExistenceChecker.ExistsFast(item.Row, stemSet);
                if (this.forceRegenerateAll)
                {
                    if (item.Status == ReviewStatus.Skipped) item.Status = ReviewStatus.Pending;
                }
                else
                {
                    if (exists && item.Status == ReviewStatus.Pending) item.Status = ReviewStatus.Skipped;
                }
            }

            this.Repaint();
        }

        private void DestroyAllPreviews()
        {
            foreach (var item in this.reviewItems)
            {
                if (item.Preview != null)
                {
                    UnityEngine.Object.DestroyImmediate(item.Preview);
                    item.Preview = null;
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private void RebindSerializedObject()
        {
            this.settingsSerializedObject = this.generationSettings != null
                ? new SerializedObject(this.generationSettings)
                : null;
        }

        /// <summary>
        /// After removing a group at <paramref name="removedIndex"/>, shift all per-index
        /// dictionaries so that state for groups with higher indices remains correct.
        /// </summary>
        private void ShiftGroupStateAfterRemove(int removedIndex)
        {
            ShiftDict(this.groupFoldouts, removedIndex);
            ShiftDict(this.groupSearches, removedIndex, defaultValue: string.Empty);
            ShiftDict(this.groupConfigFoldouts, removedIndex);

            static void ShiftDict<T>(Dictionary<int, T> dict, int removed, T defaultValue = default!)
            {
                dict.Remove(removed);
                var maxKey = -1;
                foreach (var k in dict.Keys) if (k > maxKey) maxKey = k;
                for (var i = removed; i <= maxKey; i++)
                {
                    dict[i] = dict.TryGetValue(i + 1, out var v) ? v : defaultValue;
                }
                dict.Remove(maxKey + 1);
            }
        }
    }
}
