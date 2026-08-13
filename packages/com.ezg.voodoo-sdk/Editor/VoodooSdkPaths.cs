#if UNITY_EDITOR
using System.IO;
using UnityEngine;

namespace Ezg.VoodooSdk.Editor
{
    /// <summary>
    /// Mọi đường dẫn mà package đụng tới. Gom một chỗ để khi Voodoo đổi cấu trúc thư mục
    /// thì chỉ phải sửa ở đây.
    /// </summary>
    public static class VoodooSdkPaths
    {
        #region Fields

        /// <summary>
        /// TinySauce BẮT BUỘC nằm ở đây — <c>GradleTemplateFilePathHelper.SDK_FOLDER_PATH</c>
        /// hardcode "VoodooPackages/TinySauce" và ghép với <c>Application.dataPath</c>.
        /// Chuyển sang Packages/ thì cơ chế merge gradle của Voodoo im lặng ngừng chạy.
        /// </summary>
        public const string TinySauceRoot = "Assets/VoodooPackages/TinySauce";

        /// <summary>Config do người dùng điền. Để ngoài Assets/ cho khỏi lẫn asset và dễ gitignore.</summary>
        public const string ConfigFile = "ProjectSettings/voodoo-sdk.config.json";

        /// <summary>
        /// TinySauceSettings load runtime bằng <c>Resources.Load("TinySauce/Settings")</c>.
        /// Sai tên hoặc sai chỗ thì trả về null và TinySauce không init mà không báo lỗi rõ.
        /// </summary>
        public const string SettingsAsset = "Assets/Resources/TinySauce/Settings.asset";

        public const string SettingsScript = TinySauceRoot + "/Internal/Settings/TinySauceSettings.cs";
        public const string TinySaucePrefab = TinySauceRoot + "/Prefabs/TinySauce.prefab";

        public const string FacebookSettingsAsset =
            TinySauceRoot + "/Analytics/Facebook/3rdParty/FacebookSDK/SDK/Resources/FacebookSettings.asset";

        /// <summary>Thư mục script runtime của SDK GameAnalytics do TinySauce bundle.</summary>
        public const string GaScriptsRoot =
            TinySauceRoot + "/Analytics/GameAnalytics/3rdParty/GameAnalytics/Plugins/Scripts";

        public const string GaMaxIntegrationScript = GaScriptsRoot + "/ILRD/Max/GAMaxIntegration.cs";

        /// <summary>
        /// Asmdef ta tự tạo cho SDK GameAnalytics (bản vendor không có). Thiếu nó thì không asmdef nào
        /// gọi được <c>GameAnalyticsSDK</c> — xem <see cref="VoodooSdkGaAsmdefPatcher"/>.
        /// </summary>
        public const string GaAsmdef = GaScriptsRoot + "/GameAnalytics.Scripts.asmdef";

        /// <summary>
        /// Settings asset của SDK GameAnalytics. Resource event bị loại nếu currency/itemType chưa
        /// khai ở đây TRƯỚC lúc SDK init.
        /// </summary>
        public const string GaSettingsAsset = "Assets/Resources/GameAnalytics/Settings.asset";

        /// <summary>File TinySauce ghi đè ở mỗi lần build Android — ta sửa lại SAU nó.</summary>
        public const string AndroidManifest = "Assets/Plugins/Android/AndroidManifest.xml";

        public const string LauncherManifest = "Assets/Plugins/Android/LauncherManifest.xml";

        /// <summary>FacebookSDK rời — xung đột với bản TinySauce bundle (trùng Facebook.Unity.dll).</summary>
        public const string ConflictingFacebookSdk = "Assets/FacebookSDK";

        #endregion

        #region Public

        /// <summary>Đường dẫn tuyệt đối từ đường dẫn tương đối project root.</summary>
        public static string Absolute(string projectRelative)
        {
            return Path.Combine(ProjectRoot, projectRelative);
        }

        /// <summary>Thư mục gốc project (cha của Assets/).</summary>
        public static string ProjectRoot => Directory.GetParent(Application.dataPath)!.FullName;

        public static bool TinySauceInstalled => Directory.Exists(Absolute(TinySauceRoot));

        /// <summary>
        /// Có FacebookSDK rời thật sự hay không. Kiểm tra file dll chứ không chỉ thư mục:
        /// <c>git rm</c> hay xoá tay thường để lại thư mục rỗng, check bằng
        /// <c>Directory.Exists</c> sẽ báo động giả.
        /// </summary>
        public static bool HasConflictingFacebookSdk
        {
            get
            {
                string root = Absolute(ConflictingFacebookSdk);
                return Directory.Exists(root) &&
                       Directory.GetFiles(root, "Facebook.Unity.dll", SearchOption.AllDirectories).Length > 0;
            }
        }

        #endregion
    }
}
#endif
