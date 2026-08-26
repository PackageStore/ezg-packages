#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using Ezg.Editor.Shared.EzgKit;
using Ezg.Editor.Shared.Readiness;
using UnityEditor;
using UnityEditor.Build;
using UnityEditorInternal;
using UnityEngine;

namespace Ezg.Editor.Shared.Publisher
{
    /// <summary>
    ///     Tab của MỘT bộ SDK (Ezg trong nhà / Neptune / SayGame …). Bảng SDK ba nhóm — <b>cần gắn thêm</b>
    ///     · <b>đã gắn</b> (từng ID: hiện tại → phải là, thay ở đâu) · <b>thừa</b> — và một nút
    ///     <b>Chuyển sang {X}</b>: cài SDK thiếu, gỡ SDK thừa, ghi ID, gắn define, trong một lần bấm sau
    ///     khi xem kế hoạch (<see cref="SdkSwitcher.BuildPlan" />).
    ///     <para>
    ///         Một lớp page cho mọi <see cref="IPublisherProfile" />: profile mang yêu cầu, <see cref="SdkCatalog" />
    ///         dò project, <see cref="SdkSwitcher" /> lập/thi hành kế hoạch, page vẽ.
    ///     </para>
    ///     <para>
    ///         Cùng kỷ luật snapshot với các tab khác: <see cref="Reload" /> chụp mọi thứ vào field (kể cả
    ///         <c>RequiredSdks</c> — profile Ezg đọc file); kết quả chuyển đổ vào ở ĐẦU <see cref="Draw" /> qua
    ///         <see cref="_reloadPending" />; nút ghi chạy qua <see cref="ReadinessActions.Defer" />. Ô kéo
    ///         .unitypackage đổi giá trị → chỉ dựng lại kế hoạch (<see cref="_replanPending" />), không quét lại SDK.
    ///     </para>
    ///     <para>
    ///         Không tham gia "chạy hết" (<see cref="RunAllLabel" /> = null): đổi bộ SDK là quyết định riêng.
    ///     </para>
    /// </summary>
    internal class PublisherPage : IEzgKitPage
    {
        #region Fields

        private readonly IPublisherProfile _profile;
        private PublisherState _state;
        private List<SdkReport> _reports = new();
        private SwitchPlan _plan = new();

        private readonly List<SdkReport> _missing = new();
        private readonly List<SdkReport> _installed = new();
        private readonly List<SdkReport> _platform = new();
        private readonly List<SdkReport> _extra = new();

        /// <summary>File .unitypackage người dùng kéo vào cho SDK chưa có nguồn cài — theo phiên.</summary>
        private readonly Dictionary<SdkKind, string> _manualPackages = new();

        /// <summary>SDK người dùng BỎ TICK (không import / không gỡ). Mặc định làm hết — chỉ nhớ ngoại lệ.</summary>
        private readonly HashSet<SdkKind> _excluded = new();

        /// <summary>Số ID sai/thiếu trên các SDK đã gắn — con số dev phải xử lý.</summary>
        private int _idIssues;

        /// <summary>Chụp trong Reload — getter Status/Headline không được gọi RequiredSdks (profile Ezg đọc file).</summary>
        private int _requiredCount;

        private bool _loaded;

        /// <summary>Chuỗi dựng sẵn trong Reload/Replan — Draw không nối chuỗi, không đụng đĩa.</summary>
        private string _headline = "Chưa đọc trạng thái.";
        private string _planTitle = "";
        private string _planBody = "";
        private readonly Dictionary<SdkKind, List<string>> _cachedPackages = new();

        private Vector2 _scroll;
        private string _message;
        private EzgStatus _messageStatus = EzgStatus.None;
        private bool _reloadPending;
        private bool _replanPending;

        #endregion

        internal PublisherPage(IPublisherProfile profile)
        {
            _profile = profile;
        }

        #region Page

        public string Title => _profile.Title;
        public string Subtitle => _profile.Subtitle;
        public string RunAllLabel => null;

        /// <summary>Đọc field đã dựng — getter này bị cửa sổ gọi mỗi OnGUI của mọi tab.</summary>
        public string Headline => _headline;

