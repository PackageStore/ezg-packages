using UnityEngine;

namespace Ezg.Package.Audio
{
    /// <summary>
    /// Simple MonoBehaviour that plays a sound on enable and optionally stops it on disable.
    /// Uses <see cref="AudioService.Default"/> as the audio backend.
    /// </summary>
    public class PlaySound : MonoBehaviour
    {
        #region Fields

        private bool isPlayed;

        /// <summary>The audio clip to play.</summary>
        public AudioClip audio;

        /// <summary>If true, the sound plays automatically when this object is enabled.</summary>
        public bool playOnEnable;

        /// <summary>If true, the sound is stopped when this object is disabled.</summary>
        public bool stopOnDisable;

        #endregion

        #region Initialize

        private void OnEnable()
        {
            if (playOnEnable)
            {
                if (isPlayed)
                {
                    return;
                }
                isPlayed = true;
                Play();
            }
        }

        private void OnDisable()
        {
            isPlayed = false;
            if (stopOnDisable)
            {
                AudioService.Default?.StopSound();
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Plays the assigned audio clip via <see cref="AudioService.Default"/>.
        /// Does nothing if <see cref="audio"/> is null.
        /// </summary>
        public void Play()
        {
            if (audio == null)
            {
                return;
            }

            AudioService.Default?.PlaySound(audio);
        }

        #endregion
    }
}
