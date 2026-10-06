#if UNITY_EDITOR
using System.Collections.Generic;
using Ezg.Editor.Shared.Publisher;
using UnityEditor;
using UnityEngine.UIElements;

namespace Ezg.Editor.Shared.EzgKit.Pages
{
    /// <summary>
    ///     Nhà phát hành (Nâng cao): mỗi publisher (Ezg trong nhà, Neptune CPI test, SayGame…) yêu cầu một bộ SDK + ID
    ///     riêng. Trang cho thấy SDK nào cần / đang có / thừa, ID nào phải điền ở đâu, kế hoạch "Chuyển sang" (cài thiếu,
    ///     gỡ thừa sau khi cache, đổi define) và thi hành sau khi xác nhận.
    /// </summary>
    internal sealed class PublisherPage : SetupPage
    {
        [System.NonSerialized] private int _profileIndex = -1;
        private readonly HashSet<SdkKind> _excluded = new();
        private readonly Dictionary<SdkKind, string> _manual = new();
        private readonly Dictionary<string, TextField> _idInputs = new();
        private readonly Dictionary<string, (SdkKind Kind, string Key, string Current)> _idSlots = new();
        private bool _busy;

        internal override string Id => PageIds.PUBLISHER;

        internal override string Title => "Nhà phát hành";

        internal override string Description =>
            "Bộ SDK + ID theo từng publisher: xem thiếu / thừa, điền ID, chuyển bộ SDK (cache SDK gỡ đi, cài SDK thiếu, đổi define).";

        internal override string Group => GROUP_ADVANCED;

        #region Core

        private IPublisherProfile Profile
        {
            get
            {
                if (_profileIndex < 0)
                {
                    var active = PublisherRegistry.Find(PublisherState.Load().activePublisher);
                    _profileIndex = active == null ? 0 : System.Array.IndexOf(PublisherRegistry.Profiles, active);
                }

                return PublisherRegistry.Profiles[UnityEngine.Mathf.Clamp(_profileIndex, 0, PublisherRegistry.Profiles.Length - 1)];
            }
        }

        internal override PageReport Detect()
        {
            var report = new PageReport();
            var state = PublisherState.Load();
            var active = PublisherRegistry.Find(state.activePublisher);
            var profile = active ?? PublisherRegistry.Profiles[0];
            if (profile.RequiredSdks.Length == 0) return report.Resolve($"{profile.Title}: chưa có tài liệu yêu cầu SDK.");

            var reports = SdkCatalog.Collect(profile);
            var missing = 0;
            var idIssues = 0;
            foreach (var sdk in reports)
            {
                if (!sdk.Required) continue;
                if (!sdk.Installed)
                {
                    missing++;
                    continue;
                }

                foreach (var slot in sdk.Slots)
                    if (slot.Status is EzgStatus.Warn or EzgStatus.Error)
                        idIssues++;
            }

            if (missing > 0) report.Add(EzgStatus.Warn, $"{missing} SDK bắt buộc của {profile.Title} chưa gắn.", "Xem kế hoạch rồi bấm Chuyển sang.");
            else report.Ok();
            if (idIssues > 0) report.Add(EzgStatus.Warn, $"{idIssues} ID còn trống / lệch.", "Điền ở card ID rồi bấm Điền ID.");
            else report.Ok();
            return report.Resolve(active == null ? $"Chưa chọn — đang xem {profile.Title} (mặc định)." : $"Đang dùng {active.Title} (từ {state.appliedAtUtc}).");
        }

        internal override JsonObject GetValues(bool maskSecrets)
        {
            var state = PublisherState.Load();
            var profiles = new List<object>();
            foreach (var profile in PublisherRegistry.Profiles) profiles.Add(new JsonObject().Set("id", profile.Id).Set("title", profile.Title));
            return new JsonObject()
                .Set("activePublisher", state.activePublisher ?? string.Empty)
                .Set("appliedAtUtc", state.appliedAtUtc ?? string.Empty)
                .Set("profiles", profiles);
        }

        #endregion

        #region UI

        internal override void Build(VisualElement body, IPageHost host)
        {
            var profile = Profile;
            var reports = SdkCatalog.Collect(profile);

            var pick = Ui.Card(body, "Publisher", profile.Intro);
            var titles = new List<string>();
            foreach (var p in PublisherRegistry.Profiles) titles.Add(p.DisplayName);
            Ui.DropdownRow(pick, "Xem bộ SDK của", titles, _profileIndex, profile.Subtitle, index =>
            {
                _profileIndex = index;
                _excluded.Clear();
                _manual.Clear();
                host.Rebuild();
            });
            var state = PublisherState.Load();
            Ui.InfoRow(pick, "Đang áp dụng", PublisherRegistry.Find(state.activePublisher)?.Title ?? "chưa chuyển (bộ mặc định của template)");
            if (!string.IsNullOrEmpty(profile.GuideUrl)) Ui.Link(Ui.Row(pick, "ezg-actions"), "Tài liệu " + profile.Title, profile.GuideUrl);

            if (profile.RequiredSdks.Length == 0)
            {
                Ui.Message(body, $"{profile.Title} chưa có tài liệu yêu cầu SDK — kit không cài / gỡ gì cho publisher này.", EzgStatus.None);
                return;
            }

            BuildSdkTable(body, reports, host);
            BuildIds(body, reports, host);
            BuildPlan(body, profile, reports, host);
        }

