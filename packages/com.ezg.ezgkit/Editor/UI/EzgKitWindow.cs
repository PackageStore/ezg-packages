#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ezg.Editor.Shared.EzgKit
{
    /// <summary>
    ///     Cửa sổ setup dự án — <c>Ezg &gt; EzgKit</c>. Trái: danh sách trang kèm trạng thái + tiến độ; phải: trang
    ///     đang mở (tiêu đề, việc còn phải làm, các card field, bảng thay đổi) và thanh nút cố định ở đáy.
    ///     <para>
    ///         Mở cửa sổ CHỈ ĐỌC. Mọi lệnh ghi nằm sau nút "Áp dụng" và luôn hiện bảng thay đổi + hỏi lại trước.
    ///         Claude (<c>/setup-project</c>) mở đúng cửa sổ này qua <see cref="EzgKitApi.Open" /> và đọc / ghi qua
    ///         cùng các hàm của trang.
    ///     </para>
    /// </summary>
    internal sealed class EzgKitWindow : EditorWindow, IPageHost
    {
        #region Constants

        private const string USS_PATH = "Packages/com.ezg.ezgkit/Editor/UI/EzgKit.uss";

        /// <summary>
        ///     Phiên Editor này đã có người CHỦ ĐỘNG mở kit. Dùng để đóng bản Unity tự khôi phục từ layout lúc mở
        ///     project — bản đó chạy mọi detector (reflection IAP, quét source) ngay lúc khởi động mà không ai xin.
        /// </summary>
        private const string OPENED_THIS_SESSION_KEY = "Ezg.EzgKit.OpenedThisSession";

        #endregion

        #region Fields

        [SerializeField] private string _selected = PageIds.OVERVIEW;
        [SerializeField] private bool _wizard;
        [SerializeField] private bool _advancedOpen = true;

        private List<SetupPage> _pages;
        private readonly Dictionary<string, PageReport> _reports = new();
        private readonly Dictionary<string, VisualElement> _navItems = new();

        private VisualElement _navList;
        private Label _projectLabel;
        private Label _idsLabel;
        private VisualElement _progressFill;
        private Label _progressLabel;

        private Label _pageTitle;
        private Label _pageDesc;
        private Label _pageSummary;
        private VisualElement _pagePillSlot;
        private ScrollView _bodyScroll;
        private VisualElement _body;
        private VisualElement _resultSlot;
        private VisualElement _toastSlot;
        private VisualElement _footer;

        [NonSerialized] private bool _closing;

        #endregion

        #region Menu / open

        [MenuItem("Ezg/EzgKit", false, 80)]
        internal static void OpenFromMenu() => Open(null);

        /// <summary>Mở (hoặc focus) cửa sổ, nhảy tới trang <paramref name="pageId" /> (null = giữ trang đang mở).</summary>
        internal static EzgKitWindow Open(string pageId)
        {
            SessionState.SetBool(OPENED_THIS_SESSION_KEY, true);
            var window = GetWindow<EzgKitWindow>(false, "EzgKit", true);
            window.titleContent = new GUIContent("EzgKit");
            window.minSize = new Vector2(860, 520);
            if (!string.IsNullOrEmpty(pageId)) window.Select(pageId);
            window.Show();
            window.Focus();
            return window;
        }

        #endregion

        #region Unity

        private void CreateGUI()
        {
            if (!SessionState.GetBool(OPENED_THIS_SESSION_KEY, false))
            {
                // Bản khôi phục từ layout: đóng lại, không chạy detector nào.
                _closing = true;
                EditorApplication.delayCall += () =>
                {
                    if (this != null) Close();
                };
                return;
            }

            _pages = SetupPages.Create();
            BuildShell();
            RefreshAll();
            Select(string.IsNullOrEmpty(_selected) ? PageIds.OVERVIEW : _selected);
        }

        private void OnFocus()
        {
            // Quay lại cửa sổ sau khi sửa gì đó bên ngoài (Inspector, file) → trạng thái cột trái phải khớp.
            if (_closing || _pages == null || _navList == null) return;
            RefreshAll();
        }

        #endregion

        #region Shell

        private void BuildShell()
        {
            var root = rootVisualElement;
            root.Clear();
            var sheet = LoadStyle();
            if (sheet != null) root.styleSheets.Add(sheet);

            var shell = new VisualElement();
            shell.AddToClassList("ezg-root");
            shell.AddToClassList(EditorGUIUtility.isProSkin ? "ezg-dark" : "ezg-light");
            root.Add(shell);

            var split = new TwoPaneSplitView(0, 250, TwoPaneSplitViewOrientation.Horizontal);
            split.AddToClassList("ezg-split");
            shell.Add(split);

            split.Add(BuildNav());
            split.Add(BuildMain());
        }

        private static StyleSheet LoadStyle()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(USS_PATH);
            if (sheet != null) return sheet;
            foreach (var guid in AssetDatabase.FindAssets("EzgKit t:StyleSheet"))
            {
                sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(guid));
                if (sheet != null) return sheet;
            }

            return null;
        }

        private VisualElement BuildNav()
        {
            var nav = new VisualElement();
            nav.AddToClassList("ezg-nav");

            var header = new VisualElement();
            header.AddToClassList("ezg-nav-header");
            header.Add(Ui.Text("EZGKIT · PROJECT SETUP", "ezg-brand"));
            _projectLabel = Ui.Text(string.Empty, "ezg-project");
            header.Add(_projectLabel);
            _idsLabel = Ui.Text(string.Empty, "ezg-ids");
            header.Add(_idsLabel);
            var progress = new VisualElement();
            progress.AddToClassList("ezg-progress");
            _progressFill = new VisualElement();
            _progressFill.AddToClassList("ezg-progress-fill");
            progress.Add(_progressFill);
            header.Add(progress);
            _progressLabel = Ui.Text(string.Empty, "ezg-progress-label");
            header.Add(_progressLabel);
            nav.Add(header);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("ezg-nav-scroll");
            _navList = scroll.contentContainer;
            nav.Add(scroll);

            var footer = new VisualElement();
            footer.AddToClassList("ezg-nav-footer");
            var all = Ui.Button(footer, "Setup tất cả  →", StartWizard, "primary",
                "Đi lần lượt qua các trang Setup còn việc (bỏ qua trang đã xong / để sau / không áp dụng).");
            all.AddToClassList("ezg-btn-block");
            var refresh = Ui.Button(footer, "Làm mới trạng thái", () =>
            {
                SourceIndex.Invalidate();
                RefreshAll();
                Rebuild();
                Toast("Đã đọc lại trạng thái từ project.", EzgStatus.None);
            }, "secondary", "Đọc lại mọi trang từ project. Chỉ đọc, không ghi gì.");
            refresh.AddToClassList("ezg-btn-block");
            nav.Add(footer);
            return nav;
        }

        private VisualElement BuildMain()
        {
            var main = new VisualElement();
            main.AddToClassList("ezg-main");

            var header = new VisualElement();
            header.AddToClassList("ezg-page-header");
            var titleRow = new VisualElement();
            titleRow.AddToClassList("ezg-page-title-row");
            _pageTitle = Ui.Text(string.Empty, "ezg-page-title");
            titleRow.Add(_pageTitle);
            _pagePillSlot = new VisualElement();
            titleRow.Add(_pagePillSlot);
            header.Add(titleRow);
            _pageDesc = Ui.Text(string.Empty, "ezg-page-desc");
            header.Add(_pageDesc);
            _pageSummary = Ui.Text(string.Empty, "ezg-page-summary");
            header.Add(_pageSummary);
            main.Add(header);

            _toastSlot = new VisualElement();
            _toastSlot.AddToClassList("ezg-toast");
            main.Add(_toastSlot);

            _bodyScroll = new ScrollView(ScrollViewMode.Vertical);
            _bodyScroll.AddToClassList("ezg-body-scroll");
            _body = new VisualElement();
            _body.AddToClassList("ezg-body");
            _bodyScroll.Add(_body);
            main.Add(_bodyScroll);

            _footer = new VisualElement();
            _footer.AddToClassList("ezg-footer");
            main.Add(_footer);
            return main;
        }

        #endregion

        #region Nav

        private void RebuildNav()
        {
            _navList.Clear();
            _navItems.Clear();

            var setupIndex = 0;
            VisualElement setupGroup = null;
            VisualElement advancedGroup = null;

            foreach (var page in _pages)
            {
                VisualElement container;
                switch (page.Group)
                {
                    case SetupPage.GROUP_OVERVIEW:
                        container = Group(ref setupGroup, null);
                        break;
                    case SetupPage.GROUP_ADVANCED:
                        if (advancedGroup == null)
                        {
                            var wrap = new VisualElement();
                            wrap.AddToClassList("ezg-nav-group");
                            var fold = new Foldout { text = "NÂNG CAO", value = _advancedOpen };
                            fold.AddToClassList("ezg-nav-fold");
                            fold.RegisterValueChangedCallback(evt => _advancedOpen = evt.newValue);
                            wrap.Add(fold);
                            _navList.Add(wrap);
                            advancedGroup = fold.contentContainer;
                        }

                        container = advancedGroup;
                        break;
                    default:
                        container = Group(ref setupGroup, "SETUP");
                        setupIndex++;
                        break;
                }

                container.Add(NavItem(page, page.Group == SetupPage.GROUP_SETUP ? setupIndex.ToString() : string.Empty));
            }
        }

        private VisualElement Group(ref VisualElement group, string title)
        {
            if (group != null)
            {
                if (title != null && group.userData == null)
                {
                    group.userData = title;
                    var label = Ui.Text(title, "ezg-nav-group-title");
                    group.Add(label);
                }

                return group;
            }

            group = new VisualElement();
            group.AddToClassList("ezg-nav-group");
            if (title != null)
            {
                group.userData = title;
                group.Add(Ui.Text(title, "ezg-nav-group-title"));
            }

            _navList.Add(group);
            return group;
        }

        private VisualElement NavItem(SetupPage page, string index)
        {
            var report = ReportOf(page.Id);
            var item = new VisualElement();
            item.AddToClassList("ezg-nav-item");
            if (page.Id == _selected) item.AddToClassList("ezg-selected");
            item.Add(Ui.Text(index, "ezg-nav-index"));
            item.Add(Ui.Text(page.Title, "ezg-nav-title"));
            if (page.Group != SetupPage.GROUP_OVERVIEW)
            {
                item.Add(Ui.Text(SetupStateText.Label(report.State), "ezg-nav-state"));
                item.Add(Ui.Dot(report.State));
            }

            item.tooltip = report.Summary;
            item.RegisterCallback<ClickEvent>(_ => Select(page.Id));
            _navItems[page.Id] = item;
            return item;
        }

        private void UpdateHeader()
        {
            var name = ProjectProfileFile.Get("projectName");
            if (string.IsNullOrEmpty(name) || name.Contains("__")) name = PlayerSettings.productName;
            _projectLabel.text = name;
            var android = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            var ios = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS);
            _idsLabel.text = $"Android  {Or(android)}\niOS  {Or(ios)}\nVersion  {Or(PlayerSettings.bundleVersion)}";

            var total = 0;
            var done = 0;
            foreach (var page in _pages)
            {
                if (!page.CountsInProgress) continue;
                total++;
                var state = ReportOf(page.Id).State;
                if (state is SetupState.Done or SetupState.NotApplicable) done++;
            }

            _progressFill.style.width = Length.Percent(total == 0 ? 0 : 100f * done / total);
            _progressLabel.text = $"{done}/{total} mục Setup đã xong";
        }

        private static string Or(string value) => string.IsNullOrEmpty(value) ? "—" : value;

        #endregion

        #region Page

        private PageReport ReportOf(string id) =>
            _reports.TryGetValue(id, out var report) ? report : new PageReport { State = SetupState.Todo };

        private SetupPage Current => SetupPages.Find(_pages, _selected) ?? _pages[0];

        internal void Select(string pageId)
        {
            if (_pages == null)
            {
                // CreateGUI chưa chạy (cửa sổ vừa tạo) — nhớ trang, CreateGUI sẽ mở đúng trang này.
                if (!string.IsNullOrEmpty(pageId)) _selected = pageId;
                return;
            }

            if (SetupPages.Find(_pages, pageId) == null) pageId = PageIds.OVERVIEW;
            _selected = pageId;
            foreach (var pair in _navItems)
                if (pair.Key == pageId) pair.Value.AddToClassList("ezg-selected");
                else pair.Value.RemoveFromClassList("ezg-selected");
            _toastSlot.Clear();
            Rebuild();
        }

        public void Rebuild()
        {
            if (_pages == null || _body == null) return;
            var page = Current;
            var report = ReportOf(page.Id);

            _pageTitle.text = page.Title;
            _pageDesc.text = page.Description;
            _pageSummary.text = report.Summary ?? string.Empty;
            _pageSummary.style.display = string.IsNullOrEmpty(report.Summary) ? DisplayStyle.None : DisplayStyle.Flex;
            _pagePillSlot.Clear();
            if (page.Group != SetupPage.GROUP_OVERVIEW) _pagePillSlot.Add(Ui.Pill(report.State));

            _body.Clear();
            if (page.Group != SetupPage.GROUP_OVERVIEW) Ui.Todos(_body, report.Todos);

            try
            {
                page.Build(_body, this);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Ui.Message(_body, "Dựng trang lỗi: " + exception.Message, EzgStatus.Error);
            }

            _resultSlot = new VisualElement();
            _body.Add(_resultSlot);
            BuildFooter(page);
        }

        private void BuildFooter(SetupPage page)
        {
            _footer.Clear();
            if (page.Group == SetupPage.GROUP_OVERVIEW)
            {
                Ui.Spacer(_footer);
                Ui.Button(_footer, "Setup tất cả  →", StartWizard, "primary");
                return;
            }

            if (page.CanApply)
            {
                Ui.Button(_footer, "Xem thay đổi", () => Preview(page), "secondary",
                    "Đối chiếu giá trị trên trang với project — CHƯA ghi gì.");
                Ui.Button(_footer, "Áp dụng", () => ApplyFromUi(page), "primary",
                    "Ghi vào project (hiện bảng thay đổi và hỏi lại trước).");
            }
            else
            {
                Ui.Button(_footer, "Đánh dấu đã xong", () =>
                {
                    EzgKitState.SetMarker(page.Id, EzgKitState.MARKER_DONE);
                    RefreshAll();
                    Rebuild();
                    Toast("Đã đánh dấu đã xem / đã xong.", EzgStatus.Ok);
                }, "secondary");
            }

            Ui.Spacer(_footer);

            var marker = EzgKitState.GetMarker(page.Id);
            if (marker == EzgKitState.MARKER_DEFERRED || marker == EzgKitState.MARKER_NA)
                Ui.Button(_footer, "Bỏ đánh dấu", () => Mark(page, null), "ghost",
                    "Xoá quyết định \"để sau\" / \"không áp dụng\" — trạng thái quay về theo project.");
            else
            {
                Ui.Button(_footer, "Để sau", () => Mark(page, EzgKitState.MARKER_DEFERRED), "ghost",
                    "Ghi nhận làm sau — trang không còn tính là việc đang chờ trong luồng Setup tất cả.");
                Ui.Button(_footer, "Không áp dụng", () => Mark(page, EzgKitState.MARKER_NA), "ghost",
                    "Dự án không dùng phần này.");
            }

            if (_wizard) Ui.Button(_footer, "Tiếp  →", NextWizardStep, "primary", "Sang trang Setup kế tiếp còn việc.");
        }

        private void Mark(SetupPage page, string marker)
        {
            EzgKitState.SetMarker(page.Id, marker);
            RefreshAll();
            if (_wizard && marker != null)
            {
                NextWizardStep();
                return;
            }

            Rebuild();
            Toast(marker == null ? "Đã bỏ đánh dấu." : marker == EzgKitState.MARKER_NA ? "Đã đánh dấu không áp dụng." : "Đã để sau.",
                EzgStatus.None);
        }

        #endregion

        #region Apply

        private void Preview(SetupPage page)
        {
            var values = page.CollectUi();
            if (values == null) return;
            var result = page.Apply(values, true);
            result.DryRun = true;
            ShowResult(result);
        }

        private void ApplyFromUi(SetupPage page)
        {
            var values = page.CollectUi();
            if (values == null) return;

            var preview = page.Apply(values, true);
            preview.DryRun = true;
            if (!preview.Ok)
            {
                ShowResult(preview);
                Toast("Chưa ghi được: " + preview.Error, EzgStatus.Error);
                return;
            }

            if (preview.ChangedCount == 0)
            {
                ShowResult(preview);
                EzgKitState.SetMarker(page.Id, EzgKitState.MARKER_DONE);
                RefreshAll();
                Toast("Project đã khớp — không có ô nào cần ghi.", EzgStatus.Ok);
                return;
            }

            var lines = new List<string>();
            foreach (var row in preview.Rows)
                if (!row.Matched && lines.Count < 14)
                    lines.Add($"• {row.Sink} › {row.Field}");
            if (preview.ChangedCount > lines.Count) lines.Add($"… và {preview.ChangedCount - lines.Count} ô khác");

            var message = $"Ghi {preview.ChangedCount} ô vào project:\n\n{string.Join("\n", lines)}";
            if (!string.IsNullOrEmpty(page.ApplyWarning)) message += "\n\n" + page.ApplyWarning;
            if (!EditorUtility.DisplayDialog("EzgKit — " + page.Title, message, "Áp dụng", "Huỷ")) return;

            var result = page.Apply(values, false);
            if (result.Ok) EzgKitState.SetMarker(page.Id, EzgKitState.MARKER_DONE);
            SourceIndex.Invalidate();
            RefreshAll();
            Rebuild();
            ShowResult(result);
            Toast(result.Ok ? $"Đã ghi {result.ChangedCount} ô." : "Ghi dừng giữa chừng: " + result.Error,
                result.Ok ? EzgStatus.Ok : EzgStatus.Error);
        }

        public void ShowResult(ApplyResult result)
        {
            if (_resultSlot == null) return;
            _resultSlot.Clear();
            Ui.DiffTable(_resultSlot, result);
            _bodyScroll.schedule.Execute(() => _bodyScroll.ScrollTo(_resultSlot)).ExecuteLater(50);
        }

        #endregion

        #region Wizard

        /// <summary>Bắt đầu luồng "Setup tất cả" (API <c>EzgKitApi.SetupAll</c> gọi vào đây).</summary>
        internal void RunSetupAll() => StartWizard();

        private void StartWizard()
        {
            if (_pages == null) return;
            _wizard = true;
            var next = NextPending(null);
            if (next == null)
            {
                _wizard = false;
                Select(PageIds.OVERVIEW);
                Toast("Mọi mục Setup đã xong / để sau / không áp dụng.", EzgStatus.Ok);
                return;
            }

            Select(next.Id);
            Toast("Setup tất cả: điền trang này rồi Áp dụng, hoặc Để sau / Không áp dụng. Bấm \"Tiếp →\" để sang trang kế.", EzgStatus.None);
        }

        private void NextWizardStep()
        {
            var next = NextPending(_selected);
            if (next == null)
            {
                _wizard = false;
                Select(PageIds.OVERVIEW);
                Toast("Đã đi hết các mục Setup còn việc.", EzgStatus.Ok);
                return;
            }

            Select(next.Id);
        }

        /// <summary>Trang Setup kế tiếp (sau <paramref name="afterId" />) còn ở trạng thái chưa xong / còn việc / lỗi.</summary>
        private SetupPage NextPending(string afterId)
        {
            var passed = afterId == null;
            foreach (var page in _pages)
            {
                if (!passed)
                {
                    if (page.Id == afterId) passed = true;
                    continue;
                }

                if (page.Group != SetupPage.GROUP_SETUP) continue;
                var state = ReportOf(page.Id).State;
                if (state is SetupState.Todo or SetupState.Partial or SetupState.Error) return page;
            }

            return null;
        }

        #endregion

        #region Host

        public void RefreshAll()
        {
            if (_pages == null) return;
            foreach (var page in _pages)
                _reports[page.Id] = page.DetectResolved();
            RebuildNav();
            UpdateHeader();

            // Cập nhật pill + summary của trang đang mở (không dựng lại thân — ô đang gõ dở không mất).
            var current = Current;
            var report = ReportOf(current.Id);
            _pageSummary.text = report.Summary ?? string.Empty;
            _pagePillSlot.Clear();
            if (current.Group != SetupPage.GROUP_OVERVIEW) _pagePillSlot.Add(Ui.Pill(report.State));
        }

        public void Toast(string message, EzgStatus level)
        {
            if (_toastSlot == null) return;
            _toastSlot.Clear();
            if (string.IsNullOrEmpty(message)) return;
            var label = Ui.Message(_toastSlot, message, level);
            label.style.marginTop = 10;
        }

        void IPageHost.Open(string pageId) => Select(pageId);

        /// <summary>Report đã tính cho trang (Tổng quan đọc để vẽ thẻ).</summary>
        internal PageReport ReportFor(string pageId) => ReportOf(pageId);

        internal IReadOnlyList<SetupPage> Pages => _pages;

        #endregion
    }
}
#endif
