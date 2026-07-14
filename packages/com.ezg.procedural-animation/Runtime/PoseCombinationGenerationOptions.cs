using System;

namespace Ezg.ProceduralAnimation
{
    [Serializable]
    public class PoseCombinationGenerationOptions
    {
        public int frameRate = 30;

        public bool generatePosition = true;
        public bool generateRotation = true;
        public bool generateScale = false;

        public string outputFolder = "Assets/ProceduralAnimation/Generated/Animations";
        public string clipNameTemplate = "{base}_{path}";

        public bool overwriteExistingClips = false;
        public int maxBatchCountGuard = 200;
    }
}
