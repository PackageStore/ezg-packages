#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ezg.VoodooSdk.Editor
{
    /// <summary>
    /// Kiểm tra cấu hình trước khi build. Mục đích là <b>fail sớm với thông báo rõ</b> thay vì để
    /// build chạy 20 phút rồi chết ở link/compile với lỗi khó truy.
    /// </summary>
    public class VoodooSdkPreflight : IPreprocessBuildWithReport
    {
        #region Fields

        /// <summary>Chạy trước AndroidPreBuild của TinySauce để chặn sớm.</summary>
        public int callbackOrder => -200;

        #endregion

        #region Types

        public class Result
        {
            public readonly List<string> Errors = new();
            public readonly List<string> Warnings = new();
            public bool Ok => Errors.Count == 0;
        }

        #endregion

        #region Events

        public void OnPreprocessBuild(BuildReport report)
        {
            bool ios = report.summary.platform == BuildTarget.iOS;
            Result result = Run(ios);

            foreach (string warning in result.Warnings)
                Debug.LogWarning($"[VoodooSdk] {warning}");

            if (result.Ok)
                return;

            throw new BuildFailedException(
                "[VoodooSdk] Cấu hình chưa hợp lệ:\n  - " + string.Join("\n  - ", result.Errors) +
                "\n\nChạy Tools > Voodoo SDK > Install để sửa.");
        }

        #endregion

        #region Public

        /// <summary>Chạy toàn bộ kiểm tra. <paramref name="requireIos"/> bật khi build iOS.</summary>
        public static Result Run(bool requireIos)
        {
            var result = new Result();

            if (!VoodooSdkPaths.TinySauceInstalled)
            {
                result.Errors.Add($"Chưa import TinySauce vào {VoodooSdkPaths.TinySauceRoot}");
                return result; // Các kiểm tra sau đều vô nghĩa.
            }

            VoodooSdkConfig config = VoodooSdkConfig.Load();
            if (config == null)
            {
                result.Errors.Add($"Thiếu {VoodooSdkPaths.ConfigFile}");
                return result;
            }

            List<string> missing = config.FindMissingFields(requireIos);
            if (missing.Count > 0)
                result.Errors.Add($"Config còn thiếu: {string.Join(", ", missing)}");

            if (!File.Exists(VoodooSdkPaths.Absolute(VoodooSdkPaths.SettingsAsset)))
                result.Errors.Add($"Thiếu {VoodooSdkPaths.SettingsAsset} — chưa chạy Install?");

            if (!VoodooSdkGaIlrdPatcher.IsPatched())
            {
                result.Errors.Add("GAMaxIntegration.cs còn gọi MaxSdkCallbacks.CrossPromo/" +
                                  "RewardedInterstitial — AppLovin MAX v13 đã xoá, build Player sẽ lỗi CS0117");
            }

            if (VoodooSdkPaths.HasConflictingFacebookSdk)
            {
                result.Errors.Add($"{VoodooSdkPaths.ConflictingFacebookSdk} còn tồn tại — trùng " +
                                  "Facebook.Unity.dll với bản TinySauce bundle");
            }

            if (!HasTinySauceInFirstScene())
                result.Warnings.Add("Không thấy TinySauce.prefab trong scene đầu của Build Settings");

            if (requireIos)
                CheckIos(result);

            return result;
        }

        #endregion

        #region Private

        private static void CheckIos(Result result)
        {
            string bundleId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS);
            if (string.IsNullOrWhiteSpace(bundleId) || bundleId.StartsWith("com.Company"))
            {
                result.Errors.Add($"Bundle ID iOS chưa đặt (đang là '{bundleId}') — Firebase iOS sẽ " +
                                  "fail runtime nếu không khớp GoogleService-Info.plist");
            }

            bool hasPlist = Directory.GetFiles(Application.dataPath, "GoogleService-Info.plist",
                SearchOption.AllDirectories).Length > 0;
            if (!hasPlist)
                result.Warnings.Add("Không thấy GoogleService-Info.plist — Crashlytics sẽ fail lúc build Xcode");
        }

        private static bool HasTinySauceInFirstScene()
        {
            EditorBuildSettingsScene first = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled);
            if (first == null)
                return false;

            // Đọc thẳng file scene: rẻ hơn mở scene, và chạy được trong batchmode.
            string path = VoodooSdkPaths.Absolute(first.path);
            if (!File.Exists(path))
                return false;

            string prefabGuid = AssetDatabase.AssetPathToGUID(VoodooSdkPaths.TinySaucePrefab);
            return !string.IsNullOrEmpty(prefabGuid) && File.ReadAllText(path).Contains(prefabGuid);
        }

        #endregion
    }
}
#endif
