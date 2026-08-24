#if UNITY_EDITOR
using System;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Serialized decor panel state + transient interaction flags.</summary>
    [Serializable]
    internal sealed class DecorState
    {
        [SerializeField] internal DecorLibrary Library;
        [SerializeField] internal int EntryIndex;
        [SerializeField] internal bool AreaMode;
        [SerializeField] internal float Density = 0.5f;
        [SerializeField] internal bool RandomRot = true;

        // Transient interaction state (not serialized).
        internal int DraggingDecor = -1;
        internal bool PaintingDecor;
        internal bool ErasingDecor;
        internal bool Hover;
        internal Vector2Int HoverP2;
        internal bool AreaDragging;
        internal bool AreaErasing;
        internal Vector2 AreaStart;
        internal Vector2 AreaEnd;

        internal void ResetInteraction()
        {
            DraggingDecor = -1;
            PaintingDecor = false;
            ErasingDecor = false;
            Hover = false;
            AreaDragging = false;
            AreaErasing = false;
        }
    }
}
#endif
