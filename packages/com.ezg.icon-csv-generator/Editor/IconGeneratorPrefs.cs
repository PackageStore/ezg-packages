#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    using UnityEditor;

    /// <summary>
    /// EditorPrefs wrapper for the Icon Generator tool.
    /// The API key is NEVER written to any committed asset — it lives in EditorPrefs only (per-machine).
    /// </summary>
    internal static class IconGeneratorPrefs
    {
        private const string GEMINI_API_KEY_PREF = "Ezg.IconCsvGenerator.GeminiApiKey";

        public static string GetApiKey()
        {
            return EditorPrefs.GetString(GEMINI_API_KEY_PREF, string.Empty);
        }

        public static void SetApiKey(string apiKey)
        {
            EditorPrefs.SetString(GEMINI_API_KEY_PREF, apiKey);
        }

        public static bool HasApiKey()
        {
            return EditorPrefs.HasKey(GEMINI_API_KEY_PREF) && !string.IsNullOrEmpty(GetApiKey());
        }
    }
}
