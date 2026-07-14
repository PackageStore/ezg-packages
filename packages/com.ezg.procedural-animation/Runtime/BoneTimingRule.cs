using System;

namespace Ezg.ProceduralAnimation
{
    [Serializable]
    public class BoneTimingRule
    {
        public string pathContains;
        public float delay;
        public float curveMultiplier = 1f;
    }
}
