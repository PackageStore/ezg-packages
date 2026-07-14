using System.Text;
using Ezg.ProceduralAnimation;
using UnityEditor;
using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    /// <summary>
    /// Feel-preset tuning: the factory eases (CreateDefaultFeelPresets) ship with all feel
    /// features zeroed — overshoot, bone-delay waves, stepped mode, noise and per-bone timing
    /// rules are only reachable headlessly through these methods.
    /// </summary>
    public static partial class ProceduralAnimationApi
    {
        /// <summary>
        /// Sets feel scalar fields on an existing preset. Sentinel values mean "leave unchanged":
        /// any negative float, steppedMode -1 (0 = off, 1 = on), steppedFrameRate &lt;= 0, noiseSeed int.MinValue.
        /// </summary>
        public static string ConfigureFeelPreset(
            string feelPresetPath,
            float overshootAmount = -1f,
            float overshootStartTime = -1f,
            float globalBoneDelay = -1f,
            float childDepthDelay = -1f,
            int steppedMode = -1,
            int steppedFrameRate = -1,
            float noiseAmount = -1f,
            int noiseSeed = int.MinValue)
        {
            FeelPresetAsset preset = LoadFeelPreset(feelPresetPath, out string error);
            if (preset == null)
            {
                return error;
            }

            if (overshootAmount >= 0f) preset.overshootAmount = overshootAmount;
            if (overshootStartTime >= 0f) preset.overshootStartTime = Mathf.Clamp01(overshootStartTime);
            if (globalBoneDelay >= 0f) preset.globalBoneDelay = globalBoneDelay;
            if (childDepthDelay >= 0f) preset.childDepthDelay = childDepthDelay;
            if (steppedMode >= 0) preset.steppedMode = steppedMode != 0;
            if (steppedFrameRate > 0) preset.steppedFrameRate = steppedFrameRate;
            if (noiseAmount >= 0f) preset.noiseAmount = noiseAmount;
            if (noiseSeed != int.MinValue) preset.noiseSeed = noiseSeed;

            SaveAsset(preset);
            return DescribeFeelPreset(feelPresetPath);
        }

        /// <summary>Dumps every feel field plus timing rules so a driver can verify edits.</summary>
        public static string DescribeFeelPreset(string feelPresetPath)
        {
            FeelPresetAsset preset = LoadFeelPreset(feelPresetPath, out string error);
            if (preset == null)
            {
                return error;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"PRESET {preset.name}");
            sb.AppendLine($"  mainCurve keys={preset.mainCurve?.keys.Length ?? 0}");
            sb.AppendLine($"  overshootAmount={preset.overshootAmount} overshootStartTime={preset.overshootStartTime}");
            sb.AppendLine($"  globalBoneDelay={preset.globalBoneDelay} childDepthDelay={preset.childDepthDelay}");
            sb.AppendLine($"  steppedMode={preset.steppedMode} steppedFrameRate={preset.steppedFrameRate}");
            sb.AppendLine($"  noiseAmount={preset.noiseAmount} noiseSeed={preset.noiseSeed}");
            sb.AppendLine($"  boneTimingRules={preset.boneTimingRules.Count}");
            foreach (BoneTimingRule rule in preset.boneTimingRules)
            {
                sb.AppendLine($"    RULE pathContains='{rule.pathContains}' delay={rule.delay} curveMultiplier={rule.curveMultiplier}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Adds a per-bone timing rule. Matching is case-insensitive SUBSTRING — broad tokens like
        /// "arm" also match "forearm"/"armature". Multiple matching rules add delays and multiply curve multipliers.
        /// </summary>
        public static string AddBoneTimingRule(string feelPresetPath, string pathContains, float delay, float curveMultiplier = 1f)
        {
            if (string.IsNullOrWhiteSpace(pathContains))
            {
                return "ERROR pathContains must be a non-empty substring";
            }

            FeelPresetAsset preset = LoadFeelPreset(feelPresetPath, out string error);
            if (preset == null)
            {
                return error;
            }

            preset.boneTimingRules.Add(new BoneTimingRule
            {
                pathContains = pathContains,
                delay = delay,
                curveMultiplier = curveMultiplier
            });

            SaveAsset(preset);
            return $"ADDED rule #{preset.boneTimingRules.Count - 1} pathContains='{pathContains}' delay={delay} curveMultiplier={curveMultiplier}";
        }

        /// <summary>Removes all per-bone timing rules from a preset.</summary>
        public static string ClearBoneTimingRules(string feelPresetPath)
        {
            FeelPresetAsset preset = LoadFeelPreset(feelPresetPath, out string error);
            if (preset == null)
            {
                return error;
            }

            int removed = preset.boneTimingRules.Count;
            preset.boneTimingRules.Clear();
            SaveAsset(preset);
            return $"CLEARED {removed} timing rules from {preset.name}";
        }

        /// <summary>
        /// Replaces the mainCurve with a MINIMAL hand-authored curve. keysCsv entries are
        /// "time:value" or "time:value:inSlope:outSlope" separated by ';'
        /// (e.g. "0:0:13.8:13.8;0.13:0.83:0.83:0.83;1:1:0:0" — the ready_to_attack shape).
        /// Prefer this over CopyFeelCurve from dotween_default: factory eases are 81-key
        /// sampled approximations that produce noisy, heavy curves; 2-4 explicit keys
        /// generate the clean clips the legacy weapon set is built from.
        /// </summary>
        public static string SetFeelCurveKeys(string feelPresetPath, string keysCsv)
        {
            FeelPresetAsset preset = LoadFeelPreset(feelPresetPath, out string error);
            if (preset == null)
            {
                return error;
            }

            string[] entries = keysCsv.Split(';');
            if (entries.Length < 2)
            {
                return "ERROR need at least 2 keys (start and end)";
            }

            var keys = new Keyframe[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                string[] parts = entries[i].Split(':');
                if (parts.Length != 2 && parts.Length != 4)
                {
                    return $"ERROR key '{entries[i]}' must be time:value or time:value:inSlope:outSlope";
                }

                float t = float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                float v = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                float inSlope = parts.Length == 4 ? float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) : 0f;
                float outSlope = parts.Length == 4 ? float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture) : 0f;
                keys[i] = new Keyframe(t, v, inSlope, outSlope);
            }

            preset.mainCurve = new AnimationCurve(keys);
            SaveAsset(preset);
            return $"OK {preset.name} mainCurve set to {keys.Length} keys";
        }

        /// <summary>
        /// Copies the mainCurve from one preset into another. NOTE: avoid copying from
        /// dotween_default (81-key sampled eases) — use SetFeelCurveKeys with 2-4 keys instead;
        /// copying is appropriate between hand-tuned presets only.
        /// </summary>
        public static string CopyFeelCurve(string fromPresetPath, string toPresetPath)
        {
            FeelPresetAsset from = LoadFeelPreset(fromPresetPath, out string fromError);
            if (from == null)
            {
                return fromError;
            }

            FeelPresetAsset to = LoadFeelPreset(toPresetPath, out string toError);
            if (to == null)
            {
                return toError;
            }

            to.mainCurve = new AnimationCurve(from.mainCurve.keys)
            {
                preWrapMode = from.mainCurve.preWrapMode,
                postWrapMode = from.mainCurve.postWrapMode
            };

            SaveAsset(to);
            return $"COPIED mainCurve ({from.mainCurve.keys.Length} keys) {from.name} -> {to.name}";
        }

        // ─────────────────────────────── Helpers ───────────────────────────────

        private static FeelPresetAsset LoadFeelPreset(string feelPresetPath, out string error)
        {
            FeelPresetAsset preset = AssetDatabase.LoadAssetAtPath<FeelPresetAsset>(feelPresetPath);
            error = preset == null ? $"ERROR feel preset not found: {feelPresetPath}" : null;
            return preset;
        }

        private static void SaveAsset(Object asset)
        {
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }
    }
}
