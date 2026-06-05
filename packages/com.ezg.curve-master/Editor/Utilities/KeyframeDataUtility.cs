using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static UnityEditor.AnimationUtility;

namespace AnimationCurveManipulationTool {

    public static class KeyframeDataUtility {

        public static CurveEditorBinding GetCurveEditor(AnimationCurveManipulationState.EditorTarget _editorTarget, out EditorWindow _ownerWindow, out Editor _ownerEditor, out AnimEditorBinding _animEditor) {
            _ownerWindow = null;
            _ownerEditor = null;
            _animEditor = null;
            if (_editorTarget == AnimationCurveManipulationState.EditorTarget.Animation) {
                var animationWindow = AnimationWindowBinding.Get();
                if (animationWindow == null) {
                    return null;
                }
                _animEditor = animationWindow.animEditor;
                if (_animEditor == null) {
                    return null;
                };
                if (!_animEditor.state.showCurveEditor) {
                    _animEditor.UpdateSelectedKeysToCurveEditor();
                }
                return _animEditor.curveEditor;
            }
            else if (_editorTarget == AnimationCurveManipulationState.EditorTarget.ParticleSystem) {
                var particleSystemWindow = ParticleSystemWindowBinding.Get();
                if (particleSystemWindow != null && particleSystemWindow.IsVisible()) {
                    var particleEffectUI = particleSystemWindow.ParticleEffectUI;
                    if (particleEffectUI == null) {
                        return null;
                    }
                    var particleSystemCurveEditor = particleEffectUI.ParticleSystemCurveEditor;
                    if (particleSystemCurveEditor == null) {
                        return null;
                    }
                    _ownerWindow = particleSystemWindow.GetObjectReference();
                    return particleSystemCurveEditor.CurveEditor;
                }

                var particleSystemEditor = ParticleSystemInspectorBinding.Get();
                if (particleSystemEditor != null) {
                    var particleEffectUI = particleSystemEditor.ParticleEffectUI;
                    if (particleEffectUI == null) {
                        return null;
                    }
                    var particleSystemCurveEditor = particleEffectUI.ParticleSystemCurveEditor;
                    if (particleSystemCurveEditor == null) {
                        return null;
                    }
                    _ownerEditor = particleSystemEditor.GetObjectReference();
                    return particleSystemCurveEditor.CurveEditor;
                }

                return null;
            }
            else if (_editorTarget == AnimationCurveManipulationState.EditorTarget.AnimationCurveWindow) {
                if (CurveEditorWindowBinding.visible) {
                    var curveEditorWindow = CurveEditorWindowBinding.Get();
                    if (curveEditorWindow != null) {
                        _ownerWindow = curveEditorWindow.GetObjectReference();
                        return curveEditorWindow.CurveEditor;
                    }
                }
                return null;
            }
            else {
                return null;
            }
        }

        public static CurveEditorBinding GetCurveEditor(AnimationCurveManipulationState.EditorTarget _editorTarget) {
            return GetCurveEditor(_editorTarget, out _, out _, out _);
        }

        public static void GetCurveAndKeyCount(AnimationCurveManipulationState.EditorTarget _editorTarget, out int _curveCount, out int _keyCount, out bool _hasAnyCurvePair) {
            _keyCount = 0;
            _hasAnyCurvePair = false;

            var curveEditor = GetCurveEditor(_editorTarget);
            if (curveEditor == null) {
                _curveCount = 0; _keyCount = 0;
                return;
            }

            var curveGroups = curveEditor.GetSelectedCurveIdGroups();
            _curveCount = curveGroups.Count;

            foreach (var curveGroup in curveGroups) {
                int keyframeCount = curveGroup.Value.Length;
                if (!_hasAnyCurvePair) {
                    if (keyframeCount > 1) _hasAnyCurvePair = true;
                }
                _keyCount += keyframeCount;
            }

        }

