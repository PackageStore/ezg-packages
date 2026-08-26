#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Ezg.Editor.Shared.Firebase;
using Ezg.Editor.Shared.Marketing;
using Ezg.Editor.Shared.Publisher;
using Ezg.Editor.Shared.Readiness;
using Ezg.Editor.Shared.Social;
using UnityEditor;
using UnityEngine;

namespace Ezg.Editor.Shared.EzgKit
{
    /// <summary>
    ///     EzgKit — cửa sổ tool tổng của dự án: một chỗ duy nhất cho toàn bộ việc "dựng dự án mới từ
    ///     code-template" — số marketing/ads (<see cref="MarketingSetupPage" />) và app Firebase
    ///     (<see cref="FirebaseSetupPage" />).
    ///     <para>
    ///         <b>Bố cục:</b> một thanh header LUÔN hiện ở đỉnh (tên dự án + id Android/iOS + version +
    ///         nút làm mới), dưới nó là cột nav dọc bên trái và vùng nội dung bên phải. Id nằm ở header
    ///         chứ không nằm trong tab Tổng quan vì đó là id THẬT sẽ đi vào bản build — mọi tab đều
    ///         phụ thuộc nó, nên nó phải nhìn thấy được ở mọi tab, không phải chuyển tab mới xem được.
    ///     </para>
    ///     <para>
    ///         <b>Cửa sổ vẽ phần đầu trang, page không vẽ lại:</b> tiêu đề (<see cref="IEzgKitPage.Title" />),
    ///         một dòng mô tả (<see cref="IEzgKitPage.Subtitle" />) và chip trạng thái
    ///         (<see cref="IEzgKitPage.Status" /> + <see cref="IEzgKitPage.Headline" />) do
    ///         <see cref="DrawPageHeader" /> vẽ; <see cref="IEzgKitPage.Draw" /> chỉ vẽ phần thân.
    ///     </para>
    ///     <para>
    ///         Tab xếp DỌC ở cột trái: danh sách tool còn dài ra, mà tab ngang thì thêm vài mục nữa là
    ///         chữ bị bóp lại không đọc được. Cột trái còn chỗ cho icon trạng thái của từng tab.
    ///     </para>
    ///     <para>
    ///         <b>Thứ tự có ý nghĩa:</b> marketing sheet ghi package name / bundle id vào PlayerSettings,
    ///         còn Firebase ĐỌC hai id đó để tạo app — mà Firebase không cho sửa id sau khi tạo. Vì thế
    ///         tab Tổng quan chạy Marketing trước, Firebase sau, và các page được xếp đúng thứ tự đó.
    ///     </para>
    ///     <para>
    ///         <b>Việc nặng hoãn tới cuối OnGUI:</b> đổi tab (<see cref="_pendingTab" />) và chạy hết
    ///         (<see cref="_runAllRequested" />) đều chỉ ghi cờ trong lúc vẽ, rồi mới thi hành ở cuối
    ///         <see cref="OnGUI" /> khi mọi layout group đã đóng. Đổi nội dung hay ghi file (kéo theo
    ///         recompile + domain reload) giữa lượt vẽ là Unity bắn "Mismatched LayoutGroup" và cửa
    ///         sổ vẽ rác cho tới khi đóng mở lại. Cùng lý do đó, mọi scroll view ở đây dùng
    ///         <see cref="EditorGUILayout.ScrollViewScope" /> chứ không Begin/End tay: exception
    ///         giữa chừng vẫn được đóng lại trong <c>finally</c> của scope.
    ///     </para>
    ///     <para>
    ///         Thêm tool setup mới: implement <see cref="IEzgKitPage" /> rồi thêm vào
    ///         <see cref="BuildPages" /> + <see cref="Tab" /> — GUI cửa sổ không phải sửa gì.
    ///     </para>
    /// </summary>
    internal class EzgKitWindow : EditorWindow
    {
        #region Types

