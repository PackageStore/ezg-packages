using System;
using UnityEngine;

namespace Ezg.Package.Audio
{
    /// <summary>
    /// Data model for a single sound entry with optional delay and cooldown support.
    /// </summary>
    [Serializable]
    public struct SoundPlayModel
    {
        /// <summary>The audio clip to play.</summary>
        public AudioClip Sound;

        /// <summary>Delay in seconds before the clip begins playing.</summary>
        public float DelayStart;

        /// <summary>Cooldown duration in seconds before the sound can be triggered again.</summary>
        public float Cooldown;

        /// <summary>Runtime flag indicating the sound is currently in cooldown. Not serialized.</summary>
        [NonSerialized] public bool IsCooldown;
    }
}
