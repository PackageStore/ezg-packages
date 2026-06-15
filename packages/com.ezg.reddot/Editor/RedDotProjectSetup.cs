#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ezg.Core.RedDot.Editor
{
    /// <summary>
    ///     Scaffolds the project-side RedDot classes (RedDotId + RedDotBadge) into the consuming
    ///     project, pinning their .meta GUIDs to canonical values so reused UI prefabs/assets keep
    ///     their script references when this package is imported into a new project.
    /// </summary>
    public static class RedDotProjectSetup
    {
        #region Fields

        private const string TARGET_DIR = "Assets/_Project/Features/_Shared/RedDot";

        // Canonical GUIDs — MUST stay identical across projects so references survive.
        private const string RED_DOT_ID_GUID = "1dd61ccb117b52148a296a8ae44b3408";
        private const string RED_DOT_BADGE_GUID = "c8086291de801c699f737a7d3bd34b69";

        #endregion

        #region Public Methods

        /// <summary>
        ///     Menu entry: Create > Ezg > RedDot > Project setup.
        /// </summary>
        [MenuItem("Assets/Create/Ezg/RedDot/Project setup")]
        public static void Generate()
        {
            var idPath = TARGET_DIR + "/RedDotId.cs";
            var badgePath = TARGET_DIR + "/RedDotBadge.cs";

            if ((File.Exists(idPath) || File.Exists(badgePath)) &&
                !EditorUtility.DisplayDialog(
                    "RedDot Project Setup",
                    "RedDotId.cs / RedDotBadge.cs already exist in:\n" + TARGET_DIR +
                    "\n\nOverwrite them? Their .meta GUIDs will be (re)set to the canonical values.",
                    "Overwrite", "Cancel"))
                return;

            Directory.CreateDirectory(TARGET_DIR);
            WriteScriptWithMeta(idPath, RED_DOT_ID_SOURCE, RED_DOT_ID_GUID);
            WriteScriptWithMeta(badgePath, RED_DOT_BADGE_SOURCE, RED_DOT_BADGE_GUID);

            AssetDatabase.Refresh();

            var badge = AssetDatabase.LoadAssetAtPath<MonoScript>(badgePath);
            if (badge != null)
            {
                Selection.activeObject = badge;
                EditorGUIUtility.PingObject(badge);
            }

            Debug.Log("[RedDot] Project setup done -> " + TARGET_DIR +
                      " (RedDotId " + RED_DOT_ID_GUID + ", RedDotBadge " + RED_DOT_BADGE_GUID + ").");
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Writes the .cs together with a .meta carrying the pinned GUID, so Unity imports the
        ///     script with that exact GUID instead of generating a fresh one.
        /// </summary>
        private static void WriteScriptWithMeta(string assetPath, string source, string guid)
        {
            File.WriteAllText(assetPath, source);
            File.WriteAllText(assetPath + ".meta", BuildScriptMeta(guid));
        }

        private static string BuildScriptMeta(string guid)
        {
            return "fileFormatVersion: 2\n" +
                   "guid: " + guid + "\n" +
                   "MonoImporter:\n" +
                   "  externalObjects: {}\n" +
                   "  serializedVersion: 2\n" +
                   "  defaultReferences: []\n" +
                   "  executionOrder: 0\n" +
                   "  icon: {instanceID: 0}\n" +
                   "  userData: \n" +
                   "  assetBundleName: \n" +
                   "  assetBundleVariant: \n";
        }

        #endregion

        #region Generated Sources

        private const string RED_DOT_ID_SOURCE =
@"namespace Ezg.Feature.RedDot
{
    /// <summary>
    ///     Represents identifier keys for different red-dot indicators in the game.
    /// </summary>
    public enum RedDotId
    {
        None,
        // TODO: add this project's red-dot ids here.
    }
}
";

        private const string RED_DOT_BADGE_SOURCE =
@"using Ezg.Core.RedDot;
using UnityEngine;

namespace Ezg.Feature.RedDot
{
    /// <summary>
    ///     Project-side red-dot indicator. Resolves the listened event key from the
    ///     game-specific <see cref=""RedDotId"" /> enum so the reusable <see cref=""BaseRedDot"" />
    ///     package stays free of project data.
    /// </summary>
    public abstract class RedDotBadge : BaseRedDot
    {
        [SerializeField] private RedDotId _notifId;

        /// <inheritdoc />
        protected override string EventKey => _notifId.ToString();
    }
}
";

        #endregion
    }
}
#endif