        public static Dictionary<int, Keyframe[]> GetSelectedKeyframesGroup(AnimationCurveManipulationState.EditorTarget _editorTarget, int _minKeyCount) {
            var result = new Dictionary<int, Keyframe[]>();

            var curveEditor = GetCurveEditor(_editorTarget);
            if (curveEditor == null) {
                return result;
            }

            var curveGroups = curveEditor.GetSelectedCurveIdGroups();

            int targetIndex = 0;

            foreach (var curveGroup in curveGroups) {
                var targetCurve = curveGroup.Key;
                var selectedCurves = curveGroup.Value;
                if (selectedCurves.Length < _minKeyCount) continue;
                var selectedKeyframes = new Keyframe[selectedCurves.Length];
                var offset = selectedCurves[0].key;

                for (var i = 0; i < selectedKeyframes.Length; i++) {
                    var c = curveEditor.selectedCurves[targetIndex];
                    targetIndex++;
                    var kf = curveEditor.GetKeyframe(c);
                    var isFirst = i == 0;
                    var isLast = i == selectedKeyframes.Length - 1;
                    if (!isFirst) {
                        var prev = targetCurve.GetKeyframe(offset + i - 1);
                        var dt = kf.time - prev.time;
                        var dy = kf.value - prev.value;
                        if (dy == 0) {
                            kf.inTangent = kf.inTangent * dt;
                        }
                        else {
                            kf.inTangent = kf.inTangent / dy * dt;
                        }
                    }
                    if (!isLast) {
                        var next = targetCurve.GetKeyframe(offset + i + 1);
                        var dt = next.time - kf.time;
                        var dy = next.value - kf.value;
                        if (dy == 0) {
                            kf.outTangent = kf.outTangent * dt;
                        }
                        else {
                            kf.outTangent = kf.outTangent / dy * dt;
                        }
                    }
                    if (selectedKeyframes.Length == 1) {
                        // Leave the tangents as is
                    }
                    else if (isFirst) {
                        kf.inTangent = 0;
                        kf.inWeight = 0;
                    }
                    else if (isLast) {
                        kf.outTangent = 0;
                        kf.outWeight = 0;
                    }
                    selectedKeyframes[i] = kf;
                }

                result.Add(selectedCurves[0].curveId, selectedKeyframes);
            }

            return result;
        }


        public static void ApplyCurvesTangent(AnimationCurveManipulationState.EditorTarget _editorTarget, Dictionary<int, Keyframe[]> _keyframesGroup) {
            var curveEditor = GetCurveEditor(_editorTarget, out var ownerWindow, out var ownerEditor, out var animEditor);
            if (curveEditor == null) {
                return;
            }

            var targetCurveGroups = curveEditor.GetSelectedCurveIdGroups();
            foreach (var t in targetCurveGroups) {
                var targetCurve = t.Key;
                var selections = t.Value;
                if (!_keyframesGroup.TryGetValue(selections[0].curveId, out var _keyframes)) continue;
                var offset = selections[0].key;
                var scopeLength = Mathf.Min(t.Value.Length, _keyframes.Length);
                for (var i = 0; i < scopeLength; i++) {
                    var curveSelection = selections[i];
                    var kf = curveEditor.GetKeyframe(curveSelection);
                    var source = _keyframes[i];
                    var isFirst = i == 0;
                    var isLast = i == scopeLength - 1;

                    AnimationUtilityInternal.SetKeyBroken(ref kf, true);
                    AnimationUtilityInternal.SetKeyRightWeightedMode(ref kf, true);
                    AnimationUtilityInternal.SetKeyRightTangentMode(ref kf, TangentMode.Free);
                    AnimationUtilityInternal.SetKeyLeftWeightedMode(ref kf, true);
                    AnimationUtilityInternal.SetKeyLeftTangentMode(ref kf, TangentMode.Free);

                    kf.value = source.value;

                    if (scopeLength == 1) {
                        kf.weightedMode = source.weightedMode;
                        kf.inTangent = source.inTangent;
                        kf.inWeight = source.inWeight;
                        kf.outTangent = source.outTangent;
                        kf.outWeight = source.outWeight;
                    }
                    else {
                        if (!isFirst) {
                            var prev = targetCurve.GetKeyframe(offset + i - 1);
                            var dt = kf.time - prev.time;
                            var dy = kf.value - prev.value;
                            if (dy == 0) {
                                kf.inTangent = source.inTangent / dt;
                            }
                            else {
                                kf.inTangent = source.inTangent * dy / dt;
                            }
                            kf.inWeight = source.inWeight;
                        }
                        if (!isLast) {
                            var next = targetCurve.GetKeyframe(offset + i + 1);
                            var dt = next.time - kf.time;
                            var dy = next.value - kf.value;
                            if (dy == 0) {
                                kf.outTangent = source.outTangent / dt;
                            }
                            else {
                                kf.outTangent = source.outTangent * dy / dt;
                            }
                            kf.outWeight = source.outWeight;
                        }
                    }

                    targetCurve.MoveKey(curveSelection.key, ref kf);
                    targetCurve.changed = true;
                }
            }

            if (ownerWindow != null) {
                ownerWindow.Repaint();
            }
            if (ownerEditor != null) {
                ownerEditor.Repaint();
            }
            if (animEditor != null) {
                animEditor.SaveChangedCurvesFromCurveEditor();
                animEditor.ownerWindow.Repaint();
            }
        }


