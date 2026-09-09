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
    ///     Tab của MỘT bộ SDK (Ezg trong nhà / Neptune / SayGame …). Xếp theo việc dev thật sự làm khi đi
    ///     với một publisher:
    ///     <list type="number">
    ///         <item>Nút <b>Chuyển sang {X}</b>: cài SDK thiếu, gỡ SDK thừa, ghi ID cấp sẵn, gắn define — một
    ///             lần bấm sau khi xem kế hoạch (<see cref="SdkSwitcher.BuildPlan" />).</item>
    ///         <item>Khối <b>ID phải điền</b> ngay dưới: mỗi ID một ô nhập (ID publisher cấp thì điền sẵn giá
    ///             trị phải có), một nút <b>Điền</b> ghi tất cả vào đúng file qua <see cref="PublisherIdWriter" />.
    ///             Đây là thứ người dùng mở tab để làm — không phải đọc.</item>
    ///         <item>Bảng SDK bốn nhóm (cần gắn thêm · đã gắn · nền tảng · thừa) để soi chi tiết.</item>
    ///         <item>Cuối trang: đoạn "Về {publisher}" và kế hoạch chuyển — gấp lại, đọc khi cần.</item>
    ///     </list>
    ///     <para>
    ///         Một lớp page cho mọi <see cref="IPublisherProfile" />: profile mang yêu cầu, <see cref="SdkCatalog" />
    ///         dò project, <see cref="SdkSwitcher" /> lập/thi hành kế hoạch, page vẽ.
    ///     </para>
    ///     <para>
    ///         Cùng kỷ luật snapshot với các tab khác: <see cref="Reload" /> chụp mọi thứ vào field (kể cả
    ///         <c>RequiredSdks</c> — profile Ezg đọc file); kết quả chuyển/điền đổ vào ở ĐẦU <see cref="Draw" /> qua
    ///         <see cref="_reloadPending" />; nút ghi chạy qua <see cref="ReadinessActions.Defer" />. Ô kéo
    ///         .unitypackage đổi giá trị → chỉ dựng lại kế hoạch (<see cref="_replanPending" />), không quét lại SDK.
    ///         Ô nhập ID đang gõ (chưa Điền) sống qua Reload trong <see cref="_typed" />.
    ///     </para>
    ///     <para>
    ///         Không tham gia "chạy hết" (<see cref="RunAllLabel" /> = null): đổi bộ SDK là quyết định riêng.
    ///     </para>
    /// </summary>
    internal class PublisherPage : IEzgKitPage
    {
        #region Constants

        /// <summary>Cột nhãn của ô ID: "AppsFlyer · iOS App Store ID" dài hơn nhãn thường nên rộng hơn <see cref="EzgKitStyles.LABEL_WIDTH" />.</summary>
        private const float ID_LABEL_WIDTH = 236f;

        /// <summary>Nút cuối hàng ID mở thẳng chỗ setup (chọn asset / mở file đúng dòng) để soi giá trị đã ghi.</summary>
        private const float ID_ACTION_WIDTH = 168f;

        private const float FILL_BUTTON_WIDTH = 240f;
        private const string FILL_LABEL_IDLE = "Điền ID vào project";

        #endregion

        #region Types

        /// <summary>Một ô nhập ID trên form — dựng trong <see cref="Reload" />, chuỗi hint dựng sẵn để Draw không nối chuỗi.</summary>
        private sealed class IdField
        {
            internal SdkKind Kind;
            internal string Key;
            internal string Id;
            internal string Label;
            internal string Current;
            internal string Wanted;
            internal string Input;
            internal EzgStatus Status;
            internal bool Writable;
            internal bool Installed;
            internal string Tooltip;
            internal string Hint;
            internal string WhereHint;
            internal string Note;
            internal (string Label, string Url)[] Links;

            /// <summary>Action đầu tiên của slot mở đúng chỗ setup (SelectAsset / OpenScript). Run null = không có.</summary>
            internal (string Label, Action Run) Open;

            internal bool Dirty => Writable && Installed && Input != Current;
        }

        /// <summary>Một việc đỏ/vàng phải sửa — gom lên đầu trang, CHỈ chữ (tên + cách sửa), không nút. Dựng trong <see cref="BuildIssues" />.</summary>
        private sealed class Issue
        {
            internal EzgStatus Status;
            internal string Title;
            internal string Fix;
        }

        #endregion

        #region Fields

        private readonly IPublisherProfile _profile;
        private PublisherState _state;
        private List<SdkReport> _reports = new();
        private SwitchPlan _plan = new();

        private readonly List<SdkReport> _missing = new();
        private readonly List<SdkReport> _installed = new();
        private readonly List<SdkReport> _platform = new();
        private readonly List<SdkReport> _extra = new();

        private readonly List<IdField> _idFields = new();
        private readonly List<Issue> _issues = new();
        private string _issuesTitle = "";

        /// <summary>ID người dùng đã gõ nhưng chưa Điền — giữ qua Reload (đổi tab, ReloadAll) để không mất chữ.</summary>
        private readonly Dictionary<string, string> _typed = new();

        private int _dirtyCount;
        private string _fillLabel = FILL_LABEL_IDLE;

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

            BuildIdFields();
            _headline = BuildHeadline();
            Replan();
            _loaded = true;
        }

        /// <summary>
        ///     Dựng form ID từ bảng SDK. Ô của ID publisher cấp điền sẵn giá trị phải có (một lần bấm là khớp);
        ///     ID game tự tạo điền giá trị đang có. Chữ người dùng đang gõ (khác giá trị trong file) được giữ.
        /// </summary>
        private void BuildIdFields()
        {
            _idFields.Clear();
            foreach (var report in _reports)
            {
                if (!report.Required || report.IsPlatform) continue;
                foreach (var slot in report.Slots)
                {
                    var field = new IdField
                    {
                        Kind = report.Kind,
                        Key = slot.Key,
                        Id = report.Kind + ":" + slot.Key,
                        Label = ShortName(report.Name) + " · " + slot.Label,
                        Current = slot.Current ?? "",
                        Wanted = slot.Wanted,
                        Status = slot.Status,
                        Writable = PublisherIdWriter.CanWrite(report.Kind, slot.Key),
                        Installed = report.Installed,
                        Note = slot.Note,
                        Links = report.Links,
                        Open = FirstOpenAction(slot.Actions),
                    };

                    if (_typed.TryGetValue(field.Id, out var typed) && typed != field.Current) field.Input = typed;
                    else
                    {
                        _typed.Remove(field.Id);
                        field.Input = slot.Wanted ?? field.Current;
                    }

                    var tooltip = new StringBuilder();
                    if (slot.Where != null) tooltip.Append("Ghi vào: ").Append(slot.Where);
                    if (!string.IsNullOrEmpty(slot.HowToGet))
                    {
                        if (tooltip.Length > 0) tooltip.Append("\n\n");
                        tooltip.Append("Lấy ở: ").Append(slot.HowToGet);
                    }

                    field.Tooltip = tooltip.ToString();
                    field.WhereHint = slot.Where == null ? null : "Sẽ ghi vào: " + slot.Where;
                    field.Hint = BuildIdHint(field, slot);
                    _idFields.Add(field);
                }
            }

            RefreshFillLabel();
        }

        /// <summary>Action "mở chỗ setup" của slot — catalog xếp nút chọn asset / mở file trước nút đổi tab.</summary>
        private static (string Label, Action Run) FirstOpenAction((string Label, Action Run)[] actions)
        {
            if (actions == null) return default;
            foreach (var action in actions)
                if (action.Run != null && !action.Label.StartsWith("Mở tab")) return action;
            return default;
        }

        private static string BuildIdHint(IdField field, SlotReport slot)
        {
            if (!field.Installed)
                return field.Wanted != null
                    ? $"Chưa gắn SDK — \"Chuyển sang\" cài xong sẽ điền {field.Wanted} (publisher cấp)."
                    : "Chưa gắn SDK — \"Chuyển sang\" cài xong rồi quay lại điền.";
            if (!field.Writable)
                return "Ngoài Unity — làm trên console của SDK." + (string.IsNullOrEmpty(slot.HowToGet) ? "" : " " + slot.HowToGet);
            if (field.Wanted != null && field.Current != field.Wanted)
                return $"Đang là {(field.Current.Length == 0 ? "— chưa có" : field.Current)} → publisher cấp {field.Wanted}.";
            if (field.Wanted == null && field.Current.Length == 0)
                return "Chưa có" + (string.IsNullOrEmpty(slot.HowToGet) ? "." : " — lấy ở: " + slot.HowToGet);
            return null;
        }

        /// <summary>"Meta (Facebook SDK)" → "Meta": nhãn hàng ID phải vừa một cột.</summary>
        private static string ShortName(string name)
        {
            var paren = name.IndexOf(" (", StringComparison.Ordinal);
            return paren > 0 ? name.Substring(0, paren) : name;
        }

        private void RefreshFillLabel()
        {
            _dirtyCount = 0;
            foreach (var field in _idFields)
                if (field.Dirty) _dirtyCount++;
            _fillLabel = _dirtyCount == 0 ? FILL_LABEL_IDLE : $"Điền {_dirtyCount} ID vào project";
        }

        private void Replan()
        {
            _plan = SdkSwitcher.BuildPlan(_profile, _reports, _manualPackages, _excluded);
            _planTitle = _plan.HasWork
                ? $"Kế hoạch \"Chuyển sang {_profile.Title}\": cài {_plan.Install.Count} · gỡ {_plan.Remove.Count} · chặn {_plan.Blocked.Count} · bỏ qua {_plan.Skipped.Count} · ID {_plan.Ids.Count} · define {_plan.Defines.Count}"
                : $"Kế hoạch \"Chuyển sang {_profile.Title}\": không có gì phải làm";
            _planBody = PlanBody();
            BuildIssues();
        }

        /// <summary>
        ///     Gom mọi mục Error/Warn của bảng SDK thành danh sách "Cần sửa": SDK thiếu (→ Chuyển sang), ID sai/
        ///     trống (→ ô ở khối ID phải điền), event thiếu/thiếu tham số (→ cách viết). Đỏ trước
        ///     vàng. Phụ thuộc kế hoạch (dòng "Sẽ cài / Chặn" của SDK thiếu) nên dựng sau <see cref="Replan" />.
        /// </summary>
        private void BuildIssues()
        {
            _issues.Clear();
            foreach (var report in _reports)
            {
                if (!report.Required) continue;

                if (!report.Installed)
                {
                    var action = _plan.ActionOf(report.Kind);
                    _issues.Add(new Issue
                    {
                        Status = EzgStatus.Error,
                        Title = $"Chưa gắn {report.Name}",
                        Fix = action != null && action.StartsWith("Chặn")
                            ? action
                            : $"Bấm \"Chuyển sang {_profile.Title}\" ở trên — {(action ?? "sẽ cài SDK này")}.",
                    });
                    continue;
                }

                foreach (var slot in report.Slots)
                {
                    if (slot.Status is not (EzgStatus.Warn or EzgStatus.Error)) continue;
                    var wrong = slot.Wanted != null && !string.IsNullOrEmpty(slot.Current);
                    _issues.Add(new Issue
                    {
                        Status = slot.Status,
                        Title = $"{report.Name} · {slot.Label}: {(wrong ? "sai (" + slot.Current + ")" : "chưa có")}",
                        Fix = slot.Wanted != null
                            ? $"Ô \"{slot.Label}\" ở khối ID phải điền đã điền sẵn {slot.Wanted} (publisher cấp) — bấm Điền."
                            : $"Gõ giá trị vào ô \"{slot.Label}\" ở khối ID phải điền rồi bấm Điền."
                              + (string.IsNullOrEmpty(slot.HowToGet) ? "" : " Lấy ở: " + slot.HowToGet),
                    });
                }

                foreach (var ev in report.Events)
                {
                    if (ev.Status is not (EzgStatus.Warn or EzgStatus.Error)) continue;
                    _issues.Add(new Issue
                    {
                        Status = ev.Status,
                        Title = $"{report.Name} · Event {ev.Name}: {(string.IsNullOrEmpty(ev.Value) ? "chưa có" : ev.Note)}",
                        Fix = ev.Fix,
                    });
                }
            }

            // Đỏ trước vàng; trong cùng màu giữ thứ tự SDK.
            _issues.Sort((a, b) => b.Status.CompareTo(a.Status));
            var errors = 0;
            foreach (var issue in _issues) if (issue.Status == EzgStatus.Error) errors++;
            _issuesTitle = errors > 0
                ? $"Cần sửa ({_issues.Count}) — {errors} lỗi"
                : $"Cần sửa ({_issues.Count})";
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
                if (_requiredCount == 0)
                {
                    EzgKitStyles.CollapsibleHelp("publisher-" + _profile.Id, $"Về {_profile.DisplayName}", _profile.Intro ?? "", true);
                    DrawNoGuide();
                }
                else
                {
                    DrawIssues();
                    DrawIdForm();
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

                    // Phần đọc để cuối: cần lần đầu, còn từ lần hai chỉ đẩy việc thật xuống dưới màn hình.
                    EzgKitStyles.Divider();
                    EzgKitStyles.CollapsibleHelp("publisher-" + _profile.Id, $"Về {_profile.DisplayName}", _profile.Intro ?? "");
                    EzgKitStyles.CollapsibleHelp("publisher-plan-" + _profile.Id, _planTitle, _planBody, _plan.Blocked.Count > 0);
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
                if (_plan.Blocked.Count > 0) banner += $"  ·  {_plan.Blocked.Count} mục bị chặn — xem kế hoạch cuối trang.";
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
        }

        /// <summary>
        ///     Khối "Cần sửa" đầu trang: mọi mục đỏ/vàng của bảng SDK, mỗi mục một hàng — icon · tên · cách sửa.
        ///     Chỉ chữ, không nút (nút nằm ở khối ID phải điền / card SDK). Không có gì thì không vẽ.
        /// </summary>
        private void DrawIssues()
        {
            if (_issues.Count == 0) return;

            EzgKitStyles.SectionHeader(_issuesTitle, "Mỗi dòng một việc, kèm cách sửa. Sửa xong bấm Tải lại (header) hoặc đổi tab để quét lại.");
            using (new EzgKitStyles.CardScope())
            {
                var first = true;
                foreach (var issue in _issues)
                {
                    if (!first) EzgKitStyles.Divider(2f);
                    first = false;

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EzgKitStyles.StatusIcon(issue.Status);
                        EditorGUILayout.LabelField(issue.Title, EditorStyles.boldLabel);
                    }

                    if (!string.IsNullOrEmpty(issue.Fix)) Indented("→ " + issue.Fix, EditorStyles.wordWrappedLabel);
                }
            }
        }

        /// <summary>
        ///     Khối "ID phải điền": mỗi ID một hàng — icon · nhãn (tooltip: ghi vào đâu / lấy ở đâu) · ô nhập —
        ///     và một nút Điền cho tất cả. Hàng chỉ có hint khi có việc (sai / trống / đang sửa) — hàng xanh
        ///     không kèm chữ. ID ngoài Unity hiện giá trị + link, không có ô.
        /// </summary>
        private void DrawIdForm()
        {
            EzgKitStyles.SectionHeader("ID phải điền",
                "Gõ vào ô rồi bấm Điền — tool ghi vào đúng file (FacebookSettings + AndroidManifest · GameConstant.cs · GA Settings.asset, kèm MarketingConfig.json).");

            using (new EzgKitStyles.CardScope())
            {
                if (_idFields.Count == 0)
                {
                    EditorGUILayout.LabelField("Publisher này không đòi ID nào.", EzgKitStyles.Hint);
                    return;
                }

                var first = true;
                foreach (var field in _idFields)
                {
                    if (!first) EzgKitStyles.Divider(2f);
                    first = false;
                    DrawIdField(field);
                }

                EzgKitStyles.Divider();
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(_dirtyCount == 0 || SdkDownloader.IsBusy))
                    {
                        if (EzgKitStyles.PrimaryButton(_fillLabel, GUILayout.Width(FILL_BUTTON_WIDTH)))
                            ReadinessActions.Defer(FillIds);
                    }

                    if (_dirtyCount > 0 && EzgKitStyles.SecondaryButton("Bỏ thay đổi", GUILayout.Width(120f)))
                    {
                        _typed.Clear();
                        _reloadPending = true;
                    }

                    GUILayout.FlexibleSpace();
                }
            }
        }

        private void DrawIdField(IdField field)
        {
            var editable = field.Writable && field.Installed;
            using (new EditorGUILayout.HorizontalScope())
            {
                EzgKitStyles.StatusIcon(field.Installed ? field.Status : EzgStatus.None);
                EditorGUILayout.LabelField(new GUIContent(field.Label, field.Tooltip), EditorStyles.boldLabel, GUILayout.Width(ID_LABEL_WIDTH));

                if (editable)
                {
                    EditorGUI.BeginChangeCheck();
                    var next = EditorGUILayout.TextField(field.Input);
                    if (EditorGUI.EndChangeCheck())
                    {
                        field.Input = next;
                        _typed[field.Id] = next;
                        RefreshFillLabel();
                    }
                }
                else if (!field.Writable && field.Wanted != null)
                    EditorGUILayout.SelectableLabel(field.Wanted, EzgKitStyles.ValueStyle, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                else
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.TextField(field.Input);
                    }

                // Mở thẳng chỗ setup để soi giá trị đã ghi (Inspector của asset / dòng const trong IDE).
                if (field.Open.Run != null
                    && GUILayout.Button("▸ " + field.Open.Label, EditorStyles.miniButton, GUILayout.Width(ID_ACTION_WIDTH)))
                    ReadinessActions.Defer(field.Open.Run);
            }

            if (field.Hint != null) Indented(field.Hint, editable && field.Status == EzgStatus.Error ? EditorStyles.wordWrappedLabel : EzgKitStyles.Hint);
            if (editable && field.Dirty && field.WhereHint != null) Indented(field.WhereHint, EzgKitStyles.Hint);
            if (editable && !string.IsNullOrEmpty(field.Note) && field.Status != EzgStatus.Ok) Indented(field.Note, EzgKitStyles.Hint);
            if (!field.Writable) DrawLinks(field.Links);
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
                Indented($"→ phải là  {slot.Wanted}  (publisher cấp — điền bằng khối \"ID phải điền\" đầu trang, hoặc \"Chuyển sang\")", EditorStyles.wordWrappedLabel);
            else if (slot.Wanted == null && string.IsNullOrEmpty(slot.Current))
                Indented("→ game tự tạo trên console, rồi gõ vào khối \"ID phải điền\" đầu trang.", EditorStyles.wordWrappedLabel);

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
            if (report.Status == EzgStatus.Ok) return "Đã gắn · ID khớp";

            // Card đỏ vì event thiếu chứ không phải ID sai → nói đúng thứ đang hỏng.
            var idBad = false;
            foreach (var slot in report.Slots)
                if (slot.Status is EzgStatus.Warn or EzgStatus.Error) idBad = true;
            if (!idBad && report.Events.Count > 0) return report.Status == EzgStatus.Error ? "Đã gắn · thiếu event" : "Đã gắn · event chưa đủ";
            return report.Status switch
            {
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
        ///     Nút "Điền": ghi mọi ô đang khác giá trị trong file. Dry-run trước để hỏi lại kèm danh sách đổi
        ///     (ghi GameConstant là recompile, ghi FacebookSettings là đổi manifest) — rồi ghi thật và quét lại.
        ///     Chạy ngoài lượt vẽ (qua Defer).
        /// </summary>
        private void FillIds()
        {
            var entries = new List<PublisherIdWriter.Entry>();
            foreach (var field in _idFields)
                if (field.Dirty) entries.Add(new PublisherIdWriter.Entry(field.Kind, field.Key, field.Input.Trim()));

            if (entries.Count == 0)
            {
                _message = "Không có ô nào khác giá trị trong file.";
                _messageStatus = EzgStatus.None;
                InternalEditorUtility.RepaintAllViews();
                return;
            }

            if (!PublisherIdWriter.Write(entries, true, out var preview, out var error))
            {
                _message = "Không điền được: " + error;
                _messageStatus = EzgStatus.Error;
                InternalEditorUtility.RepaintAllViews();
                return;
            }

            var lines = new List<string>();
            foreach (var line in preview)
                if (!line.Contains(PublisherIdWriter.UNCHANGED)) lines.Add("  - " + line);

            var ok = EditorUtility.DisplayDialog("EzgKit - Dien ID " + _profile.Title,
                $"Se ghi {entries.Count} ID vao project:\n\n" + string.Join("\n", lines)
                + "\n\nGameConstant.cs doi -> Unity recompile. Tiep tuc?", "Dien", "Huy");
            if (!ok) return;

            if (PublisherIdWriter.Write(entries, false, out var changes, out error))
            {
                var done = new List<string>();
                foreach (var change in changes)
                    if (!change.Contains(PublisherIdWriter.UNCHANGED)) done.Add(change);
                _message = $"Đã điền {entries.Count} ID: " + string.Join(" · ", done);
                _messageStatus = EzgStatus.Ok;
                _typed.Clear();
            }
            else
            {
                _message = "Điền dừng giữa chừng: " + error + (changes.Count > 0 ? "  |  đã ghi: " + string.Join(" · ", changes) : "");
                _messageStatus = EzgStatus.Error;
            }

            _reloadPending = true;
            InternalEditorUtility.RepaintAllViews();
        }

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
