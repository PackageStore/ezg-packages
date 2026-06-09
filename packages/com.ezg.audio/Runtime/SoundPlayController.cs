using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif
using UnityEngine;
using Random = UnityEngine.Random;

namespace Ezg.Package.Audio
{
    /// <summary>
    /// MonoBehaviour that manages playing sounds from a configurable list.
    /// Supports two modes: a list of <see cref="SoundPlayModel"/> entries (Available)
    /// or a list of <see cref="SoundPlayCustomModel"/> entries (Customs).
    /// Implements <see cref="ISoundPlay"/> for external sound injection.
    /// </summary>
    public class SoundPlayController : MonoBehaviour, ISoundPlay
    {
        #region Fields

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")] [Title("Loại sound muốn play")]
#endif
        private SoundTypes _soundTypes = SoundTypes.Available;

        [SerializeField]
#if ODIN_INSPECTOR
        [ShowIf("_soundTypes", SoundTypes.Available)] [TabGroup("Cấu hình")] [Title("Danh sách sound")]
#endif
        private List<SoundPlayModel> _soundList;

        [SerializeField]
#if ODIN_INSPECTOR
        [ShowIf("_soundTypes", SoundTypes.Customs)]
        [TabGroup("Cấu hình")]
        [Title("Danh sách âm thanh tuỳ chỉnh")]
#endif
        private List<SoundPlayCustomModel> _soundCustomList;

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")] [Title("Play on enable")]
#endif
        private bool _playOnEnable;

        /// <summary>Gets or sets the list of custom sound configurations.</summary>
        public List<SoundPlayCustomModel> SoundCustomList
        {
            get => _soundCustomList;
            set => _soundCustomList = value;
        }

        #endregion

        #region Initialize

        private void OnEnable()
        {
            if (_playOnEnable)
            {
                PlaySoundAvailable();
                PlaySoundCustom();
            }
        }

        private void OnDisable()
        {
            if (_soundList == null)
                return;

            for (int i = 0; i < _soundList.Count; i++)
            {
                SoundPlayModel model = _soundList[i];
                model.IsCooldown = false;
                _soundList[i] = model;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Plays all available sounds in <see cref="_soundList"/>, respecting cooldowns and delays.
        /// Only runs when the sound type is set to <see cref="SoundTypes.Available"/>.
        /// </summary>
        public void PlaySoundAvailable()
        {
            if (_soundTypes != SoundTypes.Available || _soundList == null)
                return;

            for (int i = 0; i < _soundList.Count; i++)
            {
                SoundPlayModel playModel = _soundList[i];
                if (playModel.IsCooldown)
                    continue;

                if (playModel.DelayStart > 0)
                {
                    AudioClip clip = playModel.Sound;
                    DelayCall(playModel.DelayStart, () => AudioService.Default?.PlaySound(clip));
                }
                else
                {
                    AudioService.Default?.PlaySound(playModel.Sound);
                }

                if (playModel.Cooldown > 0)
                {
                    int index = i;
                    playModel.IsCooldown = true;
                    _soundList[index] = playModel;
                    DelayCall(playModel.Cooldown, () => ResetCooldown(index));
                }
            }
        }

        /// <summary>
        /// Conditionally plays all custom sounds based on the given flag.
        /// </summary>
        /// <param name="isPlay">If false, playback is skipped.</param>
        public void PlaySoundCustom(bool isPlay)
        {
            if (!isPlay)
                return;

            PlaySoundCustom();
        }

        /// <summary>
        /// Plays all custom sounds in <see cref="_soundCustomList"/>, respecting cooldowns and delays.
        /// </summary>
        public void PlaySoundCustom()
        {
            if (_soundCustomList == null || _soundCustomList.Count == 0)
                return;

            foreach (var sound in _soundCustomList)
            {
                if (sound.IsCooldown)
                    continue;

                if (sound.DelayStart > 0)
                {
                    DelayCall(sound.DelayStart,
                        () => AudioService.Default?.PlaySound(sound.Clip[Random.Range(0, sound.Clip.Length)]));
                }
                else
                {
                    AudioService.Default?.PlaySound(sound.Clip[Random.Range(0, sound.Clip.Length)]);
                }

                if (sound.Cooldown > 0)
                {
                    sound.IsCooldown = true;
                    DelayCall(sound.Cooldown, () => sound.IsCooldown = false);
                }
            }
        }

        /// <summary>
        /// Plays an externally provided list of custom sounds via <see cref="ISoundPlay"/>.
        /// Supports looping sounds through a coroutine on the given controller.
        /// </summary>
        /// <param name="sounds">The list of sound configurations to play.</param>
        /// <param name="controller">The MonoBehaviour used to start coroutines for looping sounds.</param>
        public void PlaySound(List<SoundPlayCustomModel> sounds, MonoBehaviour controller)
        {
            if (sounds == null || sounds.Count == 0)
                return;

            foreach (var sound in sounds)
            {
                if (sound.IsCooldown)
                    continue;

                if (sound.DelayStart > 0)
                {
                    DelayCall(sound.DelayStart,
                        () => AudioService.Default?.PlaySound(sound.Clip[Random.Range(0, sound.Clip.Length)]));
                }
                else
                {
                    AudioService.Default?.PlaySound(sound.Clip[Random.Range(0, sound.Clip.Length)]);
                }

                if (sound.Cooldown > 0)
                {
                    sound.IsCooldown = true;
                    DelayCall(sound.Cooldown, () => sound.IsCooldown = false);
                }

                if (sound.IsLoop)
                {
                    controller.StartCoroutine(LoopAudio(sound.Clip[Random.Range(0, sound.Clip.Length)]));
                }
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Resets the cooldown flag for the sound entry at the given index.
        /// </summary>
        /// <param name="index">Index into <see cref="_soundList"/>.</param>
        private void ResetCooldown(int index)
        {
            if (_soundList == null || index < 0 || index >= _soundList.Count)
                return;

            SoundPlayModel model = _soundList[index];
            model.IsCooldown = false;
            _soundList[index] = model;
        }

        /// <summary>
        /// Schedules an action to run after a delay using UniTask.
        /// </summary>
        /// <param name="seconds">Delay in seconds.</param>
        /// <param name="action">The action to invoke after the delay.</param>
        private void DelayCall(float seconds, Action action)
        {
            DelayCallAsync(seconds, action, this.GetCancellationTokenOnDestroy()).Forget();
        }

        /// <summary>
        /// Asynchronously waits for the given duration then invokes the action.
        /// Silently exits if the token is cancelled.
        /// </summary>
        /// <param name="seconds">Delay in seconds.</param>
        /// <param name="action">The action to invoke after the delay.</param>
        /// <param name="token">Cancellation token tied to this GameObject's lifetime.</param>
        private static async UniTaskVoid DelayCallAsync(float seconds, Action action, CancellationToken token)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            action();
        }

        /// <summary>
        /// Continuously plays an audio clip at its natural interval using a coroutine.
        /// </summary>
        /// <param name="audio">The clip to loop.</param>
        /// <returns>IEnumerator for coroutine usage.</returns>
        private IEnumerator LoopAudio(AudioClip audio)
        {
            float length = audio.length;

            while (true)
            {
                AudioService.Default?.PlaySound(audio);
                yield return new WaitForSeconds(length);
            }
        }

        #endregion

        /// <summary>Defines the playback mode for the sound controller.</summary>
        public enum SoundTypes
        {
            /// <summary>Uses the standard <see cref="SoundPlayModel"/> list.</summary>
            Available,

            /// <summary>Uses the custom <see cref="SoundPlayCustomModel"/> list.</summary>
            Customs
        }
    }
}
