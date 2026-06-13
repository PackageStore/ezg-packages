using System;
using UnityEngine;

namespace Ezg.Package.Singleton
{
    /// <summary>
    ///     A generic MonoBehaviour-based Singleton implementation that automatically creates its GameObject if it does not
    ///     exist.
    /// </summary>
    /// <typeparam name="T">The type of the MonoBehaviour singleton.</typeparam>
    public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        #region Public Methods

        /// <summary>
        ///     Gets the single instance of T, locating it in the scene or spawning a new one if necessary.
        /// </summary>
        public static T Instance
        {
            get
            {
                if (!Application.isPlaying)
                    // Always allow editor lookups and never auto-create singleton outside Play Mode.
                    _isShuttingDown = false;

                if (_isShuttingDown) return null;

                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = (T)FindObjectOfType(typeof(T));

                        if (FindObjectsOfType(typeof(T)).Length > 1) return _instance;

                        if (_instance == null)
                        {
                            if (!Application.isPlaying) return null;

                            var singleton = new GameObject();
                            _instance = singleton.AddComponent(GetTypes()) as T;
                            singleton.name = "(singleton)" + typeof(T);
                        }

                        DontDestroyOnLoad(_instance.gameObject);
                    }

                    return _instance;
                }
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Gets the Type object of T.
        /// </summary>
        /// <returns>The Type of T.</returns>
        private static Type GetTypes()
        {
            return typeof(T);
        }

        #endregion

        #region Fields

        private static T _instance;
        private static readonly object _lock = new();
        private static bool _isShuttingDown;

        #endregion

        #region Initialize

        /// <summary>
        ///     Initializes the singleton instance.
        /// </summary>
        public void Init()
        {
        }

        /// <summary>
        ///     Sets the shutdown flag and cleans up the instance reference when the application quits.
        /// </summary>
        protected virtual void OnApplicationQuit()
        {
            _isShuttingDown = true;
            _instance = null;
        }

        /// <summary>
        ///     Clears the instance reference if this specific GameObject is destroyed.
        /// </summary>
        protected virtual void OnDestroy()
        {
            if (_instance != null && _instance.gameObject == gameObject) _instance = null;
        }

        #endregion
    }
}