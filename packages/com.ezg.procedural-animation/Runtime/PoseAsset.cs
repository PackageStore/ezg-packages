using System.Collections.Generic;
using UnityEngine;

namespace Ezg.ProceduralAnimation
{
    [CreateAssetMenu(menuName = "Ezg/Procedural Animation/Pose Asset")]
    public class PoseAsset : ScriptableObject
    {
        public Transform skeletonRootReference;
        public List<BonePoseData> bones = new List<BonePoseData>();
    }
}
