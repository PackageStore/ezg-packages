namespace Ezg.Package.Audio
{
    /// <summary>
    /// Abstraction the host game implements so the audio package can read and persist
    /// volume settings without depending on game-specific player data systems.
    /// </summary>
    public interface ISoundSettings
    {
        /// <summary>Current music volume (0..1).</summary>
        float GetMusicVolume();

        /// <summary>Current sound-effect volume (0..1).</summary>
        float GetSoundVolume();

        /// <summary>Persist the music volume value.</summary>
        void SetMusicVolume(float value);

        /// <summary>Persist the sound-effect volume value.</summary>
        void SetSoundVolume(float value);

        /// <summary>Flush pending changes to durable storage.</summary>
        void Save();
    }
}
