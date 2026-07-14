using System.Collections.Generic;
using UnityEngine;

namespace Ezg.ProceduralAnimation
{
    [CreateAssetMenu(menuName = "EZG Technical Art/Procedural Animation/Pose Asset")]
    public class PoseAsset : ScriptableObject
    {
        public Transform skeletonRootReference;
        public List<BonePoseData> bones = new List<BonePoseData>();
    }
}