        /// <summary>
        ///     Thứ tự phải khớp <see cref="BuildPages" /> (Overview không có page riêng). 1–4 là nhóm
        ///     "Setup Ezg"; từ 5 là nhóm "Nhà phát hành", theo đúng thứ tự <see cref="PublisherRegistry.Profiles" />.
        /// </summary>
        internal enum Tab
        {
            Overview = 0,
            Marketing = 1,
            Firebase = 2,
            Social = 3,
            Readiness = 4,
            Ezg = 5,
            Neptune = 6,
            SayGame = 7,
        }

        #endregion

        #region Fields

        private const string OVERVIEW_TITLE = "Tổng quan";

        private const string OVERVIEW_SUBTITLE =
            "Setup Ezg: các bước dựng dự án từ code-template, làm lần lượt. Nhà phát hành: quy trình riêng với từng publisher.";

        private const string SECTION_EZG = "SETUP EZG";
        private const string SECTION_PUBLISHER = "NHÀ PHÁT HÀNH";
        private const string PUBLISHER_NONE = "Ezg (mặc định — chưa chuyển)";

        /// <summary>Chip trạng thái đầu trang chỉ đủ chỗ cho một câu ngắn; dài hơn thì cắt + đưa vào tooltip.</summary>
        private const int HEADLINE_MAX = 60;

        [SerializeField] private int _tab;

        private IEzgKitPage[] _pages;

        /// <summary>Số page đầu thuộc nhóm "Setup Ezg"; phần còn lại của <see cref="_pages" /> là nhà phát hành.</summary>
        private int _ezgPageCount;

        /// <summary>Nhà phát hành đang áp preset SDK (đọc từ PublisherConfig.json trong <see cref="ReloadAll" />).</summary>
        private string _activePublisherLabel;

        /// <summary>Dùng lại giữa các lượt vẽ để không cấp phát <see cref="GUIContent" /> mỗi OnGUI.</summary>
        private GUIContent[] _navContents;

        /// <summary>Nhãn bước của luồng "chạy hết", gom lại mỗi lượt vẽ vào cùng một list.</summary>
        private readonly List<string> _runAllLabels = new();

        private Vector2 _overviewScroll;
        private Vector2 _sidebarScroll;

        /// <summary>Tab được bấm trong lượt vẽ này, áp dụng ở cuối OnGUI. -1 = không có.</summary>
        private int _pendingTab = -1;

        /// <summary>
        ///     Nút "chạy hết" đã bấm trong lượt vẽ này, chạy ở CUỐI OnGUI. Cùng cơ chế hoãn với
        ///     <see cref="_pendingTab" />, nhưng lý do nặng hơn: <see cref="RunAll" /> ghi file,
        ///     <see cref="AssetDatabase.Refresh" /> trên file .cs → recompile → domain reload; chạy
        ///     nó giữa lúc đang ở trong layout group là vỡ cửa sổ.
        /// </summary>
        private bool _runAllRequested;

        /// <summary>Nhãn version ở header, dựng lại trong <see cref="ReloadAll" /> để khỏi nối chuỗi mỗi
        /// lượt vẽ. null = PlayerSettings chưa có version.</summary>
        private string _versionLabel;

        private string _runAllMessage;

        /// <summary>Mức của <see cref="_runAllMessage" />: Ok = chạy xong, Error = dừng giữa chừng.</summary>
        private EzgStatus _runAllStatus = EzgStatus.None;

        private static readonly GUIContent _refreshContent = new("Làm mới trạng thái",
            "Đọc lại PlayerSettings và các file config. Chỉ đọc, không ghi gì vào project.");

        #endregion

        #region Menu

        [MenuItem("Ezg/EzgKit", false, 80)]
        internal static void OpenOverview() => Open(Tab.Overview);

        [MenuItem("Ezg/Marketing/Bang thong so (Marketing Dashboard)", false, 98)]
        internal static void OpenMarketing() => Open(Tab.Marketing);

