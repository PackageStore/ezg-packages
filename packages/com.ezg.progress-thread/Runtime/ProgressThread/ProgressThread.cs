using System;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace Ezg.Package.ProgressThread
{
    /// <summary>
    ///     Specifies the timescale category of the thread (whether it respects or ignores Unity's timeScale).
    /// </summary>
    public enum ThreadType
    {
        /// <summary>
        ///     Thread time is affected by Time.timeScale modifications.
        /// </summary>
        ChangeTimeScale = 0,

        /// <summary>
        ///     Thread time ignores Time.timeScale modifications.
        /// </summary>
        IgnoreTimeScale = 1
    }

    /// <summary>
    ///     Predefined keys/names for different progress thread contexts/locations.
    /// </summary>
    public static class ProgressThreadLocation
    {
        /// <summary>Limited pack hero countdown key.</summary>
        public const string LimitedPackHero = "LimitedPackHero";

        /// <summary>Limited pack countdown key.</summary>
        public const string LimitedPack = "LimitedPack";

        /// <summary>Limited pack login countdown key.</summary>
        public const string LimitedPackLogin = "LimitedPackLogin";

        /// <summary>Limited pack watch ads countdown key.</summary>
        public const string LimitedPackWatchAds = "LimitedPackWatchAds";

        /// <summary>Limited gacha countdown key.</summary>
        public const string LimitedPackGacha = "LimitedPackGacha";

        /// <summary>Limited pack owned card countdown key.</summary>
        public const string LimitedPackOwnedCard = "LimitedPackOwnedCard";

        /// <summary>Countdown key for transition to next day.</summary>
        public const string TimeToNextDay = "TimeToNextDay";

        /// <summary>Countdown key for transition to next week.</summary>
        public const string TimeToNextWeek = "TimeToNextWeek";

        /// <summary>Countdown key for transition to next month.</summary>
        public const string TimeToNextMonth = "TimeToNextMonth";
    }

    /// <summary>
    ///     Thread implementation that wraps a repeating timeline and offers observable properties for cooldown values.
    /// </summary>
#if ODIN_INSPECTOR
    [ShowInInspector]
#endif
    public class ProgressThread
    {
        #region Private Methods

        /// <summary>
        ///     Internal loop callback advances observable cooldown values and triggers event actions.
        /// </summary>
        private void UpdateObservable()
        {
            if (_spawnThread.IsRunning)
            {
                CooldownCurrent.Value = _spawnThread.ElapsedSeconds;

                CallBackPerFrame?.Invoke();
                CallBackForMono?.Invoke();
                if (CooldownCurrent.Value >= CooldownMax.Value)
                {
                    CallBack?.Invoke();
                    CallBackForLoop?.Invoke();
                }
            }
        }

        #endregion

        #region Fields

#if ODIN_INSPECTOR
        [ShowInInspector]
#endif
        private IRepeatTimeline _spawnThread;

        private readonly TimelineEvent _spawnEvent = new();

        /// <summary>
        ///     Observable maximum cooldown/duration value.
        /// </summary>
#if ODIN_INSPECTOR
        [ShowInInspector]
#endif
        public ObservableFloat CooldownMax { get; } = new();

        /// <summary>
        ///     Observable current elapsed cooldown/duration value.
        /// </summary>
#if ODIN_INSPECTOR
        [ShowInInspector]
#endif
        public ObservableFloat CooldownCurrent { get; } = new();

        /// <summary>
        ///     Progress adapter object containing additional details like start/end timestamp.
        /// </summary>
        public IProgress Adapter;

        /// <summary>
        ///     The context or source of this progress thread.
        /// </summary>
#if ODIN_INSPECTOR
        [ShowInInspector]
#endif
        public object source;

        /// <summary>
        ///     The target duration/cooldown in seconds.
        /// </summary>
        public float duration;

        private bool isInBattle;

        /// <summary>
        ///     Gets or sets the action to trigger on each loop completion.
        /// </summary>
        public Action CallBackForLoop { get; set; }

        /// <summary>
        ///     Gets or sets the action to trigger periodically for Mono updates.
        /// </summary>
        public Action CallBackForMono { get; set; }

        /// <summary>
        ///     Gets or sets the action to trigger every frame update tick.
        /// </summary>
        public Action CallBackPerFrame { get; set; }

        /// <summary>
        ///     The scale type of thread timeline.
        /// </summary>
        public ThreadType type;

        /// <summary>
        ///     Gets or sets the primary action to trigger upon cooldown completion.
        /// </summary>
        public Action CallBack { get; set; }

        #endregion

        #region Initialize

        /// <summary>
        ///     Initializes a new instance of the <see cref="ProgressThread" /> class.
        /// </summary>
        public ProgressThread()
        {
            _spawnEvent.callback += CallBack;

            Adapter = new AdaptedProgress
            {
                Current = CooldownCurrent,
                Max = CooldownMax
            };

            InitEvent();
        }

        /// <summary>
        ///     Binds the observable updating logic to the proper lifecycle event based on context.
        /// </summary>
        private void InitEvent()
        {
            var u = U.Instance;
            if (u == null) return;

            if (isInBattle)
            {
                u.GameplayLifeCycleUpdate += UpdateObservable;
                u.GameplayLifeNextSceneClear += _ThreadDisposeForService;
            }
            else
            {
                u.DontDestroyUpdate += UpdateObservable;
            }
        }

        /// <summary>
        ///     Unbinds the updating logic from lifecycle events.
        /// </summary>
        private void RemoveEvent()
        {
            var u = U.Instance;
            if (u == null) return;

            if (isInBattle)
            {
                u.GameplayLifeCycleUpdate -= UpdateObservable;
                u.GameplayLifeNextSceneClear -= _ThreadDisposeForService;
            }
            else
            {
                u.DontDestroyUpdate -= UpdateObservable;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Loads configuration details into this progress thread.
        /// </summary>
        /// <param name="seconds">Tick rate / step interval.</param>
        /// <param name="maxCoolDown">The maximum duration/cooldown.</param>
        /// <param name="source">The source identifier.</param>
        /// <param name="isInBattle">Whether this thread belongs in a battle lifecycle.</param>
        /// <param name="type">The category type of timeline updates.</param>
        /// <param name="startTime">Optional start epoch time.</param>
        /// <param name="endTime">Optional end epoch time.</param>
        public void LoadConfig(float seconds, float maxCoolDown, string source, bool isInBattle,
            ThreadType type = ThreadType.ChangeTimeScale,
            long startTime = -1, long endTime = -1)
        {
            this.isInBattle = isInBattle;
            this.source = source;
            this.type = type;
            duration = maxCoolDown;
            if (_spawnThread == null)
                if (isInBattle) _spawnThread = new RepeatTimelineInGame();
                else
                    _spawnThread = new RepeatTimeline();

            _spawnThread.SetThreadType(type);
            _spawnEvent.seconds = seconds;

            CooldownMax.Value = duration;
            Adapter.StartTime = startTime;
            Adapter.EndTime = endTime;
        }

        /// <summary>
        ///     Checks whether the thread timeline is currently running.
        /// </summary>
        /// <returns>True if active, false otherwise.</returns>
        public bool IsRunning()
        {
            return _spawnThread.IsRunning;
        }

        /// <summary>
        ///     Starts the underlying thread timeline. Called by service manager.
        /// </summary>
        public void _ThreadStartForService()
        {
            if (duration < 0) return;

            _spawnThread.Start();
        }

        /// <summary>
        ///     Restarts the underlying thread timeline. Called by service manager.
        /// </summary>
        public void _ThreadRestartForService()
        {
            if (duration < 0) return;

            _spawnThread.Restart();
        }

        /// <summary>
        ///     Stops the underlying thread timeline. Called by service manager.
        /// </summary>
        public void _ThreadStopForService()
        {
            _spawnThread.Stop();
        }

        /// <summary>
        ///     Disposes the thread timeline and unbinds event listeners.
        /// </summary>
        public void _ThreadDisposeForService()
        {
            _spawnThread.Dispose();
            RemoveEvent();

#if UNITY_EDITOR
            ThreadService.threads?.Remove(this);
#endif
        }

        /// <summary>
        ///     Stops and resets the thread timeline.
        /// </summary>
        public void Reset()
        {
            _ThreadStopForService();
            _spawnThread.Reset();
        }

        /// <summary>
        ///     Calculates the remaining time left on the cooldown.
        /// </summary>
        /// <returns>Remaining duration in seconds.</returns>
        public float GetTimeRemaining()
        {
            return CooldownMax.Value - CooldownCurrent.Value;
        }

        /// <summary>
        ///     Sets the maximum cooldown duration limit.
        /// </summary>
        /// <param name="value">The new max limit.</param>
        public void SetMaxCooldown(float value)
        {
            CooldownMax.Value = value;
        }

        #endregion
    }
}