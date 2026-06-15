#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ezg.Core.Networking.Editor
{
    /// <summary>
    ///     Editor tool that scaffolds the <c>GameNetworkManager</c> facade for the Networking module.
    ///     Menu: Create ▸ Ezg ▸ Networking ▸ Project setup.
    /// </summary>
    public static class NetworkingProjectSetup
    {
        #region Fields

        private const string TARGET_DIR = "Assets/_Project/Features/_Shared";
        private const string TARGET_FILE = TARGET_DIR + "/GameNetworkManager.cs";

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
        ///     Generates <c>GameNetworkManager.cs</c> in Assets/_Project/Features/_Shared.
        ///     Prompts before overwriting an existing file.
        /// </summary>
        [MenuItem("Assets/Create/Ezg/Networking/Project setup", priority = 2)]
        public static void Setup()
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
            AssetDatabase.Refresh();

            Debug.Log($"[Networking] Đã tạo {TARGET_FILE}");

            var asset = AssetDatabase.LoadAssetAtPath<Object>(TARGET_FILE);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
        }

        #endregion
    }
}
#endif
