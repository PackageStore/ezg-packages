using System.Collections.Generic;
using Ezg.ProceduralAnimation;
using UnityEditor;
using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    public partial class InbetweenAnimationGeneratorWindow : EditorWindow
    {
        [SerializeField]
        private InbetweenAnimationGeneratorState state = new InbetweenAnimationGeneratorState();
        private Transform skeletonRoot;
        private string feelPresetFolder = DefaultFeelPresetFactory.DefaultPresetFolder;
        [SerializeField]
        private string windowPoseCaptureFolder = string.Empty;

        private PoseCombinationGraphAsset graphAsset;
        private List<PoseCombinationPath> validPaths = new List<PoseCombinationPath>();
        private List<PoseCombinationPath> finalPaths = new List<PoseCombinationPath>();
        private Transform previewTarget;

        private InbetweenAnimationGeneratorState State
        {
            get
            {
                if (state == null)
                {
                    state = new InbetweenAnimationGeneratorState();
                }

                state.EnsureTransientCollections();
                return state;
            }
        }

        private InbetweenGeneratorMode mode { get => State.mode; set => State.mode = value; }
        private Vector2 feelPresetScrollPosition { get => State.feelPresetScrollPosition; set => State.feelPresetScrollPosition = value; }
        private Vector2 poseRenamerScrollPosition { get => State.poseRenamerScrollPosition; set => State.poseRenamerScrollPosition = value; }
        private bool dirtyGraph { get => State.dirtyGraph; set => State.dirtyGraph = value; }
        private bool autoSaveGraph { get => State.autoSaveGraph; set => State.autoSaveGraph = value; }
        private bool[] pathSelection { get => State.pathSelection; set => State.pathSelection = value; }
        private int selectedConnectionIndex { get => State.selectedConnectionIndex; set => State.selectedConnectionIndex = value; }
        private int selectedPathIndex { get => State.selectedPathIndex; set => State.selectedPathIndex = value; }
        private string batchBaseName { get => State.batchBaseName; set => State.batchBaseName = value; }
        private string pathSearchFilter { get => State.pathSearchFilter; set => State.pathSearchFilter = value; }
        private bool allConnectionsFoldout { get => State.allConnectionsFoldout; set => State.allConnectionsFoldout = value; }
        private bool batchAnimationExportFoldout { get => State.batchAnimationExportFoldout; set => State.batchAnimationExportFoldout = value; }
        private bool poseCaptureFilterFoldout { get => State.poseCaptureFilterFoldout; set => State.poseCaptureFilterFoldout = value; }
        private bool rememberPoseCaptureSelection
        {
            get => PoseCaptureSettings?.rememberSelection ?? State.rememberPoseCaptureSelection;
            set
            {
                if (PoseCaptureSettings != null)
                {
                    PoseCaptureSettings.rememberSelection = value;
                    return;
                }

                State.rememberPoseCaptureSelection = value;
            }
        }

        private string poseCaptureBoneSearchFilter
        {
            get => PoseCaptureSettings?.boneSearchFilter ?? State.poseCaptureBoneSearchFilter;
            set
            {
                if (PoseCaptureSettings != null)
                {
                    PoseCaptureSettings.boneSearchFilter = value;
                    return;
                }

                State.poseCaptureBoneSearchFilter = value;
            }
        }

        private Vector2 poseCaptureBoneScrollPosition { get => State.poseCaptureBoneScrollPosition; set => State.poseCaptureBoneScrollPosition = value; }
        private List<string> selectedPoseCaptureBonePaths => PoseCaptureSettings?.selectedBonePaths ?? State.selectedPoseCaptureBonePaths;
        private bool poseCaptureBoneSelectionInitialized
        {
            get => PoseCaptureSettings?.boneSelectionInitialized ?? State.poseCaptureBoneSelectionInitialized;
            set
            {
                if (PoseCaptureSettings != null)
                {
                    PoseCaptureSettings.boneSelectionInitialized = value;
                    return;
                }

                State.poseCaptureBoneSelectionInitialized = value;
            }
        }

        private string selectedGraphVariantId { get => State.selectedGraphVariantId; set => State.selectedGraphVariantId = value; }
        private bool useSkeletonRootAsPreviewTarget { get => State.useSkeletonRootAsPreviewTarget; set => State.useSkeletonRootAsPreviewTarget = value; }
        private bool previewFoldout { get => State.previewFoldout; set => State.previewFoldout = value; }
        private AnimationClip previewClip { get => State.previewClip; set => State.previewClip = value; }
        private bool isPlayingPreview { get => State.isPlayingPreview; set => State.isPlayingPreview = value; }
        private double previewStartTime { get => State.previewStartTime; set => State.previewStartTime = value; }
        private float previewScrubTime { get => State.previewScrubTime; set => State.previewScrubTime = value; }
        private Vector2 stageScrollPosition { get => State.stageScrollPosition; set => State.stageScrollPosition = value; }
        private Vector2 resultsScrollPosition { get => State.resultsScrollPosition; set => State.resultsScrollPosition = value; }
        private Vector2 rightPanelScrollPosition { get => State.rightPanelScrollPosition; set => State.rightPanelScrollPosition = value; }
        private float combinationGraphRightPanelWidth { get => State.combinationGraphRightPanelWidth; set => State.combinationGraphRightPanelWidth = value; }
        private bool isDraggingGraphRightSplitter { get => State.isDraggingGraphRightSplitter; set => State.isDraggingGraphRightSplitter = value; }
        private Dictionary<string, Rect> graphVariantRects => State.graphVariantRects;
        private HashSet<string> suppressedPreviewVariantIds => State.suppressedPreviewVariantIds;
        private string hoveredPreviewVariantId { get => State.hoveredPreviewVariantId; set => State.hoveredPreviewVariantId = value; }
        private string pendingLinkStageId { get => State.pendingLinkStageId; set => State.pendingLinkStageId = value; }
        private string pendingLinkVariantId { get => State.pendingLinkVariantId; set => State.pendingLinkVariantId = value; }
        private Rect graphCanvasRect { get => State.graphCanvasRect; set => State.graphCanvasRect = value; }

        private PoseCaptureGraphSettings PoseCaptureSettings
        {
            get
            {
                if (graphAsset == null)
                {
                    return null;
                }

                if (graphAsset.poseCaptureSettings == null)
                {
                    graphAsset.poseCaptureSettings = new PoseCaptureGraphSettings();
                }

                if (graphAsset.poseCaptureSettings.selectedBonePaths == null)
                {
                    graphAsset.poseCaptureSettings.selectedBonePaths = new List<string>();
                }

                if (graphAsset.poseCaptureSettings.boneSearchFilter == null)
                {
                    graphAsset.poseCaptureSettings.boneSearchFilter = string.Empty;
                }

                return graphAsset.poseCaptureSettings;
            }
        }

        private string poseCaptureFolder
        {
            get => PoseCaptureSettings?.poseCaptureFolder ?? windowPoseCaptureFolder;
            set
            {
                if (PoseCaptureSettings != null)
                {
                    PoseCaptureSettings.poseCaptureFolder = value;
                    return;
                }

                windowPoseCaptureFolder = value;
            }
        }

        [MenuItem("Tools/Ezg/Procedural Animation/Inbetween Generator")]
        public static void ShowWindow()
        {
            InbetweenAnimationGeneratorWindow window = GetWindow<InbetweenAnimationGeneratorWindow>("Inbetween Generator");
            window.minSize = new Vector2(900f, 650f);
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            StopPreview();
        }

        private void OnEditorUpdate()
        {
            if (!isPlayingPreview || previewClip == null || previewTarget == null)
            {
                return;
            }

            double elapsed = EditorApplication.timeSinceStartup - previewStartTime;
            float clipTime = (float)(elapsed % previewClip.length);
            previewScrubTime = clipTime;

            SamplePreview(clipTime);
            Repaint();
        }

        private void OnGUI()
        {
            DrawModeToolbar();
            EditorGUILayout.Space(4f);

            if (mode == InbetweenGeneratorMode.FeelPreset)
            {
                feelPresetScrollPosition = EditorGUILayout.BeginScrollView(feelPresetScrollPosition);
                DrawFeelPresetTab();
                EditorGUILayout.EndScrollView();
            }
            else if (mode == InbetweenGeneratorMode.PoseRenamer)
            {
                poseRenamerScrollPosition = EditorGUILayout.BeginScrollView(poseRenamerScrollPosition);
                DrawPoseRenamerTab();
                EditorGUILayout.EndScrollView();
            }
            else if (mode == InbetweenGeneratorMode.CombinationGraph)
            {
                DrawCombinationGraphUI();
            }
        }

        private void DrawModeToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Mode", GUILayout.Width(40f));
                if (GUILayout.Toggle(mode == InbetweenGeneratorMode.FeelPreset, "Feel Preset", EditorStyles.toolbarButton))
                {
                    mode = InbetweenGeneratorMode.FeelPreset;
                }

                if (GUILayout.Toggle(mode == InbetweenGeneratorMode.PoseRenamer, "Pose Renamer", EditorStyles.toolbarButton))
                {
                    mode = InbetweenGeneratorMode.PoseRenamer;
                }

                if (GUILayout.Toggle(mode == InbetweenGeneratorMode.CombinationGraph, "Combination Graph", EditorStyles.toolbarButton))
                {
                    mode = InbetweenGeneratorMode.CombinationGraph;
                }

                GUILayout.FlexibleSpace();
            }
        }

        private void DrawCombinationGraphUI()
        {
            DrawGraphHeader();

            if (graphAsset == null)
            {
                EditorGUILayout.HelpBox("Create or load a Pose Combination Graph asset to begin.", MessageType.Info);
                return;
            }

            EnsureDefaultGraphConnections();

            if (GUI.changed)
            {
                RecomputeValidPaths();
            }

            float availableWidth = position.width;
            float maxRightWidth = Mathf.Max(InbetweenGeneratorStyles.GraphMinRightPanelWidth, availableWidth - InbetweenGeneratorStyles.GraphMinLeftPanelWidth - InbetweenGeneratorStyles.GraphRightSplitterWidth);
            combinationGraphRightPanelWidth = Mathf.Clamp(combinationGraphRightPanelWidth, InbetweenGeneratorStyles.GraphMinRightPanelWidth, maxRightWidth);
            float rightWidth = combinationGraphRightPanelWidth;
            float leftWidth = Mathf.Max(InbetweenGeneratorStyles.GraphMinLeftPanelWidth, availableWidth - rightWidth - InbetweenGeneratorStyles.GraphRightSplitterWidth);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(leftWidth)))
                {
                    if (graphAsset.stages.Count == 0)
                    {
                        if (GUILayout.Button("Add First Stage", GUILayout.Height(32f)))
                        {
                            AddStage();
                        }
                    }
                    else
                    {
                        stageScrollPosition = EditorGUILayout.BeginScrollView(stageScrollPosition);
                        DrawStageColumns();
                        EditorGUILayout.EndScrollView();
                    }
                }

                DrawGraphRightPanelSplitter(maxRightWidth);

                using (new EditorGUILayout.VerticalScope(GUILayout.Width(rightWidth)))
                {
                    rightPanelScrollPosition = EditorGUILayout.BeginScrollView(rightPanelScrollPosition);
                    DrawPoseCombinationRightPanel();
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void DrawGraphRightPanelSplitter(float maxRightWidth)
        {
            Rect splitterRect = GUILayoutUtility.GetRect(
                InbetweenGeneratorStyles.GraphRightSplitterWidth,
                InbetweenGeneratorStyles.GraphRightSplitterWidth,
                GUILayout.ExpandHeight(true));

            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(splitterRect, new Color(0.22f, 0.22f, 0.22f, 1f));

                Rect gripRect = new Rect(splitterRect.center.x - 1f, splitterRect.y + 8f, 2f, Mathf.Max(8f, splitterRect.height - 16f));
                EditorGUI.DrawRect(gripRect, new Color(0.45f, 0.45f, 0.45f, 1f));
            }

            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && splitterRect.Contains(evt.mousePosition))
            {
                isDraggingGraphRightSplitter = true;
                evt.Use();
            }

            if (isDraggingGraphRightSplitter && evt.type == EventType.MouseDrag)
            {
                combinationGraphRightPanelWidth = Mathf.Clamp(
                    combinationGraphRightPanelWidth - evt.delta.x,
                    InbetweenGeneratorStyles.GraphMinRightPanelWidth,
                    maxRightWidth);
                Repaint();
                evt.Use();
            }

            if (isDraggingGraphRightSplitter && evt.rawType == EventType.MouseUp)
            {
                isDraggingGraphRightSplitter = false;
                evt.Use();
            }
        }

        private void DrawGraphHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("New Graph", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                {
                    CreateNewGraph();
                }

                if (GUILayout.Button("Load Graph", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                {
                    LoadGraph();
                }

                using (new EditorGUI.DisabledScope(autoSaveGraph || graphAsset == null || !dirtyGraph))
                {
                    if (GUILayout.Button("Save Graph", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                    {
                        SaveGraph();
                    }
                }

                EditorGUI.BeginChangeCheck();
                autoSaveGraph = EditorGUILayout.ToggleLeft("Auto Save", autoSaveGraph, GUILayout.Width(90f));
                if (EditorGUI.EndChangeCheck() && autoSaveGraph && dirtyGraph)
                {
                    SaveGraph();
                }

                GUILayout.FlexibleSpace();

                if (graphAsset != null)
                {
                    EditorGUI.BeginChangeCheck();
                    graphAsset = (PoseCombinationGraphAsset)EditorGUILayout.ObjectField(graphAsset, typeof(PoseCombinationGraphAsset), false, GUILayout.Width(200f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        dirtyGraph = false;
                        RecomputeValidPaths();
                    }

                    if (dirtyGraph)
                    {
                        GUILayout.Label("*", GUILayout.Width(15f));
                    }
                }
            }

            if (graphAsset != null)
            {
                EditorGUILayout.Space(2f);
                EditorGUI.BeginChangeCheck();
                skeletonRoot = (Transform)EditorGUILayout.ObjectField(InbetweenGeneratorContent.SkeletonRoot, skeletonRoot, typeof(Transform), true);
                if (EditorGUI.EndChangeCheck())
                {
                    if (useSkeletonRootAsPreviewTarget)
                    {
                        previewTarget = skeletonRoot;
                        StopPreview();
                    }

                    if (!rememberPoseCaptureSelection)
                    {
                        ResetPoseCaptureSelectionState();
                    }
                }

                DrawPoseCaptureFolderField();
                DrawPoseCaptureFilter();
            }
        }

    }
}
