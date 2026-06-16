// ***********************************************************************
// Assembly         : Assembly-CSharp
// Author           : Hermann Fischer
// Created          : 07-07-2019
//
// Last Modified By : Hermann Fischer
// Last Modified On : 07-09-2019
// ***********************************************************************
// <copyright file="JumpInJumpOut.cs" company="Total Creations">
//     Copyright (c) . All rights reserved.
// </copyright>
// <summary>The main part of the JumpInJumpOut package</summary>
// ***********************************************************************

using System;
using System.Collections;
using DG.Tweening;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif
using UnityEngine;
using UnityEngine.Events;

namespace Ezg.Core.UI
{
    namespace UI
    {
        /// <remarks>
        ///     Drives shake, scale, rotation and blending animations for menus and buttons
        /// </remarks>
        [RequireComponent(typeof(CanvasGroup))]
        public class JumpInJumpOut : MonoBehaviour
        {
            public CanvasGroup canvasGroup;

            [Tooltip(
                "Recommendation to turn this off if you use it for buttons. Actively drives the Blocks Raycast toggle of the Canvas Group")]
            public bool controlUIRaycast;

            [Tooltip("Start state will get applied without animation")]
            public bool startWithoutAnimation;

            [Tooltip("Starts with the Hide State without animation")]
            public bool hideOnStart;

            [Tooltip("Starts animation when OnEnable gets called")]
            public bool activateOnEnabled;

            [Tooltip("Calling the Show Method will result in hiding first before showing again")]
            public bool unscaleDeltatime;

            /// <summary>
            ///     Calling the Show Method will result in hiding first before showing again
            /// </summary>
            public bool hideBeforeShow;

            [Tooltip("Autohide in seconds, 0 will never autohide")]
            public float displayTime;

            public float showDuration = 0.5f;
            public float hideDuration = 0.5f;

            [Header("Blend")] [Tooltip("Toggles the blend-effect")]
            public bool blendActive = true;

#if ODIN_INSPECTOR
            [ShowIf("blendActive")]
#endif
            public Ease showBlendEase = Ease.InOutQuad;

#if ODIN_INSPECTOR
            [ShowIf("blendActive")]
#endif
            public Ease hideBlendEase = Ease.InOutQuad;

            [Header("Scale")] [Tooltip("Toggles the scale-effect")]
            public bool scaleActive = true;

#if ODIN_INSPECTOR
            [ShowIf("scaleActive")]
#endif
            public bool uniformScale;

#if ODIN_INSPECTOR
            [ShowIf("@uniformScale != true && scaleActive == true ")]
#endif
            public Vector3 scaleMin;

#if ODIN_INSPECTOR
            [ShowIf("@uniformScale != true && scaleActive == true ")]
#endif
            public Vector3 scaleMax;

#if ODIN_INSPECTOR
            [ShowIf("scaleActive")]
#endif
            public Ease showScaleEase = Ease.InOutQuad;

#if ODIN_INSPECTOR
            [ShowIf("scaleActive")]
#endif
            public Ease hideScaleEase = Ease.InOutQuad;

#if ODIN_INSPECTOR
            [ShowIf("scaleActive")]
#endif
            [Tooltip("Min represents the scale factor of the hide-state and max of the show-state")]
            public MinMaxHelper scaleMinMax = new(0f, 1f);

            [Header("Shake")]
            [Tooltip(
                "Toggles the shake-effect. NOTE: If also the scale-effect is activated, only the scale effect will be used")]
            public bool shakeActive = true;

#if ODIN_INSPECTOR
            [ShowIf("shakeActive")]
#endif
            public float showShakeStrength = 0.1f;

#if ODIN_INSPECTOR
            [ShowIf("shakeActive")]
#endif
            public int showShakeVibrato = 10;

#if ODIN_INSPECTOR
            [ShowIf("shakeActive")]
#endif
            public float showShakeRandomness = 90;

#if ODIN_INSPECTOR
            [ShowIf("shakeActive")]
#endif
            public Ease showShakeEase = Ease.InOutQuad;

#if ODIN_INSPECTOR
            [ShowIf("shakeActive")]
#endif
            public Ease hideShakeEase = Ease.InOutQuad;

#if ODIN_INSPECTOR
            [ShowIf("shakeActive")]
#endif
            public float hideShakeStrength = 0.1f;

#if ODIN_INSPECTOR
            [ShowIf("shakeActive")]
#endif
            public int hideShakeVibrato = 10;

#if ODIN_INSPECTOR
            [ShowIf("shakeActive")]
#endif
            public float hideShakeRandomness = 90;