        public static void ApplyCurves(AnimationCurveManipulationState.EditorTarget _editorTarget, Dictionary<int, Keyframe[]> _keyframesGroup) {
            var curveEditor = GetCurveEditor(_editorTarget, out var ownerWindow, out var ownerEditor, out var animEditor);
            if (curveEditor == null) {
                return;
            }

            var targetCurveGroups = curveEditor.GetSelectedCurveIdGroups();
            foreach (var t in targetCurveGroups) {
                var targetCurve = t.Key;
                var selections = t.Value;
                if (!_keyframesGroup.TryGetValue(selections[0].curveId, out var _keyframes)) continue;
                var offset = selections[0].key;
                var scopeLength = Mathf.Min(t.Value.Length, _keyframes.Length);
                for (var i = 0; i < scopeLength; i++) {
                    var curveSelection = selections[i];
                    var kf = curveEditor.GetKeyframe(curveSelection);
                    var source = _keyframes[i];
                    kf.value = source.value;
                    targetCurve.MoveKey(curveSelection.key, ref kf);
                    targetCurve.changed = true;
                }
                for (var i = 0; i < scopeLength; i++) {
                    var curveSelection = selections[i];
                    var kf = curveEditor.GetKeyframe(curveSelection);
                    var source = _keyframes[i];
                    var isFirst = offset + i == 0;
                    var isLast = offset + i == targetCurve.keyCount - 1;

                    AnimationUtilityInternal.SetKeyBroken(ref kf, true);
                    AnimationUtilityInternal.SetKeyRightWeightedMode(ref kf, true);
                    AnimationUtilityInternal.SetKeyRightTangentMode(ref kf, TangentMode.Free);
                    AnimationUtilityInternal.SetKeyLeftWeightedMode(ref kf, true);
                    AnimationUtilityInternal.SetKeyLeftTangentMode(ref kf, TangentMode.Free);

                    if (scopeLength == 1) {
                        kf.weightedMode = source.weightedMode;
                        kf.inTangent = source.inTangent;
                        kf.inWeight = source.inWeight;
                        kf.outTangent = source.outTangent;
                        kf.outWeight = source.outWeight;
                    }
                    else {
                        if (!isFirst) {
                            var prev = targetCurve.GetKeyframe(offset + i - 1);
                            var dt = kf.time - prev.time;
                            var dy = kf.value - prev.value;
                            if (dy == 0) {
                                kf.inTangent = -source.inTangent / dt;
                            }
                            else {
                                kf.inTangent = source.inTangent * dy / dt;
                            }
                            kf.inWeight = source.inWeight;
                        }
                        if (!isLast) {
                            var next = targetCurve.GetKeyframe(offset + i + 1);
                            var dt = next.time - kf.time;
                            var dy = next.value - kf.value;
                            if (dy == 0) {
                                kf.outTangent = -source.outTangent / dt;
                            }
                            else {
                                kf.outTangent = source.outTangent * dy / dt;
                            }
                            kf.outWeight = source.outWeight;
                        }
                    }

                    targetCurve.MoveKey(curveSelection.key, ref kf);
                    targetCurve.changed = true;
                }
            }

            if (ownerWindow != null) {
                ownerWindow.Repaint();
            }
            if (ownerEditor != null) {
                ownerEditor.Repaint();
            }
            if (animEditor != null) {
                animEditor.SaveChangedCurvesFromCurveEditor();
                animEditor.ownerWindow.Repaint();
            }
        }


