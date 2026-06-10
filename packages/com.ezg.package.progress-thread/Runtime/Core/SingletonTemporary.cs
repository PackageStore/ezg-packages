using UnityEngine;

namespace Ezg.Package.ProgressThread
{
    /// <summary>
    ///     A temporary generic singleton class for MonoBehaviours that automatically creates an instance if none exists.
    /// </summary>
    /// <typeparam name="T">The type of the MonoBehaviour.</typeparam>
    public class SingletonTemporary<T> : MonoBehaviour where T : MonoBehaviour
    {
        #region Fields

        protected static bool m_ShuttingDown;
        private static readonly object m_Lock = new();
        private static T m_Instance;

        /// <summary>
        ///     Gets the single instance of this class.
        /// </summary>
        public static T Instance
        {
            get
            {
                if (m_ShuttingDown)
                {
                    Debug.LogWarning("[Singleton] Instance '" + typeof(T) +
                                     "' already destroyed. Returning null.");
                    return null;
                }

                lock (m_Lock)
                {
                    if (m_Instance == null)
                    {
                        // Search for existing instance.
                        m_Instance = (T)FindObjectOfType(typeof(T));

                        // Create new instance if one doesn't already exist.
                        if (m_Instance == null)
                        {
                            // Need to create a new GameObject to attach the singleton to.
                            var singletonObject = new GameObject();
                            m_Instance = singletonObject.AddComponent<T>();
                            singletonObject.name = typeof(T) + " (Singleton)";
                        }
                    }

                    return m_Instance;
                }
            }
        }

        #endregion

        #region Initialize

        /// <summary>
        ///     Called when the application is quitting to flag that the instance is shutting down.
        /// </summary>
        protected virtual void OnApplicationQuit()
        {
            m_ShuttingDown = true;
        }

        /// <summary>
        ///     Called when the MonoBehaviour is destroyed.
        /// </summary>
        protected virtual void OnDestroy()
        {
        }

        #endregion
    }
}