using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AnimationCurveManipulationTool {

    [System.Serializable]
    public class AnimationCurveManipulationState {

        public enum EditorTarget {
            Animation = 0,
            ParticleSystem = 1,
            AnimationCurveWindow = 2
        }

        public EditorTarget editorTarget = EditorTarget.Animation;

        public bool m_hasAnyCurvePair;
        public int m_curveCount;
        public int m_keyframeCount  = 0;
        private List<AnimationWindowKeyframeBinding> m_selectedKeysOrdered;

        private HashSet<int> keyframeHashes = new HashSet<int>();


        public bool hasAnyCurvePair => m_hasAnyCurvePair;
        public int curveCount => m_curveCount;
        public int keyframeCount => m_keyframeCount;
        public AnimationClip selectedClip { get; set; } = null;

        public List<AnimationWindowKeyframeBinding> selectedKeysOrdered {
            get {
                if (m_selectedKeysOrdered == null) m_selectedKeysOrdered = new List<AnimationWindowKeyframeBinding>();
                return m_selectedKeysOrdered;
            }
        }


        public void UpdateInfo() {
            KeyframeDataUtility.GetCurveAndKeyCount(editorTarget, out m_curveCount, out m_keyframeCount, out m_hasAnyCurvePair);
        }


        public void CheckKeySelection(AnimationCurveManipulationWindow _manipulationWindow) {
            List<AnimationWindowKeyframeBinding> selectedKeys;

            if (editorTarget == EditorTarget.Animation) {
                var animationWindow = AnimationWindowBinding.Get();
                if (animationWindow == null) return;
                var animEditor = animationWindow.animEditor;
                if (animEditor == null) return;
                var state = animEditor.state;
                selectedKeys = state.selectedKeys;

                if (selectedKeys.Count == 0 && EditorWindow.focusedWindow == _manipulationWindow) {
                    animEditor.UpdateSelectedKeysFromCurveEditor();
                    selectedKeys = state.selectedKeys;
                }

                if (!_manipulationWindow.updateEachFrame) {
                    m_selectedKeysOrdered = selectedKeys;
                    return;
                }

                keyframeHashes.Clear();
                foreach (var keyframe in selectedKeys) {
                    keyframeHashes.Add(keyframe.GetHash());
                }

                foreach (var keyframe in selectedKeysOrdered.ToArray()) {
                    if (!keyframeHashes.Contains(keyframe.GetHash())) {
                        selectedKeysOrdered.Remove(keyframe);
                    }
                }

                keyframeHashes.Clear();
                foreach (var keyframe in selectedKeysOrdered) {
                    keyframeHashes.Add(keyframe.GetHash());
                }

                foreach (var keyframe in selectedKeys) {
                    if (!keyframeHashes.Contains(keyframe.GetHash())) {
                        selectedKeysOrdered.Add(keyframe);
                    }
                }
            }
            else {
                keyframeHashes.Clear();
                selectedKeysOrdered.Clear();
            }

            
        }

        #region ACTIONS
        /// <summary>
        /// 
        /// </summary>
        /// <param name="_startTangent"></param>
        /// <param name="_endTangent"></param>
        /// <param name="_applyMode">0: In | 1: In/Out | 2: Out</param>
        public void ApplyCurve(Vector2 _startTangent, Vector2 _endTangent, int _applyMode) {
            var selectedKeyframesGroup = KeyframeDataUtility.GetSelectedKeyframesGroup(editorTarget, 1);
            var invalidCurves = new List<int>();
            foreach (var kvp in selectedKeyframesGroup) {
                var selectedKeyframes = kvp.Value;
                float range = GetKeyframesValueRange(selectedKeyframes, out _);

                if (range == 0) {
                    invalidCurves.Add(kvp.Key);
                    continue;
                }

                if (selectedKeyframes.Length == 1) {
                    //if (window.currentApplyMode == 0) {
                    //    var decoy = new Keyframe();
                    //    BezierUtility.ApplyCurveToKeyframes(ref selectedKeyframes[0], ref decoy, _startTangent, _endTangent, _applyMode);
                    //}
                    //else if (window.currentApplyMode == 2) {
                    //    var decoy = new Keyframe();
                    //    BezierUtility.ApplyCurveToKeyframes(ref decoy, ref selectedKeyframes[0], _startTangent, _endTangent, _applyMode);
                    //}
                }
                else {
                    for (int i = 1; i < selectedKeyframes.Length; i++) {
                        BezierUtility.ApplyCurveToKeyframes(ref selectedKeyframes[i - 1], ref selectedKeyframes[i], _startTangent, _endTangent, _applyMode);
                    }
                }
            }
            foreach (var curveId in invalidCurves) {
                selectedKeyframesGroup.Remove(curveId);
            }
            KeyframeDataUtility.ApplyCurvesTangent(editorTarget, selectedKeyframesGroup);
        }

        public void OffsetKeyframesOnEachObject(AnimationCurveManipulationWindow _manipulationWindow) {
            if (editorTarget != EditorTarget.Animation) {
                Debug.LogError("Offset Keyframes On Each Object can only be used in Animation window.");
                return;
            }

            if (!TryGetLatestOrderedSelection(_manipulationWindow, out var _selectedKeysOrder, out var _selectedClip, out var _state)) return;
            int pathIndex = 0;
            KeyframeDataUtility.ApplyKeyframePaths(_selectedKeysOrder, _pathGroup => {
                for (int i = 0; i < _pathGroup.keyframes.Count; i++) {
                    _pathGroup.keyframes[i].time += 1.0f / _selectedClip.frameRate * pathIndex;
                }
                pathIndex++;
            });
        }

        public void OffsetKeyframesOnEachProperty(AnimationCurveManipulationWindow _manipulationWindow) {
            if (editorTarget != EditorTarget.Animation) {
                Debug.LogError("Offset Keyframes On Each Property can only be used in Animation window.");
                return;
            }

            if (!TryGetLatestOrderedSelection(_manipulationWindow, out var _selectedKeysOrder, out var _selectedClip, out var _state)) return;
            int curveIndex = 0;
            KeyframeDataUtility.ApplyKeyframeCurves(_selectedKeysOrder, _curveGroup => {
                if (curveIndex != 0) {
                    for (int i = 0; i < _curveGroup.keyframes.Count; i++) {
                        _curveGroup.keyframes[i].time += 1.0f / _selectedClip.frameRate * curveIndex;
                    }
                }
                curveIndex++;
            });
        }

        public void ReverseKeyframes() {
            KeyframeDataUtility.ReverseKeys();
        }

        public void MirrorKeyframes() {
            KeyframeDataUtility.PasteKeysMirrored();
        }

        public void AlignKeyframes(AnimationCurveManipulationWindow _manipulationWindow) {
            if (editorTarget != EditorTarget.Animation) {
                Debug.LogError("Align Keyframes can only be used in Animation window.");
                return;
            }

            var animationWindow = AnimationWindowBinding.Get();
            if (animationWindow == null) return;
            var animEditor = animationWindow.animEditor;
            if (animEditor == null) return;

            animEditor.SaveCurveEditorKeySelection();
            CheckKeySelection(_manipulationWindow);

            var state = animEditor.state;
            int curveIndex = 0;
            float[] timeSnaps = null;
            KeyframeDataUtility.ApplyKeyframeCurves(selectedKeysOrdered, _curveGroup => {
                if (curveIndex == 0) {
                    timeSnaps = new float[_curveGroup.keyframes.Count];
                    for (int i = 0; i < _curveGroup.keyframes.Count; i++) {
                        if (i == 0) {
                            timeSnaps[i] = state.currentTime;
                        }
                        else {
                            float delta = _curveGroup.keyframes[i].time - _curveGroup.keyframes[i - 1].time;
                            timeSnaps[i] = timeSnaps[i - 1] + delta;
                        }
                    }
                }
                for (int i = 0; i < _curveGroup.keyframes.Count; i++) {
                    if (i < timeSnaps.Length) {
                        _curveGroup.keyframes[i].time = timeSnaps[i];
                    }
                    else {
                        float delta = _curveGroup.keyframes[i].time - _curveGroup.keyframes[timeSnaps.Length - 1].time;
                        _curveGroup.keyframes[i].time = timeSnaps[timeSnaps.Length - 1] + delta;
                    }
                }
                curveIndex++;
            });
        }

        public void SetKeys() {
            if (editorTarget != EditorTarget.Animation) {
                Debug.LogError("Set Key can only be used in Animation window.");
                return;
            }

            SetKeysPopup.Show();
        }

        public void AddProperties() {
            if (editorTarget != EditorTarget.Animation) {
                Debug.LogError("Add Properties can only be used in Animation window.");
                return;
            }

            AddPropertiesPopup.Show();
        }

        #endregion ACTIONS


        #region PRIVATE-HELPERS
        private static float GetKeyframesValueRange(Keyframe[] selectedKeyframes, out float _minValue) {
            float minVal = float.MaxValue;
            float maxVal = float.MinValue;
            foreach (var keyframe in selectedKeyframes) {
                float val = keyframe.value;
                if (minVal > val) {
                    minVal = val;
                }
                if (maxVal < val) {
                    maxVal = val;
                }
            }
            float range = maxVal - minVal;
            _minValue = minVal;
            return range;
        }

        private bool TryGetLatestOrderedSelection(AnimationCurveManipulationWindow _manipulationWindow, out List<AnimationWindowKeyframeBinding> _selectedKeysOrder, out AnimationClip _selectedClip, out AnimationWindowStateBinding _state) {
            _selectedClip = null;
            _selectedKeysOrder = null;
            _state = null;
            var animationWindow = AnimationWindowBinding.Get();
            if (animationWindow == null) return false;
            var animEditor = animationWindow.animEditor;
            if (animEditor == null) return false;
            var state = animEditor.state;
            if (state == null) return false;
            _selectedClip = state.activeAnimationClip;
            animEditor.SaveCurveEditorKeySelection();
            CheckKeySelection(_manipulationWindow);
            _selectedKeysOrder = selectedKeysOrdered;
            _state = state;
            return true;
        }

        #endregion

    }

}