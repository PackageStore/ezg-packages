using System;

namespace Ezg.Package.ProgressThread
{
    /// <summary>
    ///     Global lifecycle update manager class providing game-wide and scene-wide tick events.
    /// </summary>
    public class U : SingletonTemporary<U>
    {
        #region Fields

        /// <summary>
        ///     Event triggered during gameplay lifecycle updates.
        /// </summary>
        public Action GameplayLifeCycleUpdate;

        /// <summary>
        ///     Event triggered when gameplay transitions and clears for the next scene.
        /// </summary>
        public Action GameplayLifeNextSceneClear;

        /// <summary>
        ///     Event triggered continuously throughout the application lifetime.
        /// </summary>
        public Action DontDestroyUpdate;

        #endregion

        #region Initialize

        /// <summary>
        ///     Called when the MonoBehaviour is destroyed.
        /// </summary>
        private void OnDestroy()
        {
            DontDestroyUpdate = null;
        }

        /// <summary>
        ///     Clears frame updates when destroyed.
        /// </summary>
        private void OnDestroyPerframe()
        {
            DontDestroyUpdate = null;
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Standard Unity Update loop invokes registered tick actions.
        /// </summary>
        public void Update()
        {
            GameplayLifeCycleUpdate?.Invoke();
            DontDestroyUpdate?.Invoke();
        }

        /// <summary>
        ///     Clears all listeners from the gameplay lifecycle update event.
        /// </summary>
        public void ClearGameplayLifeCycleUpdate()
        {
            GameplayLifeCycleUpdate = null;
        }

        #endregion
    }
}