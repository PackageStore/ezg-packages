#if UNITY_EDITOR
using System.Collections.Generic;
using Ezg.Editor.Shared.Firebase;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine.UIElements;

namespace Ezg.Editor.Shared.EzgKit.Pages
{
    /// <summary>
    ///     Firebase (Nâng cao): dùng service account tạo app Android + iOS trên project Firebase (hoặc dùng lại app có
    ///     sẵn), tải <c>google-services.json</c> / <c>GoogleService-Info.plist</c>, đăng ký SHA-1, và tạo
    ///     <c>FirebaseConfig.asset</c> với bucket Storage đúng project (mặc định của com.ezg.firebase là bucket game khác).
    /// </summary>
    internal sealed class FirebasePage : SetupPage
    {
        private TextField _keyPath, _projectId, _androidName, _iosName, _appStoreId, _sha, _keystorePass;
        private Toggle _createProject;
        private DropdownField _projectChoices;
        private Label _report;
        private string _lastReport;

        internal override string Id => PageIds.FIREBASE;

        internal override string Title => "Firebase";

        internal override string Description =>
            "Tạo app Firebase cho bundle id hiện tại, tải file config vào Assets/, đăng ký SHA-1 và tạo FirebaseConfig.asset đúng bucket.";

        internal override string Group => GROUP_ADVANCED;

        #region Core

        internal override PageReport Detect()
        {
            var report = new PageReport();
            var source = FirebaseSource.Load();
            var android = FirebaseAppProvisioner.AndroidPackage;
            var ios = FirebaseAppProvisioner.IosBundle;

            if (!FirebaseAppProvisioner.AndroidConfigExists) report.Add(EzgStatus.Warn, "Chưa có Assets/google-services.json.", "Chạy \"Tạo app + tải config\".");
            else if (!string.IsNullOrEmpty(FirebaseAppProvisioner.LocalAndroidAppId)) report.Ok();
            if (!FirebaseAppProvisioner.IosConfigExists) report.Add(EzgStatus.Warn, "Chưa có Assets/GoogleService-Info.plist.", "Chạy \"Tạo app + tải config\".");
            else report.Ok();

            var mismatch = FirebaseSetupHelpers.Mismatch(string.IsNullOrEmpty(source.projectId)
                ? FirebaseAppProvisioner.LocalAndroidConfigProjectId
                : source.projectId);
            if (mismatch != null) report.Add(EzgStatus.Error, mismatch, "Tải lại config đúng project.");

            if (FirebaseSetupHelpers.ConfigTypeExists)
            {
                var config = FirebaseSetupHelpers.FindConfig();
                var expected = FirebaseSetupHelpers.ExpectedBucket();
                if (config == null) report.Add(EzgStatus.Warn, "Thiếu Resources/FirebaseConfig.asset — save-sync dùng bucket mặc định của game khác.", "Bấm \"Tạo / sửa FirebaseConfig\".");
                else
                {
                    var bucket = Setup.SerializedAsset.GetString(config, "storageBucketUrl") ?? string.Empty;
                    if (expected != null && bucket.TrimEnd('/') != expected)
                        report.Add(EzgStatus.Error, $"FirebaseConfig.storageBucketUrl = \"{bucket}\" ≠ bucket của project ({expected}).", "Bấm \"Tạo / sửa FirebaseConfig\".");
                    else report.Ok();
                }
            }

            return report.Resolve(string.IsNullOrEmpty(source.projectId)
                ? $"Chưa khai project · {android}"
                : $"Project {source.projectId} · {android}" + (ios == android ? string.Empty : " / " + ios));
        }

        internal override JsonObject GetValues(bool maskSecrets)
        {
            var source = FirebaseSource.Load();
            return new JsonObject()
                .Set("projectId", source.projectId ?? string.Empty)
                .Set("androidDisplayName", source.androidDisplayName ?? string.Empty)
                .Set("iosDisplayName", source.iosDisplayName ?? string.Empty)
                .Set("appStoreId", source.appStoreId ?? string.Empty)
                .Set("createProjectIfMissing", source.createProjectIfMissing)
                .Set("keyPathSet", !string.IsNullOrEmpty(FirebaseSource.KeyPath))
                .Set("androidConfig", FirebaseAppProvisioner.AndroidConfigExists)
                .Set("iosConfig", FirebaseAppProvisioner.IosConfigExists)
                .Set("configProject", FirebaseAppProvisioner.LocalAndroidConfigProjectId ?? string.Empty);
        }

        #endregion

        #region UI

