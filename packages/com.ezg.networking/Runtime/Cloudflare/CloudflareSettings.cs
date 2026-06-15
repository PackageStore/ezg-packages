using UnityEngine;

namespace Ezg.Core.Networking
{
    /// <summary>
    ///     ScriptableObject for storing Cloudflare Worker configuration settings.
    /// </summary>
    [CreateAssetMenu(fileName = "Cloudflare", menuName = "Ezg/Networking/Cloudflare/Cloudflare Settings", order = 1)]
    public class CloudflareSettings : ScriptableObject
    {
        #region Fields

        /// <summary>
        ///     The base URL of the Cloudflare Worker.
        /// </summary>
        public string WorkerURL = null!;

        /// <summary>
        ///     Optional API secret key sent as Authorization header.
        /// </summary>
        public string ApiSecretKey = null!;

        #endregion
    }
}