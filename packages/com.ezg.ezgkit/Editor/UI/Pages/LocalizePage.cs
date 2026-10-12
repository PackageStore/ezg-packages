#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Ezg.Editor.Shared.Setup;
using UnityEngine.UIElements;

namespace Ezg.Editor.Shared.EzgKit.Pages
{
    /// <summary>
    ///     Localization: link file localize (Google Sheet của team), thư mục CSV / asset, tab cần tải. Ghi đồng bộ
    ///     vào <c>LocalizeDownloader.asset</c> (com.ezg.localize) và <c>project-profile.json › localize</c> — trước đây
    ///     hai nơi mặc định hai thư mục khác nhau.
    ///     <para>
    ///         Quyền ghi của skill add-localize không còn là file key trong project: skill gọi tool
    ///         <c>localize_add_keys</c> của EZG MCP, server ký bằng service account dùng chung. Trang này chỉ nhắc share
    ///         sheet cho email đó và dọn <c>localize.serviceAccount</c> của cách cũ.
    ///     </para>
    /// </summary>
    internal sealed class LocalizePage : SetupPage
    {
        #region Keys

        private const string K_SHEET = "sheet";
        private const string K_CSV = "csvRoot";
        private const string K_DEFAULT_TAB = "defaultTab";
        private const string K_STAGING_TAB = "stagingTab";
        private const string K_TABS = "tabs";

        #endregion

        #region UI state

        private TextField _sheet, _csv, _defaultTab, _stagingTab;
        private VisualElement _tabList;
        private readonly List<(TextField Name, TextField Gid, Toggle Download)> _tabRows = new();
        private Label _reach;

        #endregion

        internal override string Id => PageIds.LOCALIZE;

        internal override string Title => "Localization";

        internal override string Description =>
            "Link file localize, tab và thư mục dữ liệu ngôn ngữ — để tải localize vào game và để add-localize ghi key mới (qua EZG MCP).";

        /// <summary>
        ///     Service account của EZG MCP (secret GOOGLE_SA_KEY của ezg-mcp) — sheet phải share Editor cho email này để
        ///     add-localize ghi được. Đổi service account thì sửa ở đây; tool <c>gsheet_info</c> trả giá trị đang chạy.
        /// </summary>
        internal const string EZG_SERVICE_ACCOUNT = "ezg-game-docs@gameflow-500603.iam.gserviceaccount.com";

        /// <summary>Đường dẫn file key của cách cũ (trước EZG MCP); rỗng = không còn.</summary>
        private static string LegacyServiceAccount() => ProjectProfileFile.Get("localize", "serviceAccount") ?? string.Empty;

        internal override bool CanApply => true;

        #region Core

        private static string ProfileCsvRoot() => ProjectProfileFile.Get("localize", "csvRoot");

        /// <summary>Thư mục CSV đang dùng: profile → LocalizeDownloader → mặc định đề xuất.</summary>
        private static string CurrentCsvRoot()
        {
            var profile = ProfileCsvRoot();
            if (!string.IsNullOrEmpty(profile)) return profile;
            var downloader = LocalizeBridge.DownloaderSavePath(LocalizeBridge.FindAsset());
            return string.IsNullOrEmpty(downloader) ? LocalizeBridge.RECOMMENDED_CSV_ROOT : downloader;
        }

        private static string CurrentSheet()
        {
            var profile = ProjectProfileFile.Get("localize", "sheet");
            if (!string.IsNullOrEmpty(profile)) return profile;
            var downloader = LocalizeBridge.DownloaderUrl(LocalizeBridge.FindAsset());
            return downloader == Setup.LocalizeBridge.BaseUrl(DefaultDownloaderUrl) ? string.Empty : downloader ?? string.Empty;
        }

        /// <summary>Link mặc định ship trong com.ezg.localize — bảng mẫu của team, KHÔNG phải sheet của game.</summary>
        private const string DefaultDownloaderUrl = "https://docs.google.com/spreadsheets/d/1JDChbnV93bYxYP7ulX4X6KYZk9XAS4kHQDihaEnD-3c";

