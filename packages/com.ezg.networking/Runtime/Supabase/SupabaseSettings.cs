using UnityEngine;

namespace Ezg.Core.Networking
{
    /// <summary>
    ///     ScriptableObject for storing Supabase configuration settings.
    /// </summary>
    [CreateAssetMenu(fileName = "Supabase", menuName = "Ezg/Networking/Supabase/Supabase Settings", order = 1)]
    public class SupabaseSettings : ScriptableObject
    {
        #region Fields

        /// <summary>
        ///     The base URL for the Supabase project.
        /// </summary>
        public string SupabaseURL = null!;

        /// <summary>
        ///     The anonymous API key for the Supabase project.
        /// </summary>
        public string SupabaseAnonKey = null!;

        #endregion
    }
}