using System;
using UnityEngine;

namespace Ezg.ProceduralAnimation
{
    [Serializable]
    public class BonePoseData
    {
        public string bonePath;
        public Vector3 localPosition;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 localScale = Vector3.one;
    }
}