            [Header("Rotation")] [Tooltip("Toggles the rotation-effect")]
            public bool rotationActive = true;

#if ODIN_INSPECTOR
            [ShowIf("rotationActive")]
#endif
            public Vector3 rotationAxis = Vector3.forward;

#if ODIN_INSPECTOR
            [ShowIf("rotationActive")]
#endif
            public Ease showRotationEase = Ease.InOutQuad;

#if ODIN_INSPECTOR
            [ShowIf("rotationActive")]
#endif
            public Ease hideRotationEase = Ease.InOutQuad;

#if ODIN_INSPECTOR
            [ShowIf("rotationActive")]
#endif
            [Tooltip("The initial rotation when showing")]
            public MinMaxHelper showStartMinMaxRotation = new(-15, 15);

#if ODIN_INSPECTOR
            [ShowIf("rotationActive")]
#endif
            [Tooltip("The rotation when showing is completed")]
            public MinMaxHelper showMinMaxRotation = new(-15, 15);

#if ODIN_INSPECTOR
            [ShowIf("rotationActive")]
#endif
            [Tooltip("The rotation after hiding")]
            public MinMaxHelper hideMinMaxRotation = new(-15, 15);

            public bool callBackActive = true;

#if ODIN_INSPECTOR
            [ShowIf("callBackActive")]
#endif
            public UnityEvent onShowFinished;

#if ODIN_INSPECTOR
            [ShowIf("callBackActive")]
#endif
            public UnityEvent onHideStarted;

#if ODIN_INSPECTOR
            [ShowIf("callBackActive")]
#endif
            public UnityEvent onHideFinished;

            private string _tweenId;
            private Tween blendTween;

            private Coroutine mCoroutine;

            private State mState = State.Invisible;
            private Tween rotationTween;
            private Tween scaleTween;
            private Tween shakeTween;

            private string TweenId
            {
                get
                {
                    if (string.IsNullOrEmpty(_tweenId))
                        _tweenId = $"JumpInJumpOut_{GetInstanceID()}";
                    return _tweenId;
                }
            }

            /// <summary>
            ///     Applies CanvasGroup to the class and goes to initial state
            /// </summary>
            private void Awake()
            {
                if (canvasGroup == null)
                    canvasGroup = GetComponent<CanvasGroup>();

                if (!Application.isPlaying)
                    return;

                if (!activateOnEnabled) SetToInitialState();
            }

            /// <summary>
            ///     Shows the menu/button/UI if showOnEnabled is activated
            /// </summary>
            private void OnEnable()
            {
                if (activateOnEnabled)
                {
                    if (startWithoutAnimation)
                    {
                        SetToInitialState();
                    }
                    else
                    {
                        if (hideOnStart)
                            Hide();
                        else
                            Show();
                    }
                }
            }

