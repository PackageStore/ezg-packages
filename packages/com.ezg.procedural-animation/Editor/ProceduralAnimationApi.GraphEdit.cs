using System.Collections.Generic;
using System.Linq;
using Ezg.ProceduralAnimation;
using UnityEditor;

namespace Ezg.ProceduralAnimation.Editor
{
    /// <summary>
    /// Graph structure editing: remove/rename/reorder stages, variants and connections, plus
    /// post-hoc connection feel tuning. Mirrors the private InbetweenAnimationGeneratorWindow
    /// command semantics (last-stage guard, dangling-connection pruning, default-connection
    /// suppression keys) so headless edits interoperate cleanly with the GUI.
    /// </summary>
    public static partial class ProceduralAnimationApi
    {
        // ─────────────────────────────── Removal ───────────────────────────────

        /// <summary>Removes a stage (by id or name) plus every connection touching its variants. Mirrors the GUI's last-stage guard.</summary>
        public static string RemoveStage(string graphPath, string stageRef)
        {
            PoseCombinationGraphAsset graph = LoadGraph(graphPath, out string error);
            if (graph == null)
            {
                return error;
            }

            PoseStage stage = ResolveStage(graph, stageRef);
            if (stage == null)
            {
                return $"ERROR stage not found: {stageRef}";
            }

            if (graph.stages.Count <= 1)
            {
                return "ERROR cannot remove the last remaining stage";
            }

            List<string> variantIds = stage.variants
                .Where(v => !string.IsNullOrEmpty(v.id))
                .Select(v => v.id)
                .ToList();

            int prunedConnections = graph.connections.RemoveAll(c =>
                variantIds.Contains(c.fromVariantId) || variantIds.Contains(c.toVariantId));

            graph.stages.Remove(stage);
            Save(graph);
            return $"REMOVED stage '{stage.name}' ({variantIds.Count} variants, {prunedConnections} connections pruned)";
        }

        /// <summary>Removes a variant (by id or name) plus every connection touching it.</summary>
        public static string RemoveVariant(string graphPath, string variantRef)
        {
            PoseCombinationGraphAsset graph = LoadGraph(graphPath, out string error);
            if (graph == null)
            {
                return error;
            }

            PoseVariant variant = ResolveVariant(graph, variantRef);
            if (variant == null)
            {
                return $"ERROR variant not found: {variantRef}";
            }

            PoseStage owningStage = graph.stages.FirstOrDefault(s => s.variants.Contains(variant));
            int prunedConnections = graph.connections.RemoveAll(c =>
                c.fromVariantId == variant.id || c.toVariantId == variant.id);

            owningStage?.variants.Remove(variant);
            Save(graph);
            return $"REMOVED variant '{variant.name}' from stage '{owningStage?.name}' ({prunedConnections} connections pruned)";
        }

        /// <summary>
        /// Deletes a connection by id. For adjacent-stage connections a suppression key is recorded
        /// (mirroring the GUI delete) so the window's default-connection auto-spawn does not recreate it.
        /// </summary>
        public static string RemoveConnection(string graphPath, string connectionId)
        {
            PoseCombinationGraphAsset graph = LoadGraph(graphPath, out string error);
            if (graph == null)
            {
                return error;
            }

            PoseConnection connection = graph.FindConnectionById(connectionId);
            if (connection == null)
            {
                return $"ERROR connection not found: {connectionId}";
            }

            bool suppressed = SuppressDefaultConnectionKey(graph, connection);
            graph.connections.Remove(connection);
            Save(graph);
            return $"REMOVED connection {connectionId}" + (suppressed ? " (default-connection suppression key recorded)" : string.Empty);
        }

        // ─────────────────────────────── Reorder / rename ───────────────────────────────

        /// <summary>
        /// Moves a stage (by id or name) to a new index. Connections that become non-forward are
        /// pruned, exactly like the GUI's MoveStage. Check the report for the pruned count.
        /// </summary>
        public static string MoveStage(string graphPath, string stageRef, int newIndex)
        {
            PoseCombinationGraphAsset graph = LoadGraph(graphPath, out string error);
            if (graph == null)
            {
                return error;
            }

            PoseStage stage = ResolveStage(graph, stageRef);
            if (stage == null)
            {
                return $"ERROR stage not found: {stageRef}";
            }

            if (newIndex < 0 || newIndex >= graph.stages.Count)
            {
                return $"ERROR newIndex {newIndex} out of range 0..{graph.stages.Count - 1}";
            }

            int fromIndex = graph.stages.IndexOf(stage);
            if (fromIndex == newIndex)
            {
                return $"OK stage '{stage.name}' already at index {newIndex}";
            }

            graph.stages.RemoveAt(fromIndex);
            graph.stages.Insert(newIndex, stage);

            int pruned = graph.connections.RemoveAll(connection =>
            {
                int fromStageIndex = graph.GetStageIndex(connection.fromStageId);
                int toStageIndex = graph.GetStageIndex(connection.toStageId);
                return fromStageIndex < 0 || toStageIndex < 0 || toStageIndex <= fromStageIndex;
            });

            Save(graph);
            return $"MOVED stage '{stage.name}' {fromIndex} -> {newIndex} ({pruned} now-invalid connections pruned)";
        }

