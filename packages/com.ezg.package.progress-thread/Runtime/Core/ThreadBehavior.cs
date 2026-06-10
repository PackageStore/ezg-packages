namespace Ezg.Package.ProgressThread
{
    /// <summary>
    ///     Specifies the behavior control actions for threads.
    /// </summary>
    public enum ThreadBehavior
    {
        /// <summary>
        ///     Plays the thread.
        /// </summary>
        Play,

        /// <summary>
        ///     Continues thread execution from current state.
        /// </summary>
        Continue,

        /// <summary>
        ///     Replays the thread from start.
        /// </summary>
        Replay,

        /// <summary>
        ///     Pauses thread execution.
        /// </summary>
        Pause,

        /// <summary>
        ///     Kills and releases the thread.
        /// </summary>
        Kill,

        /// <summary>
        ///     Resets the thread.
        /// </summary>
        Reset
    }
}