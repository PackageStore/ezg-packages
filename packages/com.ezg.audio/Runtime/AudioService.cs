using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ezg.Package.Audio
{
    /// <summary>
    /// Game-agnostic audio service. Plays music and one-shot SFX and manages volume.
    /// Volume persistence is delegated to an injected <see cref="ISoundSettings"/>; the
    /// service itself has no dependency on the host game's data layer.
    /// </summary>
    public class AudioService : IAudioService
    {
        #region Fields

        private const float DEFAULT_VOLUME_MUSIC = 1f;
        private const float DEFAULT_VOLUME_SFX = 0.5f;
        private const float MUSIC_FADE_DURATION = 0.5f;

        /// <summary>
        /// Service-locator bridge for ergonomic access from call sites
        /// (<c>AudioService.Default.PlaySound(...)</c>). Assigned once at bootstrap.
        /// </summary>
        public static IAudioService Default { get; set; }

        private readonly ISoundSettings _settings;
        private readonly HashSet<AudioClip> _currentlyPlayingOneShots = new HashSet<AudioClip>();
        private readonly Dictionary<AudioClip, CancellationTokenSource> _oneShotCleanup =
            new Dictionary<AudioClip, CancellationTokenSource>();

        private CancellationTokenSource _musicFadeCts;

        /// <summary>Gets the AudioSource used for background music playback.</summary>
        public AudioSource MusicSource { get; private set; }

        /// <summary>Gets the AudioSource used for sound effect playback.</summary>
        public AudioSource SoundSource { get; private set; }

        #endregion

        #region Initialize

        /// <summary>
        /// Creates a new AudioService with the given settings provider.
        /// </summary>
        /// <param name="settings">The sound settings provider for reading and persisting volumes.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is null.</exception>
        public AudioService(ISoundSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// Creates internal music and sound <see cref="AudioSource"/>s marked as DontDestroyOnLoad.
        /// </summary>
        public void Initialize()
        {
            MusicSource = new GameObject("MusicSource").AddComponent<AudioSource>();
            SoundSource = new GameObject("SoundSource").AddComponent<AudioSource>();
            UnityEngine.Object.DontDestroyOnLoad(MusicSource.gameObject);
            UnityEngine.Object.DontDestroyOnLoad(SoundSource.gameObject);
        }

        /// <summary>
        /// Assigns externally supplied audio sources instead of creating new ones.
        /// </summary>
        /// <param name="music">The AudioSource to use for music.</param>
        /// <param name="sound">The AudioSource to use for sound effects.</param>
        public void Initialize(AudioSource music, AudioSource sound)
        {
            MusicSource = music;
            SoundSource = sound;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Toggles music on or off and persists the new volume setting.
        /// </summary>
        /// <param name="isOn">True to enable music, false to mute.</param>
        public void SetOnOffMusic(bool isOn)
        {
            SetOnOffMusicNotSave(isOn);
            _settings.SetMusicVolume(isOn ? DEFAULT_VOLUME_MUSIC : 0f);
            _settings.Save();
        }

        /// <summary>
        /// Toggles music on or off without persisting the change.
        /// </summary>
        /// <param name="isOn">True to enable music, false to mute.</param>
        public void SetOnOffMusicNotSave(bool isOn)
        {
            if (MusicSource != null)
                MusicSource.volume = isOn ? DEFAULT_VOLUME_MUSIC : 0f;
        }

        /// <summary>
        /// Toggles sound effects on or off and persists the new volume setting.
        /// </summary>
        /// <param name="isOn">True to enable sound effects, false to mute.</param>
        public void SetOnOffSound(bool isOn)
        {
            SetOnOffSoundNotSave(isOn);
            _settings.SetSoundVolume(isOn ? DEFAULT_VOLUME_MUSIC : 0f);
            _settings.Save();
        }

        /// <summary>
        /// Toggles sound effects on or off without persisting the change.
        /// </summary>
        /// <param name="isOn">True to enable sound effects, false to mute.</param>
        public void SetOnOffSoundNotSave(bool isOn)
        {
            if (SoundSource != null)
                SoundSource.volume = isOn ? DEFAULT_VOLUME_SFX : 0f;
        }

        /// <summary>
        /// Sets the music volume and persists the value.
        /// </summary>
        /// <param name="value">Volume level from 0 to 1.</param>
        public void SetVolumeMusicSource(float value)
        {
            if (MusicSource != null)
                MusicSource.volume = value;
            _settings.SetMusicVolume(value);
            _settings.Save();
        }

        /// <summary>
        /// Sets the sound effect volume and persists the value.
        /// </summary>
        /// <param name="value">Volume level from 0 to 1.</param>
        public void SetVolumeSoundSource(float value)
        {
            if (SoundSource != null)
                SoundSource.volume = value;
            _settings.SetSoundVolume(value);
            _settings.Save();
        }

        /// <summary>
        /// Re-applies persisted volume settings to the live audio sources.
        /// </summary>
        public void UpdateVolumes()
        {
            SetVolumeMusicSource(_settings.GetMusicVolume());
            SetVolumeSoundSource(_settings.GetSoundVolume());
            _settings.Save();
        }

        /// <summary>
        /// Plays background music with a fade-in. Skips if the same clip is already playing.
        /// Does nothing if music volume is zero.
        /// </summary>
        /// <param name="clip">The AudioClip to play as music.</param>
        /// <param name="isLoop">Whether the music should loop.</param>
        public void PlayMusic(AudioClip clip, bool isLoop = false)
        {
            if (_settings.GetMusicVolume() == 0f)
                return;

            if (MusicSource == null)
                Initialize();

            if (MusicSource.clip == clip && MusicSource.isPlaying)
                return;

            MusicSource.loop = isLoop;
            RestartMusicFade();
            SwapAndFadeInMusic(clip, _musicFadeCts.Token).Forget();
        }

        /// <summary>
        /// Stops the currently playing music with a fade-out.
        /// </summary>
        public void StopMusic()
        {
            if (MusicSource == null)
                return;

            RestartMusicFade();
            FadeOutAndStop(_musicFadeCts.Token).Forget();
        }

        /// <summary>
        /// Stops the sound effect source immediately.
        /// </summary>
        public void StopSound()
        {
            if (SoundSource != null)
                SoundSource.Stop();
        }

        /// <summary>
        /// Plays a one-shot sound effect. When <paramref name="isLockOneShot"/> is true,
        /// the same clip cannot overlap itself until it finishes.
        /// </summary>
        /// <param name="clip">The AudioClip to play.</param>
        /// <param name="isLockOneShot">If true, prevents the same clip from overlapping.</param>
        public void PlaySound(AudioClip clip, bool isLockOneShot = false)
        {
            if (SoundSource == null)
                Initialize();

            if (SoundSource == null || clip == null)
                return;

            if (_settings.GetSoundVolume() == 0f || SoundSource.volume == 0f)
                return;

            if (isLockOneShot && _currentlyPlayingOneShots.Contains(clip))
                return;

            SoundSource.PlayOneShot(clip);

            if (!isLockOneShot)
                return;

            _currentlyPlayingOneShots.Add(clip);

            if (_oneShotCleanup.TryGetValue(clip, out CancellationTokenSource oldCts))
            {
                oldCts?.Cancel();
                oldCts?.Dispose();
            }

            CancellationTokenSource cts = new CancellationTokenSource();
            _oneShotCleanup[clip] = cts;
            RemoveFromPlayingAfterDuration(clip, cts.Token).Forget();
        }

        /// <summary>
        /// Stops all active one-shot sound effects and clears their tracking state.
        /// </summary>
        public void StopAllOneShots()
        {
            if (SoundSource != null)
                SoundSource.Stop();

            _currentlyPlayingOneShots.Clear();

            foreach (CancellationTokenSource cts in _oneShotCleanup.Values)
            {
                cts?.Cancel();
                cts?.Dispose();
            }

            _oneShotCleanup.Clear();
        }

        /// <summary>
        /// Cleans up all audio resources and cancels all pending async operations.
        /// </summary>
        public void Cleanup()
        {
            StopAllOneShots();
            RestartMusicFade();
            _musicFadeCts?.Cancel();
            _musicFadeCts?.Dispose();
            _musicFadeCts = null;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Cancels any active music fade CTS and creates a fresh one.
        /// </summary>
        private void RestartMusicFade()
        {
            _musicFadeCts?.Cancel();
            _musicFadeCts?.Dispose();
            _musicFadeCts = new CancellationTokenSource();
        }

        /// <summary>
        /// Fades in the music source after swapping to the new clip.
        /// </summary>
        /// <param name="clip">The new music clip to play.</param>
        /// <param name="token">Cancellation token to abort the fade.</param>
        private async UniTaskVoid SwapAndFadeInMusic(AudioClip clip, CancellationToken token)
        {
            try
            {
                MusicSource.volume = 0f;
                MusicSource.clip = clip;
                MusicSource.Play();
                await FadeMusic(_settings.GetMusicVolume(), MUSIC_FADE_DURATION, token);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer music request — nothing to do.
            }
        }

        /// <summary>
        /// Fades out the music source and stops playback.
        /// </summary>
        /// <param name="token">Cancellation token to abort the fade.</param>
        private async UniTaskVoid FadeOutAndStop(CancellationToken token)
        {
            try
            {
                await FadeMusic(0f, MUSIC_FADE_DURATION, token);
                MusicSource.Stop();
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer music request — keep playing.
            }
        }

        /// <summary>
        /// Lerps the music source volume from its current value to the target over the given duration.
        /// </summary>
        /// <param name="target">Target volume level.</param>
        /// <param name="duration">Duration of the fade in seconds.</param>
        /// <param name="token">Cancellation token to abort the fade.</param>
        private async UniTask FadeMusic(float target, float duration, CancellationToken token)
        {
            float start = MusicSource.volume;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                token.ThrowIfCancellationRequested();
                elapsed += Time.unscaledDeltaTime;
                MusicSource.volume = Mathf.Lerp(start, target, elapsed / duration);
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            MusicSource.volume = target;
        }

        /// <summary>
        /// Waits for the clip to finish, then removes it from the currently-playing set.
        /// </summary>
        /// <param name="clip">The clip to track.</param>
        /// <param name="token">Cancellation token to abort the wait.</param>
        private async UniTaskVoid RemoveFromPlayingAfterDuration(AudioClip clip, CancellationToken token)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(clip.length), cancellationToken: token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _currentlyPlayingOneShots.Remove(clip);
            _oneShotCleanup.Remove(clip);
        }

        #endregion
    }
}