            /// <summary>
            ///     Sets the display settings to the initial state
            /// </summary>
            public void SetToInitialState()
            {
                if (gameObject.activeInHierarchy)
                    mState = State.Invisible;
                else
                    mState = State.Visible;

                if (startWithoutAnimation)
                {
                    if (hideOnStart)
                    {
                        canvasGroup.alpha = 0;
                        if (controlUIRaycast)
                            canvasGroup.blocksRaycasts = false;

                        if (scaleActive)
                        {
                            if (uniformScale)
                                transform.localScale = scaleMinMax.min.FillVector3();
                            else
                                transform.localScale = scaleMin;
                        }

                        if (rotationActive)
                        {
                            var endValue = rotationAxis * hideMinMaxRotation.Random();
                            transform.localRotation = Quaternion.Euler(endValue);
                        }

                        mState = State.Invisible;
                    }
                    else
                    {
                        canvasGroup.alpha = 1;
                        if (controlUIRaycast)
                            canvasGroup.blocksRaycasts = false;

                        if (scaleActive)
                        {
                            if (uniformScale)
                                transform.localScale = scaleMinMax.max.FillVector3();
                            else
                                transform.localScale = scaleMax;
                        }

                        if (rotationActive)
                        {
                            var endValue = rotationAxis * showMinMaxRotation.Random();
                            transform.localRotation = Quaternion.Euler(endValue);
                        }

                        mState = State.Visible;
                    }
                }
            }

            /// <summary>
            ///     Shows the menu/button/UI
            /// </summary>
            public void JumpIn()
            {
                Show();
            }

            /// <summary>
            ///     Hides the menu/button/UI
            /// </summary>
            public void JumpOut()
            {
                Hide();
            }

            /// <summary>
            ///     Shows the menu/button/UI
            /// </summary>
            /// <returns>
            ///     the yield instruction to wait until the execution finishes
            /// </returns>
            public YieldInstruction Show()
            {
                return Show(null);
            }

            /// <summary>
            ///     Hides the menu/button/UI
            /// </summary>
            /// <returns>
            ///     the yield instruction to wait until the execution finishes
            /// </returns>
            public YieldInstruction Hide()
            {
                return Hide(null);
            }

            /// <summary>
            ///     Shows the menu/button/UI and triggers the onFinished action after
            ///     execution
            /// </summary>
            /// <returns>
            ///     the yield instruction to wait until the execution finishes
            /// </returns>
            public YieldInstruction Show(Action onFinished)
            {
                mCoroutine = StartCoroutine(ShowRoutine(onFinished));
                return mCoroutine;
            }

            /// <summary>
            ///     Hides the menu/button/UI and triggers the onFinished action after
            ///     execution
            /// </summary>
            /// <returns>
            ///     the yield instruction to wait until the execution finishes
            /// </returns>
            public YieldInstruction Hide(Action onFinished)
            {
                if (canvasGroup.alpha == 0f)
                {
                    if (onHideStarted != null)
                        onHideStarted.Invoke();

                    if (onFinished != null)
                        onFinished.Invoke();
                    return null;
                }

                if (mCoroutine != null)
                    StopCoroutine(mCoroutine);
                if (gameObject.activeInHierarchy)
                {
                    mCoroutine = StartCoroutine(HideRoutine(onFinished));
                    return mCoroutine;
                }

                return mCoroutine;
            }

            /// <summary>
            ///     Kills all existent, active tweens
            /// </summary>
            private void KillAllTweens()
            {
                DOTween.Kill(TweenId);
                shakeTween = null;
                scaleTween = null;
                blendTween = null;
                rotationTween = null;
            }