        private void BuildSdkTable(VisualElement body, List<SdkReport> reports, IPageHost host)
        {
            var card = Ui.Card(body, "SDK", "Bắt buộc / đang có / thừa. SDK thừa được export vào cache trước khi gỡ; SDK game code còn tham chiếu thì bị chặn gỡ.");
            var head = Ui.Row(card, "ezg-table-head");
            head.Add(Ui.Text(string.Empty, "ezg-col-xs"));
            head.Add(Ui.Text("SDK", "ezg-col-m"));
            head.Add(Ui.Text("Yêu cầu", "ezg-col-s"));
            head.Add(Ui.Text("Đang có", "ezg-col-l"));
            head.Add(Ui.Text("Tuỳ chọn", "ezg-col-grow"));

            foreach (var report in reports)
            {
                var line = Ui.Row(card, "ezg-table-row");
                var dot = new VisualElement();
                dot.AddToClassList("ezg-col-xs");
                dot.Add(Ui.Dot(report.Required ? report.Status : EzgStatus.None));
                line.Add(dot);
                var name = new VisualElement();
                name.AddToClassList("ezg-col-m");
                name.Add(Ui.Text(report.Name, "ezg-strong"));
                if (!string.IsNullOrEmpty(report.Why)) name.Add(Ui.Text(report.Why, "ezg-hint"));
                line.Add(name);
                line.Add(Ui.Text(report.IsPlatform ? "nền tảng" : report.Required ? "bắt buộc" : "thừa", "ezg-col-s"));
                line.Add(Ui.Text(report.Installed ? report.Location ?? "có" : "chưa có", "ezg-col-l"));

                var options = new VisualElement();
                options.AddToClassList("ezg-col-grow");
                if (!report.IsPlatform)
                {
                    var kind = report.Kind;
                    var include = new Toggle("Đưa vào kế hoạch") { value = !_excluded.Contains(kind) };
                    include.AddToClassList("ezg-inline-toggle");
                    include.RegisterValueChangedCallback(evt =>
                    {
                        if (evt.newValue) _excluded.Remove(kind);
                        else _excluded.Add(kind);
                        host.Rebuild();
                    });
                    options.Add(include);
                    if (report.Required && !report.Installed)
                    {
                        _manual.TryGetValue(kind, out var manual);
                        var file = new Button(() =>
                        {
                            var path = EditorUtility.OpenFilePanel("Chọn .unitypackage cho " + report.Name, string.Empty, "unitypackage");
                            if (string.IsNullOrEmpty(path)) _manual.Remove(kind);
                            else _manual[kind] = path;
                            host.Rebuild();
                        }) { text = string.IsNullOrEmpty(manual) ? "Chọn .unitypackage…" : System.IO.Path.GetFileName(manual) };
                        file.AddToClassList("ezg-btn-mini");
                        options.Add(file);
                        if (!string.IsNullOrEmpty(report.InstallHint)) options.Add(Ui.Text(report.InstallHint, "ezg-hint"));
                    }
                }

                line.Add(options);
            }
        }

        private void BuildIds(VisualElement body, List<SdkReport> reports, IPageHost host)
        {
            _idInputs.Clear();
            _idSlots.Clear();
            var card = Ui.Card(body, "ID", "ID publisher cấp điền sẵn; ID game tự tạo thì gõ vào. Chỉ ghi được ID nằm trong Unity.");
            var any = false;
            foreach (var report in reports)
            {
                if (!report.Required || report.IsPlatform) continue;
                foreach (var slot in report.Slots)
                {
                    any = true;
                    var id = report.Kind + ":" + slot.Key;
                    var writable = PublisherIdWriter.CanWrite(report.Kind, slot.Key);
                    var hint = (slot.Where == null ? string.Empty : "Ghi vào: " + slot.Where)
                               + (string.IsNullOrEmpty(slot.HowToGet) ? string.Empty : "  ·  Lấy ở: " + slot.HowToGet)
                               + (string.IsNullOrEmpty(slot.Note) ? string.Empty : "  ·  " + slot.Note);
                    if (!writable)
                    {
                        Ui.InfoRow(card, $"{report.Name} · {slot.Label}", slot.Wanted ?? slot.Current ?? "—", slot.Status,
                            "Ngoài Unity — làm trên console của SDK. " + hint);
                        continue;
                    }

                    var input = Ui.TextRow(card, $"{report.Name} · {slot.Label}", slot.Wanted ?? slot.Current ?? string.Empty, hint);
                    if (!report.Installed) input.SetEnabled(false);
                    _idInputs[id] = input;
                    _idSlots[id] = (report.Kind, slot.Key, slot.Current ?? string.Empty);
                }

                foreach (var ev in report.Events)
                    Ui.InfoRow(card, $"{report.Name} · event {ev.Name}", ev.Value, ev.Status, ev.Note + (string.IsNullOrEmpty(ev.Fix) ? string.Empty : " → " + ev.Fix));
            }

            if (!any)
            {
                Ui.Hint(card, "Publisher này không yêu cầu ID nào.");
                return;
            }

            Ui.Button(Ui.Row(card, "ezg-actions"), "Điền ID", () => FillIds(host), "primary");
        }

