using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    internal enum InbetweenGeneratorMode
    {
        FeelPreset,
        PoseRenamer,
        CombinationGraph
    }

    [Serializable]
    internal sealed class InbetweenAnimationGeneratorState
    {
        public InbetweenGeneratorMode mode = InbetweenGeneratorMode.CombinationGraph;

        public Vector2 feelPresetScrollPosition;
        public Vector2 poseRenamerScrollPosition;
        public bool dirtyGraph;
        public bool autoSaveGraph;
        public bool[] pathSelection = new bool[0];
        public int selectedConnectionIndex = -1;
        public int selectedPathIndex = -1;
        public string batchBaseName = "anim";
        public string pathSearchFilter = string.Empty;
        public bool allConnectionsFoldout = true;
        public bool batchAnimationExportFoldout = true;
        public bool poseCaptureFilterFoldout = true;
        public bool rememberPoseCaptureSelection;
        public string poseCaptureBoneSearchFilter = string.Empty;
        public Vector2 poseCaptureBoneScrollPosition;
        public List<string> selectedPoseCaptureBonePaths = new List<string>();
        public bool poseCaptureBoneSelectionInitialized;
        public string selectedGraphVariantId;

        public bool useSkeletonRootAsPreviewTarget = true;
        public bool previewFoldout = true;
        [NonSerialized]
        public AnimationClip previewClip;
        [NonSerialized]
        public bool isPlayingPreview;
        [NonSerialized]
        public double previewStartTime;
        [NonSerialized]
        public float previewScrubTime;

        public Vector2 stageScrollPosition;
        public Vector2 resultsScrollPosition;
        public Vector2 rightPanelScrollPosition;
        public float combinationGraphRightPanelWidth = InbetweenGeneratorStyles.DefaultRightPanelWidth;
        public bool isDraggingGraphRightSplitter;

        [NonSerialized]
        public Dictionary<string, Rect> graphVariantRects;

        [NonSerialized]
        public HashSet<string> suppressedPreviewVariantIds;

        public string hoveredPreviewVariantId;
        public string pendingLinkStageId;
        public string pendingLinkVariantId;
        public Rect graphCanvasRect;

        public void EnsureTransientCollections()
        {
            if (pathSelection == null)
            {
                pathSelection = new bool[0];
            }

            if (batchBaseName == null)
            {
                batchBaseName = "anim";
            }

            if (pathSearchFilter == null)
            {
                pathSearchFilter = string.Empty;
            }

            if (poseCaptureBoneSearchFilter == null)
            {
                poseCaptureBoneSearchFilter = string.Empty;
            }

            if (selectedPoseCaptureBonePaths == null)
            {
                selectedPoseCaptureBonePaths = new List<string>();
            }

            if (combinationGraphRightPanelWidth <= 0f)
            {
                combinationGraphRightPanelWidth = InbetweenGeneratorStyles.DefaultRightPanelWidth;
            }

            if (graphVariantRects == null)
            {
                graphVariantRects = new Dictionary<string, Rect>();
            }

            if (suppressedPreviewVariantIds == null)
            {
                suppressedPreviewVariantIds = new HashSet<string>();
            }
        }
    }
}