        internal override PageReport Detect()
        {
            var report = new PageReport();
            var sheet = CurrentSheet();
            if (string.IsNullOrEmpty(sheet)) report.Add(EzgStatus.Warn, "Chưa khai link file localize của game.", "Dán link Google Sheet rồi Áp dụng.");
            else report.Ok();

            var downloader = LocalizeBridge.FindAsset();
            if (LocalizeBridge.Installed)
            {
                if (downloader == null) report.Add(EzgStatus.Warn, "Chưa có LocalizeDownloader.asset.", "Áp dụng ở trang này sẽ tạo.");
                else
                {
                    var url = LocalizeBridge.DownloaderUrl(downloader);
                    if (url == LocalizeBridge.BaseUrl(DefaultDownloaderUrl))
                        report.Add(EzgStatus.Warn, "LocalizeDownloader còn trỏ bảng mẫu của package.", "Áp dụng để trỏ sang sheet của game.");
                    else if (!string.IsNullOrEmpty(sheet) && url != LocalizeBridge.BaseUrl(sheet))
                        report.Add(EzgStatus.Warn, "LocalizeDownloader trỏ sheet khác project-profile.", "Áp dụng để đồng bộ.");
                    else report.Ok();

                    var save = LocalizeBridge.DownloaderSavePath(downloader);
                    var profileRoot = ProfileCsvRoot();
                    if (!string.IsNullOrEmpty(profileRoot) && save != profileRoot)
                        report.Add(EzgStatus.Warn, $"Hai nơi đọc dữ liệu khác thư mục: LocalizeDownloader \"{save}\" ≠ add-localize \"{profileRoot}\".",
                            "Chọn một thư mục rồi Áp dụng (ghi cả hai).");
                }
            }

            var legacy = LegacyServiceAccount();
            if (!string.IsNullOrEmpty(legacy))
                report.Add(EzgStatus.Warn, $"project-profile còn localize.serviceAccount \"{legacy}\" — cách cũ, cần file key trong project.",
                    "Áp dụng để bỏ trống nó, rồi xoá file key. add-localize giờ ghi qua EZG MCP (share sheet Editor cho " + EZG_SERVICE_ACCOUNT + ").");

            var languages = LocalizeBridge.LanguagesOnDisk(CurrentCsvRoot());
            if (languages.Count == 0) report.Add(EzgStatus.Warn, "Chưa có dữ liệu ngôn ngữ nào — text trong game hiện dạng #key.", "Áp dụng rồi bấm Tải localize.");
            else report.Ok();

            return report.Resolve(languages.Count == 0 ? "Chưa tải dữ liệu." : $"{languages.Count} ngôn ngữ: {string.Join(", ", languages)}");
        }

        internal override JsonObject GetValues(bool maskSecrets)
        {
            var downloader = LocalizeBridge.FindAsset();
            var tabs = new List<object>();
            foreach (var tab in LocalizeBridge.DownloaderTabs(downloader))
                tabs.Add(new JsonObject().Set("name", tab.Name).Set("gid", tab.Gid).Set("download", tab.Download));
            var csv = CurrentCsvRoot();
            return new JsonObject()
                .Set(K_SHEET, CurrentSheet())
                .Set("writeServiceAccount", EZG_SERVICE_ACCOUNT)
                .Set(K_CSV, csv)
                .Set("assetsRoot", LocalizeBridge.AssetsRootOf(csv))
                .Set(K_DEFAULT_TAB, ProjectProfileFile.Get("localize", "defaultTab") ?? "common")
                .Set(K_STAGING_TAB, ProjectProfileFile.Get("localize", "stagingTab") ?? "template")
                .Set(K_TABS, tabs)
                .Set("languagesOnDisk", LocalizeBridge.LanguagesOnDisk(csv))
                .Set("downloaderLanguages", LocalizeBridge.DownloaderLanguages(downloader))
                .Set("downloaderAsset", downloader == null ? string.Empty : UnityEditor.AssetDatabase.GetAssetPath(downloader));
        }

