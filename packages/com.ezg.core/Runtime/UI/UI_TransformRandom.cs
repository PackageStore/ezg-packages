#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif
using UnityEngine;

namespace Ezg.Core.UI
{
    public class UI_TransformRandom : MonoBehaviour
    {
        #region Initialize

        /// <summary>
        ///     Applies random rotations according to configuration on Start.
        /// </summary>
        private void Start()
        {
            XValue = IsRandomX ? Random.Range(0f, 360f) : XValue;
            YValue = IsRandomY ? Random.Range(0f, 360f) : YValue;
            ZValue = IsRandomZ ? Random.Range(0f, 360f) : ZValue;

            transform.localRotation = Quaternion.Euler(XValue, YValue, ZValue);
        }

        #endregion

        #region Fields

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")]
#endif
        private bool IsRandomX;

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")]
#endif
        private bool IsRandomY;

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")]
#endif
        private bool IsRandomZ;

        [SerializeField]
#if ODIN_INSPECTOR
        [ShowIf("@IsRandomX == false")] [TabGroup("Cấu hình")]
#endif
        private float XValue;

        [SerializeField]
#if ODIN_INSPECTOR
        [ShowIf("@IsRandomY == false")] [TabGroup("Cấu hình")]
#endif
        private float YValue;

        [SerializeField]
#if ODIN_INSPECTOR
        [ShowIf("@IsRandomZ == false")] [TabGroup("Cấu hình")]
#endif
        private float ZValue;

        #endregion
    }
}