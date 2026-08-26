#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Ezg.Editor.Shared.Publisher
{
    /// <summary>
    ///     Vá SDK bên thứ ba NGAY SAU KHI import để nó compile được trong project Ezg. Chạy như
    ///     <see cref="AssetPostprocessor" /> chứ không phải bước của switcher: <see cref="AssetDatabase.ImportPackage" />
    ///     là bất đồng bộ, file chỉ có mặt ở lượt import sau — và khi SDK vừa import làm compile đỏ thì
    ///     không có domain reload nào để <c>[InitializeOnLoad]</c> chạy; <see cref="OnPostprocessAllAssets" />
    ///     thì vẫn chạy trên domain cũ ngay khi file được import.
    ///     <para>
    ///         <b>GameAnalytics:</b> <c>GA_SettingsInspector.cs</c> dùng type <c>Game</c> (là
    ///         <c>GameAnalyticsSDK.Setup.Game</c>, import qua <c>using</c>). Project Ezg có
    ///         <c>namespace Game</c> (Ezg.Features: TabHelper, PurchaseTemplate, AssetBundleMapping…); GA
    ///         editor nằm ở Assembly-CSharp-Editor nên thấy namespace đó, và C# ưu tiên namespace toàn cục
    ///         hơn type kéo vào qua using → <c>CS0118 'Game' is a namespace but is used like a type</c>.
    ///         Vá = viết tên đầy đủ ở ba chỗ. Idempotent: đã vá thì regex không khớp nữa, không ghi lại.
    ///     </para>
    /// </summary>
    internal sealed class SdkPostInstallFixer : AssetPostprocessor
    {
        private const string GA_SETTINGS_INSPECTOR = "Assets/GameAnalytics/Editor/GA_SettingsInspector.cs";

        private static readonly Regex _listGame = new("\\bList<Game>", RegexOptions.Compiled);
        private static readonly Regex _newGame = new("\\bnew Game\\(", RegexOptions.Compiled);

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (var path in importedAssets)
                if (path == GA_SETTINGS_INSPECTOR)
                {
                    FixGameAnalyticsInspector(path);
                    return;
                }
        }

        /// <summary>Có thể gọi tay (menu/switcher) — cùng logic, cùng idempotent.</summary>
        internal static bool FixGameAnalyticsInspector(string assetPath = GA_SETTINGS_INSPECTOR)
        {
            try
            {
                var absolute = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), assetPath);
                if (!File.Exists(absolute)) return false;

                var text = File.ReadAllText(absolute);
                if (!_listGame.IsMatch(text) && !_newGame.IsMatch(text)) return false;

                text = _listGame.Replace(text, "List<GameAnalyticsSDK.Setup.Game>");
                text = _newGame.Replace(text, "new GameAnalyticsSDK.Setup.Game(");
                File.WriteAllText(absolute, text, new UTF8Encoding(false));
                Debug.Log($"[EzgKit] Đã vá {assetPath}: 'Game' → 'GameAnalyticsSDK.Setup.Game' (project có namespace Game).");
                // Import lại để compile theo bản đã vá; delayCall vì đang ở trong postprocess của chính lượt import này.
                EditorApplication.delayCall += () => AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
        }
    }
}
#endif
