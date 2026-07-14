using Ezg.ProceduralAnimation;
using UnityEditor;
using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    public static class DefaultFeelPresetFactory
    {
        public const string DefaultPresetFolder = "Assets/ProceduralAnimation/Presets";
        private const int EaseCurveSampleCount = 80;
        private const float BackOvershoot = 1.70158f;
        private const float FlashCount = 3f;

        private delegate float EaseEvaluator(float t);

        private struct EasePresetDefinition
        {
            public readonly string name;
            public readonly EaseEvaluator evaluator;

            public EasePresetDefinition(string name, EaseEvaluator evaluator)
            {
                this.name = name;
                this.evaluator = evaluator;
            }
        }

        public static void CreateDefaultPresets(string folder = DefaultPresetFolder)
        {
            AnimationClipWriter.EnsureAssetFolder(folder);

            foreach (EasePresetDefinition preset in GetDotweenEasePresets())
            {
                CreatePreset(folder, preset.name, SampleEaseCurve(preset.evaluator));
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Created DOTween-style procedural animation feel presets in {folder}");
        }

        private static void CreatePreset(
            string folder,
            string name,
            AnimationCurve curve)
        {
            string path = $"{folder}/{name}.asset";
            FeelPresetAsset preset = AssetDatabase.LoadAssetAtPath<FeelPresetAsset>(path);
            if (preset == null)
            {
                preset = ScriptableObject.CreateInstance<FeelPresetAsset>();
                AssetDatabase.CreateAsset(preset, path);
            }

            preset.mainCurve = curve;
            preset.overshootAmount = 0f;
            preset.overshootStartTime = 0.75f;
            preset.globalBoneDelay = 0f;
            preset.childDepthDelay = 0f;
            preset.steppedMode = false;
            preset.steppedFrameRate = 12;
            preset.noiseAmount = 0f;
            preset.noiseSeed = 12345;
            preset.boneTimingRules.Clear();

            EditorUtility.SetDirty(preset);
        }

        private static EasePresetDefinition[] GetDotweenEasePresets()
        {
            return new[]
            {
                new EasePresetDefinition("easeLinear", EaseLinear),
                new EasePresetDefinition("easeInSine", EaseInSine),
                new EasePresetDefinition("easeOutSine", EaseOutSine),
                new EasePresetDefinition("easeInOutSine", EaseInOutSine),
                new EasePresetDefinition("easeInQuad", EaseInQuad),
                new EasePresetDefinition("easeOutQuad", EaseOutQuad),
                new EasePresetDefinition("easeInOutQuad", EaseInOutQuad),
                new EasePresetDefinition("easeInCubic", EaseInCubic),
                new EasePresetDefinition("easeOutCubic", EaseOutCubic),
                new EasePresetDefinition("easeInOutCubic", EaseInOutCubic),
                new EasePresetDefinition("easeInQuart", EaseInQuart),
                new EasePresetDefinition("easeOutQuart", EaseOutQuart),
                new EasePresetDefinition("easeInOutQuart", EaseInOutQuart),
                new EasePresetDefinition("easeInQuint", EaseInQuint),
                new EasePresetDefinition("easeOutQuint", EaseOutQuint),
                new EasePresetDefinition("easeInOutQuint", EaseInOutQuint),
                new EasePresetDefinition("easeInExpo", EaseInExpo),
                new EasePresetDefinition("easeOutExpo", EaseOutExpo),
                new EasePresetDefinition("easeInOutExpo", EaseInOutExpo),
                new EasePresetDefinition("easeInCirc", EaseInCirc),
                new EasePresetDefinition("easeOutCirc", EaseOutCirc),
                new EasePresetDefinition("easeInOutCirc", EaseInOutCirc),
                new EasePresetDefinition("easeInElastic", EaseInElastic),
                new EasePresetDefinition("easeOutElastic", EaseOutElastic),
                new EasePresetDefinition("easeInOutElastic", EaseInOutElastic),
                new EasePresetDefinition("easeInBack", EaseInBack),
                new EasePresetDefinition("easeOutBack", EaseOutBack),
                new EasePresetDefinition("easeInOutBack", EaseInOutBack),
                new EasePresetDefinition("easeInBounce", EaseInBounce),
                new EasePresetDefinition("easeOutBounce", EaseOutBounce),
                new EasePresetDefinition("easeInOutBounce", EaseInOutBounce),
                new EasePresetDefinition("easeFlash", EaseFlash),
                new EasePresetDefinition("easeInFlash", EaseInFlash),
                new EasePresetDefinition("easeOutFlash", EaseOutFlash),
                new EasePresetDefinition("easeInOutFlash", EaseInOutFlash)
            };
        }

        private static AnimationCurve SampleEaseCurve(EaseEvaluator evaluator)
        {
            Keyframe[] keys = new Keyframe[EaseCurveSampleCount + 1];
            for (int i = 0; i <= EaseCurveSampleCount; i++)
            {
                float t = i / (float)EaseCurveSampleCount;
                keys[i] = new Keyframe(t, evaluator(t));
            }

            AnimationCurve curve = new AnimationCurve(keys);
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
            }

            return curve;
        }

        private static float EaseLinear(float t)
        {
            return t;
        }

        private static float EaseInSine(float t)
        {
            return 1f - Mathf.Cos(t * Mathf.PI * 0.5f);
        }

        private static float EaseOutSine(float t)
        {
            return Mathf.Sin(t * Mathf.PI * 0.5f);
        }

        private static float EaseInOutSine(float t)
        {
            return -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;
        }

        private static float EaseInQuad(float t)
        {
            return t * t;
        }

        private static float EaseOutQuad(float t)
        {
            return 1f - (1f - t) * (1f - t);
        }

        private static float EaseInOutQuad(float t)
        {
            return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;
        }

        private static float EaseInCubic(float t)
        {
            return t * t * t;
        }

        private static float EaseOutCubic(float t)
        {
            return 1f - Mathf.Pow(1f - t, 3f);
        }

        private static float EaseInOutCubic(float t)
        {
            return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
        }

        private static float EaseInQuart(float t)
        {
            return t * t * t * t;
        }

        private static float EaseOutQuart(float t)
        {
            return 1f - Mathf.Pow(1f - t, 4f);
        }

        private static float EaseInOutQuart(float t)
        {
            return t < 0.5f ? 8f * t * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 4f) * 0.5f;
        }

        private static float EaseInQuint(float t)
        {
            return t * t * t * t * t;
        }

        private static float EaseOutQuint(float t)
        {
            return 1f - Mathf.Pow(1f - t, 5f);
        }

        private static float EaseInOutQuint(float t)
        {
            return t < 0.5f ? 16f * t * t * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 5f) * 0.5f;
        }

        private static float EaseInExpo(float t)
        {
            return t <= 0f ? 0f : Mathf.Pow(2f, 10f * t - 10f);
        }

        private static float EaseOutExpo(float t)
        {
            return t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
        }

        private static float EaseInOutExpo(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return t < 0.5f
                ? Mathf.Pow(2f, 20f * t - 10f) * 0.5f
                : (2f - Mathf.Pow(2f, -20f * t + 10f)) * 0.5f;
        }

        private static float EaseInCirc(float t)
        {
            return 1f - Mathf.Sqrt(1f - t * t);
        }

        private static float EaseOutCirc(float t)
        {
            return Mathf.Sqrt(1f - Mathf.Pow(t - 1f, 2f));
        }

        private static float EaseInOutCirc(float t)
        {
            return t < 0.5f
                ? (1f - Mathf.Sqrt(1f - Mathf.Pow(2f * t, 2f))) * 0.5f
                : (Mathf.Sqrt(1f - Mathf.Pow(-2f * t + 2f, 2f)) + 1f) * 0.5f;
        }

        private static float EaseInBack(float t)
        {
            float c3 = BackOvershoot + 1f;
            return c3 * t * t * t - BackOvershoot * t * t;
        }

        private static float EaseOutBack(float t)
        {
            float c3 = BackOvershoot + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + BackOvershoot * Mathf.Pow(t - 1f, 2f);
        }

        private static float EaseInOutBack(float t)
        {
            float c2 = BackOvershoot * 1.525f;
            return t < 0.5f
                ? Mathf.Pow(2f * t, 2f) * ((c2 + 1f) * 2f * t - c2) * 0.5f
                : (Mathf.Pow(2f * t - 2f, 2f) * ((c2 + 1f) * (t * 2f - 2f) + c2) + 2f) * 0.5f;
        }

        private static float EaseInElastic(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return -Mathf.Pow(2f, 10f * t - 10f) * Mathf.Sin((t * 10f - 10.75f) * (2f * Mathf.PI / 3f));
        }

        private static float EaseOutElastic(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * (2f * Mathf.PI / 3f)) + 1f;
        }

        private static float EaseInOutElastic(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;

            float c5 = 2f * Mathf.PI / 4.5f;
            return t < 0.5f
                ? -(Mathf.Pow(2f, 20f * t - 10f) * Mathf.Sin((20f * t - 11.125f) * c5)) * 0.5f
                : Mathf.Pow(2f, -20f * t + 10f) * Mathf.Sin((20f * t - 11.125f) * c5) * 0.5f + 1f;
        }

        private static float EaseInBounce(float t)
        {
            return 1f - EaseOutBounce(1f - t);
        }

        private static float EaseOutBounce(float t)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;

            if (t < 1f / d1)
            {
                return n1 * t * t;
            }

            if (t < 2f / d1)
            {
                t -= 1.5f / d1;
                return n1 * t * t + 0.75f;
            }

            if (t < 2.5f / d1)
            {
                t -= 2.25f / d1;
                return n1 * t * t + 0.9375f;
            }

            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }

        private static float EaseInOutBounce(float t)
        {
            return t < 0.5f
                ? (1f - EaseOutBounce(1f - 2f * t)) * 0.5f
                : (1f + EaseOutBounce(2f * t - 1f)) * 0.5f;
        }

        private static float EaseFlash(float t)
        {
            return Flash(t);
        }

        private static float EaseInFlash(float t)
        {
            return Mathf.Lerp(t, Flash(t), t);
        }

        private static float EaseOutFlash(float t)
        {
            return Mathf.Lerp(t, Flash(t), 1f - t);
        }

        private static float EaseInOutFlash(float t)
        {
            return Mathf.Lerp(t, Flash(t), Mathf.Sin(t * Mathf.PI));
        }

        private static float Flash(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;

            float scaled = t * FlashCount;
            int segment = Mathf.FloorToInt(scaled);
            float segmentT = scaled - segment;
            return segment % 2 == 0 ? segmentT : 1f - segmentT;
        }
    }
}