            /// <summary>
            ///     Shows the menu/button/UI and triggers the onFinished action after
            ///     execution
            /// </summary>
            /// <returns>
            ///     the IEnumerator instruction to wait until the execution finishes
            /// </returns>
            public IEnumerator ShowRoutine(Action onFinished = null)
            {
                if (mState == State.Invisible)
                {
                    KillAllTweens();
                    mState = State.Visible;

                    if (shakeActive && showDuration > 0f)
                        shakeTween = transform
                            .DOShakeScale(showDuration, showShakeStrength, showShakeVibrato, showShakeRandomness)
                            .SetEase(showShakeEase).SetId(TweenId).SetUpdate(unscaleDeltatime);
                    if (scaleActive)
                    {
                        var scale = Vector3.one;
                        if (uniformScale)
                            scale = scale * scaleMinMax.max;
                        else
                            scale = scaleMax;
                        scaleTween = transform.DOScale(scale, showDuration).SetEase(showScaleEase).SetId(TweenId)
                            .SetUpdate(unscaleDeltatime);
                    }

                    if (blendActive)
                    {
                        if (controlUIRaycast)
                            canvasGroup.blocksRaycasts = true;
                        blendTween = canvasGroup.DOFade(1f, showDuration).SetEase(showBlendEase).SetId(TweenId)
                            .SetUpdate(unscaleDeltatime);
                    }
                    else
                    {
                        canvasGroup.alpha = 1f;
                        if (controlUIRaycast)
                            canvasGroup.blocksRaycasts = true;
                    }

                    if (rotationActive)
                    {
                        transform.localRotation = Quaternion.Euler(rotationAxis * showStartMinMaxRotation.Random());

                        var endValue = rotationAxis * showMinMaxRotation.Random();
                        rotationTween = transform.DOLocalRotate(endValue, showDuration).SetEase(showRotationEase)
                            .SetId(TweenId).SetUpdate(unscaleDeltatime);
                    }

                    yield return new WaitForSeconds(showDuration);

                    onShowFinished.Invoke();
                    if (onFinished != null)
                        onFinished.Invoke();

                    if (displayTime > 0)
                    {
                        yield return new WaitForSeconds(displayTime);
                        yield return HideRoutine();
                    }
                }
                else
                {
                    if (hideBeforeShow)
                    {
                        yield return HideRoutine();
                        yield return ShowRoutine(onFinished);
                    }
                    else
                    {
                        if (onFinished != null)
                            onFinished.Invoke();
                    }
                }
            }

            /// <summary>
            ///     Hides the menu/button/UI and triggers the onFinished action after
            ///     execution
            /// </summary>
            /// <returns>
            ///     the IEnumerator instruction to wait until the execution finishes
            /// </returns>
            public IEnumerator HideRoutine(Action onFinished = null)
            {
                if (mState == State.Visible)
                {
                    if (onHideStarted != null)
                        onHideStarted.Invoke();
                    KillAllTweens();
                    mState = State.Invisible;

                    if (shakeActive && hideDuration > 0f)
                        shakeTween = transform
                            .DOShakeScale(hideDuration, hideShakeStrength, hideShakeVibrato, hideShakeRandomness)
                            .SetEase(hideShakeEase).SetId(TweenId).SetUpdate(unscaleDeltatime);
                    if (scaleActive)
                    {
                        var scale = Vector3.one;
                        if (uniformScale)
                            scale = scale * scaleMinMax.min;
                        else
                            scale = scaleMin;

                        scaleTween = transform.DOScale(scale, hideDuration).SetEase(hideScaleEase).SetId(TweenId)
                            .SetUpdate(unscaleDeltatime);
                    }

                    if (blendActive)
                        blendTween = canvasGroup.DOFade(0f, hideDuration).SetEase(hideBlendEase).SetId(TweenId)
                            .SetUpdate(unscaleDeltatime);

                    if (rotationActive)
                    {
                        var endValue = rotationAxis * hideMinMaxRotation.Random();
                        rotationTween = transform.DOLocalRotate(endValue, hideDuration).SetEase(hideRotationEase)
                            .SetId(TweenId).SetUpdate(unscaleDeltatime);
                    }

                    yield return new WaitForSeconds(hideDuration);
                    if ((shakeActive && (transform.localScale.x == 0f || transform.localScale.y == 0f ||
                                         transform.localScale.z == 0f)) || blendActive)
                    {
                        canvasGroup.alpha = 0f;
                        if (controlUIRaycast)
                            canvasGroup.blocksRaycasts = false;
                    }

                    if (onFinished != null)
                        onFinished.Invoke();
                    onHideFinished.Invoke();
                }
                else
                {
                    if (controlUIRaycast)
                        canvasGroup.blocksRaycasts = false;
                    if (onHideStarted != null)
                        onHideStarted.Invoke();
                    if (onFinished != null)
                        onFinished.Invoke();
                }
            }

            private enum State
            {
                Visible,
                Invisible
            }
        }
    }
}

public enum DirectType
{
    none = -1,
    X = 0,
    Y = 1,
    Z = 2
}