        internal override void Build(VisualElement body, IPageHost host)
        {
            var source = FirebaseSource.Load();
            var keyInfo = FirebaseSetupHelpers.ReadKey(FirebaseSource.KeyPath);

            var key = Ui.Card(body, "Service account",
                "File JSON của service account có role Firebase Admin. Lưu đường dẫn theo máy (EditorPrefs) — không để file trong project.");
            _keyPath = Ui.PathRow(key, "File key JSON", FirebaseSource.KeyPath, false, null, v =>
            {
                if (string.IsNullOrEmpty(v)) return "Chưa chọn file.";
                return ProjectPaths.IsInsideProject(ProjectPaths.Abs(v)) ? "File key nằm trong project — kit từ chối dùng (dễ bị commit)." : null;
            }, null, "json");
            if (keyInfo.Error == null)
            {
                Ui.InfoRow(key, "Service account", keyInfo.Email, EzgStatus.Ok);
                Ui.InfoRow(key, "Project của key", keyInfo.ProjectId);
            }
            else Ui.InfoRow(key, "Trạng thái key", keyInfo.Error, EzgStatus.Warn);

            var project = Ui.Card(body, "Project Firebase", "Project id (không phải project number). Trống = lấy theo project của key.");
            _projectId = Ui.TextRow(project, "Project id", source.projectId);
            var probe = Ui.Row(project, "ezg-actions");
            Ui.Button(probe, "Dò project của key", () => Probe(host), "secondary", "Hỏi Google service account này thấy project nào (chỉ GET).");
            if (!string.IsNullOrEmpty(keyInfo.ProjectId) && string.IsNullOrEmpty(source.projectId))
                Ui.Button(probe, "Dùng " + keyInfo.ProjectId, () => _projectId.value = keyInfo.ProjectId, "secondary");
            _projectChoices = new DropdownField(new List<string>(), 0);
            _projectChoices.style.display = DisplayStyle.None;
            _projectChoices.RegisterValueChangedCallback(evt => _projectId.value = evt.newValue);
            project.Add(_projectChoices);
            _createProject = Ui.ToggleRow(project, "Tạo project nếu chưa có", source.createProjectIfMissing,
                "Tắt (mặc định): project id là vĩnh viễn, không xoá sạch được — không tạo ngầm.");
            _androidName = Ui.TextRow(project, "Tên app Android", source.androidDisplayName, "Trống = " + source.ResolvedAndroidName);
            _iosName = Ui.TextRow(project, "Tên app iOS", source.iosDisplayName, "Trống = " + source.ResolvedIosName);
            _appStoreId = Ui.TextRow(project, "App Store ID", source.appStoreId, "Tuỳ chọn — để Firebase link sang App Store.", Setup.Validate.AppStoreId);
            Ui.Link(Ui.Row(project, "ezg-actions"), "Firebase console", FirebaseSetupHelpers.ProjectUrl(source.projectId));

            var sha = Ui.Card(body, "SHA-1 (Google Sign-In / Play Games)", "Lấy từ keystore đang khai trong Player Settings. Mật khẩu chỉ dùng để chạy keytool, không lưu.");
            _keystorePass = Ui.TextRow(sha, "Mật khẩu keystore", PlayerSettings.Android.keystorePass, null, null, true);
            _sha = Ui.TextRow(sha, "SHA-1", string.Empty, "Trống = bỏ qua bước đăng ký SHA.");
            Ui.Button(Ui.Row(sha, "ezg-actions"), "Lấy SHA-1 từ keystore", () =>
            {
                var value = FirebaseSetupHelpers.ReadShaFromKeystore(_keystorePass.value, out var message);
                if (value != null) _sha.value = value;
                host.Toast(message, value != null ? EzgStatus.Ok : EzgStatus.Warn);
            }, "secondary");

            var config = Ui.Card(body, "File config trong project");
            ConfigRow(config, "google-services.json", FirebaseAppProvisioner.AndroidConfigExists, FirebaseAppProvisioner.LocalAndroidConfigProjectId, source.projectId);
            ConfigRow(config, "GoogleService-Info.plist", FirebaseAppProvisioner.IosConfigExists, FirebaseAppProvisioner.LocalIosConfigProjectId, source.projectId);
            Ui.InfoRow(config, "google-services.xml (build Android)", FirebaseAppProvisioner.GeneratedXmlProjectId ?? "chưa sinh",
                FirebaseAppProvisioner.GeneratedXmlProjectId == null ? EzgStatus.Warn : EzgStatus.None);
            Ui.InfoRow(config, "Bundle id hiện tại", $"{PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)} / {PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS)}");

            var actions = Ui.Card(body, "Thực thi", "Dry run chỉ đọc (GET). Chạy thật tạo app trên Firebase — package name / bundle id của app Firebase KHÔNG sửa được sau khi tạo.");
            var row = Ui.Row(actions, "ezg-actions");
            Ui.Button(row, "Lưu", () => Save(host), "secondary");
            Ui.Button(row, "Dry run", () => Run(host, true), "secondary");
            Ui.Button(row, "Tạo app + tải config", () => Run(host, false), "primary");
            Ui.Button(row, "Tạo / sửa FirebaseConfig", () => EnsureConfig(host), "secondary",
                "Tạo Assets/_Project/Resources/FirebaseConfig.asset (nếu chưa có) với storageBucketUrl theo google-services.json.");
            _report = Ui.Text(_lastReport ?? string.Empty, "ezg-log");
            _report.style.display = string.IsNullOrEmpty(_lastReport) ? DisplayStyle.None : DisplayStyle.Flex;
            actions.Add(_report);
        }