        public static void ApplyKeyframes(System.Action<AnimationWindowKeyframeBinding> _keyframeHandler) {
            var animationWindow = AnimationWindowBinding.Get();
            var animEditor = animationWindow.animEditor;
            if (animEditor == null) return;

            var state = animEditor.state;
            if (state == null) return;
            var selectedClip = state.activeAnimationClip;
            var selectedKeyframes = state.selectedKeys;

            state.SaveKeySelection("apply keyframes");

            var curves = new HashSet<object>();

            foreach (var keyframe in selectedKeyframes) {
                curves.Add(keyframe.curve);
                _keyframeHandler.Invoke(keyframe);
            }

            state.SaveCurves(selectedClip, curves, "apply keyframes");

            foreach (var keyframe in selectedKeyframes) {
                state.SelectKey(keyframe);
            }

            animEditor.ownerWindow.Repaint();
        }


        public static void ApplyKeyframes(List<AnimationWindowKeyframeBinding> _keyframes, System.Action<AnimationWindowKeyframeBinding> _keyframeHandler) {
            var animationWindow = AnimationWindowBinding.Get();
            var animEditor = animationWindow.animEditor;
            if (animEditor == null) return;

            var state = animEditor.state;
            if (state == null) return;
            var selectedClip = state.activeAnimationClip;
            var selectedKeyframes = state.selectedKeys;

            state.SaveKeySelection("apply keyframes");

            var curves = new HashSet<object>();

            for (int i=0; i<_keyframes.Count; i++) {
                var lastlyUpdatedKeyframe = selectedKeyframes.Find(_key => _key.GetHash() == _keyframes[i].GetHash());
                if (lastlyUpdatedKeyframe != null) {
                    _keyframes[i] = lastlyUpdatedKeyframe;
                }
                var keyframe = _keyframes[i];
                curves.Add(keyframe.curve);
                _keyframeHandler.Invoke(keyframe);
            }

            state.SaveCurves(selectedClip, curves, "apply keyframes");

            foreach (var keyframe in _keyframes) {
                state.SelectKey(keyframe);
            }

            animEditor.ownerWindow.Repaint();
        }

        public static void ApplyKeyframeCurves(List<AnimationWindowKeyframeBinding> _keyframes, System.Action<KeyframeCurveGroup> _curveGroupHandler) {
            var animationWindow = AnimationWindowBinding.Get();
            var animEditor = animationWindow.animEditor;
            if (animEditor == null) return;

            var state = animEditor.state;
            if (state == null) return;
            var selectedClip = state.activeAnimationClip;
            var selectedKeyframes = state.selectedKeys;

            state.SaveKeySelection("apply keyframes");

            state.ClearKeySelections();

            var curves = new HashSet<object>();
            var curveGroups = new List<KeyframeCurveGroup>();

            var prevHash = new int[_keyframes.Count];

            for (int i = 0; i < _keyframes.Count; i++) {
                prevHash[i] = _keyframes[i].GetHash();
                var lastlyUpdatedKeyframe = selectedKeyframes.Find(_key => _key.GetHash() == _keyframes[i].GetHash());
                if (lastlyUpdatedKeyframe != null) {
                    _keyframes[i] = lastlyUpdatedKeyframe;
                }
                var keyframe = _keyframes[i];
                var curve = new AnimationWindowCurveBinding(keyframe.curve);
                var curveGroup = curveGroups.Find(_group => _group.curve.GetObjectReference() == curve.GetObjectReference());
                if (curveGroup == null) {
                    curveGroup = new KeyframeCurveGroup() {
                        curve = curve,
                        keyframes = new List<AnimationWindowKeyframeBinding>()
                    };
                    curveGroups.Add(curveGroup);
                }
                curveGroup.keyframes.Add(keyframe);
                curves.Add(keyframe.curve);
                //Debug.Log("Curve: " + curveGroup.curve.propertyName + " | Keyframe Count: " + curveGroup.keyframes.Count + " | Curve Count: " + curveGroups.Count);
            }


            foreach (var pathGroup in curveGroups) {
                _curveGroupHandler.Invoke(pathGroup);
            }

            state.SaveCurves(selectedClip, curves, "apply keyframes");

            for (int i = 0; i < _keyframes.Count; i++) {
                var keyframe = _keyframes[i];
                if (prevHash[i] != keyframe.GetHash()) {
                    state.RemoveSelectedKeyHash(prevHash[i]);
                }
                state.SelectKey(keyframe);
            }

            if (state.showCurveEditor) {
                animEditor.UpdateSelectedKeysFromCurveEditor();
                animEditor.SaveCurveEditorKeySelection();
            }
            else {
                animEditor.UpdateSelectedKeysToCurveEditor();
            }

            animEditor.ownerWindow.Repaint();

        }