        /// <summary>Renames a stage. NOTE: stage names are display/addressing only — clip names come from VARIANT names.</summary>
        public static string RenameStage(string graphPath, string stageRef, string newName)
        {
            PoseCombinationGraphAsset graph = LoadGraph(graphPath, out string error);
            if (graph == null)
            {
                return error;
            }

            PoseStage stage = ResolveStage(graph, stageRef);
            if (stage == null)
            {
                return $"ERROR stage not found: {stageRef}";
            }

            string oldName = stage.name;
            stage.name = newName;
            Save(graph);
            return $"RENAMED stage '{oldName}' -> '{newName}'";
        }

        /// <summary>Renames a variant. WARNING: variant names feed the {path} clip-name token — future clips will be named differently, orphaning previously generated ones.</summary>
        public static string RenameVariant(string graphPath, string variantRef, string newName)
        {
            PoseCombinationGraphAsset graph = LoadGraph(graphPath, out string error);
            if (graph == null)
            {
                return error;
            }

            PoseVariant variant = ResolveVariant(graph, variantRef);
            if (variant == null)
            {
                return $"ERROR variant not found: {variantRef}";
            }

            string oldName = variant.name;
            variant.name = newName;
            Save(graph);
            return $"RENAMED variant '{oldName}' -> '{newName}' (generated clip names change accordingly)";
        }

        // ─────────────────────────────── Connection feel tuning ───────────────────────────────

        /// <summary>Edits feel preset and/or duration on an EXISTING connection (Connect only sets them at creation time).</summary>
        public static string SetConnectionFeel(string graphPath, string connectionId, string feelPresetPath = null, float durationSeconds = -1f)
        {
            PoseCombinationGraphAsset graph = LoadGraph(graphPath, out string error);
            if (graph == null)
            {
                return error;
            }

            PoseConnection connection = graph.FindConnectionById(connectionId);
            if (connection == null)
            {
                return $"ERROR connection not found: {connectionId}";
            }

            connection.segmentSettings ??= new InbetweenSegmentSettings();

            if (!string.IsNullOrEmpty(feelPresetPath))
            {
                FeelPresetAsset preset = AssetDatabase.LoadAssetAtPath<FeelPresetAsset>(feelPresetPath);
                if (preset == null)
                {
                    return $"ERROR feel preset not found: {feelPresetPath}";
                }

                connection.segmentSettings.feelPreset = preset;
            }

            if (durationSeconds > 0f)
            {
                connection.segmentSettings.duration = durationSeconds;
            }

            Save(graph);
            string feelName = connection.segmentSettings.feelPreset != null ? connection.segmentSettings.feelPreset.name : "<none>";
            return $"OK {connectionId} feel={feelName} duration={connection.segmentSettings.duration}";
        }

        /// <summary>
        /// Bulk-applies the graph's defaultFeelPreset and/or defaultDuration to EVERY connection.
        /// Mirrors the GUI's "Apply Feel/Duration To All" buttons, including their guards.
        /// </summary>
        public static string ApplyDefaultFeelToAllConnections(string graphPath, bool applyFeel = true, bool applyDuration = true)
        {
            PoseCombinationGraphAsset graph = LoadGraph(graphPath, out string error);
            if (graph == null)
            {
                return error;
            }

            if (!applyFeel && !applyDuration)
            {
                return "ERROR nothing to apply — set applyFeel and/or applyDuration";
            }

            if (applyFeel && graph.defaultFeelPreset == null)
            {
                return "ERROR graph has no defaultFeelPreset — call SetDefaultFeel first";
            }

            if (applyDuration && graph.defaultDuration <= 0f)
            {
                return "ERROR graph defaultDuration must be > 0 — call SetDefaultFeel first";
            }

            int updated = 0;
            foreach (PoseConnection connection in graph.connections)
            {
                connection.segmentSettings ??= new InbetweenSegmentSettings();
                if (applyFeel)
                {
                    connection.segmentSettings.feelPreset = graph.defaultFeelPreset;
                }

                if (applyDuration)
                {
                    connection.segmentSettings.duration = graph.defaultDuration;
                }

                updated++;
            }

            Save(graph);
            return $"APPLIED defaults to {updated} connections (feel={applyFeel}, duration={applyDuration})";
        }

        // ─────────────────────────────── Helpers ───────────────────────────────

        private static PoseStage ResolveStage(PoseCombinationGraphAsset graph, string stageRef)
        {
            return graph.FindStageById(stageRef)
                ?? graph.stages.FirstOrDefault(s => s.name == stageRef);
        }

        /// <summary>
        /// Records the GUI's default-connection suppression key for adjacent-stage connections so
        /// EnsureDefaultGraphConnections does not respawn the edge on next window repaint.
        /// Key format must stay in sync with InbetweenAnimationGeneratorWindow.GetDefaultConnectionKey.
        /// </summary>
        private static bool SuppressDefaultConnectionKey(PoseCombinationGraphAsset graph, PoseConnection connection)
        {
            int fromStageIndex = graph.GetStageIndex(connection.fromStageId);
            int toStageIndex = graph.GetStageIndex(connection.toStageId);
            if (fromStageIndex < 0 || toStageIndex != fromStageIndex + 1)
            {
                return false;
            }

            graph.suppressedDefaultConnectionKeys ??= new List<string>();
            string key = $"{connection.fromStageId}:{connection.fromVariantId}->{connection.toStageId}:{connection.toVariantId}";
            if (!graph.suppressedDefaultConnectionKeys.Contains(key))
            {
                graph.suppressedDefaultConnectionKeys.Add(key);
            }

            return true;
        }
    }
}