        private string BuildHeadline()
        {
            if (_requiredCount == 0) return "Chưa có tài liệu — chưa biết publisher đòi SDK gì.";
            if (_missing.Count == 0 && _idIssues == 0 && _extra.Count == 0) return $"Đúng bộ {_installed.Count} SDK, ID khớp.";
            if (_missing.Count == 0 && _idIssues == 0) return $"Đủ {_installed.Count} SDK, ID khớp · {_extra.Count} SDK thừa.";
            return $"{_missing.Count} SDK cần gắn thêm · {_idIssues} ID phải thay · {_extra.Count} SDK thừa.";
        }

        public EzgStatus Status
        {
            get
            {
                if (!_loaded || _requiredCount == 0) return EzgStatus.None;
                var worst = EzgStatus.Ok;
                foreach (var report in _reports)
                    if (report.Required && report.Status > worst) worst = report.Status;
                return worst;
            }
        }

        public void Reload()
        {
            _state = PublisherState.Load();
            _requiredCount = _profile.RequiredSdks.Length;
            _reports = SdkCatalog.Collect(_profile);

            _missing.Clear();
            _installed.Clear();
            _platform.Clear();
            _extra.Clear();
            _idIssues = 0;
            foreach (var report in _reports)
            {
                if (!report.Required) _extra.Add(report);
                else if (!report.Installed) _missing.Add(report);
                else if (report.IsPlatform) _platform.Add(report);
                else
                {
                    _installed.Add(report);
                    foreach (var slot in report.Slots)
                        if (slot.Status is EzgStatus.Warn or EzgStatus.Error) _idIssues++;
                    foreach (var ev in report.Events)
                        if (ev.Status is EzgStatus.Warn or EzgStatus.Error) _idIssues++;
                }
            }

            _cachedPackages.Clear();
            foreach (var report in _missing) _cachedPackages[report.Kind] = SdkSwitcher.CachedPackages(report.Kind);

            _headline = BuildHeadline();
            Replan();
            _loaded = true;
        }

        private void Replan()
        {
            _plan = SdkSwitcher.BuildPlan(_profile, _reports, _manualPackages, _excluded);
            _planTitle = _plan.HasWork
                ? $"Kế hoạch \"Chuyển sang {_profile.Title}\": cài {_plan.Install.Count} · gỡ {_plan.Remove.Count} · chặn {_plan.Blocked.Count} · bỏ qua {_plan.Skipped.Count} · ID {_plan.Ids.Count} · define {_plan.Defines.Count}"
                : $"Kế hoạch \"Chuyển sang {_profile.Title}\": không có gì phải làm";
            _planBody = PlanBody();
        }

        public void Draw()
        {
            if (_reloadPending)
            {
                _reloadPending = false;
                Reload();
            }
            else if (_replanPending)
            {
                _replanPending = false;
                Replan();
            }

            if (!_loaded) Reload();

            DrawTop();

            using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scroll.scrollPosition;
                if (_requiredCount == 0) DrawNoGuide();
                else
                {
                    DrawGroup("1. Cần gắn thêm", "Publisher đòi mà project chưa có — tick \"Import\" để \"Chuyển sang\" tự tải (nếu cache chưa có) rồi cài.", _missing,
                        "Không thiếu SDK nào.");
                    DrawGroup("2. Đã gắn — ID", "Có sẵn trong project; từng ID bên dưới phải đúng giá trị, đúng chỗ.", _installed,
                        "Chưa gắn SDK nào publisher đòi.");
                    DrawGroup("3. SDK nền tảng — luôn giữ",
                        "Của bản build iOS/Android (Game Center, Play Asset Delivery…), không thuộc publisher nào — không bao giờ gỡ.",
                        _platform, "Không có SDK nền tảng nào trong project.");
                    DrawGroup($"4. Thừa với {_profile.Title}",
                        "Project có, publisher không đòi — tick \"Gỡ\" để \"Chuyển sang\" export vào cache rồi xoá. SDK mà code game còn gọi thẳng bị khoá.",
                        _extra, "Không có SDK nào thừa.");
                }
            }
        }

        public bool RunAll() => true;