        [MenuItem("Ezg/Firebase/Cai dat...", false, 100)]
        internal static void OpenFirebase() => Open(Tab.Firebase);

        [MenuItem("Ezg/Social (Discord - Support - Rating)", false, 100)]
        internal static void OpenSocial() => Open(Tab.Social);

        [MenuItem("Ezg/Readiness (IAP - Firebase - SDK)", false, 101)]
        internal static void OpenReadiness() => Open(Tab.Readiness);

        [MenuItem("Ezg/Nha phat hanh/Ezg (mac dinh trong nha)", false, 120)]
        internal static void OpenEzgPublisher() => Open(Tab.Ezg);

        [MenuItem("Ezg/Nha phat hanh/Neptune (CPI Test)", false, 121)]
        internal static void OpenNeptune() => Open(Tab.Neptune);

        [MenuItem("Ezg/Nha phat hanh/SayGame", false, 122)]
        internal static void OpenSayGame() => Open(Tab.SayGame);

        internal static void Open(Tab tab)
        {
            var window = GetWindow<EzgKitWindow>(false, "EzgKit", true);
            // Set lại tiêu đề: cửa sổ đã mở từ phiên trước giữ nguyên title cũ, GetWindow không ghi đè.
            window.titleContent = new GUIContent("EzgKit");
            // Cột nav ăn mất SIDEBAR_WIDTH bên trái, header bar ăn hai dòng trên cùng: cửa sổ phải đủ
            // rộng để id Android/iOS nằm trọn một dòng và phần thân page không bị bóp.
            window.minSize = new Vector2(900, 520);
            window._tab = (int)tab;
            window.ReloadAll();
            window.Show();
            window.Focus();
        }

        #endregion

        #region Lifecycle

        private void OnEnable()
        {
            if (_pages == null) ReloadAll();
        }

        private void BuildPages()
        {
            // Nhóm "Setup Ezg": thứ tự = thứ tự chạy trong luồng "chạy hết", và = thứ tự tab sau Overview.
            var ezg = new IEzgKitPage[]
            {
                new MarketingSetupPage(),
                new FirebaseSetupPage(),
                // Ghi link social vào GameConstant sau Marketing (Marketing cũng ghi file đó — tuần tự).
                new SocialSetupPage(),
                // Chỉ đọc, không tham gia "chạy hết" (RunAllLabel = null): bảng Ready/Warning/Error
                // cho PM sau khi hai bước trên đã ghi xong.
                new ReadinessPage(),
            };
            _ezgPageCount = ezg.Length;

            // Nhóm "Nhà phát hành": một tab mỗi profile trong registry (Neptune, SayGame, …). Không tham
            // gia "chạy hết" — áp preset của một publisher là quyết định riêng, bấm trong tab của họ.
            var profiles = PublisherRegistry.Profiles;
            _pages = new IEzgKitPage[ezg.Length + profiles.Length];
            ezg.CopyTo(_pages, 0);
            for (var i = 0; i < profiles.Length; i++) _pages[ezg.Length + i] = new PublisherPage(profiles[i]);

            // GUIContent rỗng dựng sẵn một lần, mỗi lượt vẽ chỉ gán lại icon/tooltip (hai thứ đổi
            // theo trạng thái page). Chữ của mục nav thì cố định — gán luôn ở đây để OnGUI không
            // phải nối chuỗi mỗi mục mỗi lượt vẽ. Page Ezg đánh số bước; page nhà phát hành không
            // (chúng không phải bước tuần tự).
            _navContents = new GUIContent[_pages.Length + 1];
            for (var i = 0; i < _navContents.Length; i++) _navContents[i] = new GUIContent();
            _navContents[0].text = OVERVIEW_TITLE;
            for (var i = 1; i < _navContents.Length; i++)
                _navContents[i].text = i <= _ezgPageCount ? $"{i}.  {_pages[i - 1].Title}" : _pages[i - 1].Title;
        }

