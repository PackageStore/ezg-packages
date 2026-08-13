#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ezg.VoodooSdk.Editor
{
    /// <summary>
    /// Tạo assembly definition cho SDK GameAnalytics mà TinySauce bundle.
    ///
    /// Bản vendor của Voodoo KHÔNG kèm asmdef nào, nên toàn bộ namespace <c>GameAnalyticsSDK</c> rơi vào
    /// assembly dựng sẵn (<c>Assembly-CSharp-firstpass</c>, do nằm trong thư mục <c>Plugins/</c>).
    /// Assembly dựng sẵn KHÔNG tham chiếu được từ asmdef, nên mọi code nằm trong asmdef — gồm cả
    /// <c>com.ezg.tracking</c> và code game — đều không gọi được GameAnalytics. Triệu chứng là
    /// "type or namespace GameAnalyticsSDK not found" dù SDK nằm ngay trong project.
    ///
    /// Đối chứng: AppLovin MAX cũng vendor kiểu này nhưng CÓ asmdef riêng
    /// (<c>Assets/MaxSdk/Scripts/MaxSdk.Scripts.asmdef</c>) — đây chỉ là dựng lại cùng một pattern.
    ///
    /// Chạy tự động mỗi khi TinySauce được import/nâng cấp, nên asmdef không mất khi re-import SDK.
    /// </summary>
    public class VoodooSdkGaAsmdefPatcher : AssetPostprocessor
    {
        #region Fields

        /// <summary>
        /// Tên assembly. Cố định — <c>com.ezg.tracking</c> và asmdef của game tham chiếu theo đúng tên này.
        /// </summary>
        public const string AssemblyName = "GameAnalytics.Scripts";

        /// <summary>
        /// GameAnalytics tham chiếu MaxSdk trong <c>GAMaxIntegration.cs</c>/<c>GameAnalyticsILRD.cs</c> khi
        /// có define <c>gameanalytics_max_enabled</c>. Chỉ thêm reference khi MAX thật sự có trong project.
        /// </summary>
        private const string MaxSdkAssemblyName = "MaxSdk.Scripts";

        #endregion

        #region Events

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
                                                   string[] moved, string[] movedFrom)
        {
            bool touchesTinySauce = imported.Any(p => p.StartsWith(VoodooSdkPaths.TinySauceRoot));
            if (touchesTinySauce)
                Apply(logWhenClean: false);
        }

        #endregion

        #region Public

        /// <summary>Trả về true nếu asmdef đã tồn tại (hoặc không có GA để mà vá).</summary>
        public static bool IsPatched()
        {
            if (!Directory.Exists(VoodooSdkPaths.Absolute(VoodooSdkPaths.GaScriptsRoot)))
                return true; // Không có GA thì không có gì để hỏng.

            return File.Exists(VoodooSdkPaths.Absolute(VoodooSdkPaths.GaAsmdef));
        }

        /// <summary>
        /// Ghi asmdef nếu chưa có. Idempotent, và KHÔNG ghi đè file sẵn có — project có thể đã tự thêm
        /// reference riêng vào đó.
        /// </summary>
        public static void Apply(bool logWhenClean = true)
        {
            string scriptsRoot = VoodooSdkPaths.Absolute(VoodooSdkPaths.GaScriptsRoot);
            if (!Directory.Exists(scriptsRoot))
                return;

            string asmdefPath = VoodooSdkPaths.Absolute(VoodooSdkPaths.GaAsmdef);
            if (File.Exists(asmdefPath))
            {
                if (logWhenClean)
                    Debug.Log($"[VoodooSdk] {AssemblyName}.asmdef — đã có.");
                return;
            }

            bool hasMaxSdk = HasAssembly(MaxSdkAssemblyName);
            string references = hasMaxSdk ? $"\n        \"{MaxSdkAssemblyName}\"\n    " : "";

            string json =
                "{\n" +
                $"    \"name\": \"{AssemblyName}\",\n" +
                "    \"rootNamespace\": \"\",\n" +
                $"    \"references\": [{references}],\n" +
                "    \"includePlatforms\": [],\n" +
                "    \"excludePlatforms\": [],\n" +
                "    \"allowUnsafeCode\": false,\n" +
                "    \"overrideReferences\": false,\n" +
                "    \"precompiledReferences\": [],\n" +
                "    \"autoReferenced\": true,\n" +
                "    \"defineConstraints\": [],\n" +
                "    \"versionDefines\": [],\n" +
                "    \"noEngineReferences\": false\n" +
                "}\n";

            File.WriteAllText(asmdefPath, json);
            AssetDatabase.ImportAsset(VoodooSdkPaths.GaAsmdef);

            Debug.Log($"[VoodooSdk] Đã tạo {VoodooSdkPaths.GaAsmdef}" +
                      (hasMaxSdk ? $" (kèm reference {MaxSdkAssemblyName})" : "") +
                      $". Từ giờ code trong asmdef gọi được GameAnalyticsSDK.");
        }

        #endregion

        #region Private

        /// <summary>
        /// Có assembly tên này trong project hay không. Dò theo tên file asmdef thay vì đường dẫn cố định —
        /// project có thể để MAX ở chỗ khác.
        /// </summary>
        private static bool HasAssembly(string assemblyName)
        {
            return AssetDatabase.FindAssets($"{assemblyName} t:AssemblyDefinitionAsset")
                                .Select(AssetDatabase.GUIDToAssetPath)
                                .Any(path => Path.GetFileNameWithoutExtension(path) == assemblyName);
        }

        #endregion
    }
}
#endif
