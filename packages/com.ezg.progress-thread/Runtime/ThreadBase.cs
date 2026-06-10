using System;
using DG.Tweening;

namespace Ezg.Package.ProgressThread
{
    /// <summary>
    ///     Base class responsible for running tasks in parallel with Unity's main thread.
    ///     Temporarily utilizes DOTween Sequence to run parallel updates and callbacks.
    /// </summary>
    public class ThreadBase : IThread
    {
        #region Initialize

        /// <summary>
        ///     Initializes a new instance of the <see cref="ThreadBase" /> class.
        /// </summary>
        public ThreadBase()
        {
            _sequence = null;
            _sequence = DOTween.Sequence();
            initialized = false;
            // _sequence.SetRelative();
            _sequence.SetAutoKill(false);
            _sequence.OnRewind(() => { OnStart?.Invoke(this); });
        }

        #endregion

        #region Fields

        protected int id;
        protected float duration;
        protected bool initialized;
        protected Sequence _sequence;

        private Action<IThread> onStart;
        private Action<IThread> onPlay;
        private Action<IThread> onUpdate;
        private Action<IThread> onComplete;
        private Action<IThread> onPause;
        private Action<IThread> onKill;

        /// <summary>
        ///     Gets the elapsed duration of the running thread.
        /// </summary>
        public float Elapsed => _sequence.Elapsed();

        /// <summary>
        ///     Gets the unique identifier of the thread.
        /// </summary>
        public int ID => id;

        /// <summary>
        ///     Gets whether the thread has been initialized.
        /// </summary>
        public bool Initialized => initialized;

        /// <summary>
        ///     Event fired when the thread starts.
        /// </summary>
        public event Action<IThread> OnStart;

        /// <summary>
        ///     Event fired when the thread plays.
        /// </summary>
        public event Action<IThread> OnPlay;

        /// <summary>
        ///     Event fired on each update cycle.
        /// </summary>
        public event Action<IThread> OnUpdate;

        /// <summary>
        ///     Event fired when the thread completes.
        /// </summary>
        public event Action<IThread> OnComplete;

        /// <summary>
        ///     Event fired when the thread is paused.
        /// </summary>
        public event Action<IThread> OnPause;

        /// <summary>
        ///     Event fired when the thread is killed.
        /// </summary>
        public event Action<IThread> OnKill;

        #endregion

        #region Public Methods

        /// <summary>
        ///     Sets the unique identifier for the thread.
        /// </summary>
        /// <param name="value">The ID value.</param>
        /// <returns>The current thread instance for chaining.</returns>
        public IThread SetId(int value)
        {
            id = value;
            _sequence.SetId(id);
            return this;
        }

        /// <summary>
        ///     Sets the duration of the thread interval or delay.
        /// </summary>
        /// <param name="sec">Duration in seconds.</param>
        /// <returns>The current thread instance for chaining.</returns>
        public IThread SetDuration(float sec)
        {
            duration = sec;
            _sequence.PrependInterval(duration);
            return this;
        }

        /// <summary>
        ///     Plays or resumes running the thread.
        /// </summary>
        public void Play()
        {
            if (!initialized) InitNew();
            _sequence.Play();
        }

        /// <summary>
        ///     Kills the thread, stopping all execution and callbacks.
        /// </summary>
        public void Kill()
        {
            DOTween.Kill(ID);
            // _sequence.Kill();
        }

        /// <summary>
        ///     Pauses the thread execution.
        /// </summary>
        public void Pause()
        {
            _sequence.Pause();
        }

        /// <summary>
        ///     Restarts the thread from the beginning.
        /// </summary>
        public void Replay()
        {
            _sequence.Restart();
        }

        /// <summary>
        ///     Resets the thread data, killing the current play sequence if any, and re-initializing.
        /// </summary>
        public void ResetData()
        {
            if (_sequence.IsPlaying()) _sequence.Kill();
            ResetActions();
            InitNew();
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Initializes new action delegates and binds them to the DOTween sequence.
        /// </summary>
        private void InitNew()
        {
            onStart = OnStart;
            onPlay = OnPlay;
            onUpdate = OnUpdate;
            onComplete = OnComplete;
            onPause = OnPause;
            onKill = OnKill;
            _sequence.OnStart(() => { onStart?.Invoke(this); })
                .OnPlay(() => { onPlay?.Invoke(this); })
                .OnUpdate(() => { onUpdate?.Invoke(this); })
                .OnComplete(() => { onComplete?.Invoke(this); })
                .OnPause(() => { onPause?.Invoke(this); })
                .OnKill(() =>
                {
                    onKill?.Invoke(this);
                    ResetActions();
                });

            initialized = true;
        }

        /// <summary>
        ///     Resets the thread action delegates and clears the duration.
        /// </summary>
        private void ResetActions()
        {
            duration = 0;
            onStart = null;
            onPlay = null;
            onUpdate = null;
            onComplete = null;
            onPause = null;
            onKill = null;
        }

        #endregion
    }
}