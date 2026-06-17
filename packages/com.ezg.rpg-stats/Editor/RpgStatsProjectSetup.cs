#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ezg.Package.RpgStats.EditorTools
{
    /// <summary>
    ///     Sinh toàn bộ phần per-project cần thiết để dùng package RpgStats trong một project mới:
    ///     enum khóa stat, class config concrete, bootstrap loader và asset config.
    ///     Menu: Assets &gt; Create &gt; Ezg &gt; Rpg Stats &gt; Project Setup.
    /// </summary>
    public static class RpgStatsProjectSetup
    {
        #region Constants

        internal const string ROOT_FOLDER = "Assets/_Project/Features/_Shared/RpgStats";

        // Asset config phải nằm trong Resources để load runtime bằng Resources.Load.
        internal const string ASSET_FOLDER = ROOT_FOLDER + "/Resources";
        internal const string ASSET_NAME = "RpgStatsConfig";
        internal const string ASSET_PATH = ASSET_FOLDER + "/" + ASSET_NAME + ".asset";
        internal const string PENDING_KEY = "ezg_rpgstats_pending_asset";

        #endregion

        #region Menu

        [MenuItem("Assets/Create/Ezg/Rpg Stats/Project Setup", priority = 0)]
        public static void Run()
        {
            // Đã có class config concrete (project đã setup) -> tạo asset luôn, không sinh lại code.
            if (FindConcreteConfigType() != null)
            {
                RpgStatsAssetCreator.CreatePendingAsset(ASSET_PATH);
                return;
            }

            Directory.CreateDirectory(ROOT_FOLDER);
            WriteIfMissing(ROOT_FOLDER + "/RPGStatType.cs", EnumTemplate);
            WriteIfMissing(ROOT_FOLDER + "/RpgStatsConfig.cs", ConfigTemplate);
            WriteIfMissing(ROOT_FOLDER + "/RpgStatsBootstrap.cs", BootstrapTemplate);

            // Đánh dấu để pha 2 (sau biên dịch) tạo asset config.
            SessionState.SetString(PENDING_KEY, ASSET_PATH);
            AssetDatabase.Refresh();
            Debug.Log("[RpgStats] Đã sinh code per-project tại " + ROOT_FOLDER +
                      ". Đợi Unity biên dịch xong, asset config sẽ được tạo tự động.");
        }

        #endregion

        #region Helpers

        /// <summary>
        ///     Tìm class config concrete (non-abstract, non-generic) kế thừa RpgStatsConfigBase&lt;TKey&gt;.
        /// </summary>
        internal static Type FindConcreteConfigType()
        {
            return TypeCache.GetTypesDerivedFrom(typeof(RpgStatsConfigBase<>))
                .FirstOrDefault(t => !t.IsAbstract && !t.IsGenericType);
        }

        private static void WriteIfMissing(string path, string content)
        {
            if (File.Exists(path)) return;
            File.WriteAllText(path, content);
        }

        #endregion

        #region Templates

        private const string EnumTemplate =
            @"namespace Game.Stats
{
    /// <summary>
    ///     Khóa stat của project. BẮT BUỘC có None = 0. Sửa danh sách theo nhu cầu game.
    /// </summary>
    public enum RPGStatType
    {
        None = 0,
        Attack = 1,
        Health = 2,
        Defense = 3
    }
}
";

        private const string ConfigTemplate =
            @"using Ezg.Package.RpgStats;

namespace Game.Stats
{
    /// <summary>
    ///     Asset cấu hình stats của project (đóng TKey = RPGStatType).
    ///     Asset được tạo tự động bởi menu Project Setup.
    /// </summary>
    public class RpgStatsConfig : RpgStatsConfigBase<RPGStatType>
    {
    }
}
";

        private const string BootstrapTemplate =
            @"using Ezg.Package.RpgStats;
using UnityEngine;

namespace Game.Stats
{
    /// <summary>
    ///     Nạp config vào StatConfigs khi khởi động. Gắn component này vào một GameObject ở
    ///     scene đầu tiên. Nếu không gán _config, sẽ tự load asset 'RpgStatsConfig' từ Resources.
    /// </summary>
    public class RpgStatsBootstrap : MonoBehaviour
    {
        [SerializeField] private RpgStatsConfig _config;

        private void Awake()
        {
            var config = _config != null ? _config : Resources.Load<RpgStatsConfig>(""RpgStatsConfig"");
            if (config != null) config.Apply();
            else Debug.LogWarning(""[RpgStats] Không tìm thấy RpgStatsConfig (gán _config hoặc đặt asset trong Resources)."");
        }
    }
}
";

        #endregion
    }

    /// <summary>
    ///     Pha 2: sau khi code per-project biên dịch xong, tự tạo asset config đang chờ
    ///     (không thể tạo trong cùng frame với code-gen vì cần type concrete đã compile).
    /// </summary>
    [InitializeOnLoad]
    internal static class RpgStatsAssetCreator
    {
        static RpgStatsAssetCreator()
        {
            EditorApplication.delayCall += TryCreateOnReload;
        }

        private static void TryCreateOnReload()
        {
            var path = SessionState.GetString(RpgStatsProjectSetup.PENDING_KEY, "");
            if (string.IsNullOrEmpty(path)) return;

            // Tạo thành công (hoặc type đã sẵn) -> xóa cờ; nếu chưa compile xong thì giữ cờ, thử lại lần reload sau.
            if (CreatePendingAsset(path))
                SessionState.EraseString(RpgStatsProjectSetup.PENDING_KEY);
        }

        internal static bool CreatePendingAsset(string path)
        {
            var type = RpgStatsProjectSetup.FindConcreteConfigType();
            if (type == null) return false;

            if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(path) == null)
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var so = ScriptableObject.CreateInstance(type);
                AssetDatabase.CreateAsset(so, path);
                AssetDatabase.SaveAssets();
                EditorGUIUtility.PingObject(so);
                Debug.Log("[RpgStats] Đã tạo asset config: " + path);
            }

            return true;
        }
    }
}
#endif
