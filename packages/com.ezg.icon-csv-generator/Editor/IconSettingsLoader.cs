#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    using UnityEditor;
    using UnityEngine;

    // ---------------------------------------------------------------------------
    // Finds the committed IconGeneratorSettings asset via AssetDatabase.
    // Resolution order (per plan design decision #6):
    //   1. EditorPrefs GUID → GUIDToAssetPath → load (persistent user preference)
    //   2. FindAssets first-match (default discovery, now informational if >1 found)
    //   3. Transient in-memory defaults + warning (no silent failure)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Locates the active <see cref="IconGeneratorSettings"/> asset in the project.
    /// Multiple assets are now legitimate (user-selectable profiles) — their presence
    /// is logged at Info level, not Warning.
    /// Use <see cref="Load"/> once per window open; cache the result.
    /// </summary>
    internal static class IconSettingsLoader
    {
        private const string PREFS_KEY_GUID = "Ezg.IconCsvGenerator.SettingsGuid";

        /// <summary>
        /// Returns the settings asset according to the resolution order above.
        /// Never null — falls back to a transient in-memory instance when no asset exists.
        /// </summary>
        public static IconGeneratorSettings Load()
        {
            // 1. Try EditorPrefs-persisted GUID.
            var savedGuid = EditorPrefs.GetString(PREFS_KEY_GUID, string.Empty);
            if (!string.IsNullOrEmpty(savedGuid))
            {
                var savedPath = AssetDatabase.GUIDToAssetPath(savedGuid);
                if (!string.IsNullOrEmpty(savedPath))
                {
                    var savedAsset = AssetDatabase.LoadAssetAtPath<IconGeneratorSettings>(savedPath);
                    if (savedAsset != null) return savedAsset;
                }
                // GUID stale — clear it.
                EditorPrefs.DeleteKey(PREFS_KEY_GUID);
            }

            // 2. FindAssets first-match.
            var guids = AssetDatabase.FindAssets("t:IconGeneratorSettings");

            if (guids.Length == 0)
            {
                Debug.LogWarning(
                    "[IconGenerator] No IconGeneratorSettings asset found in the project. " +
                    "Create one via Assets ▸ Create ▸ Ezg ▸ Icon CSV Generator ▸ Settings. " +
                    "Using in-memory defaults (aspectRatio=1:1, imageSize=1K). " +
                    "Changes will NOT be persisted until the asset is restored.");
                return ScriptableObject.CreateInstance<IconGeneratorSettings>();
            }

            if (guids.Length > 1)
            {
                // Multiple profiles are legitimate — the user can pick one via the ObjectField.
                Debug.Log(
                    $"[IconGenerator] Found {guids.Length} IconGeneratorSettings assets. " +
                    "Using the first one. Pick a different profile via the Settings field in the window.");
            }

            var path  = AssetDatabase.GUIDToAssetPath(guids[0]);
            var asset = AssetDatabase.LoadAssetAtPath<IconGeneratorSettings>(path);

            if (asset == null)
            {
                Debug.LogWarning(
                    $"[IconGenerator] Could not load IconGeneratorSettings from '{path}'. " +
                    "Using in-memory defaults.");
                return ScriptableObject.CreateInstance<IconGeneratorSettings>();
            }

            return asset;
        }

        /// <summary>
        /// Persists the GUID of the chosen settings asset to EditorPrefs so the window
        /// reopens with the same selection. Call when the user changes the ObjectField.
        /// </summary>
        public static void PersistSelection(IconGeneratorSettings settings)
        {
            if (settings == null) return;

            var path = AssetDatabase.GetAssetPath(settings);
            if (string.IsNullOrEmpty(path)) return;

            var guid = AssetDatabase.AssetPathToGUID(path);
            if (!string.IsNullOrEmpty(guid))
            {
                EditorPrefs.SetString(PREFS_KEY_GUID, guid);
            }
        }
    }
}