        private static void ConfigRow(VisualElement parent, string label, bool exists, string projectId, string expected)
        {
            var status = !exists ? EzgStatus.Warn
                : string.IsNullOrEmpty(expected) || projectId == expected ? EzgStatus.Ok
                : EzgStatus.Error;
            Ui.InfoRow(parent, label, exists ? "project " + projectId : "chưa có", status,
                status == EzgStatus.Error ? $"Trỏ project khác '{expected}'." : null);
        }

        private void Save(IPageHost host, bool toast = true)
        {
            FirebaseSource.KeyPath = _keyPath.value.Trim();
            var source = FirebaseSource.Load();
            source.projectId = _projectId.value.Trim();
            source.androidDisplayName = _androidName.value.Trim();
            source.iosDisplayName = _iosName.value.Trim();
            source.appStoreId = _appStoreId.value.Trim();
            source.createProjectIfMissing = _createProject.value;
            source.Save();
            if (toast) host.Toast("Đã lưu ProjectSettings/FirebaseSource.json.", EzgStatus.Ok);
        }

        private void Run(IPageHost host, bool dryRun)
        {
            Save(host, false);
            var sha = string.IsNullOrWhiteSpace(_sha.value) ? null : _sha.value.Trim();
            if (!FirebaseAppProvisioner.Run(true, sha, out var plan))
            {
                ShowReport(host, plan, EzgStatus.Error);
                return;
            }

            if (dryRun)
            {
                ShowReport(host, plan, EzgStatus.Ok);
                return;
            }

            if (!EditorUtility.DisplayDialog("EzgKit — Firebase", plan + "\n\nLưu ý: package name / bundle id của app Firebase KHÔNG sửa được sau khi tạo.",
                    "Tạo", "Huỷ"))
                return;

            var ok = FirebaseAppProvisioner.Run(false, sha, out var report);
            if (ok) EzgKitState.SetMarker(Id, EzgKitState.MARKER_DONE);
            _lastReport = report;
            host.RefreshAll();
            host.Rebuild();
            host.Toast(ok ? "Đã tạo / cập nhật app Firebase và tải config." : "Firebase dừng giữa chừng — xem log bên dưới.", ok ? EzgStatus.Ok : EzgStatus.Error);
        }

        private void EnsureConfig(IPageHost host)
        {
            var bucket = FirebaseSetupHelpers.ExpectedBucket();
            var rows = new List<ChangeRow>();
            if (!FirebaseSetupHelpers.EnsureConfig(bucket, true, rows, out var error))
            {
                host.Toast(error, EzgStatus.Error);
                return;
            }

            var preview = new ApplyResult { DryRun = true };
            preview.Rows.AddRange(rows);
            if (preview.ChangedCount == 0)
            {
                host.Toast("FirebaseConfig đã đúng.", EzgStatus.Ok);
                return;
            }

            if (!EditorUtility.DisplayDialog("EzgKit — FirebaseConfig", $"Ghi {preview.ChangedCount} ô vào FirebaseConfig.asset (bucket {bucket ?? "(chưa có json — để trống)"}).", "Ghi", "Huỷ"))
                return;

            var result = new ApplyResult();
            FirebaseSetupHelpers.EnsureConfig(bucket, false, result.Rows, out _);
            host.RefreshAll();
            host.Rebuild();
            host.ShowResult(result);
        }

        private void Probe(IPageHost host)
        {
            FirebaseSource.KeyPath = _keyPath.value.Trim();
            if (!FirebaseAppProvisioner.TryListProjects(out var projects, out var error))
            {
                host.Toast(error, EzgStatus.Error);
                return;
            }

            if (projects.Count == 1)
            {
                _projectId.value = projects[0];
                host.Toast($"Service account chỉ thấy 1 project → đã chọn '{projects[0]}'.", EzgStatus.Ok);
                return;
            }

            _projectChoices.choices = projects;
            _projectChoices.style.display = DisplayStyle.Flex;
            host.Toast($"Dò được {projects.Count} project — chọn trong danh sách.", EzgStatus.None);
        }

        private void ShowReport(IPageHost host, string text, EzgStatus level)
        {
            _lastReport = text;
            if (_report != null)
            {
                _report.text = text;
                _report.style.display = DisplayStyle.Flex;
            }

            host.Toast(level == EzgStatus.Error ? "Chưa chạy được — xem log." : "Đã chạy dry run — xem kế hoạch bên dưới.", level);
        }

        #endregion
    }
}
#endif
