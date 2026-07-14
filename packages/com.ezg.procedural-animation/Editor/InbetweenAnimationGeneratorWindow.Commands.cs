using System.Collections.Generic;
using Ezg.ProceduralAnimation;
using UnityEditor;
using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    public partial class InbetweenAnimationGeneratorWindow
    {
        private void CreateNewGraph()
        {
            PoseCombinationGraphAsset newGraph = InbetweenGeneratorPersistence.CreateGraphAsset();
            if (newGraph != null)
            {
                SetActiveGraph(newGraph);
            }
        }

        private void LoadGraph()
        {
            PoseCombinationGraphAsset loaded = InbetweenGeneratorPersistence.LoadGraphAsset();
            if (loaded != null)
            {
                SetActiveGraph(loaded);
            }
        }

        private void SaveGraph()
        {
            if (graphAsset == null) return;

            InbetweenGeneratorPersistence.SaveGraphAsset(graphAsset);
            dirtyGraph = false;
            Repaint();
        }

        private void SetActiveGraph(PoseCombinationGraphAsset graph)
        {
            graphAsset = graph;
            dirtyGraph = false;
            selectedConnectionIndex = -1;
            selectedPathIndex = -1;
            selectedGraphVariantId = null;
            pendingLinkStageId = null;
            pendingLinkVariantId = null;
            suppressedPreviewVariantIds.Clear();
            StopPreview();
            RecomputeValidPaths();
        }

        private void AddStage()
        {
            PoseStage stage = new PoseStage
            {
                id = PoseCombinationGraphResolver.GenerateStageId(),
                name = $"Stage {graphAsset.stages.Count + 1}"
            };
            graphAsset.stages.Add(stage);
            MarkDirty();
        }

        private void AddStageAt(int index)
        {
            PoseStage stage = new PoseStage
            {
                id = PoseCombinationGraphResolver.GenerateStageId(),
                name = "New Stage"
            };
            graphAsset.stages.Insert(index + 1, stage);
            MarkDirty();
        }

        private void RemoveStage(int index)
        {
            if (graphAsset.stages.Count <= 1) return;

            PoseStage stage = graphAsset.stages[index];
            List<string> variantIds = new List<string>();
            foreach (PoseVariant v in stage.variants)
            {
                if (!string.IsNullOrEmpty(v.id)) variantIds.Add(v.id);
            }

            graphAsset.connections.RemoveAll(c =>
                variantIds.Contains(c.fromVariantId) || variantIds.Contains(c.toVariantId));

            graphAsset.stages.RemoveAt(index);
            selectedConnectionIndex = -1;
            if (variantIds.Contains(selectedGraphVariantId))
            {
                selectedGraphVariantId = null;
            }

            pendingLinkStageId = null;
            pendingLinkVariantId = null;
            MarkDirty();
        }

        private void MoveStage(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= graphAsset.stages.Count ||
                toIndex < 0 || toIndex >= graphAsset.stages.Count ||
                fromIndex == toIndex)
            {
                return;
            }

            PoseStage stage = graphAsset.stages[fromIndex];
            graphAsset.stages.RemoveAt(fromIndex);
            graphAsset.stages.Insert(toIndex, stage);
            PruneInvalidForwardConnections();
            selectedConnectionIndex = -1;
            selectedGraphVariantId = null;
            pendingLinkStageId = null;
            pendingLinkVariantId = null;
            MarkDirty();
        }

        private void PruneInvalidForwardConnections()
        {
            graphAsset.connections.RemoveAll(connection =>
            {
                int fromStageIndex = graphAsset.GetStageIndex(connection.fromStageId);
                int toStageIndex = graphAsset.GetStageIndex(connection.toStageId);
                return fromStageIndex < 0 || toStageIndex < 0 || toStageIndex <= fromStageIndex;
            });
        }

        private void AddVariant(int stageIndex)
        {
            PoseStage stage = graphAsset.stages[stageIndex];
            PoseVariant variant = new PoseVariant
            {
                id = PoseCombinationGraphResolver.GenerateVariantId(),
                name = $"{stage.name}{stage.variants.Count + 1}"
            };
            stage.variants.Add(variant);
            MarkDirty();
        }

        private void RemoveVariant(int stageIndex, int variantIndex)
        {
            PoseStage stage = graphAsset.stages[stageIndex];
            PoseVariant variant = stage.variants[variantIndex];

            graphAsset.connections.RemoveAll(c =>
                c.fromVariantId == variant.id || c.toVariantId == variant.id);

            stage.variants.RemoveAt(variantIndex);
            selectedConnectionIndex = -1;
            if (selectedGraphVariantId == variant.id)
            {
                selectedGraphVariantId = null;
            }

            pendingLinkStageId = null;
            pendingLinkVariantId = null;
            MarkDirty();
        }

        private PoseConnection FindConnection(string fromVariantId, string toVariantId)
        {
            for (int i = 0; i < graphAsset.connections.Count; i++)
            {
                PoseConnection c = graphAsset.connections[i];
                if (c.fromVariantId == fromVariantId && c.toVariantId == toVariantId)
                {
                    return c;
                }
            }

            return null;
        }

        private bool IsForwardConnection(PoseConnection connection)
        {
            if (connection == null)
            {
                return false;
            }

            int fromStageIndex = graphAsset.GetStageIndex(connection.fromStageId);
            int toStageIndex = graphAsset.GetStageIndex(connection.toStageId);
            return fromStageIndex >= 0 && toStageIndex > fromStageIndex;
        }

        private PoseConnection ToggleConnection(string fromStageId, string fromVariantId, string toStageId, string toVariantId)
        {
            PoseConnection existing = FindConnection(fromVariantId, toVariantId);
            if (existing != null)
            {
                existing.enabled = !existing.enabled;
                MarkDirty();
                return existing;
            }

            PoseConnection conn = PoseCombinationGraphResolver.CreateDefaultConnection(
                graphAsset, fromStageId, fromVariantId, toStageId, toVariantId);
            conn.enabled = false;
            graphAsset.connections.Add(conn);
            UnsuppressDefaultConnection(fromStageId, fromVariantId, toStageId, toVariantId);
            MarkDirty();
            return conn;
        }

        private PoseConnection CreateOrEnableConnection(string fromStageId, string fromVariantId, string toStageId, string toVariantId)
        {
            PoseConnection existing = FindConnection(fromVariantId, toVariantId);
            if (existing != null)
            {
                existing.enabled = true;
                MarkDirty();
                return existing;
            }

            PoseConnection conn = PoseCombinationGraphResolver.CreateDefaultConnection(
                graphAsset, fromStageId, fromVariantId, toStageId, toVariantId);
            graphAsset.connections.Add(conn);
            UnsuppressDefaultConnection(fromStageId, fromVariantId, toStageId, toVariantId);
            MarkDirty();
            return conn;
        }

        private void EnsureDefaultGraphConnections()
        {
            if (graphAsset == null || graphAsset.stages == null || graphAsset.stages.Count < 2)
            {
                return;
            }

            if (graphAsset.connections == null)
            {
                graphAsset.connections = new List<PoseConnection>();
            }

            EnsureSuppressedDefaultConnectionKeys();

            bool addedConnection = false;
            for (int stageIndex = 0; stageIndex < graphAsset.stages.Count - 1; stageIndex++)
            {
                PoseStage fromStage = graphAsset.stages[stageIndex];
                PoseStage toStage = graphAsset.stages[stageIndex + 1];
                if (fromStage == null || toStage == null || fromStage.variants == null || toStage.variants == null)
                {
                    continue;
                }

                for (int fromIndex = 0; fromIndex < fromStage.variants.Count; fromIndex++)
                {
                    PoseVariant fromVariant = fromStage.variants[fromIndex];
                    if (fromVariant == null || string.IsNullOrEmpty(fromVariant.id))
                    {
                        continue;
                    }

                    for (int toIndex = 0; toIndex < toStage.variants.Count; toIndex++)
                    {
                        PoseVariant toVariant = toStage.variants[toIndex];
                        if (toVariant == null || string.IsNullOrEmpty(toVariant.id) ||
                            FindConnection(fromVariant.id, toVariant.id) != null ||
                            IsDefaultConnectionSuppressed(fromStage.id, fromVariant.id, toStage.id, toVariant.id))
                        {
                            continue;
                        }

                        PoseConnection connection = PoseCombinationGraphResolver.CreateDefaultConnection(
                            graphAsset,
                            fromStage.id,
                            fromVariant.id,
                            toStage.id,
                            toVariant.id);
                        graphAsset.connections.Add(connection);
                        addedConnection = true;
                    }
                }
            }

            if (addedConnection)
            {
                MarkDirty();
            }
        }

        private void DeleteSelectedConnection()
        {
            if (selectedConnectionIndex < 0 || selectedConnectionIndex >= graphAsset.connections.Count)
            {
                return;
            }

            PoseConnection connection = graphAsset.connections[selectedConnectionIndex];
            SuppressDefaultConnection(connection);
            graphAsset.connections.RemoveAt(selectedConnectionIndex);
            selectedConnectionIndex = -1;
            MarkDirty();
        }

        private void SuppressDefaultConnection(PoseConnection connection)
        {
            if (connection == null || !IsAdjacentStageConnection(connection.fromStageId, connection.toStageId))
            {
                return;
            }

            EnsureSuppressedDefaultConnectionKeys();
            string key = GetDefaultConnectionKey(
                connection.fromStageId,
                connection.fromVariantId,
                connection.toStageId,
                connection.toVariantId);
            if (!graphAsset.suppressedDefaultConnectionKeys.Contains(key))
            {
                graphAsset.suppressedDefaultConnectionKeys.Add(key);
            }
        }

        private void UnsuppressDefaultConnection(string fromStageId, string fromVariantId, string toStageId, string toVariantId)
        {
            if (graphAsset == null || !IsAdjacentStageConnection(fromStageId, toStageId))
            {
                return;
            }

            EnsureSuppressedDefaultConnectionKeys();
            graphAsset.suppressedDefaultConnectionKeys.Remove(GetDefaultConnectionKey(fromStageId, fromVariantId, toStageId, toVariantId));
        }

        private bool IsDefaultConnectionSuppressed(string fromStageId, string fromVariantId, string toStageId, string toVariantId)
        {
            EnsureSuppressedDefaultConnectionKeys();
            return graphAsset.suppressedDefaultConnectionKeys.Contains(GetDefaultConnectionKey(fromStageId, fromVariantId, toStageId, toVariantId));
        }

        private bool IsAdjacentStageConnection(string fromStageId, string toStageId)
        {
            int fromStageIndex = graphAsset.GetStageIndex(fromStageId);
            int toStageIndex = graphAsset.GetStageIndex(toStageId);
            return fromStageIndex >= 0 && toStageIndex == fromStageIndex + 1;
        }

        private void EnsureSuppressedDefaultConnectionKeys()
        {
            if (graphAsset.suppressedDefaultConnectionKeys == null)
            {
                graphAsset.suppressedDefaultConnectionKeys = new List<string>();
            }
        }

        private string GetDefaultConnectionKey(string fromStageId, string fromVariantId, string toStageId, string toVariantId)
        {
            return $"{fromStageId}:{fromVariantId}->{toStageId}:{toVariantId}";
        }

        private void CapturePreviewForVariant(PoseVariant variant)
        {
            if (variant.pose == null)
            {
                EditorUtility.DisplayDialog("Missing Pose Asset", "Assign or capture a pose before capturing a preview image.", "OK");
                return;
            }

            string previewPath = PosePreviewCaptureUtility.BuildDefaultPreviewPath(variant.pose);
            if (string.IsNullOrEmpty(previewPath))
            {
                EditorUtility.DisplayDialog("Invalid Pose Asset", "Save the pose asset before capturing a preview image.", "OK");
                return;
            }

            string targetPath = previewPath;
            bool overwriteExisting = true;
            if (PosePreviewCaptureUtility.PreviewExistsForPose(variant.pose, out _))
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "Preview Image Exists",
                    $"A preview image already exists at:\n{previewPath}",
                    "Replace",
                    "Cancel",
                    "Rename");

                if (choice == 1)
                {
                    return;
                }

                if (choice == 2)
                {
                    string folder = System.IO.Path.GetDirectoryName(previewPath)?.Replace("\\", "/");
                    string defaultName = $"{System.IO.Path.GetFileNameWithoutExtension(previewPath)}_Preview";
                    targetPath = EditorUtility.SaveFilePanelInProject(
                        "Save Pose Preview",
                        defaultName,
                        "png",
                        "Choose where to save the pose preview image.",
                        folder);

                    if (string.IsNullOrEmpty(targetPath))
                    {
                        return;
                    }
                }
            }

            try
            {
                Texture2D preview = PosePreviewCaptureUtility.CaptureSceneViewPreview();
                if (preview == null) return;

                Texture2D saved = targetPath == previewPath
                    ? PosePreviewCaptureUtility.SavePreviewTextureForPose(preview, variant.pose, overwriteExisting)
                    : PosePreviewCaptureUtility.SavePreviewTexture(preview, targetPath, overwriteExisting);
                if (saved != null)
                {
                    variant.previewImage = saved;
                    if (!string.IsNullOrEmpty(variant.id))
                    {
                        suppressedPreviewVariantIds.Remove(variant.id);
                    }

                    MarkDirty();
                }

                DestroyImmediate(preview);
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Preview Capture Failed", ex.Message, "OK");
                Debug.LogException(ex);
            }
        }

        private void CapturePoseForVariant(PoseVariant variant)
        {
            if (skeletonRoot == null)
            {
                EditorUtility.DisplayDialog("Missing Skeleton Root", "Assign a Skeleton Root before capturing a pose.", "OK");
                return;
            }

            if (!HasValidPoseCaptureFolder())
            {
                EditorUtility.DisplayDialog("Missing Pose Capture Folder", "Assign a Pose Capture Folder before capturing new pose assets.", "OK");
                return;
            }

            if (!TryGetPoseCaptureIncludedBonePaths(out HashSet<string> includedBonePaths))
            {
                return;
            }

            string posePath = BuildPoseCapturePath(variant);
            if (AssetDatabase.LoadAssetAtPath<Object>(posePath) != null)
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "Pose Asset Exists",
                    $"A pose asset already exists at:\n{posePath}",
                    "Replace",
                    "Cancel",
                    "Rename");

                if (choice == 1)
                {
                    return;
                }

                if (choice == 2)
                {
                    posePath = EditorUtility.SaveFilePanelInProject(
                        "Save Pose Asset",
                        $"{MakeSafeFileName(variant.name)}_Pose",
                        "asset",
                        "Choose where to save the captured pose asset.",
                        poseCaptureFolder);

                    if (string.IsNullOrEmpty(posePath))
                    {
                        return;
                    }
                }
            }

            Object existingAsset = AssetDatabase.LoadAssetAtPath<Object>(posePath);
            if (existingAsset != null && !(existingAsset is PoseAsset))
            {
                EditorUtility.DisplayDialog("Invalid Pose Asset", $"The selected path is not a PoseAsset:\n{posePath}", "OK");
                return;
            }

            PoseAsset pose = AssetDatabase.LoadAssetAtPath<PoseAsset>(posePath);
            if (pose == null)
            {
                pose = CreateInstance<PoseAsset>();
                AssetDatabase.CreateAsset(pose, posePath);
            }

            PoseCaptureUtility.CaptureIntoPose(skeletonRoot, pose, includedBonePaths);
            ResetPoseCaptureSelectionAfterCapture();
            variant.pose = pose;
            AutoCapturePreviewForVariant(variant);
            MarkDirty();
            EditorGUIUtility.PingObject(pose);
        }

        private void LoadPoseForVariant(PoseVariant variant)
        {
            if (skeletonRoot == null)
            {
                EditorUtility.DisplayDialog("Missing Skeleton Root", "Assign a Skeleton Root before loading a pose.", "OK");
                return;
            }

            if (variant.pose == null)
            {
                EditorUtility.DisplayDialog("Missing Pose Asset", "Assign a pose before loading it onto the skeleton root.", "OK");
                return;
            }

            PoseCaptureUtility.ApplyPoseToSkeleton(skeletonRoot, variant.pose);
            EditorGUIUtility.PingObject(variant.pose);
        }

        private void ReplacePoseForVariant(PoseVariant variant)
        {
            if (skeletonRoot == null)
            {
                EditorUtility.DisplayDialog("Missing Skeleton Root", "Assign a Skeleton Root before replacing a pose.", "OK");
                return;
            }

            if (variant.pose == null)
            {
                EditorUtility.DisplayDialog("Missing Pose Asset", "Assign a pose before replacing it.", "OK");
                return;
            }

            if (!TryGetPoseCaptureIncludedBonePaths(out HashSet<string> includedBonePaths))
            {
                return;
            }

            PoseCaptureUtility.CaptureIntoPose(skeletonRoot, variant.pose, includedBonePaths);
            ResetPoseCaptureSelectionAfterCapture();
            AutoCapturePreviewForVariant(variant);
            MarkDirty();
            EditorGUIUtility.PingObject(variant.pose);
        }

        private void AutoCapturePreviewForVariant(PoseVariant variant)
        {
            try
            {
                Texture2D preview = PosePreviewCaptureUtility.CaptureSceneViewPreview();
                if (preview == null) return;

                Texture2D saved = PosePreviewCaptureUtility.SavePreviewTextureForPose(preview, variant.pose, true);
                if (saved != null)
                {
                    variant.previewImage = saved;
                    if (!string.IsNullOrEmpty(variant.id))
                    {
                        suppressedPreviewVariantIds.Remove(variant.id);
                    }
                }

                DestroyImmediate(preview);
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Preview Capture Failed", $"Pose was captured, but preview capture failed:\n{ex.Message}", "OK");
                Debug.LogException(ex);
            }
        }

        private string BuildPoseCapturePath(PoseVariant variant)
        {
            string safeName = MakeSafeFileName(string.IsNullOrEmpty(variant.name) ? "Pose" : variant.name);
            return $"{poseCaptureFolder}/{safeName}.asset";
        }

        private string MakeSafeFileName(string name)
        {
            foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }

            string trimmed = name.Trim();
            return string.IsNullOrEmpty(trimmed) ? "Pose" : trimmed;
        }

        private void RecomputeValidPaths()
        {
            if (graphAsset == null) return;

            validPaths = PoseCombinationGraphResolver.ResolveValidPaths(graphAsset);
            finalPaths = PoseCombinationGraphResolver.FilterPathsReachingFinalStage(graphAsset, validPaths);

            pathSelection = new bool[finalPaths.Count];

            if (selectedPathIndex >= finalPaths.Count)
            {
                selectedPathIndex = -1;
            }
        }

        private void StartPreview()
        {
            if (selectedPathIndex < 0 || selectedPathIndex >= finalPaths.Count || previewTarget == null)
            {
                return;
            }

            PoseCombinationPath path = finalPaths[selectedPathIndex];
            if (!path.HasAllPoses(graphAsset) || !path.HasAllValidSegments(graphAsset))
            {
                EditorUtility.DisplayDialog("Invalid Path", "The selected path has missing poses or invalid segment settings.", "OK");
                return;
            }

            try
            {
                previewClip = PoseCombinationClipBuilder.BuildPreviewClip(graphAsset, path);
                previewStartTime = EditorApplication.timeSinceStartup;
                previewScrubTime = 0f;
                isPlayingPreview = true;
                SamplePreview(0f);
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Preview Failed", ex.Message, "OK");
                Debug.LogException(ex);
            }
        }

        private void StopPreview()
        {
            isPlayingPreview = false;
        }

        private void SamplePreview(float time)
        {
            if (previewTarget == null || previewClip == null) return;

            time = Mathf.Clamp(time, 0f, previewClip.length);
            previewClip.SampleAnimation(previewTarget.gameObject, time);
        }

        private void BatchGenerate()
        {
            if (graphAsset == null) return;

            int selectedCount = 0;
            for (int i = 0; i < pathSelection.Length; i++)
            {
                if (pathSelection[i]) selectedCount++;
            }

            if (selectedCount == 0)
            {
                EditorUtility.DisplayDialog("No Paths Selected", "Select at least one valid path for batch export.", "OK");
                return;
            }

            if (selectedCount > graphAsset.generationOptions.maxBatchCountGuard)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "Large Batch",
                    $"You are about to generate {selectedCount} clips, which exceeds the max batch guard of {graphAsset.generationOptions.maxBatchCountGuard}.",
                    "Proceed Anyway", "Cancel");
                if (!proceed) return;
            }

            HashSet<string> clipNames = new HashSet<string>();
            List<string> duplicates = new List<string>();
            int batchIdx = 0;
            for (int i = 0; i < finalPaths.Count; i++)
            {
                if (!pathSelection[i]) continue;
                string name = PoseCombinationGraphResolver.ResolveClipName(graphAsset, finalPaths[i], batchBaseName, batchIdx);
                if (!clipNames.Add(name))
                {
                    duplicates.Add(name);
                }

                batchIdx++;
            }

            if (duplicates.Count > 0)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "Duplicate Clip Names",
                    $"The following clip names would be duplicated:\n\n{string.Join("\n", duplicates)}\n\nGeneration will overwrite earlier clips with the same name. Continue?",
                    "Continue", "Cancel");
                if (!proceed) return;
            }

            int generated = 0;
            int skipped = 0;
            int failed = 0;
            int batchIndex = 0;
            List<string> failedPaths = new List<string>();

            try
            {
                for (int i = 0; i < finalPaths.Count; i++)
                {
                    if (!pathSelection[i]) continue;

                    PoseCombinationPath path = finalPaths[i];

                    List<string> validationErrors = path.GetValidationErrors(graphAsset);
                    if (validationErrors.Count > 0)
                    {
                        skipped++;
                        failedPaths.Add($"{path.GetPathDisplayName(graphAsset)} skipped:\n- {string.Join("\n- ", validationErrors)}");
                        continue;
                    }

                    try
                    {
                        PoseCombinationClipBuilder.BuildAndSaveClip(graphAsset, path, batchBaseName, batchIndex);
                        generated++;
                        batchIndex++;
                    }
                    catch (System.Exception ex)
                    {
                        failed++;
                        failedPaths.Add($"{path.GetPathDisplayName(graphAsset)}: {ex.Message}");
                        batchIndex++;
                    }
                }
            }
            finally
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            string message = $"Generated: {generated}\nSkipped: {skipped}\nFailed: {failed}";
            if (failedPaths.Count > 0)
            {
                message += "\n\nFailures:\n" + string.Join("\n", failedPaths);
            }

            if (failed > 0 || skipped > 0)
            {
                EditorUtility.DisplayDialog("Batch Generation Complete", message, "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Batch Generation Complete", message, "OK");
            }

            if (generated > 0 && !string.IsNullOrEmpty(graphAsset.generationOptions.outputFolder))
            {
                for (int i = 0; i < finalPaths.Count; i++)
                {
                    if (!pathSelection[i]) continue;
                    if (!finalPaths[i].HasAllPoses(graphAsset) || !finalPaths[i].HasAllValidSegments(graphAsset)) continue;

                    string firstClipName = PoseCombinationGraphResolver.ResolveClipName(graphAsset, finalPaths[i], batchBaseName, 0);
                    string outputPath = AnimationClipWriter.BuildOutputPath(graphAsset.generationOptions.outputFolder, firstClipName);
                    Object result = AssetDatabase.LoadAssetAtPath<Object>(outputPath);
                    if (result != null)
                    {
                        EditorGUIUtility.PingObject(result);
                    }

                    break;
                }
            }
        }

        private void MarkDirty(bool recomputePaths = true)
        {
            dirtyGraph = true;

            if (recomputePaths)
            {
                RecomputeValidPaths();
            }

            if (autoSaveGraph)
            {
                SaveGraph();
            }
        }
    }
}