        /// <summary>Chụp lại trạng thái mọi page. Chỉ ĐỌC — mở cửa sổ không được ghi gì vào project.</summary>
        private void ReloadAll()
        {
            if (_pages == null) BuildPages();
            foreach (var page in _pages) page.Reload();

            // Nối chuỗi đúng một lần ở đây thay vì mỗi lượt vẽ header.
            var version = PlayerSettings.bundleVersion;
            _versionLabel = string.IsNullOrEmpty(version) ? null : "v" + version;

            var active = PublisherRegistry.Find(PublisherState.Load().activePublisher);
            _activePublisherLabel = active == null ? PUBLISHER_NONE : active.DisplayName;
        }

        private void OnGUI()
        {
            if (_pages == null) ReloadAll();

            // _tab đi qua serialize của EditorWindow: kẹp lại trước khi dùng làm index, tránh vỡ cửa sổ
            // nếu số page thay đổi giữa hai phiên.
            _tab = Mathf.Clamp(_tab, 0, _navContents.Length - 1);

            // Header bar nằm NGOÀI mọi scroll view: id dự án không được cuộn mất khi đọc nội dung tab.
            DrawHeaderBar();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawSidebar();
                GUILayout.Space(10);

                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Space(8);
                    DrawContent();
                    GUILayout.Space(6);
                }

                GUILayout.Space(8);
            }

            ApplyPendingTab();

