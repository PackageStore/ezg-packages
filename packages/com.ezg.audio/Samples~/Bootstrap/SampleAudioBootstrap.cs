using Ezg.Package.Audio;
using UnityEngine;

namespace Ezg.Package.Audio.Samples
{
    /// <summary>
    /// Minimal bootstrap that wires EZG Audio up once at startup.
    ///
    /// It builds an <see cref="AudioService"/> from <see cref="SampleSoundSettings"/>, creates the
    /// internal music/sound sources, applies persisted volumes, then assigns the shared
    /// <see cref="AudioService.Default"/> instance — so any script can call
    /// <c>AudioService.Default.PlaySound(clip)</c> / <c>PlayMusic(clip)</c> without extra setup.
    ///
    /// Copy this file into your game and swap <see cref="SampleSoundSettings"/> for your own
    /// <see cref="ISoundSettings"/> implementation.
    /// </summary>
    public static class SampleAudioBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            var service = new AudioService(new SampleSoundSettings());
            service.Initialize();           // creates DontDestroyOnLoad Music/Sound AudioSources
            service.UpdateVolumes();        // apply persisted volumes to the live sources
            AudioService.Default = service; // service-locator used by PlaySound / SoundPlayController
        }
    }
}
