using Newtonsoft.Json;
using UnityEngine;

namespace Ezg.Core.Networking
{
    /// <summary>
    ///     Static class for managing Cloudflare DB endpoints.
    /// </summary>
    public static class CloudflareDB
    {
        #region Public Methods

        /// <summary>
        ///     Creates a new query for the specified endpoint.
        /// </summary>
        /// <typeparam name="T">The data type response.</typeparam>
        /// <param name="endPoint">The endpoint name.</param>
        /// <returns>A CloudflareQuery instance.</returns>
        public static CloudflareQuery<T> Endpoint<T>(string endPoint)
        {
            return new CloudflareQuery<T>(endPoint);
        }

        #endregion

        #region Fields

        private static CloudflareSettings _settings;

        /// <summary>
        ///     The base URL loaded from the Cloudflare asset config.
        /// </summary>
        public static string BaseUrl => Settings.WorkerURL;

        /// <summary>
        ///     Optional API secret key loaded from the Cloudflare asset config.
        /// </summary>
        public static string ApiSecretKey => Settings.ApiSecretKey;

        private static CloudflareSettings Settings
        {
            get
            {
                if (_settings == null)
                    _settings = Resources.Load<CloudflareSettings>("Cloudflare");
                return _settings;
            }
        }

        #endregion
    }

    /// <summary>
    ///     Standard API response wrapper for Cloudflare worker requests.
    /// </summary>
    /// <typeparam name="T">The data type contained in the response.</typeparam>
    public class ApiResponse<T>
    {
        #region Fields

        [JsonProperty("status")] public int Status;

        [JsonProperty("data")] public T Data;

        [JsonProperty("error")] public string Error;

        #endregion
    }
}