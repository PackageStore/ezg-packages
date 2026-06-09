using System.Collections.Generic;
using UnityEngine;

namespace Ezg.Package.Audio
{
    /// <summary>
    /// Contract for components that can play a list of custom sound configurations.
    /// </summary>
    public interface ISoundPlay
    {
        /// <summary>Gets or sets the list of custom sound configurations to play.</summary>
        List<SoundPlayCustomModel> SoundCustomList { get; set; }

        /// <summary>
        /// Plays a list of custom sound models, using the given MonoBehaviour for coroutine support.
        /// </summary>
        /// <param name="sounds">The list of sound configurations to play.</param>
        /// <param name="controller">The MonoBehaviour used to start coroutines for looping sounds.</param>
        void PlaySound(List<SoundPlayCustomModel> sounds, MonoBehaviour controller);
    }
}
