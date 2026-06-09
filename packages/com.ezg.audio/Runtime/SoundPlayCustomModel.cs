using System;
using UnityEngine;

namespace Ezg.Package.Audio
{
    /// <summary>
    /// Data model for a custom sound entry that supports multiple clip variants,
    /// optional delay, cooldown, and looping behavior.
    /// </summary>
    [Serializable]
    public class SoundPlayCustomModel
    {
        /// <summary>Array of audio clips; one is chosen at random during playback.</summary>
        public AudioClip[] Clip;

        /// <summary>Delay in seconds before the clip begins playing.</summary>
        [Min(0)] public float DelayStart;

        /// <summary>Cooldown duration in seconds before the sound can be triggered again.</summary>
        public float Cooldown;

        /// <summary>Whether this sound should loop continuously.</summary>
        public bool IsLoop;

        /// <summary>Runtime flag indicating the sound is currently in cooldown. Not serialized.</summary>
        [NonSerialized] public bool IsCooldown;
    }
}
