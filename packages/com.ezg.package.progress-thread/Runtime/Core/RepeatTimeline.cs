using System;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif
using UnityEngine;

namespace Ezg.Package.ProgressThread
{
    /// <summary>
    ///     A timeline implementation that triggers a specific event repeatedly at a specified rate, registered to global
    ///     lifecycle updates.
    /// </summary>
    [Serializable]
    public class RepeatTimeline : IRepeatTimeline
    {
        #region Event Handlers

        /// <summary>
        ///     Event handler called on update tick. Calculates passed repeat cycles and triggers the callback if interval has
        ///     elapsed.
        /// </summary>
        private void OnUpdate()
        {
            if (!IsRunning) return;

            _previousElapsed = ElapsedSeconds;
            var deltaTime = Time.deltaTime;
            ElapsedSeconds += deltaTime;

            if (_event is null) return;

            var repeatRate = _event.seconds;
            const int oneRepeat = 1;

            var repeatCycle = (int)(ElapsedSeconds / repeatRate);
            var passedRepeats = repeatCycle > oneRepeat;

            if (passedRepeats)
            {
                var surplusCycle = repeatCycle - oneRepeat;
                var surplusSeconds = surplusCycle * repeatRate;

                _previousElapsed -= surplusSeconds;
                ElapsedSeconds -= surplusSeconds;
            }

            var isRepeat = repeatRate > _previousElapsed && repeatRate <= ElapsedSeconds;

            if (isRepeat)
            {
                _event.callback?.Invoke();

                _previousElapsed -= repeatRate;
                ElapsedSeconds -= repeatRate;
            }
        }

        #endregion

        #region Fields

        private float _previousElapsed;

        /// <summary>
        ///     The category type of the thread runner.
        /// </summary>
        public ThreadType type;

        private bool _isRunning;

        /// <summary>
        ///     Gets the elapsed duration of the timeline as a TimeSpan.
        /// </summary>
        public TimeSpan Elapsed => TimeSpan.FromMilliseconds(ElapsedMilliseconds);

        /// <summary>
        ///     Gets the elapsed duration in milliseconds.
        /// </summary>
        #if ODIN_INSPECTOR
        [ShowInInspector]
        #endif
        public float ElapsedMilliseconds => ElapsedSeconds * TimeDefine.MillisecondsPerSecond;

        /// <summary>
        ///     Gets the elapsed duration in seconds.
        /// </summary>
        #if ODIN_INSPECTOR
        [ShowInInspector]
        #endif
        public float ElapsedSeconds { get; private set; }

        /// <summary>
        ///     Gets whether the timeline is currently active.
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

