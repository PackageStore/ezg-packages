#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Ezg.Editor.Shared.Marketing;
using Ezg.Editor.Shared.Setup;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ezg.Editor.Shared.EzgKit.Pages
{
    /// <summary>
    ///     Sheet marketing (Google Sheet của PM: package name, MAX / AdMob / Facebook id, AppsFlyer key, Apple ID…)
    ///     → <c>ProjectSettings/MarketingConfig.json</c> → AdsConfig, AppLovinSettings, consent MAX, FacebookSettings,
    ///     AndroidManifest, GameConstant và <c>AppSecretsConfig</c>. Phần AppSecrets không có trong sheet (webhook,
    ///     endpoint, sandbox) gõ thẳng ở trang này.
    /// </summary>
    internal sealed class MarketingPage : SetupPage
    {
        #region Keys

        private const string K_SHEET = "sheetUrl";
        private const string K_PREFIX = "projectPrefix";
        private const string K_FETCH = "fetch";
        private const string K_PLAYER = "writePlayerSettings";

        #endregion

        #region UI state

        private TextField _sheet, _prefix;
        private Toggle _player;
        private readonly Dictionary<string, TextField> _secretFields = new();
        private Toggle _sandbox;
        private Label _fetchMessage;

        #endregion

        internal override string Id => PageIds.MARKETING;

        internal override string Title => "Marketing & AppSecrets";

        internal override string Description =>
            "Tải sheet marketing của PM rồi ghi id quảng cáo / attribution vào đúng chỗ project đọc; điền key riêng của app (AppSecretsConfig).";

        internal override bool CanApply => true;

        #region Core

        internal override PageReport Detect()
        {
            var report = new PageReport();
            var sheet = MarketingSheetFetcher.CurrentSheetUrl;
            if (string.IsNullOrEmpty(sheet)) report.Add(EzgStatus.Warn, "Chưa khai link sheet marketing.", "Dán link sheet ở card \"Sheet marketing\" rồi bấm Tải sheet.");
            else report.Ok();

            var hasConfig = File.Exists(MarketingConfig.JsonPath);
            if (!hasConfig && !string.IsNullOrEmpty(sheet))
                report.Add(EzgStatus.Warn, "Chưa tải sheet (MarketingConfig.json chưa có).", "Bấm Tải sheet.");

            if (hasConfig)
            {
                var status = MarketingConfigApplier.Collect(true, new MarketingConfigApplier.Options { WritePlayerSettings = false });
                if (status.Config == null)
                    report.Add(EzgStatus.Error, "MarketingConfig.json hỏng.", "Tải lại sheet.");
                else if (status.PendingCount > 0)
                    report.Add(EzgStatus.Warn, $"{status.PendingCount} ô trong project còn lệch sheet.", "Bấm Áp dụng để ghi.");
                else report.Ok();
            }

            if (AppSecretsSink.TypeExists)
            {
                var asset = AppSecretsSink.FindAsset();
                if (asset == null)
                    report.Add(EzgStatus.Warn, "Chưa có AppSecretsConfig.asset — AppsFlyer, bug logger, link pháp lý đang tắt.",
                        "Điền card AppSecrets rồi Áp dụng (kit tạo asset).");
                else
                {
                    var placement = AppSecretsSink.PlacementProblem();
                    if (placement != null) report.Add(EzgStatus.Error, placement, "Chuyển asset vào Resources, đặt tên AppSecretsConfig.");
                    if (string.IsNullOrEmpty(AppSecretsSink.Read(AppSecretsSink.F_APPSFLYER_KEY)))
                        report.Add(EzgStatus.Warn, "Chưa có AppsFlyer dev key.", "Lấy ở AppsFlyer > App Settings.");
                    else report.Ok();
                    if (string.IsNullOrEmpty(AppSecretsSink.Read(AppSecretsSink.F_PRIVACY)) || string.IsNullOrEmpty(AppSecretsSink.Read(AppSecretsSink.F_TERMS)))
                        report.Add(EzgStatus.Warn, "Thiếu link Privacy policy / Terms of service.", "Store review và consent MAX đều cần.");
                    else report.Ok();
                    if (AppSecretsSink.ReadBool(AppSecretsSink.F_SANDBOX))
                        report.Add(EzgStatus.Warn, "Purchase sandbox AppsFlyer đang BẬT.", "Tắt trước khi build store (doanh thu thật không ghi nhận khi bật).");
                }
            }

            return report.Resolve(string.IsNullOrEmpty(sheet) ? "Chưa có sheet marketing." : hasConfig ? "Đã tải sheet." : "Có link sheet, chưa tải.");
        }

        internal override JsonObject GetValues(bool maskSecrets)
        {
            var values = new JsonObject()
                .Set(K_SHEET, MarketingSheetFetcher.CurrentSheetUrl)
                .Set(K_PREFIX, MarketingSheetFetcher.CurrentProjectPrefix)
                .Set(K_PLAYER, false);
            var secrets = AppSecretsSink.ReadAll();
            foreach (var (field, _, secret, _) in AppSecretsSink.StringFields)
            {
                secrets.TryGetValue(field, out var value);
                values.Set(field, maskSecrets && secret ? Mask.Secret(value) : value ?? string.Empty);
            }

            secrets.TryGetValue(AppSecretsSink.F_SANDBOX, out var sandbox);
            values.Set(AppSecretsSink.F_SANDBOX, sandbox == "true");
            values.Set("appSecretsAsset", AppSecretsSink.AssetPath ?? string.Empty);
            return values;
        }

        /// <summary>
        ///     <c>fetch</c> = true thì tải sheet trước (CHỈ khi không dry-run — tải là ghi MarketingConfig.json).
        ///     <c>writePlayerSettings</c> mặc định false: bundle id / product name thuộc trang Thông tin dự án.
        ///     Field AppSecrets nào có mặt trong <paramref name="values" /> thì ghi (giá trị che • bị bỏ qua).
        /// </summary>
        internal override ApplyResult Apply(JsonObject values, bool dryRun)
        {
            var result = new ApplyResult { DryRun = dryRun };

            var sheet = values.Has(K_SHEET) ? values.Str(K_SHEET).Trim() : null;
            var prefix = values.Has(K_PREFIX) ? values.Str(K_PREFIX).Trim() : null;
            if (sheet != null)
            {
                var error = Validate.GoogleSheet(sheet);
                if (error != null) return ApplyResult.Fail("Link sheet: " + error);
                var currentSheet = MarketingSheetFetcher.CurrentSheetUrl;
                var currentPrefix = MarketingSheetFetcher.CurrentProjectPrefix;
                result.Rows.Add(new ChangeRow("MarketingSource.json", "sheetUrl", currentSheet, sheet, currentSheet == sheet));
                if (prefix != null) result.Rows.Add(new ChangeRow("MarketingSource.json", "projectPrefix", currentPrefix, prefix, currentPrefix == prefix));
                if (!dryRun && (currentSheet != sheet || prefix != null && currentPrefix != prefix))
                    MarketingSheetFetcher.SaveSheetUrl(sheet, prefix ?? currentPrefix);
            }

            if (values.Bool(K_FETCH))
            {
                if (dryRun) result.Notes.Add("Tải sheet chỉ chạy khi Áp dụng (tải = ghi lại MarketingConfig.json).");
                else if (!MarketingSheetFetcher.Fetch(out var fetchReport)) return ApplyResult.Fail(fetchReport);
                else result.Notes.Add(fetchReport.Trim());
            }

            var overrides = new Dictionary<string, string>();
            foreach (var (field, _, _, _) in AppSecretsSink.StringFields)
            {
                if (!values.Has(field)) continue;
                var value = values.Str(field);
                if (value.IndexOf('•') >= 0) continue; // giá trị che từ GetValues(true) — không ghi đè
                overrides[field] = value;
            }

            if (values.Has(AppSecretsSink.F_SANDBOX)) overrides[AppSecretsSink.F_SANDBOX] = values.Bool(AppSecretsSink.F_SANDBOX) ? "true" : "false";

            var status = MarketingConfigApplier.Collect(dryRun, new MarketingConfigApplier.Options
            {
                WritePlayerSettings = values.Bool(K_PLAYER),
                AppSecretsOverrides = overrides.Count == 0 ? null : overrides,
            });

            foreach (var change in status.Rows)
                result.Rows.Add(new ChangeRow(change.Sink, change.Field, change.OldValue, change.NewValue, change.Matched,
                    change.Sink == "AdsConfig" && change.Field.Contains("SdkKey")));
            foreach (var skipped in status.Skipped) result.Notes.Add("Bỏ qua: " + skipped);
            foreach (var todo in status.Todos) result.Notes.Add("Việc tay: " + todo);
            foreach (var error in status.Errors)
                if (status.Config != null || !error.StartsWith("Chua tai sheet")) result.Notes.Add("Lỗi: " + error);
                else result.Notes.Add("Chưa có sheet — chỉ ghi phần AppSecrets.");
            return result;
        }

        #endregion

        #region UI

        internal override void Build(VisualElement body, IPageHost host)
        {
            var values = GetValues(false);

            var sheetCard = Ui.Card(body, "Sheet marketing",
                "Sheet phải mở \"Anyone with the link – Viewer\". Kit dò dòng theo NHÃN ở cột đầu (Package name, Max SDK key, AF key…), cột Android/iOS theo hàng tiêu đề.");
            _sheet = Ui.TextRow(sheetCard, "Link Google Sheet", values.Str(K_SHEET), null, v => Validate.GoogleSheet(v));
            _prefix = Ui.TextRow(sheetCard, "Mã dự án trong sheet", values.Str(K_PREFIX),
                "Ví dụ I001 — hàng tiêu đề có I001a (Android) / I001i (iOS). Trống = tự dò cột.");
            _player = Ui.ToggleRow(sheetCard, "Ghi cả bundle id / tên game", false,
                "Tắt (mặc định): package name / tên game của sheet chỉ là gợi ý ở trang Thông tin dự án.");
            var actions = Ui.Row(sheetCard, "ezg-actions");
            Ui.Button(actions, "Tải sheet", () => Fetch(host), "secondary", "Lưu link rồi tải sheet về MarketingConfig.json (chưa ghi vào project).");
            if (!string.IsNullOrEmpty(values.Str(K_SHEET))) Ui.Link(actions, "Mở sheet", values.Str(K_SHEET));
            _fetchMessage = Ui.Hint(sheetCard, string.Empty);
            SheetSummary(sheetCard);

            if (!AppSecretsSink.TypeExists)
            {
                Ui.Message(body, "Dự án không có AppSecretsConfig (template cũ) — AppsFlyer key / App Store ID / link pháp lý ghi vào const trong GameConstant.cs từ sheet.",
                    EzgStatus.None);
                return;
            }

            var assetPath = values.Str("appSecretsAsset");
            var secrets = Ui.Card(body, "AppSecrets (key riêng của app)",
                string.IsNullOrEmpty(assetPath)
                    ? $"Chưa có asset — Áp dụng sẽ tạo {AppSecretsSink.DEFAULT_ASSET_PATH}. Ô trống ở đây = giữ như sheet / để trống."
                    : $"{assetPath} — runtime đọc qua Resources.Load. Ô có giá trị thắng sheet.");
            _secretFields.Clear();
            foreach (var (field, label, secret, hint) in AppSecretsSink.StringFields)
            {
                System.Func<string, string> validate = field switch
                {
                    AppSecretsSink.F_IOS_APP_ID => Validate.AppStoreId,
                    AppSecretsSink.F_DISCORD_BUG => Validate.DiscordWebhook,
                    AppSecretsSink.F_DISCORD_FEEDBACK => Validate.DiscordWebhook,
                    AppSecretsSink.F_PRIVACY => v => Validate.Url(v),
                    AppSecretsSink.F_TERMS => v => Validate.Url(v),
                    AppSecretsSink.F_FEEDBACK => v => Validate.Url(v),
                    AppSecretsSink.F_SERVER_TIME => v => Validate.Url(v),
                    AppSecretsSink.F_EMPLOYEE_API => v => Validate.Url(v),
                    _ => null,
                };
                _secretFields[field] = Ui.TextRow(secrets, label, values.Str(field), hint, validate, secret);
            }

            _sandbox = Ui.ToggleRow(secrets, "Purchase sandbox (AppsFlyer)", values.Bool(AppSecretsSink.F_SANDBOX),
                "Chỉ bật khi test TestFlight / License Tester. PHẢI tắt cho bản store.");
            var fill = Ui.Row(secrets, "ezg-actions");
            Ui.Button(fill, "Lấy từ sheet", FillFromSheet, "secondary", "Điền AppsFlyer key, App Store ID, Privacy / Terms từ MarketingConfig.json.");
        }

        private static void SheetSummary(VisualElement parent)
        {
            if (!File.Exists(MarketingConfig.JsonPath))
            {
                Ui.Hint(parent, "Chưa tải sheet lần nào.");
                return;
            }

            var cfg = MarketingConfig.Load();
            if (cfg == null)
            {
                Ui.Message(parent, "MarketingConfig.json hỏng — xem Console, tải lại sheet.", EzgStatus.Error);
                return;
            }

            var fold = Ui.Fold(parent, "Giá trị đọc từ sheet", false);
            Ui.InfoRow(fold, "Package name", cfg.packageName);
            Ui.InfoRow(fold, "Tên game", cfg.gameName);
            Ui.InfoRow(fold, "Apple ID", cfg.appleId);
            Ui.InfoRow(fold, "AppsFlyer key", Mask.Secret(cfg.appsflyerDevKey));
            Ui.InfoRow(fold, "MAX SDK key", Mask.Secret(cfg.max?.sdkKey));
            Ui.InfoRow(fold, "MAX Android (inter / rewarded)", $"{cfg.max?.android?.interstitial} / {cfg.max?.android?.rewarded}");
            Ui.InfoRow(fold, "MAX iOS (inter / rewarded)", $"{cfg.max?.ios?.interstitial} / {cfg.max?.ios?.rewarded}");
            Ui.InfoRow(fold, "AdMob app id Android / iOS", $"{cfg.admob?.android?.appId} / {cfg.admob?.ios?.appId}");
            Ui.InfoRow(fold, "Facebook app id", cfg.facebook?.appId);
            Ui.InfoRow(fold, "Privacy / Terms", $"{cfg.applovin?.privacyPolicyUrl} / {cfg.applovin?.termsOfServiceUrl}");
        }

        private void Fetch(IPageHost host)
        {
            var url = _sheet.value.Trim();
            var error = Validate.GoogleSheet(url, false);
            if (error != null)
            {
                host.Toast(error, EzgStatus.Error);
                return;
            }

            MarketingSheetFetcher.SaveSheetUrl(url, _prefix.value.Trim());
            if (MarketingSheetFetcher.Fetch(out var report))
            {
                host.RefreshAll();
                host.Rebuild();
                host.Toast("Đã tải sheet. Bấm \"Xem thay đổi\" để xem sẽ ghi gì vào project.", EzgStatus.Ok);
            }
            else
            {
                if (_fetchMessage != null) _fetchMessage.text = report;
                host.Toast("Tải sheet lỗi: " + report, EzgStatus.Error);
            }
        }

        private void FillFromSheet()
        {
            var cfg = File.Exists(MarketingConfig.JsonPath) ? MarketingConfig.Load() : null;
            if (cfg == null) return;

            void Fill(string field, string value)
            {
                if (!string.IsNullOrEmpty(value) && _secretFields.TryGetValue(field, out var input)) input.value = value;
            }

            Fill(AppSecretsSink.F_APPSFLYER_KEY, cfg.appsflyerDevKey);
            Fill(AppSecretsSink.F_IOS_APP_ID, cfg.appleId);
            Fill(AppSecretsSink.F_PRIVACY, cfg.applovin?.privacyPolicyUrl);
            Fill(AppSecretsSink.F_TERMS, cfg.applovin?.termsOfServiceUrl);
        }

        internal override JsonObject CollectUi()
        {
            var values = new JsonObject()
                .Set(K_SHEET, _sheet.value)
                .Set(K_PREFIX, _prefix.value)
                .Set(K_PLAYER, _player.value);
            foreach (var pair in _secretFields) values.Set(pair.Key, pair.Value.value);
            if (_sandbox != null) values.Set(AppSecretsSink.F_SANDBOX, _sandbox.value);

            // Link sheet trống mà dự án chưa có sheet: đừng ghi đè MarketingSource bằng chuỗi rỗng.
            if (string.IsNullOrWhiteSpace(_sheet.value)) values.Remove(K_SHEET);
            return values;
        }

        #endregion
    }
}
#endif
