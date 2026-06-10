using System;
using System.Collections.Generic;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif
using UnityEngine;

namespace Ezg.Package.ProgressThread
{
    /// <summary>
    ///     A timeline implementation that tracks elapsed time and triggers registered events at specific timestamps.
    /// </summary>
    [Serializable]
    public class LinearTimeline : ILinearTimeline
    {
        #region Event Handlers

        /// <summary>
        ///     Event handler called on update tick. Advances elapsed time and triggers matching events.
        /// </summary>
        private void OnUpdate()
        {
            if (!IsRunning) return;

            _previousElapsed = ElapsedSeconds;
            var deltaTime = Time.deltaTime;
            ElapsedSeconds += deltaTime;

            try
            {
                foreach (var @event in _events)
                {
                    var atSeconds = @event.seconds;

                    var isComplete = atSeconds > _previousElapsed && atSeconds <= ElapsedSeconds;
                    if (isComplete) @event.callback?.Invoke();
                }
            }
            catch (InvalidOperationException)
            {
                Debug.LogWarning("Invalid Operation Exception: Events was destroy when running");
            }
        }

        #endregion

        #region Fields

        private float _previousElapsed;
        private ThreadType type;
        private bool _isRunning;

        #if ODIN_INSPECTOR
        [ShowInInspector]
        #endif
        private readonly List<TimelineEvent> _events = new();

        /// <summary>
        ///     Gets the elapsed time as a TimeSpan.
        /// </summary>
        public TimeSpan Elapsed => TimeSpan.FromMilliseconds(ElapsedMilliseconds);

        /// <summary>
        ///     Gets the elapsed time in milliseconds.
        /// </summary>
        #if ODIN_INSPECTOR
        [ShowInInspector]
        #endif
        public float ElapsedMilliseconds => ElapsedSeconds * TimeDefine.MillisecondsPerSecond;

        /// <summary>
        ///     Gets the elapsed time in seconds.
        /// </summary>
        #if ODIN_INSPECTOR
        [ShowInInspector]
        #endif
        public float ElapsedSeconds { get; private set; }

        /// <summary>
        ///     Gets whether the timeline is currently active and updating.
        /// </summary>
        #if ODIN_INSPECTOR
        [ShowInInspector]
        #endif
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (_isRunning == value) return;
                _isRunning = value;

                var u = U.Instance;
                if (u == null) return;

                if (_isRunning) u.DontDestroyUpdate += OnUpdate;
                else u.DontDestroyUpdate -= OnUpdate;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Starts the timeline update sequence.
        /// </summary>
        public void Start()
        {
            IsRunning = true;
        }

        /// <summary>
        ///     Stops the timeline update sequence.
        /// </summary>
        public void Stop()
        {
            IsRunning = false;
        }

        /// <summary>
        ///     Resets the timeline to zero and restarts it.
        /// </summary>
        public void Restart()
        {
            Reset();
            Start();
        }

        /// <summary>
        ///     Resets the elapsed time to zero.
        /// </summary>
        public void Reset()
        {
            ElapsedSeconds = default;
        }

        /// <summary>
        ///     Adds an event to be triggered at a specific timeline timestamp.
        /// </summary>
        /// <param name="event">The timeline event to add.</param>
        /// <returns>True if the event was successfully added, false if it already exists.</returns>
        public bool AddEvent(TimelineEvent @event)
        {
            if (_events.Contains(@event)) return false;

            _events.Add(@event);
            return true;
        }

        /// <summary>
        ///     Removes a registered event from the timeline.
        /// </summary>
        /// <param name="event">The event to remove.</param>
        /// <returns>True if the event was removed, false otherwise.</returns>
        public bool RemoveEvent(TimelineEvent @event)
        {
            return _events.Remove(@event);
        }

        /// <summary>
        ///     Disposes the timeline, stopping the update cycle and clearing all events.
        /// </summary>
        public void Dispose()
        {
            IsRunning = false;
            _events.Clear();
        }

        /// <summary>
        ///     Sets the thread category type for the timeline.
        /// </summary>
        /// <param name="type">The thread type.</param>
        public void SetThreadType(ThreadType type)
        {
            this.type = type;
        }

        #endregion
    }

    /// <summary>
    ///     Represents an event containing a callback mapped to a specific timestamp in seconds.
    /// </summary>
    [Serializable]
    public class TimelineEvent
    {
        /// <summary>
        ///     The timestamp in seconds at which the callback should be invoked.
        /// </summary>
        public float seconds;

        /// <summary>
        ///     The action callback to invoke.
        /// </summary>
        public Action callback;
    }

    /// <summary>
    ///     Interface defining standard stopwatch operations.
    /// </summary>
    public interface IStopwatch
    {
        /// <summary>
        ///     Gets the elapsed duration as a TimeSpan.
        /// </summary>
        TimeSpan Elapsed { get; }

        /// <summary>
        ///     Gets the elapsed duration in seconds.
        /// </summary>
        float ElapsedSeconds { get; }

        /// <summary>
        ///     Gets the elapsed duration in milliseconds.
        /// </summary>
        float ElapsedMilliseconds { get; }

        /// <summary>
        ///     Gets whether the stopwatch is running.
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        ///     Starts the stopwatch.
        /// </summary>
        void Start();

        /// <summary>
        ///     Stops the stopwatch.
        /// </summary>
        void Stop();

        /// <summary>
        ///     Restarts the stopwatch.
        /// </summary>
        void Restart();

        /// <summary>
        ///     Resets the stopwatch to zero.
        /// </summary>
        void Reset();
    }

    /// <summary>
    ///     Interface for a timeline that supports adding and removing events.
    /// </summary>
    public interface IMultiEventTimeline
    {
        /// <summary>
        ///     Adds a timeline event.
        /// </summary>
        /// <param name="event">The timeline event.</param>
        /// <returns>True if successfully added, false otherwise.</returns>
        bool AddEvent(TimelineEvent @event);

        /// <summary>
        ///     Removes a timeline event.
        /// </summary>
        /// <param name="event">The timeline event to remove.</param>
        /// <returns>True if successfully removed, false otherwise.</returns>
        bool RemoveEvent(TimelineEvent @event);
    }

    /// <summary>
    ///     Aggregated timeline interface combining stopwatch, event management, disposal, and thread classification.
    /// </summary>
    public interface ILinearTimeline : IStopwatch, IMultiEventTimeline, IDisposable, ISetThreadType
    {
    }

    /// <summary>
    ///     Constants representing time definitions.
    /// </summary>
    public static class TimeDefine
    {
        /// <summary>
        ///     Number of milliseconds in a single second.
        /// </summary>
        public const int MillisecondsPerSecond = 1000;
    }
}