                if (_isRunning)
                    u.DontDestroyUpdate += OnUpdate;
                else
                    u.DontDestroyUpdate -= OnUpdate;
            }
        }

        #if ODIN_INSPECTOR
        [ShowInInspector]
        #endif
        private TimelineEvent _event;

        #endregion

        #region Public Methods

        /// <summary>
        ///     Starts the timeline repetition updates.
        /// </summary>
        public void Start()
        {
            IsRunning = true;
        }

        /// <summary>
        ///     Stops the timeline repetition updates.
        /// </summary>
        public void Stop()
        {
            IsRunning = false;
        }

        /// <summary>
        ///     Resets elapsed time and restarts repetition updates.
        /// </summary>
        public void Restart()
        {
            Reset();
            Start();
        }

        /// <summary>
        ///     Resets the elapsed time back to zero.
        /// </summary>
        public void Reset()
        {
            ElapsedSeconds = default;
        }

        /// <summary>
        ///     Configures the repeating timeline event.
        /// </summary>
        /// <param name="event">The event configuration specifying interval rate and action.</param>
        public void SetEvent(TimelineEvent @event)
        {
            _event = @event;
        }

        /// <summary>
        ///     Disposes the timeline instance, stopping updates and removing reference events.
        /// </summary>
        public void Dispose()
        {
            IsRunning = false;
            _event = null;
        }

        /// <summary>
        ///     Sets the thread category type.
        /// </summary>
        /// <param name="type">The thread category.</param>
        public void SetThreadType(ThreadType type)
        {
            this.type = type;
        }

        #endregion
    }

    /// <summary>
    ///     A timeline implementation that triggers a specific event repeatedly, registered to Gameplay lifecycle updates.
    /// </summary>
    [Serializable]
    public class RepeatTimelineInGame : IRepeatTimeline
    {
        #region Event Handlers

        /// <summary>
        ///     Event handler called on update tick. Calculates passed repeat cycles and triggers the callback if interval has
        ///     elapsed.
        /// </summary>
        private void OnUpdate()
        {
            if (!IsRunning) return;

            _previousElapsed = ElapsedSeconds;
            var deltaTime = Time.deltaTime;
            ElapsedSeconds += deltaTime;

            if (_event is null) return;

            var repeatRate = _event.seconds;
            const int oneRepeat = 1;

            var repeatCycle = (int)(ElapsedSeconds / repeatRate);
            var passedRepeats = repeatCycle > oneRepeat;

            if (passedRepeats)
            {
                var surplusCycle = repeatCycle - oneRepeat;
                var surplusSeconds = surplusCycle * repeatRate;

                _previousElapsed -= surplusSeconds;
                ElapsedSeconds -= surplusSeconds;
            }

            var isRepeat = repeatRate > _previousElapsed && repeatRate <= ElapsedSeconds;

            if (isRepeat)
            {
                _event.callback?.Invoke();

                _previousElapsed -= repeatRate;
                ElapsedSeconds -= repeatRate;
            }
        }

        #endregion

        #region Fields

        private float _previousElapsed;

        /// <summary>
        ///     The category type of the thread runner.
        /// </summary>
        public ThreadType type;

        private bool _isRunning;

        /// <summary>
        ///     Gets the elapsed duration of the timeline as a TimeSpan.
        /// </summary>
        public TimeSpan Elapsed => TimeSpan.FromMilliseconds(ElapsedMilliseconds);

        /// <summary>
        ///     Gets the elapsed duration in milliseconds.
        /// </summary>
        #if ODIN_INSPECTOR
        [ShowInInspector]
        #endif
        public float ElapsedMilliseconds => ElapsedSeconds * TimeDefine.MillisecondsPerSecond;

        /// <summary>
        ///     Gets the elapsed duration in seconds.
        /// </summary>
        #if ODIN_INSPECTOR
        [ShowInInspector]
        #endif
        public float ElapsedSeconds { get; private set; }

        /// <summary>
        ///     Gets whether the timeline is currently active.
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

                if (_isRunning)
                    u.GameplayLifeCycleUpdate += OnUpdate;
                else
                    u.GameplayLifeCycleUpdate -= OnUpdate;
            }
        }

        #if ODIN_INSPECTOR
        [ShowInInspector]
        #endif
        private TimelineEvent _event;

        #endregion

        #region Public Methods

        /// <summary>
        ///     Starts the timeline repetition updates.
        /// </summary>
        public void Start()
        {
            IsRunning = true;
        }

        /// <summary>
        ///     Stops the timeline repetition updates.
        /// </summary>
        public void Stop()
        {
            IsRunning = false;
        }

        /// <summary>
        ///     Resets elapsed time and restarts repetition updates.
        /// </summary>
        public void Restart()
        {
            Reset();
            Start();
        }

        /// <summary>
        ///     Resets the elapsed time back to zero.
        /// </summary>
        public void Reset()
        {
            ElapsedSeconds = default;
        }

        /// <summary>
        ///     Configures the repeating timeline event.
        /// </summary>
        /// <param name="event">The event configuration specifying interval rate and action.</param>
        public void SetEvent(TimelineEvent @event)
        {
            _event = @event;
        }

        /// <summary>
        ///     Disposes the timeline instance, stopping updates and removing reference events.
        /// </summary>
        public void Dispose()
        {
            IsRunning = false;
            _event = null;
        }

        /// <summary>
        ///     Sets the thread category type.
        /// </summary>
        /// <param name="type">The thread category.</param>
        public void SetThreadType(ThreadType type)
        {
            this.type = type;
        }

        #endregion
    }

    /// <summary>
    ///     Aggregated repeating timeline interface combining stopwatch, repeat configuration, disposal, and thread
    ///     classification.
    /// </summary>
    public interface IRepeatTimeline : IStopwatch, IRepeatEventTimeline, IDisposable, ISetThreadType
    {
    }

    /// <summary>
    ///     Interface for configuring repeating timeline events.
    /// </summary>
    public interface IRepeatEventTimeline
    {
        /// <summary>
        ///     Registers the timeline event.
        /// </summary>
        /// <param name="event">The repeating timeline event.</param>
        void SetEvent(TimelineEvent @event);
    }

    /// <summary>
    ///     Interface for setting the thread classification type.
    /// </summary>
    public interface ISetThreadType
    {
        /// <summary>
        ///     Sets the category of the thread.
        /// </summary>
        /// <param name="type">The thread category.</param>
        void SetThreadType(ThreadType type);
    }
}