        internal override ApplyResult Apply(JsonObject values, bool dryRun)
        {
            var result = new ApplyResult { DryRun = dryRun };
            var sheet = values.Has(K_SHEET) ? values.Str(K_SHEET).Trim() : null;
            if (sheet != null && sheet.Length > 0)
            {
                var error = Validate.GoogleSheet(sheet);
                if (error != null) return ApplyResult.Fail("Link file localize: " + error);
            }

            var csv = values.Has(K_CSV) ? values.Str(K_CSV).Trim().TrimEnd('/') : null;
            if (csv != null && csv.Length > 0 && !csv.EndsWith("/LocalizationData"))
                return ApplyResult.Fail("Thư mục CSV phải kết thúc bằng /LocalizationData (asset sinh ở thư mục anh em Resources/LocalizationData).");

            // Cách cũ: bỏ trống đường dẫn key (script add-localize không còn đọc nó) và nhắc xoá file key.
            var legacy = LegacyServiceAccount();
            if (!string.IsNullOrEmpty(legacy))
            {
                var legacyAbs = Path.IsPathRooted(legacy) ? legacy : ProjectPaths.Abs(legacy);
                if (File.Exists(legacyAbs))
                    result.Notes.Add($"File key cũ \"{legacy}\" không còn được dùng — xoá nó (và đừng commit). add-localize ghi qua EZG MCP.");
            }

            List<LocalizeBridge.Tab> tabs = null;
            var list = values.List(K_TABS);
            if (list != null)
            {
                tabs = new List<LocalizeBridge.Tab>();
                foreach (var item in list)
                    if (item is JsonObject obj && !string.IsNullOrWhiteSpace(obj.Str("name")))
                        tabs.Add(new LocalizeBridge.Tab { Name = obj.Str("name").Trim(), Gid = obj.Str("gid").Trim(), Download = obj.Bool("download", true) });
            }

            if (!LocalizeBridge.WriteDownloader(string.IsNullOrEmpty(sheet) ? null : sheet, string.IsNullOrEmpty(csv) ? null : csv, tabs,
                    dryRun, result.Rows, out var downloaderError))
                return ApplyResult.Fail(downloaderError);

            var entries = new List<(string[], string)>
            {
                (new[] { "localize", "sheet" }, string.IsNullOrEmpty(sheet) ? null : sheet),
                (new[] { "localize", "serviceAccount" }, string.IsNullOrEmpty(legacy) ? null : string.Empty),
                (new[] { "localize", "csvRoot" }, string.IsNullOrEmpty(csv) ? null : csv),
                (new[] { "localize", "assetsRoot" }, string.IsNullOrEmpty(csv) ? null : LocalizeBridge.AssetsRootOf(csv)),
                (new[] { "localize", "defaultTab" }, values.Has(K_DEFAULT_TAB) ? values.Str(K_DEFAULT_TAB).Trim() : null),
                (new[] { "localize", "stagingTab" }, values.Has(K_STAGING_TAB) ? values.Str(K_STAGING_TAB).Trim() : null),
            };
            if (!ProjectProfileFile.Set(entries, dryRun, result.Rows, out var profileError))
                return ApplyResult.Fail("project-profile.json: " + profileError);

            if (values.Bool("download") && !dryRun) result.Notes.Add(LocalizeBridge.StartDownload());
            return result;
        }

        #endregion

        #region UI

        internal override void Build(VisualElement body, IPageHost host)
        {
            var values = GetValues(false);

            var source = Ui.Card(body, "File localize",
                "Google Sheet của game: mỗi tab một nhóm key (common, shop…), cột = ngôn ngữ. Đọc qua link export CSV nên sheet phải mở \"Anyone with the link – Viewer\".");
            _sheet = Ui.TextRow(source, "Link file localize", values.Str(K_SHEET), null, v => Validate.GoogleSheet(v), onChange: _ => _reach.text = string.Empty);
            var sheetActions = Ui.Row(source, "ezg-actions");
            Ui.Button(sheetActions, "Kiểm tra link", () => CheckReach(), "secondary", "Tải thử CSV của tab đầu — sheet private sẽ trả HTML.");
            if (!string.IsNullOrEmpty(values.Str(K_SHEET))) Ui.Link(sheetActions, "Mở sheet", values.Str(K_SHEET));
            _reach = Ui.Hint(source, string.Empty);

            var write = Ui.Card(body, "Quyền ghi (add-localize)",
                "Skill add-localize ghi key mới qua EZG MCP (tool localize_add_keys) bằng service account dùng chung của EZG — " +
                "không cần file key trong project. Chủ sheet chỉ cần Share quyền Editor cho email dưới đây.");
            Ui.InfoRow(write, "Share Editor cho", EZG_SERVICE_ACCOUNT);
            var writeActions = Ui.Row(write, "ezg-actions");
            Ui.Button(writeActions, "Copy email", () =>
            {
                UnityEditor.EditorGUIUtility.systemCopyBuffer = EZG_SERVICE_ACCOUNT;
                host.Toast("Đã copy " + EZG_SERVICE_ACCOUNT, EzgStatus.Ok);
            }, "secondary", "Dán vào ô Share của Google Sheet, chọn Editor.");
            if (!string.IsNullOrEmpty(LegacyServiceAccount()))
                Ui.Hint(write, $"Còn localize.serviceAccount \"{LegacyServiceAccount()}\" từ cách cũ — Áp dụng sẽ bỏ trống nó; xoá file key đi.");
            _defaultTab = Ui.TextRow(write, "Tab mặc định", values.Str(K_DEFAULT_TAB), "Tab add-localize ghi vào khi không chỉ định (common).");
            _stagingTab = Ui.TextRow(write, "Tab staging", values.Str(K_STAGING_TAB), "Tab template dùng để dịch máy trước khi chuyển sang tab thật.");

            var data = Ui.Card(body, "Dữ liệu trong project",
                $"CSV ở <thư mục>/<lang>/<tab>.csv; asset runtime sinh ở thư mục anh em Resources/LocalizationData. Đề xuất: {LocalizeBridge.RECOMMENDED_CSV_ROOT}");
            _csv = Ui.PathRow(data, "Thư mục CSV", values.Str(K_CSV), true, "Ghi cho cả LocalizeDownloader và add-localize — hai bên dùng chung một chỗ.",
                v => string.IsNullOrEmpty(v) || v.TrimEnd('/').EndsWith("/LocalizationData") ? null : "Phải kết thúc bằng /LocalizationData.");
            Ui.InfoRow(data, "Ngôn ngữ đang có", string.Join(", ", (List<string>)values["languagesOnDisk"]),
                ((List<string>)values["languagesOnDisk"]).Count == 0 ? EzgStatus.Warn : EzgStatus.Ok);
            Ui.InfoRow(data, "Ngôn ngữ downloader tải", string.Join(", ", (List<string>)values["downloaderLanguages"]));
            Ui.InfoRow(data, "LocalizeDownloader.asset", values.Str("downloaderAsset"),
                string.IsNullOrEmpty(values.Str("downloaderAsset")) ? EzgStatus.Warn : EzgStatus.None,
                LocalizeBridge.Installed ? null : "Dự án chưa cài com.ezg.localize.");

            var tabsCard = Ui.Card(body, "Tab cần tải", "Tên tab + gid (số sau #gid= trên link khi mở tab đó). Sheet copy từ bảng mẫu giữ nguyên gid.");
            _tabList = new VisualElement();
            tabsCard.Add(_tabList);
            _tabRows.Clear();
            foreach (var item in values.List(K_TABS) ?? new List<object>())
                if (item is JsonObject tab)
                    AddTabRow(tab.Str("name"), tab.Str("gid"), tab.Bool("download", true));
            var tabActions = Ui.Row(tabsCard, "ezg-actions");
            Ui.Button(tabActions, "+ Thêm tab", () => AddTabRow(string.Empty, string.Empty, true), "secondary");
            Ui.Button(tabActions, "Tải localize", () =>
            {
                host.Toast(LocalizeBridge.StartDownload(), EzgStatus.None);
            }, "primary", "Chạy luồng Download Data Language của LocalizeDownloader (Áp dụng trước nếu vừa đổi link / thư mục).");
        }

