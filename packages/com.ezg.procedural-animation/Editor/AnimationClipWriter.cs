using System.Collections.Generic;
using System.IO;
using Ezg.ProceduralAnimation;
using UnityEditor;
using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    public static class AnimationClipWriter
    {
        private const float MaxReasonableOvershoot = 0.5f;
        private const float MaxReasonableNoise = 10f;
        private const float PositionEpsilon = 0.00001f;
        private const float RotationEpsilon = 0.001f;
        private const float ScaleEpsilon = 0.00001f;
        private const float KeyTimeEpsilon = 0.0001f;

        private class BoneCurveSet
        {
            public readonly AnimationCurve posX = new AnimationCurve();
            public readonly AnimationCurve posY = new AnimationCurve();
            public readonly AnimationCurve posZ = new AnimationCurve();
            public readonly AnimationCurve rotX = new AnimationCurve();
            public readonly AnimationCurve rotY = new AnimationCurve();
            public readonly AnimationCurve rotZ = new AnimationCurve();
            public readonly AnimationCurve rotW = new AnimationCurve();
            public readonly AnimationCurve scaleX = new AnimationCurve();
            public readonly AnimationCurve scaleY = new AnimationCurve();
            public readonly AnimationCurve scaleZ = new AnimationCurve();
        }

        private class SegmentEaseContext
        {
            public InbetweenSegmentSettings segment;
            public float groupDuration;
            public float segmentStartInGroup;

            public bool SpansMultipleSegments
            {
                get { return groupDuration > segment.duration + KeyTimeEpsilon; }
            }

            public float SegmentStartNormalized
            {
                get { return groupDuration > 0f ? segmentStartInGroup / groupDuration : 0f; }
            }

            public float SegmentEndNormalized
            {
                get { return groupDuration > 0f ? (segmentStartInGroup + segment.duration) / groupDuration : 1f; }
            }
        }

        public static AnimationClip GenerateClip(InbetweenGenerationSettings settings, bool overwriteExisting)
        {
            ValidateSettings(settings);
            EnsureAssetFolder(settings.outputFolder);
            WarnAboutSettings(settings);

            AnimationClip clip = BuildClipData(settings);
            float totalDuration = GetTotalDuration(settings);

            string outputPath = BuildOutputPath(settings.outputFolder, settings.clipName);
            AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath);
            if (existing != null)
            {
                if (!overwriteExisting)
                {
                    throw new IOException($"Animation clip already exists: {outputPath}");
                }

                OverwriteClipInPlace(existing, clip);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                int boneCount = CountBoneCurves(existing);
                Debug.Log($"Overwrote in-between clip '{existing.name}' in place with {settings.segments.Count} segments, {boneCount} animated bones, duration {totalDuration:0.###}s at {outputPath}");
                return existing;
            }

            AssetDatabase.CreateAsset(clip, outputPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int newBoneCount = CountBoneCurves(clip);
            Debug.Log($"Generated in-between clip '{clip.name}' with {settings.segments.Count} segments, {newBoneCount} animated bones, duration {totalDuration:0.###}s at {outputPath}");
            return clip;
        }

        public static AnimationClip GenerateClipInMemory(InbetweenGenerationSettings settings)
        {
            ValidateForInMemory(settings);
            WarnAboutSettings(settings);

            return BuildClipData(settings);
        }

        private static void OverwriteClipInPlace(AnimationClip target, AnimationClip source)
        {
            ClearAnimationClipData(target);

            target.name = source.name;
            target.frameRate = source.frameRate;
            target.wrapMode = source.wrapMode;
            target.legacy = source.legacy;
            target.localBounds = source.localBounds;

            AnimationUtility.SetAnimationClipSettings(target, AnimationUtility.GetAnimationClipSettings(source));

            EditorCurveBinding[] floatBindings = AnimationUtility.GetCurveBindings(source);
            for (int i = 0; i < floatBindings.Length; i++)
            {
                AnimationUtility.SetEditorCurve(target, floatBindings[i], AnimationUtility.GetEditorCurve(source, floatBindings[i]));
            }

            EditorCurveBinding[] objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(source);
            for (int i = 0; i < objectBindings.Length; i++)
            {
                AnimationUtility.SetObjectReferenceCurve(target, objectBindings[i], AnimationUtility.GetObjectReferenceCurve(source, objectBindings[i]));
            }

            AnimationUtility.SetAnimationEvents(target, AnimationUtility.GetAnimationEvents(source));
            EditorUtility.SetDirty(target);
        }

        private static void ClearAnimationClipData(AnimationClip clip)
        {
            EditorCurveBinding[] floatBindings = AnimationUtility.GetCurveBindings(clip);
            for (int i = 0; i < floatBindings.Length; i++)
            {
                AnimationUtility.SetEditorCurve(clip, floatBindings[i], null);
            }

            EditorCurveBinding[] objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            for (int i = 0; i < objectBindings.Length; i++)
            {
                AnimationUtility.SetObjectReferenceCurve(clip, objectBindings[i], null);
            }

            AnimationUtility.SetAnimationEvents(clip, new AnimationEvent[0]);
            clip.ClearCurves();
        }

        private static AnimationClip BuildClipData(InbetweenGenerationSettings settings)
        {
            AnimationClip clip = new AnimationClip
            {
                name = settings.clipName,
                frameRate = settings.frameRate
            };

            Dictionary<string, BoneCurveSet> curvesByPath = new Dictionary<string, BoneCurveSet>();
            List<SegmentEaseContext> easeContexts = BuildSegmentEaseContexts(settings);
            float segmentStartTime = 0f;

            for (int segmentIndex = 0; segmentIndex < settings.segments.Count; segmentIndex++)
            {
                PoseAsset poseA = settings.poses[segmentIndex];
                PoseAsset poseB = settings.poses[segmentIndex + 1];
                InbetweenSegmentSettings segment = settings.segments[segmentIndex];
                SegmentEaseContext easeContext = easeContexts[segmentIndex];

                Dictionary<string, BonePoseData> poseADict = ToDictionary(poseA);
                Dictionary<string, BonePoseData> poseBDict = ToDictionary(poseB);
                List<string> matchingPaths = GetMatchingPaths(poseADict, poseBDict);

                if (matchingPaths.Count == 0)
                {
                    throw new System.InvalidOperationException($"Pose {segmentIndex + 1} and Pose {segmentIndex + 2} do not contain any matching bone paths.");
                }

                LogMissingBones(poseADict, poseBDict, segmentIndex);

                foreach (string bonePath in matchingPaths)
                {
                    BoneCurveSet curves = GetOrCreateCurves(curvesByPath, bonePath);
                    WriteSegmentKeys(curves, bonePath, poseADict[bonePath], poseBDict[bonePath], settings, easeContext, segmentStartTime);
                }

                segmentStartTime += segment.duration;
            }

            foreach (KeyValuePair<string, BoneCurveSet> pair in curvesByPath)
            {
                WriteCurvesToClip(clip, pair.Key, pair.Value, settings);
            }

            return clip;
        }

        private static List<SegmentEaseContext> BuildSegmentEaseContexts(InbetweenGenerationSettings settings)
        {
            List<SegmentEaseContext> contexts = new List<SegmentEaseContext>();
            List<float> segmentStartTimes = new List<float>();
            float total = 0f;

            for (int i = 0; i < settings.segments.Count; i++)
            {
                contexts.Add(null);
                segmentStartTimes.Add(total);
                total += settings.segments[i].duration;
            }

            int groupStart = 0;
            while (groupStart < settings.segments.Count)
            {
                FeelPresetAsset groupPreset = settings.segments[groupStart].feelPreset;
                int groupEnd = groupStart;

                while (groupEnd + 1 < settings.segments.Count
                    && settings.segments[groupEnd + 1].feelPreset == groupPreset)
                {
                    groupEnd++;
                }

                float groupStartTime = segmentStartTimes[groupStart];
                float groupEndTime = segmentStartTimes[groupEnd] + settings.segments[groupEnd].duration;
                float groupDuration = groupEndTime - groupStartTime;

                for (int i = groupStart; i <= groupEnd; i++)
                {
                    contexts[i] = new SegmentEaseContext
                    {
                        segment = settings.segments[i],
                        groupDuration = groupDuration,
                        segmentStartInGroup = segmentStartTimes[i] - groupStartTime
                    };
                }

                groupStart = groupEnd + 1;
            }

            return contexts;
        }

        private static float GetTotalDuration(InbetweenGenerationSettings settings)
        {
            float total = 0f;
            for (int i = 0; i < settings.segments.Count; i++)
            {
                total += settings.segments[i].duration;
            }

            return total;
        }

        private static int CountBoneCurves(AnimationClip clip)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            HashSet<string> paths = new HashSet<string>();
            for (int i = 0; i < bindings.Length; i++)
            {
                paths.Add(bindings[i].path);
            }

            return paths.Count;
        }

        public static string BuildOutputPath(string outputFolder, string clipName)
        {
            string safeName = MakeSafeFileName(clipName);
            return $"{outputFolder.TrimEnd('/')}/{safeName}.anim";
        }

        public static void EnsureAssetFolder(string folder)
        {
            if (!BonePathUtility.IsAssetFolderPath(folder))
            {
                throw new System.ArgumentException("Output folder must be inside the Assets folder.");
            }

            string normalized = folder.Replace("\\", "/").TrimEnd('/');
            if (AssetDatabase.IsValidFolder(normalized))
            {
                return;
            }

            string[] parts = normalized.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        public static void ValidateForInMemory(InbetweenGenerationSettings settings)
        {
            if (settings == null)
            {
                throw new System.ArgumentNullException(nameof(settings));
            }

            if (settings.poses == null || settings.poses.Count < 2)
            {
                throw new System.ArgumentException("At least two poses are required.");
            }

            for (int i = 0; i < settings.poses.Count; i++)
            {
                if (settings.poses[i] == null)
                {
                    throw new System.ArgumentException($"Pose {i + 1} is required.");
                }
            }

            if (settings.segments == null || settings.segments.Count != settings.poses.Count - 1)
            {
                throw new System.ArgumentException("Each adjacent pose pair requires one transition segment.");
            }

            for (int i = 0; i < settings.segments.Count; i++)
            {
                InbetweenSegmentSettings segment = settings.segments[i];
                if (segment == null)
                {
                    throw new System.ArgumentException($"Segment {i + 1} is required.");
                }

                if (segment.feelPreset == null)
                {
                    throw new System.ArgumentException($"Segment {i + 1} requires a Feel Preset.");
                }

                if (segment.duration <= 0f)
                {
                    throw new System.ArgumentException($"Segment {i + 1} duration must be greater than zero.");
                }
            }

            if (settings.frameRate <= 0)
            {
                throw new System.ArgumentException("Frame Rate must be greater than zero.");
            }

            if (string.IsNullOrWhiteSpace(settings.clipName))
            {
                throw new System.ArgumentException("Clip Name cannot be empty.");
            }

            if (!settings.generatePosition && !settings.generateRotation && !settings.generateScale)
            {
                throw new System.ArgumentException("Enable at least one transform channel.");
            }
        }

        private static void ValidateSettings(InbetweenGenerationSettings settings)
        {
            if (settings == null)
            {
                throw new System.ArgumentNullException(nameof(settings));
            }

            if (settings.poses == null || settings.poses.Count < 2)
            {
                throw new System.ArgumentException("At least two poses are required.");
            }

            for (int i = 0; i < settings.poses.Count; i++)
            {
                if (settings.poses[i] == null)
                {
                    throw new System.ArgumentException($"Pose {i + 1} is required.");
                }
            }

            if (settings.segments == null || settings.segments.Count != settings.poses.Count - 1)
            {
                throw new System.ArgumentException("Each adjacent pose pair requires one transition segment.");
            }

            for (int i = 0; i < settings.segments.Count; i++)
            {
                InbetweenSegmentSettings segment = settings.segments[i];
                if (segment == null)
                {
                    throw new System.ArgumentException($"Segment {i + 1} is required.");
                }

                if (segment.feelPreset == null)
                {
                    throw new System.ArgumentException($"Segment {i + 1} requires a Feel Preset.");
                }

                if (segment.duration <= 0f)
                {
                    throw new System.ArgumentException($"Segment {i + 1} duration must be greater than zero.");
                }
            }

            if (settings.frameRate <= 0)
            {
                throw new System.ArgumentException("Frame Rate must be greater than zero.");
            }

            if (string.IsNullOrWhiteSpace(settings.clipName))
            {
                throw new System.ArgumentException("Clip Name cannot be empty.");
            }

            if (!settings.generatePosition && !settings.generateRotation && !settings.generateScale)
            {
                throw new System.ArgumentException("Enable at least one transform channel.");
            }
        }

        private static void WarnAboutSettings(InbetweenGenerationSettings settings)
        {
            if (settings.generateScale)
            {
                Debug.LogWarning("Scale curve generation is enabled. This can produce unexpected results on rigs that rely on authored bind scales.");
            }

            for (int i = 0; i < settings.segments.Count; i++)
            {
                FeelPresetAsset feelPreset = settings.segments[i].feelPreset;
                if (feelPreset.overshootAmount > MaxReasonableOvershoot)
                {
                    Debug.LogWarning($"Segment {i + 1} overshoot amount {feelPreset.overshootAmount} is high and may create extreme transforms.");
                }

                if (feelPreset.noiseAmount > MaxReasonableNoise)
                {
                    Debug.LogWarning($"Segment {i + 1} noise amount {feelPreset.noiseAmount} is high and may create jittery rotations.");
                }
            }
        }

        private static Dictionary<string, BonePoseData> ToDictionary(PoseAsset pose)
        {
            Dictionary<string, BonePoseData> result = new Dictionary<string, BonePoseData>();

            foreach (BonePoseData bone in pose.bones)
            {
                if (bone == null)
                {
                    continue;
                }

                string path = bone.bonePath ?? string.Empty;
                if (!result.ContainsKey(path))
                {
                    result.Add(path, bone);
                }
            }

            return result;
        }

        private static List<string> GetMatchingPaths(Dictionary<string, BonePoseData> poseA, Dictionary<string, BonePoseData> poseB)
        {
            List<string> paths = new List<string>();
            foreach (string path in poseA.Keys)
            {
                if (poseB.ContainsKey(path))
                {
                    paths.Add(path);
                }
            }

            paths.Sort(System.StringComparer.Ordinal);
            return paths;
        }

        private static void LogMissingBones(Dictionary<string, BonePoseData> poseA, Dictionary<string, BonePoseData> poseB, int segmentIndex)
        {
            foreach (string path in poseA.Keys)
            {
                if (!poseB.ContainsKey(path))
                {
                    Debug.LogWarning($"Segment {segmentIndex + 1}: skipping bone '{DisplayPath(path)}' because it exists in Pose {segmentIndex + 1} but not Pose {segmentIndex + 2}.");
                }
            }

            foreach (string path in poseB.Keys)
            {
                if (!poseA.ContainsKey(path))
                {
                    Debug.LogWarning($"Segment {segmentIndex + 1}: skipping bone '{DisplayPath(path)}' because it exists in Pose {segmentIndex + 2} but not Pose {segmentIndex + 1}.");
                }
            }
        }

        private static BoneCurveSet GetOrCreateCurves(Dictionary<string, BoneCurveSet> curvesByPath, string bonePath)
        {
            if (!curvesByPath.TryGetValue(bonePath, out BoneCurveSet curves))
            {
                curves = new BoneCurveSet();
                curvesByPath.Add(bonePath, curves);
            }

            return curves;
        }

        private static void WriteSegmentKeys(
            BoneCurveSet curves,
            string bonePath,
            BonePoseData a,
            BonePoseData b,
            InbetweenGenerationSettings settings,
            SegmentEaseContext easeContext,
            float segmentStartTime)
        {
            InbetweenSegmentSettings segment = easeContext.segment;
            Quaternion startRotation = a.localRotation;
            Quaternion endRotation = AlignQuaternion(startRotation, b.localRotation);
            List<float> sampleTimes = BuildSparseSampleTimes(bonePath, easeContext);

            if (settings.generatePosition)
            {
                AddSparseScalarSegment(curves.posX, a.localPosition.x, b.localPosition.x, bonePath, easeContext, segmentStartTime, sampleTimes, PositionEpsilon);
                AddSparseScalarSegment(curves.posY, a.localPosition.y, b.localPosition.y, bonePath, easeContext, segmentStartTime, sampleTimes, PositionEpsilon);
                AddSparseScalarSegment(curves.posZ, a.localPosition.z, b.localPosition.z, bonePath, easeContext, segmentStartTime, sampleTimes, PositionEpsilon);
            }

            if (settings.generateRotation && Quaternion.Angle(startRotation, endRotation) > RotationEpsilon)
            {
                foreach (float normalized in sampleTimes)
                {
                    float time = segmentStartTime + normalized * segment.duration;
                    Quaternion rotation = EvaluateRotation(startRotation, endRotation, bonePath, settings, easeContext, time, normalized);
                    Quaternion inTangent = EstimateRotationTangent(startRotation, endRotation, bonePath, settings, easeContext, normalized, -1f);
                    Quaternion outTangent = EstimateRotationTangent(startRotation, endRotation, bonePath, settings, easeContext, normalized, 1f);

                    AddSparseKey(curves.rotX, time, rotation.x, inTangent.x, outTangent.x, segment.feelPreset.steppedMode);
                    AddSparseKey(curves.rotY, time, rotation.y, inTangent.y, outTangent.y, segment.feelPreset.steppedMode);
                    AddSparseKey(curves.rotZ, time, rotation.z, inTangent.z, outTangent.z, segment.feelPreset.steppedMode);
                    AddSparseKey(curves.rotW, time, rotation.w, inTangent.w, outTangent.w, segment.feelPreset.steppedMode);
                }
            }

            if (settings.generateScale)
            {
                AddSparseScalarSegment(curves.scaleX, a.localScale.x, b.localScale.x, bonePath, easeContext, segmentStartTime, sampleTimes, ScaleEpsilon);
                AddSparseScalarSegment(curves.scaleY, a.localScale.y, b.localScale.y, bonePath, easeContext, segmentStartTime, sampleTimes, ScaleEpsilon);
                AddSparseScalarSegment(curves.scaleZ, a.localScale.z, b.localScale.z, bonePath, easeContext, segmentStartTime, sampleTimes, ScaleEpsilon);
            }
        }

        private static List<float> BuildSparseSampleTimes(string bonePath, SegmentEaseContext easeContext)
        {
            InbetweenSegmentSettings segment = easeContext.segment;
            List<float> times = new List<float>();
            AddUniqueTime(times, 0f);
            AddUniqueTime(times, 1f);

            if (segment.feelPreset.steppedMode)
            {
                int stepCount = Mathf.Max(1, Mathf.CeilToInt(segment.feelPreset.steppedFrameRate * easeContext.groupDuration));
                for (int i = 1; i < stepCount; i++)
                {
                    AddGroupTimeIfInsideSegment(times, easeContext, i / (float)stepCount);
                }

                times.Sort();
                return times;
            }

            float delay = GetBoneDelay(bonePath, segment.feelPreset);
            if (delay > 0f && delay < 1f)
            {
                AddGroupTimeIfInsideSegment(times, easeContext, delay);
            }

            float curveMultiplier = GetCurveMultiplier(bonePath, segment.feelPreset);
            if (segment.feelPreset.mainCurve != null && curveMultiplier > 0f)
            {
                for (int i = 0; i < segment.feelPreset.mainCurve.length; i++)
                {
                    float adjusted = Mathf.Clamp01(segment.feelPreset.mainCurve.keys[i].time / curveMultiplier);
                    AddGroupTimeIfInsideSegment(times, easeContext, AdjustedToRawTime(adjusted, delay));
                }

                if (curveMultiplier > 1f)
                {
                    AddGroupTimeIfInsideSegment(times, easeContext, AdjustedToRawTime(1f / curveMultiplier, delay));
                }
            }

            if (segment.feelPreset.overshootAmount > 0f)
            {
                float start = Mathf.Clamp01(segment.feelPreset.overshootStartTime);
                AddGroupTimeIfInsideSegment(times, easeContext, AdjustedToRawTime(start, delay));
                AddGroupTimeIfInsideSegment(times, easeContext, AdjustedToRawTime(Mathf.Lerp(start, 1f, 0.5f), delay));
            }

            times.Sort();
            return times;
        }

        private static void AddGroupTimeIfInsideSegment(List<float> times, SegmentEaseContext easeContext, float groupNormalized)
        {
            float start = easeContext.SegmentStartNormalized;
            float end = easeContext.SegmentEndNormalized;
            if (groupNormalized < start - KeyTimeEpsilon || groupNormalized > end + KeyTimeEpsilon)
            {
                return;
            }

            AddUniqueTime(times, GroupTimeToSegmentTime(easeContext, groupNormalized));
        }

        private static void AddSparseScalarSegment(
            AnimationCurve curve,
            float startValue,
            float endValue,
            string bonePath,
            SegmentEaseContext easeContext,
            float segmentStartTime,
            List<float> sampleTimes,
            float epsilon)
        {
            InbetweenSegmentSettings segment = easeContext.segment;
            if (Mathf.Abs(startValue - endValue) <= epsilon)
            {
                return;
            }

            foreach (float normalized in sampleTimes)
            {
                float time = segmentStartTime + normalized * segment.duration;
                float value = EvaluateScalar(startValue, endValue, bonePath, easeContext, normalized);
                float inTangent = EstimateScalarTangent(startValue, endValue, bonePath, easeContext, normalized, -1f);
                float outTangent = EstimateScalarTangent(startValue, endValue, bonePath, easeContext, normalized, 1f);
                AddSparseKey(curve, time, value, inTangent, outTangent, segment.feelPreset.steppedMode);
            }
        }

        private static float EvaluateScalar(float startValue, float endValue, string bonePath, SegmentEaseContext easeContext, float normalized)
        {
            float eased = EvaluateEasedTime(normalized, bonePath, easeContext);
            return Mathf.LerpUnclamped(startValue, endValue, eased);
        }

        private static Quaternion EvaluateRotation(
            Quaternion startRotation,
            Quaternion endRotation,
            string bonePath,
            InbetweenGenerationSettings settings,
            SegmentEaseContext easeContext,
            float time,
            float normalized)
        {
            InbetweenSegmentSettings segment = easeContext.segment;
            float eased = EvaluateEasedTime(normalized, bonePath, easeContext);
            Quaternion rotation = Quaternion.SlerpUnclamped(startRotation, endRotation, eased);

            if (normalized > 0f && normalized < 1f)
            {
                int noiseFrame = Mathf.RoundToInt(time * settings.frameRate);
                rotation = ApplyRotationNoise(rotation, segment.feelPreset, bonePath, noiseFrame);
            }

            return rotation;
        }

        private static float EvaluateEasedTime(float normalized, string bonePath, SegmentEaseContext easeContext)
        {
            InbetweenSegmentSettings segment = easeContext.segment;
            if (!easeContext.SpansMultipleSegments)
            {
                return EvaluateFeelAtGroupTime(normalized, bonePath, segment, segment.duration);
            }

            float groupNormalized = SegmentTimeToGroupTime(easeContext, normalized);
            float segmentStart = easeContext.SegmentStartNormalized;
            float segmentEnd = easeContext.SegmentEndNormalized;
            float startEased = EvaluateFeelAtGroupTime(segmentStart, bonePath, segment, easeContext.groupDuration);
            float endEased = EvaluateFeelAtGroupTime(segmentEnd, bonePath, segment, easeContext.groupDuration);
            float eased = EvaluateFeelAtGroupTime(groupNormalized, bonePath, segment, easeContext.groupDuration);

            return InverseLerpUnclamped(startEased, endEased, eased);
        }

        private static float EvaluateFeelAtGroupTime(float normalized, string bonePath, InbetweenSegmentSettings segment, float duration)
        {
            float adjusted = ApplyBoneDelay(normalized, bonePath, segment.feelPreset);
            adjusted = ApplySteppedMode(adjusted, segment.feelPreset, duration);

            float curveMultiplier = GetCurveMultiplier(bonePath, segment.feelPreset);
            float eased = segment.feelPreset.mainCurve != null
                ? segment.feelPreset.mainCurve.Evaluate(Mathf.Clamp01(adjusted * curveMultiplier))
                : adjusted;

            return ApplyOvershoot(eased, adjusted, segment.feelPreset);
        }

        private static float EstimateScalarTangent(float startValue, float endValue, string bonePath, SegmentEaseContext easeContext, float normalized, float direction)
        {
            InbetweenSegmentSettings segment = easeContext.segment;
            if (segment.feelPreset.steppedMode)
            {
                return 0f;
            }

            float sample = Mathf.Clamp01(normalized + direction * 0.001f);
            if (Mathf.Approximately(sample, normalized))
            {
                sample = Mathf.Clamp01(normalized - direction * 0.001f);
            }

            float value = EvaluateScalar(startValue, endValue, bonePath, easeContext, normalized);
            float neighbor = EvaluateScalar(startValue, endValue, bonePath, easeContext, sample);
            float timeDelta = (sample - normalized) * segment.duration;
            return Mathf.Abs(timeDelta) > Mathf.Epsilon ? (neighbor - value) / timeDelta : 0f;
        }

        private static Quaternion EstimateRotationTangent(
            Quaternion startRotation,
            Quaternion endRotation,
            string bonePath,
            InbetweenGenerationSettings settings,
            SegmentEaseContext easeContext,
            float normalized,
            float direction)
        {
            InbetweenSegmentSettings segment = easeContext.segment;
            if (segment.feelPreset.steppedMode)
            {
                return new Quaternion(0f, 0f, 0f, 0f);
            }

            float sample = Mathf.Clamp01(normalized + direction * 0.001f);
            if (Mathf.Approximately(sample, normalized))
            {
                sample = Mathf.Clamp01(normalized - direction * 0.001f);
            }

            float time = normalized * segment.duration;
            float sampleTime = sample * segment.duration;
            Quaternion value = EvaluateRotation(startRotation, endRotation, bonePath, settings, easeContext, time, normalized);
            Quaternion neighbor = EvaluateRotation(startRotation, endRotation, bonePath, settings, easeContext, sampleTime, sample);
            float timeDelta = (sample - normalized) * segment.duration;

            if (Mathf.Abs(timeDelta) <= Mathf.Epsilon)
            {
                return new Quaternion(0f, 0f, 0f, 0f);
            }

            return new Quaternion(
                (neighbor.x - value.x) / timeDelta,
                (neighbor.y - value.y) / timeDelta,
                (neighbor.z - value.z) / timeDelta,
                (neighbor.w - value.w) / timeDelta);
        }

        private static float SegmentTimeToGroupTime(SegmentEaseContext easeContext, float segmentNormalized)
        {
            return Mathf.Clamp01((easeContext.segmentStartInGroup + segmentNormalized * easeContext.segment.duration) / easeContext.groupDuration);
        }

        private static float GroupTimeToSegmentTime(SegmentEaseContext easeContext, float groupNormalized)
        {
            float segmentDurationInGroup = easeContext.SegmentEndNormalized - easeContext.SegmentStartNormalized;
            if (segmentDurationInGroup <= Mathf.Epsilon)
            {
                return 0f;
            }

            return Mathf.Clamp01((groupNormalized - easeContext.SegmentStartNormalized) / segmentDurationInGroup);
        }

        private static float InverseLerpUnclamped(float a, float b, float value)
        {
            if (Mathf.Abs(b - a) <= Mathf.Epsilon)
            {
                return 0f;
            }

            return (value - a) / (b - a);
        }

        private static void AddSparseKey(AnimationCurve curve, float time, float value, float inTangent, float outTangent, bool constant)
        {
            int keyIndex = FindKeyIndex(curve, time);
            if (keyIndex >= 0)
            {
                Keyframe existing = curve.keys[keyIndex];
                existing.value = value;
                existing.outTangent = outTangent;
                curve.MoveKey(keyIndex, existing);
                ApplyTangentMode(curve, keyIndex, constant);
                return;
            }

            Keyframe key = new Keyframe(time, value, inTangent, outTangent);
            keyIndex = curve.AddKey(key);
            ApplyTangentMode(curve, keyIndex, constant);
        }

        private static int FindKeyIndex(AnimationCurve curve, float time)
        {
            for (int i = 0; i < curve.length; i++)
            {
                if (Mathf.Abs(curve.keys[i].time - time) <= KeyTimeEpsilon)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void ApplyTangentMode(AnimationCurve curve, int keyIndex, bool constant)
        {
            AnimationUtility.TangentMode mode = constant ? AnimationUtility.TangentMode.Constant : AnimationUtility.TangentMode.Free;
            AnimationUtility.SetKeyLeftTangentMode(curve, keyIndex, mode);
            AnimationUtility.SetKeyRightTangentMode(curve, keyIndex, mode);
        }

        private static void AddUniqueTime(List<float> times, float time)
        {
            time = Mathf.Clamp01(time);
            for (int i = 0; i < times.Count; i++)
            {
                if (Mathf.Abs(times[i] - time) <= KeyTimeEpsilon)
                {
                    return;
                }
            }

            times.Add(time);
        }

        private static Quaternion AlignQuaternion(Quaternion reference, Quaternion value)
        {
            if (Quaternion.Dot(reference, value) >= 0f)
            {
                return value;
            }

            return new Quaternion(-value.x, -value.y, -value.z, -value.w);
        }

        private static void WriteCurvesToClip(AnimationClip clip, string bonePath, BoneCurveSet curves, InbetweenGenerationSettings settings)
        {
            if (settings.generatePosition && curves.posX.length > 0)
            {
                SetCurve(clip, bonePath, "m_LocalPosition.x", curves.posX);
                SetCurve(clip, bonePath, "m_LocalPosition.y", curves.posY);
                SetCurve(clip, bonePath, "m_LocalPosition.z", curves.posZ);
            }

            if (settings.generateRotation && curves.rotX.length > 0)
            {
                SetCurve(clip, bonePath, "m_LocalRotation.x", curves.rotX);
                SetCurve(clip, bonePath, "m_LocalRotation.y", curves.rotY);
                SetCurve(clip, bonePath, "m_LocalRotation.z", curves.rotZ);
                SetCurve(clip, bonePath, "m_LocalRotation.w", curves.rotW);
            }

            if (settings.generateScale && curves.scaleX.length > 0)
            {
                SetCurve(clip, bonePath, "m_LocalScale.x", curves.scaleX);
                SetCurve(clip, bonePath, "m_LocalScale.y", curves.scaleY);
                SetCurve(clip, bonePath, "m_LocalScale.z", curves.scaleZ);
            }
        }

        private static float ApplyBoneDelay(float t, string bonePath, FeelPresetAsset feelPreset)
        {
            float delay = GetBoneDelay(bonePath, feelPreset);

            if (delay <= 0f)
            {
                return Mathf.Clamp01(t);
            }

            return Mathf.Clamp01(Mathf.InverseLerp(delay, 1f, t));
        }

        private static float GetBoneDelay(string bonePath, FeelPresetAsset feelPreset)
        {
            float delay = feelPreset.globalBoneDelay;
            delay += BonePathUtility.GetDepth(bonePath) * feelPreset.childDepthDelay;

            foreach (BoneTimingRule rule in feelPreset.boneTimingRules)
            {
                if (rule == null || string.IsNullOrEmpty(rule.pathContains))
                {
                    continue;
                }

                if (bonePath.IndexOf(rule.pathContains, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    delay += rule.delay;
                }
            }

            return Mathf.Clamp(delay, 0f, 0.999f);
        }

        private static float AdjustedToRawTime(float adjusted, float delay)
        {
            return Mathf.Lerp(delay, 1f, Mathf.Clamp01(adjusted));
        }

        private static float ApplySteppedMode(float t, FeelPresetAsset feelPreset, float duration)
        {
            if (!feelPreset.steppedMode)
            {
                return t;
            }

            float stepCount = Mathf.Max(1f, feelPreset.steppedFrameRate * duration);
            return Mathf.Floor(t * stepCount) / stepCount;
        }

        private static float ApplyOvershoot(float easedT, float adjustedT, FeelPresetAsset feelPreset)
        {
            if (feelPreset.overshootAmount <= 0f || adjustedT < feelPreset.overshootStartTime)
            {
                return easedT;
            }

            float overshootT = Mathf.InverseLerp(feelPreset.overshootStartTime, 1f, adjustedT);
            float wave = Mathf.Sin(overshootT * Mathf.PI);
            return easedT + wave * feelPreset.overshootAmount;
        }

        private static float GetCurveMultiplier(string bonePath, FeelPresetAsset feelPreset)
        {
            float multiplier = 1f;
            foreach (BoneTimingRule rule in feelPreset.boneTimingRules)
            {
                if (rule == null || string.IsNullOrEmpty(rule.pathContains))
                {
                    continue;
                }

                if (bonePath.IndexOf(rule.pathContains, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    multiplier *= Mathf.Max(0f, rule.curveMultiplier);
                }
            }

            return multiplier;
        }

        private static Quaternion ApplyRotationNoise(Quaternion rotation, FeelPresetAsset feelPreset, string bonePath, int frame)
        {
            if (feelPreset.noiseAmount <= 0f || string.IsNullOrEmpty(bonePath))
            {
                return rotation;
            }

            if (bonePath.IndexOf("hips", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return rotation;
            }

            float x = DeterministicNoise(feelPreset.noiseSeed, bonePath, frame, 0) * feelPreset.noiseAmount;
            float y = DeterministicNoise(feelPreset.noiseSeed, bonePath, frame, 1) * feelPreset.noiseAmount;
            float z = DeterministicNoise(feelPreset.noiseSeed, bonePath, frame, 2) * feelPreset.noiseAmount;
            return rotation * Quaternion.Euler(x, y, z);
        }

        private static float DeterministicNoise(int seed, string bonePath, int frame, int axis)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)seed) * 16777619u;
                hash = (hash ^ (uint)frame) * 16777619u;
                hash = (hash ^ (uint)axis) * 16777619u;

                for (int i = 0; i < bonePath.Length; i++)
                {
                    hash = (hash ^ bonePath[i]) * 16777619u;
                }

                return (hash / (float)uint.MaxValue) * 2f - 1f;
            }
        }

        private static void SetCurve(AnimationClip clip, string path, string propertyName, AnimationCurve curve)
        {
            EditorCurveBinding binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), propertyName);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }

            return name.Trim();
        }

        private static string DisplayPath(string path)
        {
            return string.IsNullOrEmpty(path) ? "<root>" : path;
        }
    }
}
