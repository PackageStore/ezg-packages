using UnityEngine;

namespace Ezg.Package.Audio
{
    /// <summary>
    /// Generic audio playback contract: music, one-shot sound effects and volume control.
    /// Implementations are game-agnostic — see <see cref="AudioService"/>.
    /// </summary>
    public interface IAudioService
    {
        /// <summary>Gets the AudioSource used for background music playback.</summary>
        AudioSource MusicSource { get; }

        /// <summary>Gets the AudioSource used for sound effect playback.</summary>
        AudioSource SoundSource { get; }

        /// <summary>Create internal music/sound <see cref="AudioSource"/>s (DontDestroyOnLoad).</summary>
        void Initialize();

        /// <summary>Use externally supplied audio sources instead of creating new ones.</summary>
        /// <param name="music">The AudioSource to use for music.</param>
        /// <param name="sound">The AudioSource to use for sound effects.</param>
        void Initialize(AudioSource music, AudioSource sound);

        /// <summary>
        /// Play a one-shot sound effect. When <paramref name="isLockOneShot"/> is true the
        /// same clip cannot overlap itself until it finishes.
        /// </summary>
        /// <param name="clip">The AudioClip to play.</param>
        /// <param name="isLockOneShot">If true, prevents the same clip from overlapping.</param>
        void PlaySound(AudioClip clip, bool isLockOneShot = false);

        /// <summary>Plays background music with an optional loop setting.</summary>
        /// <param name="clip">The AudioClip to play as music.</param>
        /// <param name="isLoop">Whether the music should loop.</param>
        void PlayMusic(AudioClip clip, bool isLoop = false);

        /// <summary>Stops the currently playing background music with a fade-out.</summary>
        void StopMusic();

        /// <summary>Stops all currently playing sound effects immediately.</summary>
        void StopSound();

        /// <summary>Toggles music on or off and persists the setting.</summary>
        /// <param name="isOn">True to enable music, false to disable.</param>
        void SetOnOffMusic(bool isOn);

        /// <summary>Toggles music on or off without persisting the setting.</summary>
        /// <param name="isOn">True to enable music, false to disable.</param>
        void SetOnOffMusicNotSave(bool isOn);

        /// <summary>Toggles sound effects on or off and persists the setting.</summary>
        /// <param name="isOn">True to enable sound effects, false to disable.</param>
        void SetOnOffSound(bool isOn);

        /// <summary>Toggles sound effects on or off without persisting the setting.</summary>
        /// <param name="isOn">True to enable sound effects, false to disable.</param>
        void SetOnOffSoundNotSave(bool isOn);

        /// <summary>Sets the music volume and persists the value.</summary>
        /// <param name="value">Volume level from 0 to 1.</param>
        void SetVolumeMusicSource(float value);

        /// <summary>Sets the sound effect volume and persists the value.</summary>
        /// <param name="value">Volume level from 0 to 1.</param>
        void SetVolumeSoundSource(float value);

        /// <summary>Re-apply persisted volumes to the live audio sources.</summary>
        void UpdateVolumes();

        /// <summary>Stops all active one-shot sound effects and clears their tracking state.</summary>
        void StopAllOneShots();

        /// <summary>Cleans up all audio resources and cancels pending async operations.</summary>
        void Cleanup();
    }
}
