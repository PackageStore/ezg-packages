#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Ezg.Editor.Shared.Iap;
using Ezg.Editor.Shared.Setup;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine.UIElements;

namespace Ezg.Editor.Shared.EzgKit.Pages
{
    /// <summary>
    ///     Danh tính của app: tên dự án (agent system), công ty, tên hiển thị, bundle id Android/iOS. Mọi trang
    ///     khác (Firebase tạo app, IAP prefix SKU, link store) phụ thuộc mấy giá trị này nên nó đứng đầu luồng.
    /// </summary>
    internal sealed class ProjectInfoPage : SetupPage
    {
        #region Keys

        private const string K_PROJECT = "projectName";
        private const string K_COMPANY = "companyName";
        private const string K_PRODUCT = "productName";
        private const string K_ANDROID = "bundleAndroid";
        private const string K_IOS = "bundleIos";
        private const string K_SAME = "iosSameAsAndroid";

        #endregion

        #region UI state

        private TextField _project, _company, _product, _android, _ios;
        private Toggle _same;
        private Label _iapWarning;

        #endregion

        internal override string Id => PageIds.PROJECT;

        internal override string Title => "Thông tin dự án";

        internal override string Description =>
            "Tên dự án, công ty, tên hiển thị và bundle id — mọi bước sau (Firebase, gói bán, link store) đều theo các giá trị này.";

        internal override bool CanApply => true;

        internal override string ApplyWarning =>
            "Bundle id là danh tính của app trên store: app đã lên store thì KHÔNG đổi được. Đổi bundle id cũng làm product id IAP và config Firebase cũ lệch.";

        #region Core

        internal override PageReport Detect()
        {
            var report = new PageReport();
            var values = GetValues(false);

            if (ProjectProfileFile.Exists)
                Check(report, Validate.ProjectName(values.Str(K_PROJECT)), "Tên dự án (project-profile.json)");

            Check(report, Validate.CompanyName(values.Str(K_COMPANY)), "Company name");
            Check(report, Validate.ProductName(values.Str(K_PRODUCT)), "Product name");
            Check(report, Validate.BundleId(values.Str(K_ANDROID)), "Bundle id Android");
            Check(report, Validate.BundleIdIos(values.Str(K_IOS)), "Bundle id iOS");

            var android = values.Str(K_ANDROID);
            return report.Resolve(string.IsNullOrEmpty(android)
                ? "Chưa có bundle id."
                : $"{values.Str(K_PRODUCT)} · {android}" + (values.Str(K_IOS) == android ? string.Empty : $" / {values.Str(K_IOS)}"));
        }

        private static void Check(PageReport report, string error, string label)
        {
            if (error == null) report.Ok();
            else report.Add(EzgStatus.Warn, $"{label}: {error}", "Điền ở trang này rồi Áp dụng.");
        }

