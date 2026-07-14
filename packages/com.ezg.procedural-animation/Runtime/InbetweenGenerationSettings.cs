using System;
using System.Collections.Generic;

namespace Ezg.ProceduralAnimation
{
    [Serializable]
    public class InbetweenSegmentSettings
    {
        public FeelPresetAsset feelPreset;
        public float duration = 0.5f;
    }

    [Serializable]
    public class InbetweenGenerationSettings
    {
        public List<PoseAsset> poses = new List<PoseAsset>();
        public List<InbetweenSegmentSettings> segments = new List<InbetweenSegmentSettings>();

        public int frameRate = 30;

        public bool generatePosition = true;
        public bool generateRotation = true;
        public bool generateScale = false;

        public string clipName = "Generated_Inbetween";
        public string outputFolder = "Assets/ProceduralAnimation/Generated/Animations";
    }
}