        private void AddTabRow(string name, string gid, bool download)
        {
            var row = Ui.Row(_tabList, "ezg-field");
            var nameField = new TextField { value = name };
            nameField.AddToClassList("ezg-input");
            nameField.style.width = 160;
            nameField.style.marginRight = 6;
            var gidField = new TextField { value = gid };
            gidField.AddToClassList("ezg-input");
            gidField.AddToClassList("ezg-grow");
            var toggle = new Toggle("Tải") { value = download };
            toggle.AddToClassList("ezg-inline-toggle");
            toggle.style.marginLeft = 8;
            row.Add(nameField);
            row.Add(gidField);
            row.Add(toggle);
            var remove = new Button(() =>
            {
                _tabList.Remove(row);
                _tabRows.RemoveAll(r => r.Name == nameField);
            }) { text = "✕" };
            remove.AddToClassList("ezg-btn-mini");
            row.Add(remove);
            _tabRows.Add((nameField, gidField, toggle));
        }

        private void CheckReach()
        {
            var id = LocalizeBridge.SheetId(_sheet.value);
            if (id == null)
            {
                _reach.text = "Không phải link Google Sheets.";
                return;
            }

            _reach.text = "Đang kiểm…";
            var gid = _tabRows.Count > 0 ? _tabRows[0].Gid.value : "0";
            AsyncHttp.Get($"https://docs.google.com/spreadsheets/d/{id}/export?format=csv&gid={gid}", response =>
            {
                if (!response.Ok) _reach.text = "✕ Không tải được: " + response.Error;
                else if ((response.Body ?? string.Empty).TrimStart().StartsWith("<"))
                    _reach.text = "✕ Google trả HTML — sheet đang private. Share > Anyone with the link (Viewer).";
                else _reach.text = $"✓ Tải được ({(response.Body ?? string.Empty).Split('\n').Length} dòng ở tab gid={gid}).";
            });
        }

        internal override JsonObject CollectUi()
        {
            var tabs = new List<object>();
            foreach (var (name, gid, download) in _tabRows)
                if (!string.IsNullOrWhiteSpace(name.value))
                    tabs.Add(new JsonObject().Set("name", name.value).Set("gid", gid.value).Set("download", download.value));

            return new JsonObject()
                .Set(K_SHEET, _sheet.value)
                .Set(K_CSV, _csv.value)
                .Set(K_DEFAULT_TAB, _defaultTab.value)
                .Set(K_STAGING_TAB, _stagingTab.value)
                .Set(K_TABS, tabs);
        }

        #endregion
    }
}
#endif
