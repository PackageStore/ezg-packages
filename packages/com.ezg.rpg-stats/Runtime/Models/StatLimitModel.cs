using System;

namespace Ezg.Package.RpgStats
{
    [Serializable]
    public struct StatLimitModel<TKey>
    {
        public TKey type;
        public float minValue;
        public float maxValue;
    }
}
