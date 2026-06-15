#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ezg.Core.Networking.Editor
{
    /// <summary>
    ///     Editor tool that scaffolds the Networking module for a project:
    ///     generates the <c>GameNetworkManager</c> facade and creates the default
    ///     Cloudflare/Supabase settings assets under <c>Assets/_Project/Resources</c>.
    ///     Menu: Create ▸ Ezg ▸ Networking ▸ Project setup.
    /// </summary>
    public static class NetworkingProjectSetup
    {
        #region Fields

        private const string TARGET_DIR = "Assets/_Project/Features/_Shared";
        private const string TARGET_FILE = TARGET_DIR + "/GameNetworkManager.cs";

        // Settings assets must live exactly here so Resources.Load("Cloudflare"/"Supabase") resolves.
        private const string RESOURCES_DIR = "Assets/_Project/Resources";
        private const string CLOUDFLARE_ASSET = RESOURCES_DIR + "/Cloudflare.asset";
        private const string SUPABASE_ASSET = RESOURCES_DIR + "/Supabase.asset";

        /// <summary>
        ///     Template content written to the generated GameNetworkManager.cs (Endpoint-only facade).
        /// </summary>
        private const string TEMPLATE =
@"using Ezg.Core.Networking;

namespace Ezg.Feature.Networking
{
    /// <summary>
    /// Manages network communication, combining Supabase (Read) and Cloudflare Workers (Write).
    /// </summary>
    public class GameNetworkManager : SupabaseManager<GameNetworkManager>
    {
        #region Public Methods

        // --- Endpoint Access ---

        /// <summary>
        /// Creates a Cloudflare query for a specific endpoint.
        /// </summary>
        /// <typeparam name=""T"">The data type expected from the endpoint.</typeparam>
        /// <param name=""endPoint"">The relative path to the endpoint.</param>
        /// <returns>A new CloudflareQuery instance.</returns>
        public static CloudflareQuery<T> Endpoint<T>(string endPoint)
        {
            return new CloudflareQuery<T>(endPoint);
        }

        #endregion
    }
}
";

        #endregion

        #region Public Methods

        /// <summary>
        ///     Runs the full Networking project setup: generates <c>GameNetworkManager.cs</c> and
        ///     creates the Cloudflare/Supabase settings assets in <c>Assets/_Project/Resources</c>.
        ///     Each step checks for an existing file/asset and prompts before overwriting.
        /// </summary>
        [MenuItem("Assets/Create/Ezg/Networking/Project setup", priority = 2)]
        public static void Setup()
        {
            GenerateGameNetworkManager();

            EnsureResourcesFolder();
            CreateSettingsAsset<CloudflareSettings>(CLOUDFLARE_ASSET);
            CreateSettingsAsset<SupabaseSettings>(SUPABASE_ASSET);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Generates <c>GameNetworkManager.cs</c> in Assets/_Project/Features/_Shared.
        ///     Prompts before overwriting an existing file.
        /// </summary>
        private static void GenerateGameNetworkManager()
        {
            if (File.Exists(TARGET_FILE))
            {
                var overwrite = EditorUtility.DisplayDialog(
                    "Networking Project Setup",
                    $"{TARGET_FILE} đã tồn tại.\nGhi đè bằng template mặc định (chỉ giữ hàm Endpoint)?",
                    "Ghi đè", "Huỷ");

                if (!overwrite) return;
            }

            Directory.CreateDirectory(TARGET_DIR);
            File.WriteAllText(TARGET_FILE, TEMPLATE);

            Debug.Log($"[Networking] Đã tạo {TARGET_FILE}");

            var asset = AssetDatabase.LoadAssetAtPath<Object>(TARGET_FILE);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
        }

        /// <summary>
        ///     Creates a settings ScriptableObject at the exact <paramref name="assetPath" /> under
        ///     Assets/_Project/Resources. Prompts before overwriting an existing asset; skipping keeps
        ///     the current values untouched.
        /// </summary>
        /// <typeparam name="T">The settings ScriptableObject type to create.</typeparam>
        /// <param name="assetPath">The exact asset path (e.g. Assets/_Project/Resources/Cloudflare.asset).</param>
        private static void CreateSettingsAsset<T>(string assetPath) where T : ScriptableObject
        {
            if (File.Exists(assetPath))
            {
                var overwrite = EditorUtility.DisplayDialog(
                    "Networking Project Setup",
                    $"{assetPath} đã tồn tại.\nGhi đè bằng asset mặc định (xoá giá trị đang có)?",
                    "Ghi đè", "Giữ nguyên");

                if (!overwrite) return;

                AssetDatabase.DeleteAsset(assetPath);
            }

            var settings = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(settings, assetPath);

            Debug.Log($"[Networking] Đã tạo {assetPath}");
        }

        /// <summary>
        ///     Ensures Assets/_Project/Resources exists as a valid asset folder before creating assets in it.
        /// </summary>
        private static void EnsureResourcesFolder()
        {
            if (AssetDatabase.IsValidFolder(RESOURCES_DIR)) return;

            if (!AssetDatabase.IsValidFolder("Assets/_Project"))
                AssetDatabase.CreateFolder("Assets", "_Project");

            AssetDatabase.CreateFolder("Assets/_Project", "Resources");
        }

        #endregion
    }
}
#endif
