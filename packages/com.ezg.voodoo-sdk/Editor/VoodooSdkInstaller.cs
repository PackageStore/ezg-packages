#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ezg.VoodooSdk.Editor
{
    /// <summary>
    /// Điểm vào duy nhất để cài đặt. Gọi từ menu hoặc từ CLI batchmode
    /// (<c>-executeMethod Ezg.VoodooSdk.Editor.VoodooSdkInstaller.InstallFromCommandLine</c>).
    ///
    /// Chỉ đọc <see cref="VoodooSdkPaths.ConfigFile"/> — không có gì khác cần chỉnh tay.
    /// </summary>
    public static class VoodooSdkInstaller
    {
        #region Fields

        private const string MenuRoot = "Tools/Voodoo SDK/";
        private const string NewtonsoftDefine = "NEWTONSOFT";

        /// <summary>
        /// Bật assembly <c>Ezg.Tracking.GameAnalytics</c> của <c>com.ezg.tracking</c> — sink GameAnalytics
        /// nằm sau define này để project không có GA vẫn build được.
        /// </summary>
        private const string GameAnalyticsDefine = "EZG_GAMEANALYTICS";
        private const int ExitOk = 0;
        private const int ExitFail = 1;

        #endregion

        #region Public

        [MenuItem(MenuRoot + "Create config template", priority = 0)]
        public static void CreateConfigTemplate()
        {
            if (!VoodooSdkConfig.WriteTemplateIfMissing())
                Debug.Log($"[VoodooSdk] {VoodooSdkPaths.ConfigFile} đã tồn tại — không ghi đè.");
        }

        [MenuItem(MenuRoot + "Install", priority = 1)]
        public static void InstallMenu()
        {
            Install(out string error);
            if (error != null)
                EditorUtility.DisplayDialog("Voodoo SDK", error, "OK");
        }

        [MenuItem(MenuRoot + "Validate", priority = 2)]
        public static void ValidateMenu()
        {
            var result = VoodooSdkPreflight.Run(EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS);
            foreach (string warning in result.Warnings)
                Debug.LogWarning($"[VoodooSdk] {warning}");
            foreach (string error in result.Errors)
                Debug.LogError($"[VoodooSdk] {error}");
            if (result.Ok)
                Debug.Log("[VoodooSdk] Validate: PASS");
        }

        /// <summary>Entry cho batchmode. Thoát code 0 khi thành công, 1 khi thất bại.</summary>
        public static void InstallFromCommandLine()
        {
            bool ok = Install(out string error);
            if (!ok)
                Debug.LogError($"[VoodooSdk] {error}");
            EditorApplication.Exit(ok ? ExitOk : ExitFail);
        }

        /// <summary>Chạy toàn bộ cài đặt. Idempotent — gọi lại bao nhiêu lần cũng được.</summary>
        public static bool Install(out string error)
        {
            error = null;

            if (!VoodooSdkPaths.TinySauceInstalled)
            {
                error = $"Chưa import TinySauce vào {VoodooSdkPaths.TinySauceRoot}. " +
                        "Import file .unitypackage của Voodoo trước.";
                return false;
            }

            if (VoodooSdkConfig.WriteTemplateIfMissing())
            {
                error = $"Vừa tạo {VoodooSdkPaths.ConfigFile}. Điền các giá trị FILL_ME rồi chạy lại.";
                return false;
            }

            VoodooSdkConfig config = VoodooSdkConfig.Load();
            if (config == null)
            {
                error = $"Không đọc được {VoodooSdkPaths.ConfigFile}.";
                return false;
            }

            bool needIos = EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS;
            var missing = config.FindMissingFields(needIos);
            if (missing.Count > 0)
            {
                error = $"Config còn thiếu: {string.Join(", ", missing)}";
                return false;
            }

            WarnAboutConflictingFacebookSdk();
            VoodooSdkGaIlrdPatcher.Apply(logWhenClean: false);
            VoodooSdkGaAsmdefPatcher.Apply(logWhenClean: false);
            EnsureDefines();

            if (!VoodooSdkSettingsGenerator.Generate(config, out error))
                return false;

            VoodooSdkGaResourcePatcher.Apply(config.gameAnalytics.resourceCurrencies,
                                             config.gameAnalytics.resourceItemTypes);

            AddPrefabToFirstScene();
            AssetDatabase.Refresh();

            Debug.Log("[VoodooSdk] Cài đặt xong. Chạy Tools > Voodoo SDK > Validate để kiểm tra lại.");
            return true;
        }

        #endregion

        #region Private

        /// <summary>
        /// Không tự xoá: có project cố ý dùng FacebookSDK riêng. Chỉ cảnh báo để người dùng quyết.
        /// </summary>
        private static void WarnAboutConflictingFacebookSdk()
        {
            if (!VoodooSdkPaths.HasConflictingFacebookSdk)
                return;

            Debug.LogWarning($"[VoodooSdk] {VoodooSdkPaths.ConflictingFacebookSdk} trùng với bản Facebook " +
                             "mà TinySauce bundle (cùng Facebook.Unity.dll) — build sẽ lỗi duplicate type. " +
                             "Xoá thư mục đó rồi chạy lại Install.");
        }

        /// <summary>
        /// Thêm các scripting define mà bộ SDK cần:
        ///
        /// <c>NEWTONSOFT</c> — <c>AndroidPreBuild.GetTemplatePropertiesToAdd()</c> của TinySauce nằm sau
        /// <c>#if NEWTONSOFT</c>. Thiếu define này thì cơ chế merge gradle im lặng không chạy —
        /// không lỗi, chỉ thiếu dependency lúc build.
        ///
        /// <c>EZG_GAMEANALYTICS</c> — bật assembly sink GameAnalytics trong <c>com.ezg.tracking</c>.
        /// Thiếu define thì mọi call GameAnalytics là no-op, cũng im lặng không lỗi.
        /// </summary>
        private static void EnsureDefines()
        {
            AddDefine(NewtonsoftDefine);

            // Chỉ bật sink khi SDK GameAnalytics thật sự có mặt.
            if (Directory.Exists(VoodooSdkPaths.Absolute(VoodooSdkPaths.GaScriptsRoot)))
                AddDefine(GameAnalyticsDefine);
        }

        /// <summary>Thêm 1 define cho Android + iOS nếu chưa có. Idempotent.</summary>
        private static void AddDefine(string define)
        {
            foreach (NamedBuildTarget target in new[] { NamedBuildTarget.Android, NamedBuildTarget.iOS })
            {
                string symbols = PlayerSettings.GetScriptingDefineSymbols(target);
                if (symbols.Split(';').Contains(define))
                    continue;

                PlayerSettings.SetScriptingDefineSymbols(target, define + ";" + symbols);
                Debug.Log($"[VoodooSdk] Đã thêm define {define} cho {target.TargetName}.");
            }
        }

        /// <summary>
        /// TinySauce.prefab phải có đúng 1 instance trên toàn bộ scene trong Build Settings
        /// (xem SceneChecker của Voodoo). Thêm vào scene đầu nếu chưa có.
        /// </summary>
        private static void AddPrefabToFirstScene()
        {
            EditorBuildSettingsScene target = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled);
            if (target == null)
            {
                Debug.LogWarning("[VoodooSdk] Build Settings chưa có scene nào được bật.");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VoodooSdkPaths.TinySaucePrefab);
            if (prefab == null)
            {
                Debug.LogWarning($"[VoodooSdk] Không nạp được {VoodooSdkPaths.TinySaucePrefab}.");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(target.path, OpenSceneMode.Single);
            bool exists = scene.GetRootGameObjects().Any(root =>
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) == VoodooSdkPaths.TinySaucePrefab);

            if (exists)
                return;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "TinySauce";
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[VoodooSdk] Đã thêm TinySauce.prefab vào {target.path}.");
        }

        #endregion
    }
}
#endif
