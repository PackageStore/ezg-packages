namespace Ezg.Package.ProgressThread
{
    /// <summary>
    ///     Defines operations and state queries for a generic stopwatch.
    /// </summary>
    /// <typeparam name="T">The type of the stopwatch implementation.</typeparam>
    public interface IStopwatch<T> : IEventStopwatch<T>
    {
        /// <summary>
        ///     Gets or sets the unique identifier of the stopwatch.
        /// </summary>
        int ID { get; set; }

        /// <summary>
        ///     Gets whether the stopwatch is currently running.
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        ///     Gets the elapsed duration of the stopwatch.
        /// </summary>
        float Elapsed { get; }

        /// <summary>
        ///     Sets the total duration of the stopwatch.
        /// </summary>
        /// <param name="second">The duration in seconds.</param>
        /// <returns>The stopwatch instance for chaining.</returns>
        T SetDuration(float second);

        /// <summary>
        ///     Starts or resumes the stopwatch.
        /// </summary>
        void Start();

        /// <summary>
        ///     Pauses or stops the stopwatch.
        /// </summary>
        void Stop();

        /// <summary>
        ///     Restarts the stopwatch from zero.
        /// </summary>
        void Restart();

        /// <summary>
        ///     Resets the elapsed time.
        ///     - If stopped, does not resume.
        ///     - If running, continues running.
        /// </summary>
        void Reset();

        /// <summary>
        ///     Sets the update interval for the stopwatch tick.
        /// </summary>
        /// <param name="second">The interval in seconds.</param>
        /// <returns>The stopwatch instance for chaining.</returns>
        T SetInterval(float second = 1);
    }
}