        #endregion

        #region Draw

        private void DrawTop()
        {
            var banner = Headline;
            var active = _state.activePublisher;
            if (_requiredCount > 0)
            {
                if (active == _profile.Id) banner += $"  ·  Bộ SDK đang áp: {_profile.Title} ({_state.appliedAtUtc}).";
                else if (!string.IsNullOrEmpty(active)) banner += $"  ·  Bộ SDK đang áp: \"{active}\".";
                if (_plan.Blocked.Count > 0) banner += $"  ·  {_plan.Blocked.Count} mục bị chặn — xem kế hoạch.";
            }

            EzgKitStyles.Banner(banner, Status);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_requiredCount == 0 || !_plan.HasWork || SdkDownloader.IsBusy))
                {
                    if (EzgKitStyles.PrimaryButton($"Chuyển sang {_profile.Title}", GUILayout.Width(240f)))
                        ReadinessActions.Defer(Switch);
                }

                if (!string.IsNullOrEmpty(_profile.GuideUrl)
                    && EzgKitStyles.SecondaryButton("↗ Mở guide của publisher", GUILayout.Width(190f)))
                    Application.OpenURL(_profile.GuideUrl);

                GUILayout.FlexibleSpace();
            }

            if (SdkDownloader.IsBusy)
                EditorGUILayout.LabelField("Đang tải SDK: " + SdkDownloader.Status, EditorStyles.wordWrappedMiniLabel);
            else if (!string.IsNullOrEmpty(_message))
            {
                var previous = GUI.contentColor;
                GUI.contentColor = EzgKitStyles.ColorOf(_messageStatus);
                EditorGUILayout.LabelField(_message, EditorStyles.wordWrappedMiniLabel);
                GUI.contentColor = previous;
            }

            EzgKitStyles.CollapsibleHelp("publisher-" + _profile.Id, $"Về {_profile.DisplayName}", _profile.Intro ?? "",
                _requiredCount == 0);

            if (_requiredCount == 0) return;

            EzgKitStyles.CollapsibleHelp("publisher-plan-" + _profile.Id, _planTitle, _planBody, _plan.Blocked.Count > 0);
        }

        private string PlanBody()
        {
            var sb = new StringBuilder();
            Append(sb, "Cài thêm", _plan.Install);
            Append(sb, "Gỡ (export vào cache trước)", _plan.Remove);
            Append(sb, "Chặn — không làm được, lý do", _plan.Blocked);
            Append(sb, "Bỏ qua theo tick của bạn", _plan.Skipped);
            if (_plan.Ids.Count > 0) sb.Append("Ghi ID:\n  • ").Append(string.Join("\n  • ", _plan.Ids)).Append('\n');
            else if (_plan.IdError != null) sb.Append("Ghi ID: ").Append(_plan.IdError).Append('\n');
            if (_plan.Defines.Count > 0) sb.Append("Scripting define (Android + iOS): ").Append(string.Join(", ", _plan.Defines)).Append('\n');
            sb.Append("\nCache trên máy: ").Append(SdkSwitcher.CacheDir);
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string title, List<SwitchPlan.Step> steps)
        {
            if (steps.Count == 0) return;
            sb.Append(title).Append(":\n");
            foreach (var step in steps) sb.Append("  • ").Append(SdkCatalog.NameOf(step.Kind)).Append(" — ").Append(step.Text).Append('\n');
        }

        private void DrawNoGuide()
        {
            EzgKitStyles.SectionHeader("SDK đang có trong project",
                "Chưa biết publisher này đòi gì — liệt kê để khi có guide đối chiếu nhanh. Không cài/gỡ gì.");
            foreach (var report in _extra) DrawSdkCard(report);
        }

        private void DrawGroup(string title, string subtitle, List<SdkReport> reports, string emptyText)
        {
            EzgKitStyles.SectionHeader(title, subtitle);
            if (reports.Count == 0)
            {
                using (new EzgKitStyles.CardScope())
                {
                    EditorGUILayout.LabelField(emptyText, EzgKitStyles.Hint);
                }

                return;
            }

            foreach (var report in reports) DrawSdkCard(report);
        }

        private void DrawSdkCard(SdkReport report)
        {
            using (new EzgKitStyles.CardScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawChoice(report);
                    EzgKitStyles.StatusIcon(report.Status);
                    GUILayout.Label(report.Name, EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                    if (!string.IsNullOrEmpty(report.Location))
                        GUILayout.Label(report.Location, EzgKitStyles.MutedLabel, GUILayout.ExpandWidth(false));
                    GUILayout.FlexibleSpace();
                    EzgKitStyles.Pill(report.Status, PillText(report));
                }

                if (!string.IsNullOrEmpty(report.Why)) Indented(report.Why, EzgKitStyles.Hint);

                var action = _requiredCount == 0 ? null : _plan.ActionOf(report.Kind);
                if (action != null) Indented(action, action.StartsWith("Chặn") ? EditorStyles.wordWrappedLabel : EzgKitStyles.Hint);

                if (report.Required && !report.Installed)
                {
                    DrawInstallSource(report);
                    if (report.Slots.Count > 0)
                    {
                        Indented("ID sẽ phải điền sau khi gắn:", EditorStyles.miniBoldLabel);
                        foreach (var slot in report.Slots) DrawSlotBrief(slot);
                    }
                }
                else if (report.Required)
                {
                    foreach (var slot in report.Slots) DrawSlot(slot);
                    foreach (var ev in report.Events) DrawEvent(ev);
                }

                DrawLinks(report.Links);
            }
        }

        /// <summary>
        ///     Ô tick "Import" (SDK thiếu) / "Gỡ" (SDK thừa) ở đầu card. Mặc định tick. SDK bị chặn (code còn
        ///     gọi thẳng / không có nguồn cài) thì ô khoá + bỏ tick — lý do nằm ở dòng "Chặn:" ngay dưới.
        ///     Đổi tick chỉ dựng lại kế hoạch, không quét lại project.
        /// </summary>
        private void DrawChoice(SdkReport report)
        {
            if (_requiredCount == 0) return;
            var isMissing = report.Required && !report.Installed;
            var isExtra = !report.Required && report.Installed;
            if (!isMissing && !isExtra) return;

            var blocked = _plan.IsBlocked(report.Kind);
            var label = isMissing ? "Import" : "Gỡ";
            using (new EditorGUI.DisabledScope(blocked))
            {
                var on = !blocked && !_excluded.Contains(report.Kind);
                var next = GUILayout.Toggle(on, label, GUILayout.Width(64f));
                if (next == on) return;
                if (next) _excluded.Remove(report.Kind);
                else _excluded.Add(report.Kind);
                _replanPending = true;
            }
        }

        /// <summary>
        ///     SDK trong Assets/ chưa có nguồn cài (không có cache): ô kéo .unitypackage. UPM thì không cần —
        ///     spec đã nằm trong catalog/cache.
        /// </summary>
        private void DrawInstallSource(SdkReport report)
        {
            var spec = SdkCatalog.SpecOf(report.Kind);
            if (!spec.HasAssets) return;

            _manualPackages.TryGetValue(report.Kind, out var current);
            if (!_cachedPackages.TryGetValue(report.Kind, out var cached)) cached = new List<string>();
            var auto = SdkDownloader.CanDownload(report.Kind);
            var howTo = cached.Count > 0
                ? $"Đã có {cached.Count} file trong cache ({System.IO.Path.GetFileName(cached[0])}) — chỉ kéo file khác nếu muốn version khác."
                : auto
                    ? "Không cần làm gì: \"Chuyển sang\" tự tải bản mới nhất từ trang release rồi import. Kéo file vào đây chỉ khi muốn version cụ thể."
                    : "SDK này không có nguồn tải tự động — tải .unitypackage từ trang release rồi kéo vào đây.";
            var links = spec.ReleasePageUrl == null
                ? Array.Empty<(string, string)>()
                : new[] { ("Trang release", spec.ReleasePageUrl) };

            var next = SetupGui.ManualFilePathField("File .unitypackage", current ?? "", howTo, "Chọn .unitypackage",
                "unitypackage", cached.Count > 0 || auto ? SetupGui.FieldNeed.Optional : SetupGui.FieldNeed.Required, links);
            if ((next ?? "") == (current ?? "")) return;

            if (string.IsNullOrEmpty(next)) _manualPackages.Remove(report.Kind);
            else _manualPackages[report.Kind] = next;
            _replanPending = true;
        }

        /// <summary>Một ID của SDK đã gắn: nhãn — giá trị hiện tại — giá trị phải có — chỗ thay — nút.</summary>
        private static void DrawSlot(SlotReport slot)
        {
            EzgKitStyles.Divider(2f);
            using (new EditorGUILayout.HorizontalScope())
            {
                EzgKitStyles.StatusIcon(slot.Status);
                EditorGUILayout.LabelField(slot.Label, EditorStyles.boldLabel, GUILayout.Width(EzgKitStyles.LABEL_WIDTH));
                var current = slot.Current;
                if (slot.Where == null && slot.Wanted != null) current = slot.Wanted; // ID ngoài Unity: chỉ có giá trị cấp sẵn
                if (string.IsNullOrEmpty(current))
                    EditorGUILayout.LabelField("— chưa có", EzgKitStyles.EmptyValueStyle);
                else
                    EditorGUILayout.SelectableLabel(current, EzgKitStyles.ValueStyle, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }

            if (slot.Wanted != null && slot.Where != null && slot.Current != slot.Wanted)
                Indented($"→ phải là  {slot.Wanted}  (publisher cấp — \"Chuyển sang\" ghi giúp nếu nằm trong GameConstant)", EditorStyles.wordWrappedLabel);
            else if (slot.Wanted == null && string.IsNullOrEmpty(slot.Current))
                Indented("→ game tự tạo trên console, rồi điền vào chỗ dưới.", EditorStyles.wordWrappedLabel);

            if (!string.IsNullOrEmpty(slot.Where)) Indented("Thay ở: " + slot.Where, EzgKitStyles.Hint);
            else Indented("Ngoài Unity — làm trên console của SDK.", EzgKitStyles.Hint);
            if (!string.IsNullOrEmpty(slot.Note)) Indented(slot.Note, EzgKitStyles.Hint);
            if (!string.IsNullOrEmpty(slot.HowToGet)) Indented("Lấy ở: " + slot.HowToGet, EzgKitStyles.Hint);
            DrawActions(slot.Actions);
        }

        /// <summary>ID của SDK CHƯA gắn: chỉ nhãn + giá trị cấp sẵn (nếu có) + chỗ sẽ điền.</summary>
        private static void DrawSlotBrief(SlotReport slot)
        {
            var value = slot.Wanted != null ? slot.Wanted + "  (publisher cấp)" : "game tự tạo";
            Indented($"• {slot.Label}: {value}" + (slot.Where != null ? $"  →  {slot.Where}" : ""), EditorStyles.wordWrappedLabel);
        }

        private static void DrawEvent(EventReport ev)
        {
            EzgKitStyles.Divider(2f);
            using (new EditorGUILayout.HorizontalScope())
            {
                EzgKitStyles.StatusIcon(ev.Status);
                EditorGUILayout.LabelField("Event " + ev.Name, EditorStyles.boldLabel, GUILayout.Width(EzgKitStyles.LABEL_WIDTH));
                if (string.IsNullOrEmpty(ev.Value))
                    EditorGUILayout.LabelField("— chưa có", EzgKitStyles.EmptyValueStyle);
                else
                    EditorGUILayout.SelectableLabel(ev.Value, EzgKitStyles.ValueStyle, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }

            if (!string.IsNullOrEmpty(ev.Note)) Indented(ev.Note, EzgKitStyles.Hint);
            if (!string.IsNullOrEmpty(ev.Fix)) Indented("→ " + ev.Fix, EditorStyles.wordWrappedLabel);
            DrawActions(ev.Actions);
        }

        private static string PillText(SdkReport report)
        {
            if (!report.Required) return "Thừa";
            if (!report.Installed) return "Cần gắn thêm";
            if (report.IsPlatform) return "Nền tảng · giữ";
            return report.Status switch
            {
                EzgStatus.Ok => "Đã gắn · ID khớp",
                EzgStatus.Warn => "Đã gắn · thiếu ID",
                EzgStatus.Error => "Đã gắn · ID sai",
                _ => "Đã gắn",
            };
        }

        private static void DrawActions((string Label, Action Run)[] actions)
        {
            if (actions == null || actions.Length == 0) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EzgKitStyles.ICON_WIDTH + 4f);
                foreach (var action in actions)
                {
                    if (action.Run == null) continue;
                    if (GUILayout.Button("▸ " + action.Label, EditorStyles.miniButton, GUILayout.MaxWidth(220f)))
                        ReadinessActions.Defer(action.Run);
                }

                GUILayout.FlexibleSpace();
            }
        }

        private static void DrawLinks((string Label, string Url)[] links)
        {
            if (links == null || links.Length == 0) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EzgKitStyles.ICON_WIDTH + 4f);
                foreach (var link in links)
                {
                    if (string.IsNullOrEmpty(link.Url)) continue;
                    if (GUILayout.Button("↗ " + link.Label, EditorStyles.miniButton, GUILayout.MaxWidth(220f)))
                        Application.OpenURL(link.Url);
                }

                GUILayout.FlexibleSpace();
            }
        }

        private static void Indented(string text, GUIStyle style)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EzgKitStyles.ICON_WIDTH + 4f);
                EditorGUILayout.LabelField(text, style);
            }
        }

        #endregion

        #region Actions

        /// <summary>
        ///     Chuyển bộ SDK: hỏi lại kèm toàn bộ kế hoạch (cài / gỡ / chặn / ID / define) — gỡ Firebase 367MB
        ///     hay đổi dev key AppsFlyer đều không phải chỉnh nhỏ. Chạy ngoài lượt vẽ (qua Defer).
        /// </summary>
        private void Switch()
        {
            var plan = SdkSwitcher.BuildPlan(_profile, _reports, _manualPackages, _excluded);
            if (!plan.HasWork)
            {
                _message = "Khong co gi phai lam — project da dung bo SDK nay.";
                _messageStatus = EzgStatus.Ok;
                InternalEditorUtility.RepaintAllViews();
                return;
            }

            var jobs = plan.DownloadJobs();
            var ok = EditorUtility.DisplayDialog("EzgKit - Chuyen sang " + _profile.Title,
                plan.Summary()
                + (jobs.Count > 0 ? $"\n{jobs.Count} SDK chua co trong cache se duoc TAI VE truoc (Firebase ~1 GB).\n" : "")
                + "\nSDK bi go duoc export vao cache truoc:\n" + SdkSwitcher.CacheDir
                + "\n\nUnity se reimport / resolve package / recompile sau khi chuyen. Tiep tuc?", "Chuyen", "Huy");
            if (!ok) return;

            if (jobs.Count == 0)
            {
                Execute(plan);
                return;
            }

            _message = "Dang tai SDK…";
            _messageStatus = EzgStatus.None;
            SdkDownloader.Start(jobs, (downloaded, message) =>
            {
                if (!downloaded)
                {
                    _message = "Tai SDK loi — chua chuyen gi: " + message;
                    _messageStatus = EzgStatus.Error;
                    _reloadPending = true;
                    InternalEditorUtility.RepaintAllViews();
                    return;
                }

                Execute(plan);
            });
        }

        /// <summary>Thi hành sau khi mọi file cài đã sẵn (cache / vừa tải).</summary>
        private void Execute(SwitchPlan plan)
        {
            if (SdkSwitcher.Execute(_profile, _reports, plan, out var log, out var error))
            {
                _message = "Da chuyen: " + string.Join(" · ", log);
                _messageStatus = plan.Blocked.Count > 0 ? EzgStatus.Warn : EzgStatus.Ok;
            }
            else
            {
                _message = "Chuyen dung giua chung: " + error + (log.Count > 0 ? "  |  da lam: " + string.Join(" · ", log) : "");
                _messageStatus = EzgStatus.Error;
            }

            _reloadPending = true;
            InternalEditorUtility.RepaintAllViews();
        }

        #endregion
    }
}
#endif
