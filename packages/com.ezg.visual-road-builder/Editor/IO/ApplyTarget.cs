#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Bake-target prefab, parent name, save folder.</summary>
    [System.Serializable]
    internal sealed class ApplyTarget
    {
        internal const string DefaultRoadParentName = "RoadParent";

        // Đích bake mesh: node "RoadParent" bên trong prefab asset này. Apply dùng LoadPrefabContents
        // → dựng lại mesh trong RoadParent, KHÔNG đụng các node gameplay khác (dữ liệu map ghi ở file SO).
        [SerializeField] internal GameObject LevelPrefab;
        [SerializeField] internal string RoadParentName = DefaultRoadParentName;
        [SerializeField] internal DefaultAsset SaveFolder;
    }
}
#endif
