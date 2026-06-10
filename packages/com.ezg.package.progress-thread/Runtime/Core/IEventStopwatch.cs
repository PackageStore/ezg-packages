using System;

namespace Ezg.Package.ProgressThread
{
    /// <summary>
    ///     Defines events for a stopwatch, supporting generic parameter T for callbacks.
    /// </summary>
    /// <typeparam name="T">The type parameter passed to event callbacks.</typeparam>
    public interface IEventStopwatch<T>
    {
        /// <summary>
        ///     Event triggered when the stopwatch starts.
        /// </summary>
        event Action<T> OnStart;

        /// <summary>
        ///     Event triggered when the stopwatch completes.
        /// </summary>
        event Action<T> OnComplete;

        /// <summary>
        ///     Event triggered at each interval step.
        /// </summary>
        event Action<T> OnInterval;

        /// <summary>
        ///     Event triggered when the stopwatch stops.
        /// </summary>
        event Action<T> OnStop;

        /// <summary>
        ///     Event triggered when the stopwatch is killed or disposed.
        /// </summary>
        event Action<T> OnKill;
    }
}