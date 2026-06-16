using DG.Tweening;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif
using UnityEngine;

namespace Ezg.Core.UI
{
    public class UI_TweenMove
    {
        #region Fields

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")]
#endif
        private bool _useCurve;

        [SerializeField]
#if ODIN_INSPECTOR
        [ShowIf("_useCurve")] [TabGroup("Cấu hình")]
#endif
        private AnimationCurve _animCurve;

        [SerializeField]
#if ODIN_INSPECTOR
        [ShowIf("_useCurve", false)] [TabGroup("Cấu hình")]
#endif
        private Ease _easeAnim;

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")]
#endif
        private float _speed;

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")]
#endif
        private bool _playOnAwake;

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")]
#endif
        private bool _isLoop;

        #endregion
    }
}