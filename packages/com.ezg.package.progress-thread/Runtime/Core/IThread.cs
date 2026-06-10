using System;

namespace Ezg.Package.ProgressThread
{
    /// <summary>
    ///     When initializing a thread, please initialize according to the order of [INDEXER] from SetDuration to OnKill.
    /// </summary>
    public interface IThread
    {
        /// <summary>
        ///     Gets the elapsed duration of the thread.
        /// </summary>
        float Elapsed { get; }

        /// <summary>
        ///     Gets the unique identifier of the thread.
        /// </summary>
        int ID { get; }

        /// <summary>
        ///     Gets whether the thread is initialized.
        /// </summary>
        bool Initialized { get; }

        /// <summary>
        ///     Plays or resumes the thread.
        /// </summary>
        void Play();

        /// <summary>
        ///     Pauses the thread execution.
        /// </summary>
        void Pause();

        /// <summary>
        ///     Restarts the thread from the beginning.
        /// </summary>
        void Replay();

        /// <summary>
        ///     Kills the thread, stopping all execution.
        /// </summary>
        void Kill();

        /// <summary>
        ///     Resets the thread's data and state.
        /// </summary>
        void ResetData();

        #region INDEXER

        /// <summary>
        ///     Sets the total running duration or interval of the thread.
        /// </summary>
        /// <param name="sec">The duration in seconds.</param>
        /// <returns>The thread instance for chaining.</returns>
        IThread SetDuration(float sec);

        /// <summary>
        ///     Sets the unique ID for this thread instance.
        /// </summary>
        /// <param name="value">The ID value.</param>
        /// <returns>The thread instance for chaining.</returns>
        IThread SetId(int value);

        /// <summary>
        ///     Event fired when the thread starts.
        /// </summary>
        event Action<IThread> OnStart;

        /// <summary>
        ///     Event fired when the thread plays.
        /// </summary>
        event Action<IThread> OnPlay;

        /// <summary>
        ///     Event fired on each update tick of the thread.
        /// </summary>
        event Action<IThread> OnUpdate;

        /// <summary>
        ///     Event fired when the thread completes execution.
        /// </summary>
        event Action<IThread> OnComplete;

        /// <summary>
        ///     Event fired when the thread is paused.
        /// </summary>
        event Action<IThread> OnPause;

        /// <summary>
        ///     Event fired when the thread is killed.
        /// </summary>
        event Action<IThread> OnKill;

        #endregion
    }
}