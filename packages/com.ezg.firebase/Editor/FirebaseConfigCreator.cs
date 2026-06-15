using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ezg.Core.Firebase.Editor
{
    /// <summary>
    ///     Menu <b>Create &gt; Ezg &gt; Firebase &gt; Firebase Config</b>: tạo asset <see cref="FirebaseConfig"/>
    ///     trong thư mục đang chọn, ĐỒNG THỜI scaffold lớp glue game-side <c>GameRemoteConfig.cs</c> tại
    ///     <c>Assets/_Project/Features/_Shared</c> (hỏi xác nhận nếu file đã tồn tại). Dùng khi cài module trên
    ///     project mới — vì <c>GameRemoteConfig</c> là game-specific nên KHÔNG đi kèm package.
    /// </summary>
    internal static class FirebaseConfigCreator
    {
        #region Fields

        private const string MENU_PATH = "Assets/Create/Ezg/Firebase/Firebase Config";
        private const int MENU_PRIORITY = 0;

        private const string CONFIG_FOLDER = "Assets/_Project/Resources";
        private const string CONFIG_ASSET_NAME = "FirebaseConfig.asset";

        private const string CLASS_FOLDER = "Assets/_Project/Features/_Shared";
        private const string CLASS_FILE_NAME = "GameRemoteConfig.cs";

        #endregion

        #region Private Methods

        [MenuItem(MENU_PATH, priority = MENU_PRIORITY)]
        private static void Create()
        {
            TryCreateGameRemoteConfigClass();
            CreateConfigAsset();
            AssetDatabase.Refresh();
        }

        /// <summary>
        ///     Tạo asset FirebaseConfig tại thư mục cố định <see cref="CONFIG_FOLDER"/>. Nếu asset đã tồn tại
        ///     thì hỏi xác nhận ghi đè; chọn "Bỏ qua" sẽ giữ nguyên asset hiện tại.
        /// </summary>
        private static void CreateConfigAsset()
        {
            var assetPath = $"{CONFIG_FOLDER}/{CONFIG_ASSET_NAME}";

            if (File.Exists(assetPath))
            {
                var overwrite = EditorUtility.DisplayDialog(
                    "FirebaseConfig đã tồn tại",
                    $"Asset đã tồn tại tại:\n{assetPath}\n\nGhi đè asset hiện tại?",
                    "Ghi đè",
                    "Bỏ qua");
                if (!overwrite)
                {
                    Debug.Log("[Firebase] Bỏ qua tạo FirebaseConfig — giữ nguyên asset hiện tại.");
                    return;
                }

                AssetDatabase.DeleteAsset(assetPath);
            }

            EnsureFolder(CONFIG_FOLDER);
            var config = ScriptableObject.CreateInstance<FirebaseConfig>();
            AssetDatabase.CreateAsset(config, assetPath);
            AssetDatabase.SaveAssets();
            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
            Debug.Log($"[Firebase] Đã tạo config asset: {assetPath}");
        }

        /// <summary>
        ///     Scaffold <c>GameRemoteConfig.cs</c> tại thư mục cố định. Nếu file đã tồn tại thì hỏi xác nhận
        ///     ghi đè; người dùng chọn "Bỏ qua" sẽ giữ nguyên file hiện tại.
        /// </summary>
        private static void TryCreateGameRemoteConfigClass()
        {
            var path = $"{CLASS_FOLDER}/{CLASS_FILE_NAME}";

            if (File.Exists(path))
            {
                var overwrite = EditorUtility.DisplayDialog(
                    "GameRemoteConfig đã tồn tại",
                    $"File đã tồn tại tại:\n{path}\n\nGhi đè file hiện tại?",
                    "Ghi đè",
                    "Bỏ qua");
                if (!overwrite)
                {
                    Debug.Log("[Firebase] Bỏ qua tạo GameRemoteConfig — giữ nguyên file hiện tại.");
                    return;
                }
            }

            EnsureFolder(CLASS_FOLDER);
            File.WriteAllText(path, BuildTemplate());
            AssetDatabase.ImportAsset(path);
            Debug.Log($"[Firebase] Đã tạo GameRemoteConfig: {path}");
        }

        /// <summary>
        ///     Tạo (đệ quy) folder nếu chưa tồn tại.
        /// </summary>
        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parts = folder.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        /// <summary>
        ///     Nội dung skeleton cho GameRemoteConfig — chỉ phụ thuộc package <c>Ezg.Core.Firebase</c>,
        ///     KHÔNG chứa code đặc thù game (AdsManager, EventName...) để compile được trên mọi project.
        /// </summary>
        private static string BuildTemplate()
        {
            return
@"using Ezg.Core.Firebase;

namespace Ezg.Feature.Firebase
{
    /// <summary>
    ///     Lớp glue phía game cho Firebase Remote Config. Đọc các key đặc thù của game qua getter generic
    ///     của FirebaseRemoteManager (GetInt/GetBool/GetString/HasKey), map vào state/hệ thống của game.
    ///     Đăng ký Apply trước khi fetch:
    ///         FirebaseRemoteManager.OnRemoteConfigApplied = GameRemoteConfig.Apply;
    ///         FirebaseRemoteManager.InitRemoteConfig();
    /// </summary>
    public static class GameRemoteConfig
    {
        #region Fields

        // TODO: Khai báo remote config key của game.
        // public const string EXAMPLE_KEY = ""example_key"";

        // TODO: Khai báo field nhận giá trị từ remote config.
        // public static int exampleValue;

        #endregion

        #region Public Methods

        /// <summary>
        ///     Handler cho FirebaseRemoteManager.OnRemoteConfigApplied (gọi sau khi fetch &amp; activate xong).
        /// </summary>
        public static void Apply()
        {
            // TODO: Đọc key và áp dụng vào game. Ví dụ:
            // exampleValue = FirebaseRemoteManager.GetInt(EXAMPLE_KEY, exampleValue);
            //
            // if (FirebaseRemoteManager.HasKey(EXAMPLE_KEY))
            //     someSystem.Value = FirebaseRemoteManager.GetBool(EXAMPLE_KEY);
        }

        #endregion
    }
}
";
        }

        #endregion
    }
}