            // Từ đây trở xuống MỌI layout group đã đóng — chỗ duy nhất an toàn cho việc nặng:
            // RunAll() ghi file + AssetDatabase.Refresh() có thể kéo theo recompile / domain reload.
            if (!_runAllRequested) return;
            _runAllRequested = false;
            RunAll();
        }

        #endregion

        #region Header bar

        /// <summary>
        ///     Thanh đỉnh cửa sổ. Dòng dưới là id THẬT sẽ đi vào bản build (PlayerSettings) — Marketing
        ///     ghi nó, Firebase tạo app theo nó, nên nó phải nhìn thấy được ở MỌI tab chứ không nằm
        ///     riêng trong tab Tổng quan.
        /// </summary>
        private void DrawHeaderBar()
        {
            using (new EzgKitStyles.BarScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("EzgKit", EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                    GUILayout.Label("·", EzgKitStyles.MutedLabel, GUILayout.ExpandWidth(false));
                    GUILayout.Label(PlayerSettings.productName, EditorStyles.label,
                        GUILayout.ExpandWidth(false));
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button(_refreshContent, EditorStyles.miniButton, GUILayout.Width(150)))
                    {
                        ReloadAll();
                        _runAllMessage = null;
                        _runAllStatus = EzgStatus.None;
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    MetaId("Android", FirebaseAppProvisioner.AndroidPackage);
                    MetaSeparator();
                    MetaId("iOS", FirebaseAppProvisioner.IosBundle);
                    MetaSeparator();
                    MetaId(null, _versionLabel);
                    MetaSeparator();
                    // Nhà phát hành đang áp preset: dev key AppsFlyer trong build là của ai — phải thấy ở mọi tab.
                    MetaId("Phát hành", _activePublisherLabel);

                    GUILayout.FlexibleSpace();
                }
            }
        }

        /// <summary>Một mẩu id ở header. Rỗng thì tô vàng "— chưa có": thiếu id là còn việc phải làm.</summary>
        private static void MetaId(string label, string value)
        {
            if (!string.IsNullOrEmpty(label))
                GUILayout.Label(label, EzgKitStyles.MutedLabel, GUILayout.ExpandWidth(false));

            if (!string.IsNullOrEmpty(value))
            {
                GUILayout.Label(value, EzgKitStyles.MutedLabel, GUILayout.ExpandWidth(false));
                return;
            }

            var previous = GUI.contentColor;
            GUI.contentColor = EzgKitStyles.WarnColor;
            GUILayout.Label("— chưa có", EzgKitStyles.MutedLabel, GUILayout.ExpandWidth(false));
            GUI.contentColor = previous;
        }

        private static void MetaSeparator() =>
            GUILayout.Label("   ·   ", EzgKitStyles.MutedLabel, GUILayout.ExpandWidth(false));

        #endregion

        #region Sidebar

        /// <summary>
        ///     Cột nav dọc. Cuộn được vì danh sách tool sẽ dài ra, còn cửa sổ có thể bị kéo thấp.
        ///     Mỗi mục page mang icon trạng thái + tooltip là <see cref="IEzgKitPage.Headline" />:
        ///     nhìn cột trái là biết tab nào còn việc mà không phải mở từng tab.
        /// </summary>
        private void DrawSidebar()
        {
            // ExpandHeight: cột phải cao hết cửa sổ, không co lại bằng chiều cao của mấy cái nút.
            using (new EditorGUILayout.VerticalScope(EzgKitStyles.SidebarStyle,
                       GUILayout.Width(EzgKitStyles.SIDEBAR_WIDTH), GUILayout.ExpandHeight(true)))
            {
                // Scope chứ không BeginScrollView/EndScrollView tay: Dispose chạy trong finally nên
                // exception ném từ trong vòng lặp vẫn đóng được scroll view, không để lại layout
                // group mở (Unity sẽ spam "Mismatched LayoutGroup" tới khi đóng mở lại cửa sổ).
                using (var scroll = new EditorGUILayout.ScrollViewScope(_sidebarScroll,
                           GUIStyle.none, GUIStyle.none, GUILayout.ExpandHeight(true)))
                {
                    _sidebarScroll = scroll.scrollPosition;

                    for (var i = 0; i < _navContents.Length; i++)
                    {
                        // Nhãn nhóm: hai nhóm tab là hai việc khác nhau (dựng dự án theo Ezg / đi với
                        // một nhà phát hành) — không có nhãn thì Neptune nhìn như "bước 5" của setup.
                        if (i == 1) SectionLabel(SECTION_EZG);
                        else if (i == 1 + _ezgPageCount) SectionLabel(SECTION_PUBLISHER);

                        // text đã gán sẵn trong BuildPages — ở đây chỉ đổi phần chạy theo trạng thái.
                        var content = _navContents[i];

                        if (i == 0)
                        {
                            // Tổng quan không có trạng thái riêng — icon ở đây chỉ làm nhiễu cột.
                            content.image = null;
                            content.tooltip = string.Empty;
                        }
                        else
                        {
                            var page = _pages[i - 1];
                            content.image = EzgKitStyles.IconOf(page.Status);
                            content.tooltip = page.Headline ?? string.Empty;
                        }

                        // Toggle kiểu nút: tab đang chọn giữ trạng thái nhấn; bấm lại chính nó không
                        // đổi gì.
                        if (GUILayout.Toggle(_tab == i, content, EzgKitStyles.NavItemStyle) && _tab != i)
                            Select(i);
                    }
                }

                EzgKitStyles.Divider(2f);
                EditorGUILayout.LabelField("Mở cửa sổ chỉ đọc, không ghi gì vào project.",
                    EzgKitStyles.Hint);
            }
        }

        private static void SectionLabel(string text)
        {
            GUILayout.Space(8f);
            EditorGUILayout.LabelField(text, EzgKitStyles.Hint);
        }

        /// <summary>
        ///     Ghi nhận yêu cầu đổi tab, KHÔNG đổi ngay: đổi giữa lượt vẽ là nội dung không còn khớp
        ///     layout mà lượt Layout trước đó đã dựng (Unity bắn "Mismatched LayoutGroup").
        /// </summary>
        private void Select(int tab)
        {
            if (tab != _tab) _pendingTab = tab;
        }

        /// <summary>Áp dụng đổi tab sau khi vẽ xong. Đổi tab là chụp lại: page trước vừa ghi
        /// PlayerSettings thì số bên tab mới phải là số mới.</summary>
        private void ApplyPendingTab()
        {
            if (_pendingTab < 0) return;

            _tab = Mathf.Clamp(_pendingTab, 0, _navContents.Length - 1);
            _pendingTab = -1;

            if (_tab == 0) ReloadAll();
            else _pages[_tab - 1].Reload();

            GUI.FocusControl(null);
            Repaint();
        }

        #endregion

        #region Content

        private void DrawContent()
        {
            if (_tab == 0)
            {
                DrawPageHeader(OVERVIEW_TITLE, OVERVIEW_SUBTITLE, EzgStatus.None, null);
                DrawOverview();
                return;
            }

            var page = _pages[_tab - 1];
            DrawPageHeader(page.Title, page.Subtitle, page.Status, page.Headline);
            page.Draw();
        }

        /// <summary>
        ///     Phần đầu trang, do CỬA SỔ vẽ cho mọi tab (page không tự vẽ lại) — nhờ vậy mọi tab có
        ///     đúng một kiểu tiêu đề, và chip trạng thái luôn ở cùng một chỗ để mắt bắt được ngay.
        /// </summary>
        private static void DrawPageHeader(string title, string subtitle, EzgStatus status,
            string headline)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(title, EzgKitStyles.PageTitle);
                    if (!string.IsNullOrEmpty(subtitle))
                        EditorGUILayout.LabelField(subtitle, EzgKitStyles.Hint);
                }

                GUILayout.FlexibleSpace();
                StatusPill(status, headline);
            }

            EzgKitStyles.Divider();
        }

        /// <summary>
        ///     Chip trạng thái của trang. Headline có thể dài cả câu, mà chip dài thì đẩy hết tiêu đề
        ///     sang trái và tự nó cũng khó đọc — cắt còn <see cref="HEADLINE_MAX" /> ký tự, câu đầy đủ
        ///     giữ trong tooltip của chip.
        /// </summary>
        private static void StatusPill(EzgStatus status, string headline)
        {
            if (string.IsNullOrEmpty(headline)) return;

            if (headline.Length <= HEADLINE_MAX)
            {
                EzgKitStyles.Pill(status, headline);
                return;
            }

            EzgKitStyles.Pill(status, headline.Substring(0, HEADLINE_MAX - 1).TrimEnd() + "…", headline);
        }

        #endregion

        #region Overview

        /// <summary>
        ///     Tab Tổng quan = CHECKLIST theo bước, không phải bài đọc: một câu về thứ tự bắt buộc,
        ///     rồi từng thẻ bước với trạng thái + nút mở tab, cuối cùng là nút chạy hết. Phần giải
        ///     thích dài nằm trong khối gấp lại — cần cho lần đầu, vướng đường từ lần thứ hai.
        /// </summary>
        private void DrawOverview()
        {
            // Scope chứ không BeginScrollView/EndScrollView tay: page con hay thẻ bước ném exception
            // thì Dispose trong finally vẫn đóng scroll view, cửa sổ không kẹt ở layout group mở.
            using (var scroll = new EditorGUILayout.ScrollViewScope(_overviewScroll))
            {
                _overviewScroll = scroll.scrollPosition;

                EzgKitStyles.Banner(
                    "Chạy Marketing trước rồi mới tới Firebase: Marketing ghi package name / bundle id "
                    + "vào PlayerSettings, Firebase tạo app theo đúng hai id đó và KHÔNG cho sửa lại "
                    + "sau khi tạo.",
                    AnyPending() ? EzgStatus.Warn : EzgStatus.Ok);

                EzgKitStyles.CollapsibleHelp("overview-order", "Vì sao phải chạy đúng thứ tự?",
                    "1. Marketing — tải Google Sheet rồi ghi package name, bundle id, key SDK vào "
                    + "PlayerSettings / AdsConfig / AppLovinSettings / FacebookSettings / AndroidManifest.\n"
                    + "2. Firebase — tạo app Android + iOS theo ĐÚNG id vừa ghi ở bước 1, rồi tải "
                    + "google-services.json / GoogleService-Info.plist về Assets/.\n\n"
                    + "Chạy ngược thứ tự là app Firebase mang id cũ, mà Firebase KHÔNG cho sửa "
                    + "packageName / bundleId sau khi tạo.");

                DrawStepCards();
                DrawRunAll();
                DrawPublisherCards();
            }
        }

        /// <summary>Mỗi page Ezg một thẻ bước: số bước tô theo trạng thái, headline, nút mở tab.</summary>
        private void DrawStepCards()
        {
            EzgKitStyles.SectionHeader("Setup Ezg — trạng thái từng bước");

            for (var i = 0; i < _ezgPageCount; i++)
            {
                var page = _pages[i];

                using (new EzgKitStyles.CardScope())
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        // Badge + chip đều lấy màu từ trạng thái: bước còn việc nổi lên nhờ MÀU của
                        // dấu hiệu, không phải nhờ tô nền cả thẻ (tô nền là mất chỗ nhấn mạnh khác).
                        EzgKitStyles.StepBadge(i + 1, page.Status);
                        GUILayout.Label(page.Title, EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                        GUILayout.FlexibleSpace();
                        EzgKitStyles.Pill(page.Status, ShortLabel(page.Status));
                    }

                    EditorGUILayout.LabelField(page.Headline, EditorStyles.wordWrappedLabel);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (EzgKitStyles.SecondaryButton("Mở tab", GUILayout.Width(110),
                                GUILayout.Height(24f)))
                            Select(i + 1);

                        GUILayout.FlexibleSpace();
                    }
                }
            }
        }

        /// <summary>
        ///     Mỗi nhà phát hành một thẻ: tiến độ checklist + nút mở tab. Không có số bước — đi với
        ///     publisher nào là chọn, không phải tuần tự; và không có nút chạy hết cho nhóm này.
        /// </summary>
        private void DrawPublisherCards()
        {
            if (_pages.Length <= _ezgPageCount) return;

            EzgKitStyles.SectionHeader("Nhà phát hành",
                $"Bộ SDK đang áp: {_activePublisherLabel}. Mỗi publisher là một bộ SDK trọn gói — bấm \"Chuyển sang\" trong tab của họ để cài SDK họ cần, gỡ SDK thừa, ghi ID.");

            for (var i = _ezgPageCount; i < _pages.Length; i++)
            {
                var page = _pages[i];

                using (new EzgKitStyles.CardScope())
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(page.Title, EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                        GUILayout.FlexibleSpace();
                        EzgKitStyles.Pill(page.Status, page.Status == EzgStatus.None ? "Chưa có tài liệu" : ShortLabel(page.Status));
                    }

                    EditorGUILayout.LabelField(page.Headline, EditorStyles.wordWrappedLabel);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (EzgKitStyles.SecondaryButton("Mở tab", GUILayout.Width(110), GUILayout.Height(24f)))
                            Select(i + 1);

                        GUILayout.FlexibleSpace();
                    }
                }
            }
        }

        private void DrawRunAll()
        {
            EzgKitStyles.SectionHeader("Chạy hết (Setup Ezg)");

            using (new EzgKitStyles.CardScope())
            {
                _runAllLabels.Clear();
                foreach (var page in _pages)
                {
                    if (page.RunAllLabel == null) continue;
                    _runAllLabels.Add(page.RunAllLabel);
                }

                EzgKitStyles.Bullets(_runAllLabels, "Không có bước nào tham gia luồng chạy hết.");
                EditorGUILayout.Space(4);

                // Chỉ ĐẶT CỜ, không chạy tại chỗ: nút này nằm trong CardScope + scroll view của tab
                // Tổng quan, mà RunAll() ghi file rồi Refresh() → recompile → domain reload giữa
                // chừng OnGUI. OnGUI chạy nó sau khi mọi layout group đã đóng.
                if (EzgKitStyles.PrimaryButton("Setup toàn bộ dự án  (Marketing → Firebase)"))
                    _runAllRequested = true;
            }

            if (string.IsNullOrEmpty(_runAllMessage)) return;

            EzgKitStyles.Banner(_runAllMessage, _runAllStatus);
        }

        /// <summary>
        ///     Chạy lần lượt từng page. Dừng ngay ở page đầu tiên báo false — bước sau ăn dữ liệu của
        ///     bước trước, chạy tiếp trên dữ liệu lỗi thì tạo ra app Firebase sai id không xoá được.
        ///     <para>
        ///         Gọi ở CUỐI <see cref="OnGUI" /> qua cờ <see cref="_runAllRequested" />, không gọi
        ///         thẳng từ nút. Exception (tải web hỏng, IOException lúc ghi file, …) bị bắt hết tại
        ///         đây và biến thành <see cref="_runAllMessage" />: để nó thoát ra khỏi OnGUI là mất
        ///         phần vẽ còn lại của lượt đó.
        ///     </para>
        ///     <para>
        ///         <see cref="Select" /> ở đây chỉ ghi <see cref="_pendingTab" />, mà
        ///         <see cref="ApplyPendingTab" /> thì đã chạy xong trước đó — nên tab thật sự đổi ở
        ///         lượt OnGUI kế tiếp. <see cref="Repaint" /> ở cuối để lượt đó xảy ra ngay, không
        ///         phải đợi user rê chuột vào cửa sổ.
        ///     </para>
        /// </summary>
        private void RunAll()
        {
            if (!EditorUtility.DisplayDialog("EzgKit - setup toan bo",
                    "Se ghi vao PlayerSettings, cac file settings cua SDK, va tao app tren project "
                    + "Firebase dang khai.\n\nTung buoc van hoi lai truoc khi lam viec khong hoan tac "
                    + "duoc.", "Chay", "Huy"))
                return;

            try
            {
                for (var i = 0; i < _pages.Length; i++)
                {
                    var page = _pages[i];
                    if (page.RunAllLabel == null) continue;

                    if (page.RunAll()) continue;

                    // Lỗi của page nằm trong GUI của chính nó (report / HelpBox) — nhảy sang tab đó.
                    Select(i + 1);
                    _runAllMessage = $"Dừng ở bước \"{page.Title}\" — xem chi tiết trong tab vừa mở.";
                    _runAllStatus = EzgStatus.Error;
                    return;
                }

                ReloadAll();
                _runAllMessage = "Đã chạy hết. Xem lại trạng thái từng bước ở trên.";
                _runAllStatus = EzgStatus.Ok;
            }
            catch (Exception exception)
            {
                // Banner chỉ đủ chỗ cho một câu; stack trace vẫn phải vào Console cho dev lần ra.
                Debug.LogException(exception);
                _runAllMessage = $"Lỗi khi chạy: {exception.Message}";
                _runAllStatus = EzgStatus.Error;
            }
            finally
            {
                Repaint();
            }
        }

        #endregion

        #region Helpers

        /// <summary>Còn page nào phải làm tiếp không — quyết định màu banner ở tab Tổng quan.</summary>
        private bool AnyPending()
        {
            foreach (var page in _pages)
                if (page.Status is EzgStatus.Warn or EzgStatus.Error)
                    return true;

            return false;
        }

        /// <summary>Chữ trong chip của thẻ bước. Ngắn, vì chi tiết đã nằm ngay dòng headline bên dưới.</summary>
        private static string ShortLabel(EzgStatus status) =>
            status switch
            {
                EzgStatus.Ok => "Đã xong",
                EzgStatus.Warn => "Còn việc",
                EzgStatus.Error => "Lỗi",
                _ => "Chưa rõ",
            };

        #endregion
    }
}
#endif
