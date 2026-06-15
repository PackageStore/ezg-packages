using System;
using System.IO;
using Newtonsoft.Json;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;
using UnityEngine;

namespace Ezg.Core.Networking
{
    /// <summary>
    ///     This is a simple implementation of IGotrueSessionPersistence that uses Unity's
    ///     Application.persistentDataPath to store the session.
    ///     This implementation works on iOS, Android, as well as desktop platforms and the Unity Editor.
    ///     Because this implementation uses the .NET System.IO packages, it will work even if SaveSession is called
    ///     by the refresh background thread.
    ///     Don't use the Unity PlayerPrefs to store the session - you can only use PlayerPrefs from the main UI thread,
    ///     and this will break the refresh background thread.
    /// </summary>
    public class UnitySession : IGotrueSessionPersistence<Session>
    {
        #region Private Methods

        /// <summary>
        ///     Gets the file path for the session cache.
        /// </summary>
        /// <returns>The full file path.</returns>
        private static string FilePath()
        {
            const string cacheFileName = "gotrue.cache";
            return Path.Combine(Application.persistentDataPath, cacheFileName);
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Saves the session to persistent storage.
        /// </summary>
        /// <param name="session">The session to save.</param>
        public void SaveSession(Session? session)
        {
            if (session == null)
            {
                DestroySession();
                return;
            }

            try
            {
                var filePath = FilePath();
                var str = JsonConvert.SerializeObject(session);
                using StreamWriter file = new(filePath);
                file.Write(str);
            }
            catch (Exception e)
            {
                Debug.Log(e.Message);
                Debug.LogException(e);
                throw;
            }
        }

        /// <summary>
        ///     Destroys the saved session from persistent storage.
        /// </summary>
        public void DestroySession()
        {
            var filePath = FilePath();
            if (File.Exists(filePath)) File.Delete(filePath);
        }

        /// <summary>
        ///     Loads the session from persistent storage.
        /// </summary>
        /// <returns>The loaded session, or null if not found.</returns>
        public Session? LoadSession()
        {
            var filePath = FilePath();

            if (!File.Exists(filePath)) return null;

            using StreamReader file = new(filePath);
            var sessionJson = file.ReadToEnd();

            if (string.IsNullOrEmpty(sessionJson)) return null;

            try
            {
                return JsonConvert.DeserializeObject<Session>(sessionJson);
            }
            catch (Exception e)
            {
                Debug.Log(e.Message);
                Debug.LogException(e);
                return null;
            }
        }

        #endregion
    }
}