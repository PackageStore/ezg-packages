using System.Collections.Generic;
using Ezg.ProceduralAnimation;
using UnityEditor;
using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    public partial class InbetweenAnimationGeneratorWindow
    {
        private void DrawStageColumns()
        {
            int removeStageIndex = -1;
            int addStageBeforeIndex = -1;
            int moveStageLeftIndex = -1;
            int moveStageRightIndex = -1;
            graphVariantRects.Clear();

            graphCanvasRect = EditorGUILayout.BeginHorizontal();
            {
                for (int stageIndex = 0; stageIndex < graphAsset.stages.Count; stageIndex++)
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(InbetweenGeneratorStyles.GraphStageColumnWidth)))
                    {
                        DrawStageHeader(
                            stageIndex,
                            ref removeStageIndex,
                            ref addStageBeforeIndex,
                            ref moveStageLeftIndex,
                            ref moveStageRightIndex);
                        DrawStageVariants(stageIndex);
                    }

                    if (stageIndex < graphAsset.stages.Count - 1)
                    {
                        GUILayout.Space(InbetweenGeneratorStyles.GraphConnectionGapWidth);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            DrawGraphConnectionWires();
            HandleGraphConnectionWireInput();

            if (removeStageIndex >= 0)
            {
                RemoveStage(removeStageIndex);
            }

            if (addStageBeforeIndex >= 0)
            {
                AddStageAt(addStageBeforeIndex);
            }

            if (moveStageLeftIndex >= 0)
            {
                MoveStage(moveStageLeftIndex, moveStageLeftIndex - 1);
            }

            if (moveStageRightIndex >= 0)
            {
                MoveStage(moveStageRightIndex, moveStageRightIndex + 1);
            }
        }

        private void DrawStageHeader(
            int stageIndex,
            ref int removeStageIndex,
            ref int addStageBeforeIndex,
            ref int moveStageLeftIndex,
            ref int moveStageRightIndex)
        {
            PoseStage stage = graphAsset.stages[stageIndex];

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(stageIndex <= 0))
                {
                    if (GUILayout.Button("◀", EditorStyles.toolbarButton, GUILayout.Width(24f)))
                    {
                        moveStageLeftIndex = stageIndex;
                    }
                }

                using (new EditorGUI.DisabledScope(stageIndex >= graphAsset.stages.Count - 1))
                {
                    if (GUILayout.Button("▶", EditorStyles.toolbarButton, GUILayout.Width(24f)))
                    {
                        moveStageRightIndex = stageIndex;
                    }
                }

                EditorGUI.BeginChangeCheck();
                stage.name = EditorGUILayout.TextField(stage.name, GUILayout.ExpandWidth(true));
                if (EditorGUI.EndChangeCheck())
                {
                    MarkDirty(false);
                }

                if (GUILayout.Button("+", EditorStyles.toolbarButton, GUILayout.Width(24f)))
                {
                    addStageBeforeIndex = stageIndex;
                }

                using (new EditorGUI.DisabledScope(graphAsset.stages.Count <= 1))
                {
                    if (GUILayout.Button("×", EditorStyles.toolbarButton, GUILayout.Width(24f)))
                    {
                        removeStageIndex = stageIndex;
                    }
                }
            }
        }

        private void DrawStageVariants(int stageIndex)
        {
            PoseStage stage = graphAsset.stages[stageIndex];
            int removeVariantIndex = -1;

            for (int variantIndex = 0; variantIndex < stage.variants.Count; variantIndex++)
            {
                PoseVariant variant = stage.variants[variantIndex];
                DrawVariantBlock(stage, variant, stageIndex, variantIndex, ref removeVariantIndex);
            }

            if (removeVariantIndex >= 0)
            {
                RemoveVariant(stageIndex, removeVariantIndex);
            }

            if (GUILayout.Button("+ Add Variant", GUILayout.Height(22f)))
            {
                AddVariant(stageIndex);
            }
        }

        private void DrawVariantBlock(PoseStage stage, PoseVariant variant, int stageIndex, int variantIndex, ref int removeVariantIndex)
        {
            Color previousGuiColor = GUI.color;
            GUI.color = GetGraphVariantColor(variant.id, previousGuiColor);

            Rect boxRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    variant.name = EditorGUILayout.TextField(variant.name, GUILayout.Width(80f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        MarkDirty(false);
                    }

                    GUILayout.FlexibleSpace();

                    using (new EditorGUI.DisabledScope(variantIndex <= 0))
                    {
                        if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(22f)))
                        {
                            stage.variants[variantIndex] = stage.variants[variantIndex - 1];
                            stage.variants[variantIndex - 1] = variant;
                            MarkDirty();
                        }
                    }

                    using (new EditorGUI.DisabledScope(variantIndex >= stage.variants.Count - 1))
                    {
                        if (GUILayout.Button("▼", EditorStyles.miniButtonMid, GUILayout.Width(22f)))
                        {
                            stage.variants[variantIndex] = stage.variants[variantIndex + 1];
                            stage.variants[variantIndex + 1] = variant;
                            MarkDirty();
                        }
                    }

                    if (GUILayout.Button("×", EditorStyles.miniButtonRight, GUILayout.Width(22f)))
                    {
                        removeVariantIndex = variantIndex;
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    PoseAsset newPose = (PoseAsset)EditorGUILayout.ObjectField(new GUIContent("Pose Asset", "<b>PoseAsset</b> gán cho variant này. Chứa dữ liệu transform của tất cả bone tại một tư thế.\n<i>Ví dụ: Idle, Walk, Attack</i>"), variant.pose, typeof(PoseAsset), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        SetVariantPose(variant, newPose);
                    }

                    using (new EditorGUI.DisabledScope(variant.pose == null))
                    {
                        if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22f)))
                        {
                            SetVariantPose(variant, null);
                        }
                    }
                }

                LoadPreviewImageForVariant(variant);

                if (variant.previewImage != null)
                {
                    DrawVariantPreviewImage(variant);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (variant.previewImage == null && variant.pose != null && GUILayout.Button("Capture View", GUILayout.Height(22f)))
                    {
                        CapturePreviewForVariant(variant);
                    }

                    if (variant.pose == null)
                    {
                        if (GUILayout.Button("Capture Pose", GUILayout.Height(22f)))
                        {
                            CapturePoseForVariant(variant);
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("Replace Pose", GUILayout.Height(22f)))
                        {
                            ReplacePoseForVariant(variant);
                        }

                        if (GUILayout.Button("Load Pose To Skeleton", GUILayout.Height(22f)))
                        {
                            LoadPoseForVariant(variant);
                        }
                    }
                }
            }
            EditorGUILayout.EndVertical();
            GUI.color = previousGuiColor;

            if (!string.IsNullOrEmpty(variant.id))
            {
                graphVariantRects[variant.id] = boxRect;
                DrawSelectedVariantOutline(variant.id, boxRect);
            }

            GUILayout.Space(2f);
        }

        private Color GetGraphVariantColor(string variantId, Color baseColor)
        {
            if (string.IsNullOrEmpty(selectedGraphVariantId) ||
                string.IsNullOrEmpty(variantId) ||
                variantId == selectedGraphVariantId ||
                IsVariantConnectedToSelectedVariant(variantId))
            {
                return baseColor;
            }

            Color faded = baseColor;
            faded.a *= 0.3f;
            return faded;
        }

        private bool IsVariantConnectedToSelectedVariant(string variantId)
        {
            if (graphAsset == null || string.IsNullOrEmpty(selectedGraphVariantId))
            {
                return false;
            }

            for (int i = 0; i < graphAsset.connections.Count; i++)
            {
                PoseConnection connection = graphAsset.connections[i];
                if (connection == null || !IsForwardConnection(connection))
                {
                    continue;
                }

                if (connection.fromVariantId == selectedGraphVariantId && connection.toVariantId == variantId)
                {
                    return true;
                }

                if (connection.toVariantId == selectedGraphVariantId && connection.fromVariantId == variantId)
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawSelectedVariantOutline(string variantId, Rect rect)
        {
            if (Event.current.type != EventType.Repaint || selectedGraphVariantId != variantId)
            {
                return;
            }

            Color color = new Color(0.25f, 0.95f, 1f, 0.9f);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, 2f), color);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - 2f, rect.width, 2f), color);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, 2f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 2f, rect.yMin, 2f, rect.height), color);
        }

        private void SetVariantPose(PoseVariant variant, PoseAsset pose)
        {
            variant.pose = pose;
            if (!string.IsNullOrEmpty(variant.id))
            {
                suppressedPreviewVariantIds.Remove(variant.id);
            }

            variant.previewImage = pose != null
                ? PosePreviewCaptureUtility.LoadPreviewForPose(pose)
                : null;
            MarkDirty();
        }

        private void LoadPreviewImageForVariant(PoseVariant variant)
        {
            if (variant == null ||
                variant.previewImage != null ||
                variant.pose == null ||
                string.IsNullOrEmpty(variant.id) ||
                suppressedPreviewVariantIds.Contains(variant.id))
            {
                return;
            }

            Texture2D preview = PosePreviewCaptureUtility.LoadPreviewForPose(variant.pose);
            if (preview == null)
            {
                return;
            }

            variant.previewImage = preview;
            MarkDirty(false);
        }

        private void DrawVariantPreviewImage(PoseVariant variant)
        {
            Rect previewRect = GUILayoutUtility.GetRect(60f, 60f, GUILayout.Width(60f), GUILayout.Height(60f));
            GUI.DrawTexture(previewRect, variant.previewImage, ScaleMode.ScaleToFit);
            EditorGUIUtility.AddCursorRect(previewRect, MouseCursor.Arrow);

            bool isHovered = previewRect.Contains(Event.current.mousePosition);
            string variantId = variant.id ?? string.Empty;
            if (isHovered && hoveredPreviewVariantId != variantId)
            {
                hoveredPreviewVariantId = variantId;
                Repaint();
            }
            else if (!isHovered && hoveredPreviewVariantId == variantId)
            {
                hoveredPreviewVariantId = string.Empty;
                Repaint();
            }

            if (Event.current.type == EventType.MouseMove && isHovered)
            {
                Repaint();
            }

            if (!isHovered)
            {
                return;
            }

            Rect removeRect = new Rect(previewRect.xMax - 18f, previewRect.y + 2f, 16f, 16f);
            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.25f, 0.25f, 1f);
            if (GUI.Button(removeRect, "×", EditorStyles.miniButton))
            {
                if (!string.IsNullOrEmpty(variant.id))
                {
                    suppressedPreviewVariantIds.Add(variant.id);
                }

                variant.previewImage = null;
                MarkDirty(false);
            }

            GUI.backgroundColor = previousBackground;
        }

        private void DrawGraphConnectionWires()
        {
            if (Event.current.type != EventType.Repaint || graphAsset == null)
            {
                return;
            }

            Handles.BeginGUI();

            for (int connectionIndex = 0; connectionIndex < graphAsset.connections.Count; connectionIndex++)
            {
                PoseConnection conn = graphAsset.connections[connectionIndex];
                if (conn == null || !IsForwardConnection(conn))
                {
                    continue;
                }

                if (!IsGraphConnectionVisibleForSelectedVariant(conn))
                {
                    continue;
                }

                if (!TryGetVariantWireAnchors(conn.fromVariantId, conn.toVariantId, out Vector2 start, out Vector2 end))
                {
                    continue;
                }

                bool isSelected = selectedConnectionIndex == connectionIndex;
                if (isSelected)
                {
                    DrawGraphWire(start, end, new Color(0.25f, 0.95f, 1f, 0.85f), 6f, conn.enabled);
                }

                DrawGraphWire(start, end, GetGraphWireColor(conn.enabled), conn.enabled ? 2.5f : 2f, conn.enabled);
            }

            DrawGraphConnectionPorts();
            DrawPendingGraphLink();
            Handles.EndGUI();
        }

        private Color GetGraphWireColor(bool isConnected)
        {
            if (isConnected)
            {
                return new Color(0.2f, 0.8f, 0.32f, 0.95f);
            }

            return new Color(0.65f, 0.65f, 0.65f, 0.85f);
        }

        private void DrawGraphWire(Vector2 start, Vector2 end, Color color, float width, bool isConnected)
        {
            if (isConnected)
            {
                DrawGraphBezier(start, end, color, width);
                return;
            }

            DrawGraphDashedBezier(start, end, color, width);
        }

        private void DrawGraphBezier(Vector2 start, Vector2 end, Color color, float width)
        {
            float tangent = Mathf.Max(40f, Mathf.Abs(end.x - start.x) * 0.5f);
            Vector2 startTangent = start + Vector2.right * tangent;
            Vector2 endTangent = end + Vector2.left * tangent;
            Handles.DrawBezier(
                new Vector3(start.x, start.y, 0f),
                new Vector3(end.x, end.y, 0f),
                new Vector3(startTangent.x, startTangent.y, 0f),
                new Vector3(endTangent.x, endTangent.y, 0f),
                color,
                null,
                width);
        }

        private void DrawGraphDashedBezier(Vector2 start, Vector2 end, Color color, float width)
        {
            Color previousColor = Handles.color;
            Handles.color = color;

            float tangent = Mathf.Max(40f, Mathf.Abs(end.x - start.x) * 0.5f);
            Vector2 startTangent = start + Vector2.right * tangent;
            Vector2 endTangent = end + Vector2.left * tangent;

            const int Steps = 36;
            const float DashLength = 10f;
            const float GapLength = 7f;

            Vector2 previous = start;
            float distanceInPattern = 0f;
            bool drawingDash = true;

            for (int i = 1; i <= Steps; i++)
            {
                float t = i / (float)Steps;
                Vector2 current = EvaluateCubicBezier(start, startTangent, endTangent, end, t);
                Vector2 segmentStart = previous;
                float segmentLength = Vector2.Distance(previous, current);

                while (segmentLength > 0.001f)
                {
                    float patternLimit = drawingDash ? DashLength : GapLength;
                    float remainingPattern = patternLimit - distanceInPattern;
                    float drawLength = Mathf.Min(segmentLength, remainingPattern);
                    Vector2 segmentEnd = Vector2.Lerp(segmentStart, current, drawLength / segmentLength);

                    if (drawingDash)
                    {
                        Handles.DrawAAPolyLine(
                            width,
                            new Vector3(segmentStart.x, segmentStart.y, 0f),
                            new Vector3(segmentEnd.x, segmentEnd.y, 0f));
                    }

                    segmentStart = segmentEnd;
                    segmentLength -= drawLength;
                    distanceInPattern += drawLength;

                    if (distanceInPattern >= patternLimit - 0.001f)
                    {
                        distanceInPattern = 0f;
                        drawingDash = !drawingDash;
                    }
                }

                previous = current;
            }

            Handles.color = previousColor;
        }

        private void DrawGraphConnectionPorts()
        {
            for (int stageIndex = 0; stageIndex < graphAsset.stages.Count; stageIndex++)
            {
                PoseStage stage = graphAsset.stages[stageIndex];
                for (int variantIndex = 0; variantIndex < stage.variants.Count; variantIndex++)
                {
                    PoseVariant variant = stage.variants[variantIndex];
                    if (string.IsNullOrEmpty(variant.id) || !graphVariantRects.TryGetValue(variant.id, out Rect rect))
                    {
                        continue;
                    }

                    if (stageIndex > 0)
                    {
                        DrawGraphPort(new Vector2(rect.xMin, rect.center.y), false);
                    }

                    if (stageIndex < graphAsset.stages.Count - 1)
                    {
                        bool isPendingSource = variant.id == pendingLinkVariantId && stage.id == pendingLinkStageId;
                        DrawGraphPort(new Vector2(rect.xMax, rect.center.y), isPendingSource);
                    }
                }
            }
        }

        private void DrawGraphPort(Vector2 center, bool isPendingSource)
        {
            Color previousColor = Handles.color;
            Vector3 center3 = new Vector3(center.x, center.y, 0f);
            Handles.color = isPendingSource ? new Color(1f, 0.65f, 0.15f, 1f) : new Color(0.76f, 0.76f, 0.76f, 1f);
            Handles.DrawSolidDisc(center3, Vector3.forward, 4f);
            Handles.color = Color.white;
            Handles.DrawWireDisc(center3, Vector3.forward, isPendingSource ? 6f : 4f);
            Handles.color = previousColor;
        }

        private void DrawPendingGraphLink()
        {
            if (string.IsNullOrEmpty(pendingLinkVariantId) ||
                !graphVariantRects.TryGetValue(pendingLinkVariantId, out Rect sourceRect))
            {
                return;
            }

            Vector2 start = new Vector2(sourceRect.xMax, sourceRect.center.y);
            Vector2 end = Event.current.mousePosition;
            DrawGraphWire(start, end, new Color(1f, 0.65f, 0.15f, 0.8f), 2f, true);
        }

        private void HandleGraphConnectionWireInput()
        {
            Event evt = Event.current;
            if (evt.type != EventType.MouseDown || graphAsset == null)
            {
                return;
            }

            if (evt.button == 0 && TryHandleGraphPortClick(evt.mousePosition))
            {
                Repaint();
                evt.Use();
                return;
            }

            if (evt.button == 0 && TryFindGraphVariantHit(evt.mousePosition, out string variantId))
            {
                selectedGraphVariantId = variantId;
                selectedConnectionIndex = -1;
                pendingLinkStageId = null;
                pendingLinkVariantId = null;
                Repaint();
                return;
            }

            if (!TryFindGraphConnectionHit(evt.mousePosition, out GraphConnectionHit hit))
            {
                if (graphCanvasRect.Contains(evt.mousePosition) &&
                    (selectedConnectionIndex != -1 || !string.IsNullOrEmpty(selectedGraphVariantId)))
                {
                    selectedConnectionIndex = -1;
                    selectedGraphVariantId = null;
                    pendingLinkStageId = null;
                    pendingLinkVariantId = null;
                    Repaint();
                }
                else if (graphCanvasRect.Contains(evt.mousePosition) && !string.IsNullOrEmpty(pendingLinkVariantId))
                {
                    pendingLinkStageId = null;
                    pendingLinkVariantId = null;
                    Repaint();
                }

                return;
            }

            PoseConnection connection = FindConnection(hit.fromVariantId, hit.toVariantId);
            if (connection == null)
            {
                connection = CreateOrEnableConnection(hit.fromStageId, hit.fromVariantId, hit.toStageId, hit.toVariantId);
            }

            if (evt.button == 1)
            {
                connection.enabled = !connection.enabled;
                MarkDirty();
            }

            selectedConnectionIndex = graphAsset.connections.IndexOf(connection);
            Repaint();
            evt.Use();
        }

        private bool TryHandleGraphPortClick(Vector2 mousePosition)
        {
            if (!TryFindGraphPortHit(mousePosition, out GraphPortHit hit))
            {
                return false;
            }

            if (hit.isOutput)
            {
                bool isSameSource = pendingLinkStageId == hit.stageId && pendingLinkVariantId == hit.variantId;
                pendingLinkStageId = isSameSource ? null : hit.stageId;
                pendingLinkVariantId = isSameSource ? null : hit.variantId;
                selectedGraphVariantId = hit.variantId;
                selectedConnectionIndex = -1;
                return true;
            }

            if (string.IsNullOrEmpty(pendingLinkVariantId))
            {
                return false;
            }

            int fromStageIndex = graphAsset.GetStageIndex(pendingLinkStageId);
            if (hit.stageIndex <= fromStageIndex)
            {
                pendingLinkStageId = null;
                pendingLinkVariantId = null;
                return true;
            }

            PoseConnection connection = CreateOrEnableConnection(
                pendingLinkStageId,
                pendingLinkVariantId,
                hit.stageId,
                hit.variantId);
            selectedConnectionIndex = graphAsset.connections.IndexOf(connection);
            selectedGraphVariantId = hit.variantId;
            pendingLinkStageId = null;
            pendingLinkVariantId = null;
            return true;
        }

        private bool TryFindGraphPortHit(Vector2 mousePosition, out GraphPortHit hit)
        {
            hit = new GraphPortHit();
            const float PortHitRadius = 9f;

            for (int stageIndex = 0; stageIndex < graphAsset.stages.Count; stageIndex++)
            {
                PoseStage stage = graphAsset.stages[stageIndex];
                for (int variantIndex = 0; variantIndex < stage.variants.Count; variantIndex++)
                {
                    PoseVariant variant = stage.variants[variantIndex];
                    if (string.IsNullOrEmpty(variant.id) || !graphVariantRects.TryGetValue(variant.id, out Rect rect))
                    {
                        continue;
                    }

                    if (stageIndex < graphAsset.stages.Count - 1 &&
                        Vector2.Distance(mousePosition, new Vector2(rect.xMax, rect.center.y)) <= PortHitRadius)
                    {
                        hit = new GraphPortHit
                        {
                            isOutput = true,
                            stageId = stage.id,
                            variantId = variant.id,
                            stageIndex = stageIndex
                        };
                        return true;
                    }

                    if (stageIndex > 0 &&
                        Vector2.Distance(mousePosition, new Vector2(rect.xMin, rect.center.y)) <= PortHitRadius)
                    {
                        hit = new GraphPortHit
                        {
                            isOutput = false,
                            stageId = stage.id,
                            variantId = variant.id,
                            stageIndex = stageIndex
                        };
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryFindGraphConnectionHit(Vector2 mousePosition, out GraphConnectionHit hit)
        {
            hit = new GraphConnectionHit();
            float bestDistance = 12f;

            for (int connectionIndex = 0; connectionIndex < graphAsset.connections.Count; connectionIndex++)
            {
                PoseConnection connection = graphAsset.connections[connectionIndex];
                if (connection == null || !IsForwardConnection(connection))
                {
                    continue;
                }

                if (!IsGraphConnectionVisibleForSelectedVariant(connection))
                {
                    continue;
                }

                if (!TryGetVariantWireAnchors(connection.fromVariantId, connection.toVariantId, out Vector2 start, out Vector2 end))
                {
                    continue;
                }

                float distance = GetDistanceToGraphBezier(mousePosition, start, end);
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                hit = new GraphConnectionHit
                {
                    fromStageId = connection.fromStageId,
                    fromVariantId = connection.fromVariantId,
                    toStageId = connection.toStageId,
                    toVariantId = connection.toVariantId
                };
            }

            return !string.IsNullOrEmpty(hit.fromVariantId);
        }

        private bool TryFindGraphVariantHit(Vector2 mousePosition, out string variantId)
        {
            foreach (KeyValuePair<string, Rect> pair in graphVariantRects)
            {
                if (pair.Value.Contains(mousePosition))
                {
                    variantId = pair.Key;
                    return true;
                }
            }

            variantId = null;
            return false;
        }

        private bool IsGraphConnectionVisibleForSelectedVariant(PoseConnection connection)
        {
            string variantId = !string.IsNullOrEmpty(pendingLinkVariantId)
                ? pendingLinkVariantId
                : selectedGraphVariantId;

            if (string.IsNullOrEmpty(variantId))
            {
                return false;
            }

            return connection.fromVariantId == variantId || connection.toVariantId == variantId;
        }

        private bool TryGetVariantWireAnchors(string fromVariantId, string toVariantId, out Vector2 start, out Vector2 end)
        {
            start = Vector2.zero;
            end = Vector2.zero;

            if (string.IsNullOrEmpty(fromVariantId) || string.IsNullOrEmpty(toVariantId))
            {
                return false;
            }

            if (!graphVariantRects.TryGetValue(fromVariantId, out Rect fromRect) ||
                !graphVariantRects.TryGetValue(toVariantId, out Rect toRect))
            {
                return false;
            }

            start = new Vector2(fromRect.xMax, fromRect.center.y);
            end = new Vector2(toRect.xMin, toRect.center.y);
            return true;
        }

        private float GetDistanceToGraphBezier(Vector2 point, Vector2 start, Vector2 end)
        {
            float tangent = Mathf.Max(40f, Mathf.Abs(end.x - start.x) * 0.5f);
            Vector2 startTangent = start + Vector2.right * tangent;
            Vector2 endTangent = end + Vector2.left * tangent;

            float best = float.MaxValue;
            Vector2 previous = start;
            const int Steps = 24;
            for (int i = 1; i <= Steps; i++)
            {
                float t = i / (float)Steps;
                Vector2 current = EvaluateCubicBezier(start, startTangent, endTangent, end, t);
                best = Mathf.Min(best, DistanceToLineSegment(point, previous, current));
                previous = current;
            }

            return best;
        }

        private Vector2 EvaluateCubicBezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
        {
            float u = 1f - t;
            return (u * u * u * a) +
                (3f * u * u * t * b) +
                (3f * u * t * t * c) +
                (t * t * t * d);
        }

        private float DistanceToLineSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 segment = b - a;
            float lengthSq = segment.sqrMagnitude;
            if (lengthSq < 0.0001f)
            {
                return Vector2.Distance(point, a);
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - a, segment) / lengthSq);
            Vector2 projection = a + segment * t;
            return Vector2.Distance(point, projection);
        }

        private struct GraphConnectionHit
        {
            public string fromStageId;
            public string fromVariantId;
            public string toStageId;
            public string toVariantId;
        }

        private struct GraphPortHit
        {
            public bool isOutput;
            public string stageId;
            public string variantId;
            public int stageIndex;
        }
    }
}