        public static void ApplyKeyframePaths(List<AnimationWindowKeyframeBinding> _keyframes, System.Action<KeyframePathGroup> _pathGroupHandler) {
            var animationWindow = AnimationWindowBinding.Get();
            var animEditor = animationWindow.animEditor;
            if (animEditor == null) return;

            var state = animEditor.state;
            if (state == null) return;
            var selectedClip = state.activeAnimationClip;
            var selectedKeyframes = state.selectedKeys;

            state.SaveKeySelection("apply keyframes");

            state.ClearKeySelections();

            var curves = new HashSet<object>();
            var pathGroups = new List<KeyframePathGroup>();

            var prevHash = new int[_keyframes.Count];
            var prevTime = new float[_keyframes.Count];

            for (int i = 0; i < _keyframes.Count; i++) {
                prevHash[i] = _keyframes[i].GetHash();
                prevTime[i] = _keyframes[i].time;
                var lastlyUpdatedKeyframe = selectedKeyframes.Find(_key => _key.GetHash() == _keyframes[i].GetHash());
                if (lastlyUpdatedKeyframe != null) {
                    _keyframes[i] = lastlyUpdatedKeyframe;
                }
                var keyframe = _keyframes[i];
                var curve = new AnimationWindowCurveBinding(keyframe.curve);
                var pathGroup = pathGroups.Find(_group => _group.path == curve.path);
                if (pathGroup == null) {
                    pathGroup = new KeyframePathGroup() {
                        path = curve.path,
                        keyframes = new List<AnimationWindowKeyframeBinding>()
                    };
                    pathGroups.Add(pathGroup);
                }
                pathGroup.keyframes.Add(keyframe);
                curves.Add(keyframe.curve);
                //Debug.Log("Check Hash: " + keyframe.GetHash() + "  |  Prev: " + prevHash[i]);
            }


            foreach (var pathGroup in pathGroups) {
                _pathGroupHandler.Invoke(pathGroup);
            }

            state.SaveCurves(selectedClip, curves, "apply keyframes");

            for (int i=0; i<_keyframes.Count; i++) {
                var keyframe = _keyframes[i];
                var curve = new AnimationWindowCurveBinding(keyframe.curve);
                if (!curve.HasKeyframe(AnimationKeyTimeBinding.Time(prevTime[i], curve.clip.frameRate))) {
                    state.RemoveSelectedKeyHash(prevHash[i]);
                }
                if (curve.HasKeyframe(AnimationKeyTimeBinding.Time(keyframe.time, curve.clip.frameRate))) {
                    state.SelectKey(keyframe);
                }
            }
            
            if (state.showCurveEditor) {
                animEditor.UpdateSelectedKeysFromCurveEditor();
                animEditor.SaveCurveEditorKeySelection();
            }
            else {
                animEditor.UpdateSelectedKeysToCurveEditor();
            }

            animEditor.ownerWindow.Repaint();
            
        }

        public static void PasteKeysMirrored() {
            var animationWindow = AnimationWindowBinding.Get();
            if (animationWindow == null) return;
            var animEditor = animationWindow.animEditor;
            if (animEditor == null) return;
            var state = animEditor.state;

            animEditor.UpdateSelectedKeysFromCurveEditor();
            animEditor.SaveCurveEditorKeySelection();
            state.PasteKeysMirrored();
            animEditor.UpdateSelectedKeysToCurveEditor();
            animEditor.SaveChangedCurvesFromCurveEditor();
            animEditor.ownerWindow.Repaint();
        }

        public static void ReverseKeys() {
            var animationWindow = AnimationWindowBinding.Get();
            if (animationWindow == null) return;
            var animEditor = animationWindow.animEditor;
            if (animEditor == null) return;
            var state = animEditor.state;

            animEditor.UpdateSelectedKeysFromCurveEditor();
            animEditor.SaveCurveEditorKeySelection();
            state.ReverseKeys();
            animEditor.UpdateSelectedKeysToCurveEditor();
            animEditor.SaveChangedCurvesFromCurveEditor();
        }

        public class KeyframeCurveGroup {
            public AnimationWindowCurveBinding curve;
            public List<AnimationWindowKeyframeBinding> keyframes;
        }

        public class KeyframePathGroup {
            public string path;
            public List<AnimationWindowKeyframeBinding> keyframes;
        }

    }

}