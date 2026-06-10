using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ezg.Package.ProgressThread
{
    /// <summary>
    ///     Type of time tracking.
    /// </summary>
    public enum TimeType
    {
        /// <summary>Cooldown time tracking type.</summary>
        Cooldown = 1
    }

    /// <summary>
    ///     Extension and service class for creating, configuring, and managing the lifecycle of ProgressThreads.
    /// </summary>
    public static class ThreadService
    {
        #region Fields

#if UNITY_EDITOR
        /// <summary>
        ///     Global list of active progress threads tracked in the editor.
        /// </summary>
        public static List<ProgressThread> threads;
#endif

        #endregion

        #region Private Methods

        /// <summary>
        ///     Spawns a new progress thread instance and tracks it in editor list.
        /// </summary>
        /// <returns>The spawned ProgressThread.</returns>
        private static ProgressThread GetNewProgressThread()
        {
            var progressThread = new ProgressThread();
#if UNITY_EDITOR
            threads ??= new List<ProgressThread>();
            threads.Add(progressThread);
#endif
            return progressThread;
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Allows a repeating/looping progress thread in battle.
        /// </summary>
        /// <param name="thread">The thread to initialize.</param>
        /// <param name="interval">The timer interval in seconds.</param>
        /// <param name="source">The source context.</param>
        /// <param name="threadType">The type of timescale modification.</param>
        /// <param name="startTime">The start epoch timestamp.</param>
        /// <param name="endTime">The end epoch timestamp.</param>
        /// <returns>The configured progress thread.</returns>
        public static ProgressThread TimerInBattle(this ProgressThread thread, float interval, string source = "",
            ThreadType threadType = ThreadType.ChangeTimeScale,
            long startTime = -1, long endTime = -1)
        {
            if (thread != null)
            {
                thread.Dispose();
                thread = null;
            }

            thread = GetNewProgressThread();
            thread.LoadConfig(0, interval, source, true, threadType, startTime, endTime);
            return thread;
        }

        /// <summary>
        ///     Creates a progress thread in battle that runs until a specific timestamp and stops.
        /// </summary>
        /// <param name="thread">The thread to initialize.</param>
        /// <param name="rangeTime">The duration in seconds.</param>
        /// <param name="source">The source context.</param>
        /// <param name="threadType">The type of timescale modification.</param>
        /// <param name="startTime">The start epoch timestamp.</param>
        /// <param name="endTime">The end epoch timestamp.</param>
        /// <returns>The configured progress thread.</returns>
        public static ProgressThread IntervalInBattle(this ProgressThread thread, float rangeTime, string source = "",
            ThreadType threadType = ThreadType.ChangeTimeScale,
            long startTime = -1, long endTime = -1)
        {
            if (thread != null)
            {
                thread.Dispose();
                thread = null;
            }

            thread = GetNewProgressThread();
            thread.LoadConfig(0, rangeTime, source, true, threadType, startTime, endTime);
            thread.CallBackForLoop = thread._ThreadRestartForService;
            return thread;
        }

        /// <summary>
        ///     Allows a repeating/looping progress thread.
        /// </summary>
        /// <param name="thread">The thread to initialize.</param>
        /// <param name="interval">The timer interval in seconds.</param>
        /// <param name="source">The source context.</param>
        /// <param name="threadType">The type of timescale modification.</param>
        /// <param name="startTime">The start epoch timestamp.</param>
        /// <param name="endTime">The end epoch timestamp.</param>
        /// <returns>The configured progress thread.</returns>
        public static ProgressThread Timer(this ProgressThread thread, float interval, string source = "",
            ThreadType threadType = ThreadType.ChangeTimeScale,
            long startTime = -1, long endTime = -1)
        {
            if (thread != null)
            {
                thread.Dispose();
                thread = null;
            }

            thread = GetNewProgressThread();
            thread.LoadConfig(0, interval, source, false, threadType, startTime, endTime);
            return thread;
        }

        /// <summary>
        ///     Creates a progress thread that runs until a specific timestamp and stops.
        /// </summary>
        /// <param name="thread">The thread to initialize.</param>
        /// <param name="rangeTime">The duration in seconds.</param>
        /// <param name="source">The source context.</param>
        /// <param name="threadType">The type of timescale modification.</param>
        /// <param name="startTime">The start epoch timestamp.</param>
        /// <param name="endTime">The end epoch timestamp.</param>
        /// <returns>The configured progress thread.</returns>
        public static ProgressThread Interval(this ProgressThread thread, float rangeTime, string source = "",
            ThreadType threadType = ThreadType.ChangeTimeScale,
            long startTime = -1, long endTime = -1)
        {
            if (thread != null)
            {
                thread.Dispose();
                thread = null;
            }

            thread = GetNewProgressThread();
            thread.LoadConfig(0, rangeTime, source, false, threadType, startTime, endTime);
            thread.CallBackForLoop = thread._ThreadRestartForService;
            return thread;
        }

        /// <summary>
        ///     Subscribes a completion callback action to the progress thread.
        /// </summary>
        /// <param name="thread">The progress thread.</param>
        /// <param name="callBack">The action to execute upon completion.</param>
        /// <returns>The progress thread.</returns>
        public static ProgressThread Subscribe(this ProgressThread thread, Action callBack)
        {
            if (thread == null) return null;
            thread.CallBack = () =>
            {
                thread?.Stop();
                callBack?.Invoke();
            };

            return thread;
        }

        /// <summary>
        ///     Subscribes a completion callback action to a newly created progress thread.
        /// </summary>
        /// <param name="callBack">The action to execute upon completion.</param>
        /// <returns>The newly created progress thread.</returns>
        public static ProgressThread Subscribe(Action callBack)
        {
            var thread = new ProgressThread();
            thread.CallBack = () =>
            {
                thread?.Stop();
                callBack?.Invoke();
            };

            return thread;
        }

        /// <summary>
        ///     Subscribes a callback to run on every frame update of the progress thread.
        /// </summary>
        /// <param name="thread">The progress thread.</param>
        /// <param name="callBackPerFrame">The frame update callback action.</param>
        /// <returns>The progress thread.</returns>
        public static ProgressThread SubscribePerFrame(this ProgressThread thread, Action callBackPerFrame)
        {
            if (thread == null) return null;
            thread.CallBackPerFrame = callBackPerFrame;
            return thread;
        }

        /// <summary>
        ///     Binds the progress thread lifecycle to a MonoBehaviour; if the MonoBehaviour becomes null or disabled, the thread
        ///     is disposed.
        /// </summary>
        /// <param name="thread">The progress thread.</param>
        /// <param name="mono">The target MonoBehaviour to track.</param>
        /// <returns>The progress thread.</returns>
        public static ProgressThread AddTo(this ProgressThread thread, MonoBehaviour mono)
        {
            if (thread == null) return null;
            thread.CallBackForMono = () =>
            {
                if (mono == null || !mono.isActiveAndEnabled) thread._ThreadDisposeForService();
            };
            return thread;
        }

        /// <summary>
        ///     Starts the progress thread.
        /// </summary>
        /// <param name="thread">The progress thread.</param>
        /// <returns>The progress thread.</returns>
        public static ProgressThread Start(this ProgressThread thread)
        {
            thread?._ThreadStartForService();
            return thread;
        }

        /// <summary>
        ///     Stops the progress thread.
        /// </summary>
        /// <param name="thread">The progress thread.</param>
        /// <returns>The progress thread.</returns>
        public static ProgressThread Stop(this ProgressThread thread)
        {
            thread?._ThreadStopForService();
            return thread;
        }

        /// <summary>
        ///     Restarts the progress thread.
        /// </summary>
        /// <param name="thread">The progress thread.</param>
        /// <returns>The progress thread.</returns>
        public static ProgressThread Restart(this ProgressThread thread)
        {
            thread?._ThreadRestartForService();
            return thread;
        }

        /// <summary>
        ///     Disposes the progress thread.
        /// </summary>
        /// <param name="thread">The progress thread.</param>
        /// <returns>The progress thread.</returns>
        public static ProgressThread Dispose(this ProgressThread thread)
        {
            thread?._ThreadDisposeForService();
            return thread;
        }

        /// <summary>
        ///     Attaches a visual progress view to display the thread's state.
        /// </summary>
        /// <param name="thread">The progress thread.</param>
        /// <param name="view">The progress view component.</param>
        /// <returns>The progress thread.</returns>
        public static ProgressThread SetView(this ProgressThread thread, ProgressView view)
        {
            if (view != null)
            {
                view.gameObject.SetActive(true);
                view.SetSource(thread?.Adapter);
            }

            return thread;
        }

        /// <summary>
        ///     Sets the source object context for the progress thread.
        /// </summary>
        /// <param name="thread">The progress thread.</param>
        /// <param name="source">The source context object.</param>
        /// <returns>The progress thread.</returns>
        public static ProgressThread SetSource(this ProgressThread thread, object source)
        {
            if (thread == null) return null;
            thread.source = source;
            return thread;
        }

        #endregion
    }
}