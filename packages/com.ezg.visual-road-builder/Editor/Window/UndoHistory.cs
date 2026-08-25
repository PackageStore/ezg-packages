#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>
    /// UNDO HISTORY — snapshot ~50 buoc, serialize theo window. Ctrl+Z /
    /// Ctrl+Shift+Z chay he nay, hoat dong bat ke Auto Save bat/tat. Observer trung
    /// tam tu chup 1 buoc moi lan chinh sua "lang" xuong.
    /// </summary>
    public sealed partial class VisualRoadBuilderTool
    {
        private const int HistoryLimit = 50;
        private const double SettleDelay = 0.35;
        private const double DotRepaintInterval = 1.0 / 30.0;
        private const string AutoSavePrefKey = "VisualRoadBuilder.AutoSave";

        [Serializable]
        private sealed class CanvasState
        {
            public int width, height;
            public float cellWorldSize;
            public Vector2Int originCell;
            public List<int> edges = new();
            public List<int> highwayEdges = new();
            public List<int> hwDecorEdges = new();
            public List<int> road2Edges = new();
            public List<int> pathEdges = new();
            public List<int> stations = new();
            public List<int> stations2 = new();
            public List<int> parkings = new();
            public List<DecorItem> decors = new();
            public List<int> rampFlips = new();
        }

        [SerializeField] private List<CanvasState> _undo = new();
        [SerializeField] private List<CanvasState> _redo = new();
        [SerializeField] private CanvasState _committed;
        [SerializeField] private int _savedSig;
        [SerializeField] private bool _savedBaselineSet;

        private int _committedSig;
        private int _lastLiveSig;
        private bool _dirty;
        private bool _pendingChange;
        private double _lastEditTime;
        private double _lastDotRepaint;
        private bool _autoSave;
        private string _saveInfo;
        private Texture2D _dotTex;

        private void InitHistoryAndAutosave()
        {
            _autoSave = EditorPrefs.GetBool(AutoSavePrefKey, false);
            _committed ??= CaptureState();
            _committedSig = ComputeCanvasSignature();
            _lastLiveSig = _committedSig;
            if (!_savedBaselineSet)
            {
                _savedSig = _committedSig;
                _savedBaselineSet = true;
            }
            _dirty = _committedSig != _savedSig;
            EditorApplication.update += OnEditorTick;
        }

        private void TeardownHistoryAndAutosave()
        {
            EditorApplication.update -= OnEditorTick;
            if (_dotTex != null) DestroyImmediate(_dotTex);
        }

        private void OnEditorTick()
        {
            double now = EditorApplication.timeSinceStartup;
            bool settled = now - _lastEditTime >= SettleDelay;
            if (_pendingChange && settled) CommitHistoryStep();
            if (_autoSave && _dirty && settled) SaveToSo(false);
            if (_dirty && now - _lastDotRepaint >= DotRepaintInterval)
            {
                _lastDotRepaint = now;
                Repaint();
            }
        }

        private void TrackCanvasState()
        {
            int sig = ComputeCanvasSignature();
            _dirty = sig != _savedSig;
            if (sig != _lastLiveSig)
            {
                _lastLiveSig = sig;
                if (sig != _committedSig)
                {
                    _pendingChange = true;
                    _lastEditTime = EditorApplication.timeSinceStartup;
                }
                else _pendingChange = false;
            }

            if (_pendingChange)
            {
                EventType t = Event.current.type;
                if (t == EventType.MouseUp || t == EventType.KeyUp) CommitHistoryStep();
            }
        }

        private void CommitHistoryStep()
        {
            _pendingChange = false;
            int sig = ComputeCanvasSignature();
            if (sig == _committedSig) return;

            _undo.Add(_committed);
            if (_undo.Count > HistoryLimit) _undo.RemoveAt(0);
            _redo.Clear();
            _committed = CaptureState();
            _committedSig = sig;
        }

        private void HandleUndoRedoShortcuts()
        {
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.ValidateCommand when e.commandName is "Undo" or "Redo":
                    e.Use();
                    break;
                case EventType.ExecuteCommand when e.commandName == "Undo":
                    PerformUndo();
                    e.Use();
                    break;
                case EventType.ExecuteCommand when e.commandName == "Redo":
                    PerformRedo();
                    e.Use();
                    break;
            }
        }

        private void PerformUndo()
        {
            if (_pendingChange) CommitHistoryStep();
            if (_undo.Count == 0) return;

            _redo.Add(CaptureState());
            if (_redo.Count > HistoryLimit) _redo.RemoveAt(0);
            RestoreState(_undo[^1]);
            _undo.RemoveAt(_undo.Count - 1);
            AfterHistoryJump();
        }

        private void PerformRedo()
        {
            if (_redo.Count == 0) return;

            _undo.Add(CaptureState());
            if (_undo.Count > HistoryLimit) _undo.RemoveAt(0);
            RestoreState(_redo[^1]);
            _redo.RemoveAt(_redo.Count - 1);
            AfterHistoryJump();
        }

        private void AfterHistoryJump()
        {
            ClearSelection();
            PruneOutOfRangeEdges();
            _committed = CaptureState();
            _committedSig = ComputeCanvasSignature();
            _lastLiveSig = _committedSig;
            _pendingChange = false;
            _dirty = _committedSig != _savedSig;
            Repaint();
        }
    }
}
#endif
