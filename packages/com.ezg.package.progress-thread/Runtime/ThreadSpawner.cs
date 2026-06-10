using System.Collections.Generic;
using DG.Tweening;

namespace Ezg.Package.ProgressThread
{
    /// <summary>
    ///     Spawns and manages life cycles of threads and stopwatches, ensuring they pause or resume correctly with application
    ///     state.
    /// </summary>
    public class ThreadSpawner : SingletonTemporary<ThreadSpawner>
    {
        #region Fields

        /// <summary>
        ///     Collection of actively running threads.
        /// </summary>
        public Dictionary<int, IThread> threadsRelease = new();

        /// <summary>
        ///     Collection of actively running stopwatches.
        /// </summary>
        public Dictionary<int, Stopwatch> stopwatchsRelease = new();

        private int threadCounter;
        private int stopwatchConter;

        #endregion

        #region Initialize

        /// <summary>
        ///     Called when the MonoBehaviour is destroyed. Kills all released threads and stopwatches.
        /// </summary>
        protected override void OnDestroy()
        {
            base.OnDestroy();
            KillAllReleased();
        }

        /// <summary>
        ///     Binds to the application pause event to pause or resume all threads/stopwatches.
        /// </summary>
        /// <param name="pauseStatus">True if the application is paused, false otherwise.</param>
        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                PauseAllReleased();
                m_ShuttingDown = true;
                return;
            }

            m_ShuttingDown = false;
            ContinueAllReleased();
        }

        /// <summary>
        ///     Called when the application is quitting. Cleans up DOTween tweens.
        /// </summary>
        protected override void OnApplicationQuit()
        {
            base.OnApplicationQuit();
            // KillAll() or PauseAll() ... let check and replace
            KillAllReleased();
        }

        /// <summary>
        ///     Binds to the application focus event to resume or pause all threads/stopwatches.
        /// </summary>
        /// <param name="hasFocus">True if application has focus, false otherwise.</param>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                ContinueAllReleased();
                return;
            }

            PauseAllReleased();
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Spawns a new stopwatch and registers it for lifecycle management.
        /// </summary>
        /// <returns>The spawned Stopwatch instance.</returns>
        public Stopwatch GetAStopwatch()
        {
            var stopwatch = new Stopwatch();
            stopwatch.OnKill += RemoveRelease;
            stopwatchConter++;
            stopwatch.ID = stopwatchConter;
            stopwatchsRelease.Add(stopwatch.ID, stopwatch);
            return stopwatchsRelease[stopwatch.ID];
        }

        /// <summary>
        ///     Spawns a new thread and registers it for lifecycle management.
        /// </summary>
        /// <returns>The spawned thread instance.</returns>
        public IThread GetAThread()
        {
            IThread thread = new ThreadBase();
            threadCounter++;
            thread.SetId(threadCounter);
            threadsRelease.Add(thread.ID, thread);
            thread.OnKill += RemoveRelease;
            return threadsRelease[threadCounter];
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Unregisters a stopwatch from management when it is killed.
        /// </summary>
        /// <param name="stopwatch">The stopwatch to unregister.</param>
        private void RemoveRelease(Stopwatch stopwatch)
        {
            if (m_ShuttingDown) return;
            stopwatchsRelease.Remove(stopwatch.ID);
        }

        /// <summary>
        ///     Unregisters a thread from management when it is killed.
        /// </summary>
        /// <param name="thread">The thread to unregister.</param>
        private void RemoveRelease(IThread thread)
        {
            if (m_ShuttingDown) return;
            threadsRelease.Remove(thread.ID);
        }

        /// <summary>
        ///     Kills all registered threads and stopwatches using DOTween.Kill.
        /// </summary>
        private void KillAllReleased()
        {
            foreach (var thread in threadsRelease) DOTween.Kill(thread.Value.ID);

            foreach (var stopwatch in stopwatchsRelease) DOTween.Kill(stopwatch.Value.ID);
        }

        /// <summary>
        ///     Pauses all registered threads and stopwatches.
        /// </summary>
        private void PauseAllReleased()
        {
            foreach (var thread in threadsRelease) thread.Value.Pause();

            foreach (var stopwatch in stopwatchsRelease) stopwatch.Value.Stop();
        }

        /// <summary>
        ///     Resumes or starts all registered threads and stopwatches.
        /// </summary>
        private void ContinueAllReleased()
        {
            foreach (var thread in threadsRelease) thread.Value.Play();
            foreach (var stopwatch in stopwatchsRelease) stopwatch.Value.Start();
        }

        #endregion
    }
}