        internal override JsonObject GetValues(bool maskSecrets)
        {
            var android = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) ?? string.Empty;
            var ios = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS) ?? string.Empty;
            var project = ProjectProfileFile.Get("projectName") ?? string.Empty;
            return new JsonObject()
                .Set(K_PROJECT, project)
                .Set(K_COMPANY, PlayerSettings.companyName)
                .Set(K_PRODUCT, PlayerSettings.productName)
                .Set(K_ANDROID, android)
                .Set(K_IOS, ios)
                .Set(K_SAME, android == ios);
        }

        internal override ApplyResult Apply(JsonObject values, bool dryRun)
        {
            var result = new ApplyResult { DryRun = dryRun };

            string Value(string key) => values.Has(key) ? values.Str(key).Trim() : null;

            var company = Value(K_COMPANY);
            var product = Value(K_PRODUCT);
            var android = Value(K_ANDROID);
            var ios = values.Bool(K_SAME) && android != null ? android : Value(K_IOS);

            // Chặn giá trị sai định dạng — ghi bundle id rỗng / sai là hỏng build.
            foreach (var (value, error) in new[]
                     {
                         (android, android == null ? null : Validate.BundleId(android)),
                         (ios, ios == null ? null : Validate.BundleIdIos(ios)),
                     })
                if (error != null && !error.StartsWith("Đang là id mẫu", StringComparison.Ordinal))
                    return ApplyResult.Fail($"Bundle id \"{value}\": {error}");

            const string sink = "PlayerSettings";
            var playerSettings = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
            if (!dryRun && playerSettings != null) Undo.RecordObject(playerSettings, "EzgKit: Thông tin dự án");

            void SetText(string field, string current, string wanted, Action<string> write)
            {
                if (wanted == null) return;
                var same = current == wanted;
                result.Rows.Add(new ChangeRow(sink, field, current, wanted, same));
                if (!same && !dryRun) write(wanted);
            }

            SetText("companyName", PlayerSettings.companyName, company, v => PlayerSettings.companyName = v);
            SetText("productName", PlayerSettings.productName, product, v => PlayerSettings.productName = v);
            SetText("applicationIdentifier[Android]", PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android), android,
                v => PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, v));
            SetText("applicationIdentifier[iOS]", PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS), ios,
                v => PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, v));

            var project = Value(K_PROJECT);
            if (!string.IsNullOrEmpty(project))
            {
                if (!ProjectProfileFile.Set(new[] { (new[] { "projectName" }, project) }, dryRun, result.Rows, out var error))
                    return ApplyResult.Fail("project-profile.json: " + error);
            }

            if (!dryRun) AssetDatabase.SaveAssets();
            result.Notes.Add("Builder của template chạy lại sẽ ghi đè ProjectSettings — khi đó mở trang này bấm Áp dụng lại (giá trị đọc từ state).");
            if (!dryRun)
            {
                EzgKitState.SetAnswer(Id, K_COMPANY, PlayerSettings.companyName);
                EzgKitState.SetAnswer(Id, K_PRODUCT, PlayerSettings.productName);
                EzgKitState.SetAnswer(Id, K_ANDROID, PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android));
                EzgKitState.SetAnswer(Id, K_IOS, PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS));
            }

            return result;
        }

        #endregion

        #region UI

        internal override void Build(VisualElement body, IPageHost host)
        {
            var values = GetValues(false);

            if (ProjectProfileFile.Exists)
            {
                var agent = Ui.Card(body, "Agent system", "Tên Claude và các script trong .claude/ dùng để gọi dự án (project-profile.json › projectName).");
                _project = Ui.TextRow(agent, "Tên dự án", values.Str(K_PROJECT), "Không đổi tên thư mục / file .sln — chỉ là tên hiển thị cho agent.",
                    Validate.ProjectName);
            }

            var identity = Ui.Card(body, "Danh tính app", "Ghi vào Player Settings.");
            _company = Ui.TextRow(identity, "Company name", values.Str(K_COMPANY), "Tên studio / publisher hiện trên store.", Validate.CompanyName);
            _product = Ui.TextRow(identity, "Product name", values.Str(K_PRODUCT), "Tên game dưới icon trên máy.", Validate.ProductName);

            var bundle = Ui.Card(body, "Bundle id", "Danh tính app trên Google Play / App Store. Đã lên store là không đổi được.");
            _android = Ui.TextRow(bundle, "Android", values.Str(K_ANDROID), "Ví dụ com.studio.game", Validate.BundleId,
                onChange: _ => SyncIos());
            _same = Ui.ToggleRow(bundle, "iOS dùng chung id Android", values.Bool(K_SAME), null, _ => SyncIos());
            _ios = Ui.TextRow(bundle, "iOS", values.Str(K_IOS), null, Validate.BundleIdIos, onChange: _ => CheckIap());
            _iapWarning = Ui.Hint(bundle, string.Empty);
            SyncIos();

            var suggestion = MarketingSuggestion();
            if (suggestion != null) Ui.Message(body, suggestion, EzgStatus.None);
        }

        private void SyncIos()
        {
            if (_same == null || _ios == null) return;
            _ios.SetEnabled(!_same.value);
            if (_same.value) _ios.value = _android.value;
            CheckIap();
        }

        /// <summary>Product id IAP đang theo bundle cũ → nhắc ngay khi gõ bundle mới.</summary>
        private void CheckIap()
        {
            if (_iapWarning == null) return;
            var bundle = _android?.value?.Trim();
            _iapWarning.text = string.Empty;
            if (string.IsNullOrEmpty(bundle)) return;
            try
            {
                var audit = IapAudit.Current();
                if (!audit.HasClient) return;
                var mismatched = 0;
                foreach (var id in audit.Catalog.RegisteredIds)
                    if (!id.StartsWith(bundle + ".", StringComparison.Ordinal))
                        mismatched++;
                if (mismatched > 0)
                    _iapWarning.text = $"⚠ {mismatched}/{audit.Catalog.RegisteredIds.Count} product id IAP không mang prefix \"{bundle}.\" — sửa gói bán (MCP gói bán) cho khớp.";
            }
            catch (Exception)
            {
                // Trang IAP lo báo lỗi đọc catalog; ở đây chỉ là gợi ý.
            }
        }

        /// <summary>Sheet marketing đã tải có package name / tên game khác → gợi ý (không ghi đè lặng lẽ).</summary>
        private static string MarketingSuggestion()
        {
            try
            {
                var path = Marketing.MarketingConfig.JsonPath;
                if (!System.IO.File.Exists(path)) return null;
                var cfg = UnityEngine.JsonUtility.FromJson<Marketing.MarketingConfig>(System.IO.File.ReadAllText(path));
                if (cfg == null) return null;
                var android = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
                if (!string.IsNullOrEmpty(cfg.packageName) && cfg.packageName != android)
                    return $"Sheet marketing ghi package name \"{cfg.packageName}\" (tên game \"{cfg.gameName}\") — khác giá trị hiện tại. Điền vào ô trên nếu đúng.";
            }
            catch (Exception)
            {
                // Bỏ qua — sheet hỏng thì trang Marketing báo.
            }

            return null;
        }

        internal override JsonObject CollectUi()
        {
            var values = new JsonObject();
            if (_project != null) values.Set(K_PROJECT, _project.value);
            values.Set(K_COMPANY, _company.value)
                .Set(K_PRODUCT, _product.value)
                .Set(K_ANDROID, _android.value)
                .Set(K_SAME, _same.value)
                .Set(K_IOS, _same.value ? _android.value : _ios.value);
            return values;
        }

        #endregion
    }
}
#endif
