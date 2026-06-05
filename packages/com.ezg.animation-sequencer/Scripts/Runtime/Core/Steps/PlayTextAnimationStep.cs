#if DOTWEEN_ENABLED
using System;
using DG.Tweening;
using UnityEngine;

namespace BrunoMikoski.AnimationSequencer
{
    [Serializable]
    public sealed class PlayTextAnimationStep : AnimationStepBase
    {
        [SerializeField]
        private I2.TextAnimation.TextAnimation textAnimation;
        public I2.TextAnimation.TextAnimation TextAnimationTarget
        {
            get => textAnimation;
            set => textAnimation = value;
        }

        [SerializeField]
        [Tooltip("Animation slot index to play (0-based)")]
        private int slotIndex;
        public int SlotIndex
        {
            get => slotIndex;
            set => slotIndex = value;
        }

        [SerializeField]
        [Tooltip("Stop all playing animations before triggering the new one")]
        private bool stopCurrentAnimations = true;
        public bool StopCurrentAnimations
        {
            get => stopCurrentAnimations;
            set => stopCurrentAnimations = value;
        }

        public override string DisplayName => "Play Text Animation";

        public override void AddTweenToSequence(Sequence animationSequence)
        {
            Sequence sequence = DOTween.Sequence();
            sequence.SetDelay(Delay);
            sequence.AppendCallback(() =>
            {
                if (textAnimation == null)
                    return;

                if (stopCurrentAnimations)
                    textAnimation.StopAllAnimations();

                textAnimation.PlayAnimation(slotIndex);
            });

            if (FlowType == FlowType.Join)
                animationSequence.Join(sequence);
            else
                animationSequence.Append(sequence);
        }

        public override void ResetToInitialState()
        {
        }

        public override string GetDisplayNameForEditor(int index)
        {
            string display = "NULL";
            if (textAnimation != null)
                display = textAnimation.name;
            return $"{index}. Play Text Animation: {display} [Slot {slotIndex}]";
        }
    }
}
#endif
