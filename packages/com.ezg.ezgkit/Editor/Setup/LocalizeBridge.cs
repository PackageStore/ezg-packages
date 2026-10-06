#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Ezg.Editor.Shared.EzgKit;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ezg.Editor.Shared.Setup
{
    /// <summary>
    ///     Cầu nối tới <c>LocalizeDownloader</c> của <c>com.ezg.localize</c> (asset cấu hình sheet → CSV → asset
    ///     ngôn ngữ) và tới mục <c>localize</c> của <c>.claude/project-profile.json</c> (skill <c>add-localize</c>
    ///     đọc). Hai nơi trước đây mặc định HAI thư mục khác nhau — đây là chỗ ghi chúng về cùng một chỗ.
    ///     <para>
    ///         Quy ước của package: CSV ở <c>&lt;root&gt;/LocalizationData/&lt;lang&gt;/&lt;tab&gt;.csv</c>, asset sinh ra ở
    ///         thư mục anh em <c>&lt;root&gt;/Resources/LocalizationData/…</c> (runtime <c>Resources.Load</c>).
    ///     </para>
    /// </summary>
    internal static class LocalizeBridge
    {
        #region Constants

        internal const string TYPE = "LocalizeDownloader";

        /// <summary>Thư mục CSV đề xuất: cạnh <c>LocalizeHelper</c> của template, đúng chỗ add-localize mặc định.</summary>
        internal const string RECOMMENDED_CSV_ROOT = "Assets/_Project/Features/_Shared/Localize/LocalizationData";

        internal const string DEFAULT_ASSET_PATH = "Assets/_Project/Localize/LocalizeDownloader.asset";

        #endregion

        #region Types

        internal sealed class Tab
        {
            internal string Name;
            internal string Gid;
            internal bool Download = true;
        }

        #endregion

        #region Query

        internal static bool Installed => DownloaderType != null;

        private static Type DownloaderType => TypeFinder.Find(TYPE, typeof(ScriptableObject));

        internal static ScriptableObject FindAsset() => SerializedAsset.Find(TYPE);

        /// <summary>Id spreadsheet từ mọi dạng link (edit, export, link gốc).</summary>
        internal static string SheetId(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var match = Regex.Match(url, @"/spreadsheets/d/(?:e/)?([a-zA-Z0-9-_]+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>Link gốc mà LocalizeDownloader cần (nó tự nối <c>/export?format=csv&amp;gid=</c>).</summary>
        internal static string BaseUrl(string url)
        {
            var id = SheetId(url);
            return id == null ? null : "https://docs.google.com/spreadsheets/d/" + id;
        }

        internal static string AssetsRootOf(string csvRoot) =>
            string.IsNullOrEmpty(csvRoot) ? null : csvRoot.Replace("/LocalizationData", "/Resources/LocalizationData");

        internal static string DownloaderUrl(Object asset) => SerializedAsset.GetString(asset, "downloadPath");

        internal static string DownloaderSavePath(Object asset) => SerializedAsset.GetString(asset, "saveFilePath");

        internal static List<string> DownloaderLanguages(Object asset)
        {
            var result = new List<string>();
            if (asset == null) return result;
            var prop = new SerializedObject(asset).FindProperty("codeList");
            if (prop == null || !prop.isArray) return result;
            for (var i = 0; i < prop.arraySize; i++) result.Add(prop.GetArrayElementAtIndex(i).stringValue);
            return result;
        }

        internal static List<Tab> DownloaderTabs(Object asset)
        {
            var result = new List<Tab>();
            if (asset == null) return result;
            var prop = new SerializedObject(asset).FindProperty("itemList");
            if (prop == null || !prop.isArray) return result;
            for (var i = 0; i < prop.arraySize; i++)
            {
                var item = prop.GetArrayElementAtIndex(i);
                result.Add(new Tab
                {
                    Name = item.FindPropertyRelative("sheetName")?.stringValue,
                    Gid = item.FindPropertyRelative("sheetId")?.stringValue,
                    Download = item.FindPropertyRelative("download")?.boolValue ?? true,
                });
            }

            return result;
        }

        /// <summary>Thư mục ngôn ngữ đang có trong <paramref name="csvRoot" /> (en, vi, …).</summary>
        internal static List<string> LanguagesOnDisk(string csvRoot)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(csvRoot)) return result;
            var abs = ProjectPaths.Abs(csvRoot);
            if (!Directory.Exists(abs)) return result;
            foreach (var dir in Directory.GetDirectories(abs)) result.Add(Path.GetFileName(dir));
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        #endregion

        #region Write

        /// <summary>
        ///     Tạo (nếu chưa có) và ghi asset LocalizeDownloader: link sheet, thư mục CSV, danh sách tab. Tab null =
        ///     giữ danh sách hiện có.
        /// </summary>
        internal static bool WriteDownloader(string sheetUrl, string csvRoot, IList<Tab> tabs, bool dryRun,
            List<ChangeRow> changes, out string error)
        {
            error = null;
            var type = DownloaderType;
            if (type == null)
            {
                changes?.Add(new ChangeRow("LocalizeDownloader", "(asset)", "không có", "bỏ qua (dự án chưa cài com.ezg.localize)", true));
                return true;
            }

            var asset = FindAsset();
            if (asset == null)
            {
                changes?.Add(new ChangeRow("LocalizeDownloader", "(asset)", "chưa có", DEFAULT_ASSET_PATH, false));
                if (dryRun)
                {
                    changes?.Add(new ChangeRow("LocalizeDownloader", "downloadPath", string.Empty, BaseUrl(sheetUrl) ?? string.Empty, false));
                    changes?.Add(new ChangeRow("LocalizeDownloader", "saveFilePath", string.Empty, csvRoot ?? string.Empty, false));
                    return true;
                }

                // CreateWithDefaultValues giữ codeList + itemList mặc định của package (gid của bảng mẫu của team —
                // sheet copy từ bảng mẫu giữ nguyên gid).
                var factory = type.GetMethod("CreateWithDefaultValues", BindingFlags.Public | BindingFlags.Static);
                asset = (factory?.Invoke(null, null) as ScriptableObject) ?? ScriptableObject.CreateInstance(type);
                AppSecretsSink.EnsureFolder(Path.GetDirectoryName(DEFAULT_ASSET_PATH)?.Replace('\\', '/'));
                AssetDatabase.CreateAsset(asset, DEFAULT_ASSET_PATH);
                AssetDatabase.SaveAssets();
            }

            var values = new List<(string, object, bool)>
            {
                ("downloadPath", BaseUrl(sheetUrl), false),
                ("saveFilePath", csvRoot, false),
            };
            SerializedAsset.Write(asset, "LocalizeDownloader", values, dryRun, changes);

            if (tabs != null) WriteTabs(asset, tabs, dryRun, changes);
            return true;
        }

        private static void WriteTabs(Object asset, IList<Tab> tabs, bool dryRun, List<ChangeRow> changes)
        {
            var before = Describe(DownloaderTabs(asset));
            var after = Describe(tabs);
            changes?.Add(new ChangeRow("LocalizeDownloader", "itemList", before, after, before == after));
            if (dryRun || before == after) return;

            var so = new SerializedObject(asset);
            var prop = so.FindProperty("itemList");
            if (prop == null || !prop.isArray) return;
            prop.arraySize = tabs.Count;
            for (var i = 0; i < tabs.Count; i++)
            {
                var item = prop.GetArrayElementAtIndex(i);
                var name = item.FindPropertyRelative("sheetName");
                var gid = item.FindPropertyRelative("sheetId");
                var download = item.FindPropertyRelative("download");
                if (name != null) name.stringValue = tabs[i].Name ?? string.Empty;
                if (gid != null) gid.stringValue = tabs[i].Gid ?? string.Empty;
                if (download != null) download.boolValue = tabs[i].Download;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }

        internal static string Describe(IEnumerable<Tab> tabs)
        {
            var parts = new List<string>();
            foreach (var tab in tabs) parts.Add($"{tab.Name}({tab.Gid}){(tab.Download ? string.Empty : "-off")}");
            return string.Join(", ", parts);
        }

        #endregion

        #region Download

        private static UnityEditor.Editor _downloadEditor;

        /// <summary>
        ///     Chạy đúng luồng "Download Data Language" của inspector LocalizeDownloader (tải CSV từng tab rồi sinh
        ///     asset). Hàm đó private trong custom editor của package nên gọi qua reflection; không gọi được thì
        ///     chọn asset để người dùng bấm nút trong Inspector. Trả về câu mô tả đã làm gì.
        /// </summary>
        internal static string StartDownload()
        {
            var asset = FindAsset();
            if (asset == null) return "Chưa có asset LocalizeDownloader — bấm Áp dụng trước.";

            try
            {
                if (_downloadEditor != null) Object.DestroyImmediate(_downloadEditor);
                _downloadEditor = UnityEditor.Editor.CreateEditor(asset);
                var type = _downloadEditor.GetType();
                var data = type.GetField("data", BindingFlags.NonPublic | BindingFlags.Instance);
                var method = type.GetMethod("DownloadLanguage", BindingFlags.NonPublic | BindingFlags.Instance);
                if (data == null || method == null) throw new MissingMethodException(type.Name, "DownloadLanguage");
                data.SetValue(_downloadEditor, asset);
                method.Invoke(_downloadEditor, null);
                return "Đang tải localize (xem thanh tiến độ ở Inspector của LocalizeDownloader; xong có hộp thoại báo).";
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[EzgKit] Không gọi được luồng tải của LocalizeDownloader: " + exception.Message);
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
                return "Không gọi trực tiếp được — đã chọn asset LocalizeDownloader, bấm \"Download Data Language\" trong Inspector.";
            }
        }

        #endregion
    }
}
#endif
