using System;
using Ezg.Package.Singleton;
using Supabase;
using Supabase.Gotrue;
using UnityEngine;
using Client = Supabase.Client;
using UniTask = Cysharp.Threading.Tasks.UniTask;

namespace Ezg.Core.Networking
{
    /// <summary>
    ///     Singleton manager for Supabase client operations.
    /// </summary>
    /// <typeparam name="T">The type of the inheriting MonoBehaviour.</typeparam>
    public class SupabaseManager<T> : Singleton<T> where T : MonoBehaviour
    {
        #region Fields

        /// <summary>
        ///     Indicates if the client is currently online.
        /// </summary>
        public bool IsOnline;

        private readonly NetworkStatus _networkStatus = new();
        private Client? _client;

        #endregion

        #region Public Methods

        /// <summary>
        ///     Gets the Supabase client instance.
        /// </summary>
        /// <returns>The Supabase client.</returns>
        public Client? Supabase()
        {
            return _client;
        }

        /// <summary>
        ///     Initializes the Supabase client.
        /// </summary>
        /// <returns>A task representing the operation.</returns>
        public async UniTask Init()
        {
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                IsOnline = false;
                return;
            }

            // --- Configuration ---
            var settings = Resources.Load<SupabaseSettings>("Supabase");
            var url = settings.SupabaseURL;
            var key = settings.SupabaseAnonKey;

            var options = new SupabaseOptions
            {
                AutoConnectRealtime = true,
                AutoRefreshToken = true
            };

            _client = new Client(url, key, options);

            try
            {
                await _client.InitializeAsync();
                IsOnline = _client.Auth.Online;
                Debug.Log("Supabase is init: " + IsOnline);
            }
            catch
            {
                Debug.LogError("Supabase is not init: " + IsOnline);
            }
        }

        /// <summary>
        ///     Refreshes the current session.
        /// </summary>
        /// <returns>A task representing the operation.</returns>
        public async UniTask RefreshSession()
        {
            if (_client != null) await _client.Auth.RefreshSession();
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Callback for debugging Supabase auth events.
        /// </summary>
        /// <param name="message">The debug message.</param>
        /// <param name="e">The exception, if any.</param>
        private void DebugListener(string message, Exception e)
        {
            Debug.Log(message, gameObject);

            if (e != null) Debug.LogException(e, gameObject);
        }

        /// <summary>
        ///     Clean up the client when the application quits.
        /// </summary>
        private void OnApplicationQuit()
        {
            if (_client != null)
            {
                _client.Auth.Shutdown();
                _client = null;
            }
        }

        #endregion
    }
}