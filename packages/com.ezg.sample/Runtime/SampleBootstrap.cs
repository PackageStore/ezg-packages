using UnityEngine;

namespace Ezg.Sample
{
    /// <summary>
    /// Trivial bootstrap proving the com.ezg.sample package compiles after being
    /// installed from the Easygoing scoped registry. Safe to delete.
    /// </summary>
    public static class SampleBootstrap
    {
        public const string Version = "0.0.1";

        public static void Ping()
        {
            Debug.Log($"[EZG] com.ezg.sample v{Version} loaded from scoped registry.");
        }
    }
}
