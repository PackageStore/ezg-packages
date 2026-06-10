using System;
using DG.Tweening;
using UnityEngine;

namespace Ezg.Package.ProgressThread
{
    /// <summary>
    ///     Represents a timer or stopwatch that triggers events at start, completion, interval ticks, stopping, and
    ///     destruction using DOTween.
    /// </summary>
    public class Stopwatch : IStopwatch<Stopwatch>
    {
        #region Private Methods

        /// <summary>
        ///     Resets the internal variables and unbinds actions.
        /// </summary>
        private void ResetData()
        {
            duration = 3;
            interval = 1;
            _initialized = false;
            _actionOnStart = null;
            _actionOnComplete = null;
            _actionOnInterval = null;
            _actionOnStop = null;
            _actionOnKill = null;
        }

        #endregion

        #region Fields

        private Action<Stopwatch> _actionOnStart;
        private Action<Stopwatch> _actionOnComplete;
        private Action<Stopwatch> _actionOnInterval;
        private Action<Stopwatch> _actionOnStop;
        private Action<Stopwatch> _actionOnKill;

        private readonly Sequence _sequence;
        private bool _initialized;
        private int id;
        private float duration;
        private float interval;
        private float t;

        /// <summary>
        ///     Gets or sets the unique ID of the stopwatch.
        /// </summary>
        public int ID
        {
            get => id;
            set
            {
                id = value;
                _sequence.SetId(value);
            }
        }

        /// <summary>
        ///     Gets whether the stopwatch is currently running.
        /// </summary>
        public bool IsRunning => _sequence.IsPlaying();

        /// <summary>
        ///     Gets the elapsed time since the stopwatch started.
        /// </summary>
        public float Elapsed => _sequence.Elapsed();

        /// <summary>
        ///     Event fired when the stopwatch starts.
        /// </summary>
        public event Action<Stopwatch> OnStart;

        /// <summary>
        ///     Event fired when the stopwatch completes its duration.
        /// </summary>
        public event Action<Stopwatch> OnComplete;

        /// <summary>
        ///     Event fired at each interval tick.
        /// </summary>
        public event Action<Stopwatch> OnInterval;

        /// <summary>
        ///     Event fired when the stopwatch stops/pauses.
        /// </summary>
        public event Action<Stopwatch> OnStop;

        /// <summary>
        ///     Event fired when the stopwatch is killed/destroyed.
        /// </summary>
        public event Action<Stopwatch> OnKill;

        #endregion

        #region Initialize

        /// <summary>
        ///     Initializes a new instance of the <see cref="Stopwatch" /> class.
        /// </summary>
        public Stopwatch()
        {
            ResetData();
            _sequence = DOTween.Sequence();
            _sequence.SetAutoKill(false);
            //  ThreadSpawner.Instance.AddStopWatch(this);
        }

        /// <summary>
        ///     Initializes DOTween sequence callbacks and binds action handlers.
        /// </summary>
        private void InitActions()
        {
            _initialized = true;
            _actionOnStart = OnStart;
            _actionOnComplete = OnComplete;
            _actionOnInterval = OnInterval;
            _actionOnStop = OnStop;
            _actionOnKill = OnKill;

            _sequence.OnPlay(() => { _actionOnStart?.Invoke(this); })
                .OnComplete(() => { _actionOnComplete?.Invoke(this); })
                .OnPause(() => { _actionOnStop?.Invoke(this); })
                .OnUpdate(() =>
                {
                    if (t > 0)
                    {
                        t -= Time.deltaTime;
                    }
                    else
                    {
                        t = interval;
                        _actionOnInterval?.Invoke(this);
                    }
                })
                .OnKill(() =>
                {
                    _actionOnKill?.Invoke(this);
                    ResetData();
                });
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Sets the total duration of the stopwatch.
        /// </summary>
        /// <param name="second">The duration in seconds.</param>
        /// <returns>The current stopwatch instance for chaining.</returns>
        public Stopwatch SetDuration(float second)
        {
            duration = second;
            _sequence.PrependInterval(duration);
            return this;
        }

        /// <summary>
        ///     Sets the tick interval of the stopwatch.
        /// </summary>
        /// <param name="second">The interval in seconds.</param>
        /// <returns>The current stopwatch instance for chaining.</returns>
        public Stopwatch SetInterval(float second = 1)
        {
            if (second <= 0) second = 1;
            interval = second;
            return this;
        }

        /// <summary>
        ///     Starts or resumes running the stopwatch.
        /// </summary>
        public void Start()
        {
            if (!_initialized) InitActions();
            _sequence.Play();
        }

        /// <summary>
        ///     Pauses/stops running the stopwatch.
        /// </summary>
        public void Stop()
        {
            _sequence.Pause();
        }

        /// <summary>
        ///     Restarts the stopwatch from the beginning.
        /// </summary>
        public void Restart()
        {
            if (!_initialized) Start();
            else
                _sequence.Restart();
        }

        /// <summary>
        ///     Resets and restarts the stopwatch, restoring play/pause state.
        /// </summary>
        public void Reset()
        {
            var isplaying = IsRunning;
            Restart();
            if (!isplaying) Stop();
        }

        #endregion
    }
}