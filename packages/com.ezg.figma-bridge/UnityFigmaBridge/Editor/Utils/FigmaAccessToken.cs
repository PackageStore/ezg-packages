using UnityEngine;

namespace UnityFigmaBridge.Editor.Utils
{
    internal static class FigmaAccessToken
    {
        internal const string PLAYER_PREFS_KEY = "FIGMA_PERSONAL_ACCESS_TOKEN";
        private const string PLACEHOLDER = "REPLACE_WITH_FIGMA_PAT";

        internal static string Read()
        {
            return Sanitize(PlayerPrefs.GetString(PLAYER_PREFS_KEY));
        }

        internal static void Write(string token)
        {
            token = token?.Trim();
            if (string.IsNullOrEmpty(token))
            {
                PlayerPrefs.DeleteKey(PLAYER_PREFS_KEY);
                Debug.Log("Figma access token cleared");
            }
            else
            {
                PlayerPrefs.SetString(PLAYER_PREFS_KEY, token);
                Debug.Log("Figma access token saved");
            }
            PlayerPrefs.Save();
        }

        internal static bool TryPrompt()
        {
            var current = PlayerPrefs.GetString(PLAYER_PREFS_KEY);
            var entered = EditorInputDialog.Show(
                "Personal Access Token",
                "Please enter your Figma Personal Access Token (you can create in the 'Developer settings' page)",
                current);

            if (string.IsNullOrEmpty(entered)) return false;

            PlayerPrefs.SetString(PLAYER_PREFS_KEY, entered);
            PlayerPrefs.Save();
            Debug.Log("Figma access token updated");
            return true;
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            value = value.Trim();
            if (value.Length == 0 || value == PLACEHOLDER) return null;
            return value;
        }
    }
}
