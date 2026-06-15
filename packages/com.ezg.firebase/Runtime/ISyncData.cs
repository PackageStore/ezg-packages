using System;
using Cysharp.Threading.Tasks;
using UnityEngine.Events;

namespace Ezg.Core.Firebase
{
    /// <summary>
    ///     Interface for syncing player data with a backend/server.
    /// </summary>
    public interface ISyncData
    {
        /// <summary>
        ///     Pushes player data to the server.
        /// </summary>
        /// <param name="data">The serialized player data string.</param>
        /// <param name="fileName">The name of the file to save the data in.</param>
        /// <param name="failedAction">Callback executed if the push fails.</param>
        /// <param name="successAction">Callback executed if the push succeeds.</param>
        /// <returns>A UniTask representing the asynchronous operation.</returns>
        public UniTask PushPlayerData(string data, string fileName, UnityAction failedAction,
            UnityAction successAction);

        /// <summary>
        ///     Deletes the specified data file from the server.
        /// </summary>
        /// <param name="fileName">The name of the file to delete.</param>
        /// <param name="failedAction">Callback executed if deletion fails.</param>
        /// <param name="successAction">Callback executed if deletion succeeds.</param>
        /// <returns>A UniTask representing the asynchronous operation.</returns>
        public UniTask DeleteData(string fileName, UnityAction failedAction, UnityAction successAction);

        /// <summary>
        ///     Retrieves player data from the server.
        /// </summary>
        /// <param name="fileName">The name of the file to retrieve data from.</param>
        /// <returns>A UniTask returning the raw byte array of player data.</returns>
        public UniTask<byte[]> GetPlayerData(string fileName);

        /// <summary>
        ///     Gets the creation time of the data.
        /// </summary>
        /// <returns>The creation DateTime.</returns>
        public DateTime GetTimeCreateData();
    }
}