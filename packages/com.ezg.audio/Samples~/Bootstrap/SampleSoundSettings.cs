using Ezg.Package.Audio;
using UnityEngine;

namespace Ezg.Package.Audio.Samples
{
    /// <summary>
    /// Example <see cref="ISoundSettings"/> implementation backed by PlayerPrefs.
    /// This is the bridge the package uses to read/persist volumes without depending
    /// on any specific game's data layer.
    ///
    /// In a real game, replace PlayerPrefs with your own save system / player profile
    /// (e.g. PlayerDataManager.Settings) — keep the same five methods.
    /// </summary>
    public sealed class SampleSoundSettings : ISoundSettings
    {
        private const string MUSIC_KEY = "ezg.audio.sample.music";
        private const string SOUND_KEY = "ezg.audio.sample.sound";
        private const float DEFAULT_VOLUME = 1f;

        public float GetMusicVolume() => PlayerPrefs.GetFloat(MUSIC_KEY, DEFAULT_VOLUME);

        public float GetSoundVolume() => PlayerPrefs.GetFloat(SOUND_KEY, DEFAULT_VOLUME);

        public void SetMusicVolume(float value) => PlayerPrefs.SetFloat(MUSIC_KEY, value);

        public void SetSoundVolume(float value) => PlayerPrefs.SetFloat(SOUND_KEY, value);

        public void Save() => PlayerPrefs.Save();
    }
}