        private void FillIds(IPageHost host)
        {
            var entries = new List<PublisherIdWriter.Entry>();
            foreach (var pair in _idInputs)
            {
                var slot = _idSlots[pair.Key];
                var value = pair.Value.value.Trim();
                if (value != slot.Current) entries.Add(new PublisherIdWriter.Entry(slot.Kind, slot.Key, value));
            }

            if (entries.Count == 0)
            {
                host.Toast("Không có ô nào khác giá trị trong project.", EzgStatus.None);
                return;
            }

            if (!PublisherIdWriter.Write(entries, true, out var preview, out var error))
            {
                host.Toast("Không điền được: " + error, EzgStatus.Error);
                return;
            }

            var lines = new List<string>();
            foreach (var line in preview)
                if (!line.Contains(PublisherIdWriter.UNCHANGED))
                    lines.Add("• " + line);
            if (!EditorUtility.DisplayDialog("EzgKit — Điền ID " + Profile.Title, $"Ghi {entries.Count} ID:\n\n{string.Join("\n", lines)}", "Điền", "Huỷ")) return;

            var ok = PublisherIdWriter.Write(entries, false, out var changes, out error);
            host.RefreshAll();
            host.Rebuild();
            host.Toast(ok ? "Đã điền: " + string.Join(" · ", changes) : "Điền dừng giữa chừng: " + error, ok ? EzgStatus.Ok : EzgStatus.Error);
        }

        private void BuildPlan(VisualElement body, IPublisherProfile profile, List<SdkReport> reports, IPageHost host)
        {
            var plan = SdkSwitcher.BuildPlan(profile, reports, _manual, _excluded);
            var card = Ui.Card(body, $"Kế hoạch \"Chuyển sang {profile.Title}\"",
                plan.HasWork
                    ? $"cài {plan.Install.Count} · gỡ {plan.Remove.Count} · chặn {plan.Blocked.Count} · bỏ qua {plan.Skipped.Count} · ID {plan.Ids.Count} · define {plan.Defines.Count}"
                    : "Không có gì phải làm — project đã dùng bộ SDK này.");
            card.Add(Ui.Text(plan.Summary(), "ezg-log"));
            if (!plan.HasWork) return;

            var jobs = plan.DownloadJobs();
            Ui.Hint(card, "SDK bị gỡ được export vào cache trước: " + SdkSwitcher.CacheDir
                          + (jobs.Count > 0 ? $" · {jobs.Count} SDK chưa có trong cache sẽ được TẢI VỀ trước (Firebase ~1 GB)." : string.Empty));
            Ui.Button(Ui.Row(card, "ezg-actions"), _busy ? "Đang chuyển…" : $"Chuyển sang {profile.Title}", () => Switch(host, profile, reports, plan), "danger",
                "Cài / gỡ SDK, đổi define, ghi ID — Unity sẽ reimport / resolve package / recompile.");
        }

        private void Switch(IPageHost host, IPublisherProfile profile, List<SdkReport> reports, SwitchPlan plan)
        {
            if (_busy) return;
            var jobs = plan.DownloadJobs();
            if (!EditorUtility.DisplayDialog("EzgKit — Chuyển sang " + profile.Title,
                    plan.Summary() + (jobs.Count > 0 ? $"\n{jobs.Count} SDK sẽ được tải về trước.\n" : "\n")
                                   + "SDK bị gỡ được export vào cache:\n" + SdkSwitcher.CacheDir
                                   + "\n\nUnity sẽ reimport / resolve package / recompile. Tiếp tục?", "Chuyển", "Huỷ"))
                return;

            void Execute()
            {
                var ok = SdkSwitcher.Execute(profile, reports, plan, out var log, out var error);
                _busy = false;
                if (ok) EzgKitState.SetMarker(Id, EzgKitState.MARKER_DONE);
                host.RefreshAll();
                host.Rebuild();
                host.Toast(ok ? "Đã chuyển: " + string.Join(" · ", log) : "Chuyển dừng giữa chừng: " + error + (log.Count > 0 ? " | đã làm: " + string.Join(" · ", log) : string.Empty),
                    ok ? plan.Blocked.Count > 0 ? EzgStatus.Warn : EzgStatus.Ok : EzgStatus.Error);
            }

            _busy = true;
            if (jobs.Count == 0)
            {
                Execute();
                return;
            }

            host.Toast("Đang tải SDK…", EzgStatus.None);
            SdkDownloader.Start(jobs, (downloaded, message) =>
            {
                if (!downloaded)
                {
                    _busy = false;
                    host.Toast("Tải SDK lỗi — chưa chuyển gì: " + message, EzgStatus.Error);
                    return;
                }

                Execute();
            });
        }

        #endregion
    }
}
#endif
