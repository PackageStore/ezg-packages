#if UNITY_EDITOR
using System.Collections.Generic;
using Ezg.Editor.Shared.EzgKit;
using UnityEditor;
using UnityEngine;

namespace Ezg.Editor.Shared.Marketing
{
    /// <summary>
    ///     Bảng điều khiển marketing/ads trong cửa sổ setup tổng: một nút gen-all, và toàn bộ thông số
    ///     setup của dự án hiện ra ngay trong tab để PM tự soi mà không cần mở Console hay đi hỏi dev.
    ///     <para>
    ///         Page dựng bảng từ <see cref="MarketingConfigApplier.Collect" /> (dữ liệu có cấu trúc),
    ///         KHÔNG parse lại text báo cáo — thêm sink mới trong applier là bảng tự có thêm dòng.
    ///     </para>
    ///     <para>
    ///         <b>Thứ tự bố cục = thứ tự việc phải làm:</b> nguồn dữ liệu + nút chạy nằm ngoài scroll
    ///         (luôn với tới được), rồi tới bảng đối chiếu, rồi việc còn phải làm tay, cuối cùng mới tới
    ///         khối chỉ-đọc "Tham khảo" — khối này GẤP LẠI mặc định vì nó dài gấp nhiều lần phần việc.
    ///     </para>
    ///     <para>
    ///         Bộ lọc <see cref="_onlyPending" /> ("chỉ hiện ô đang lệch") là thứ quan trọng nhất của
    ///         trang: lệch 3 ô trong 60 ô thì phải tìm ra 3 ô đó trong một cái nhìn, không phải cuộn.
    ///     </para>
    /// </summary>
    internal class MarketingSetupPage : IEzgKitPage
    {
        #region Fields

        /// <summary>Số dòng trong khối "Nhận dạng app" — chỉ để ghi lên tiêu đề foldout.</summary>
        private const int IDENTITY_ROW_COUNT = 10;

        /// <summary>Số dòng trong khối dashboard MAX (6 MAX + 6 Admob + 6 Unity Ads + 4 Facebook).</summary>
        private const int DASHBOARD_ROW_COUNT = 22;

        private const string HELP_SOURCE =
            "Link sheet: mở Google Sheet marketing của dự án rồi copy nguyên URL trên thanh địa chỉ. "
            + "Sheet phải để Share > Anyone with the link (Viewer), nếu không tool tải về sẽ ra trang "
            + "đăng nhập chứ không ra dữ liệu.\n\n"
            + "Prefix cột dự án: tên cột ở hàng tiêu đề của sheet, ví dụ I001 cho cặp cột I001a (Android) "
            + "và I001i (iOS). Để trống thì tool tự dò: ưu tiên cột có chữ \"android\"/\"ios\", sau đó tới "
            + "cột đặt tên theo dạng chữ + 3 số + a/i.";

        private string _sheetUrl;
        private string _projectPrefix;
        private Vector2 _scroll;
        private MarketingConfigApplier.Status _status;
        private string _message;
        private bool _messageIsError;

        /// <summary>Lọc bảng xuống còn đúng những ô đang lệch sheet.</summary>
        private bool _onlyPending;

        /// <summary>
        ///     Sink nào đang mở, CHỈ ghi khi người dùng tự bấm. Sink chưa có trong dict thì mở/gấp theo
        ///     trạng thái (lệch = mở sẵn, khớp hết = gấp) — mặc định "mở hết" cũ làm trang dài vô ích.
        /// </summary>
        private readonly Dictionary<string, bool> _expanded = new();

        /// <summary>Các khối chỉ-đọc ở mục "Tham khảo" — gấp lại mặc định.</summary>
        private bool _identityOpen;
        private bool _dashboardOpen;
        private bool _skippedOpen;

        /// <summary>
        ///     <see cref="MarketingConfigApplier.Status.Rows" /> gom sẵn theo sink, giữ nguyên thứ tự
        ///     xuất hiện. Gom trước khi vẽ để vòng lặp vẽ không phải tự lần ranh giới nhóm.
        /// </summary>
        private readonly List<SinkGroup> _groups = new();

        /// <summary>Một sink (AdsConfig, AppLovinSettings, …) cùng các ô đã đối chiếu của nó.</summary>
        private sealed class SinkGroup
        {
            internal string Name;

            /// <summary>
            ///     Khoá <see cref="_expanded" /> — DUY NHẤT theo nhóm, khác <see cref="Name" /> ở chỗ
            ///     nhóm trùng tên được gắn thêm chỉ số. Tiêu đề vẫn vẽ bằng <see cref="Name" />.
            /// </summary>
            internal string Key;

            internal readonly List<MarketingConfigApplier.Change> Rows = new();

            /// <summary>Số ô đang lệch trong nhóm này.</summary>
            internal int Pending;
        }

        #endregion

        #region Page

        public string Title => "Marketing";

        public string Subtitle =>
            "Tải bảng thông số từ Google Sheet rồi ghi vào PlayerSettings và các file settings của SDK.";

        public string RunAllLabel => "Tai sheet marketing + ghi vao project";

        public string Headline
        {
            get
            {
                if (_status?.Config == null) return "Chưa có dữ liệu — cần dán link sheet.";

                var todos = _status.Todos.Count;
                var pending = _status.PendingCount;

                if (_status.Errors.Count > 0)
                    return $"{_status.Errors.Count} lỗi, {pending} ô lệch, {todos} việc tay.";

                return pending > 0
                    ? $"{pending}/{_status.Rows.Count} ô lệch sheet, {todos} việc tay."
                    : $"Khớp sheet ({_status.Rows.Count} ô), {todos} việc tay.";
            }
        }

        /// <summary>
        ///     Chưa có dữ liệu là <see cref="EzgStatus.Warn" /> chứ không phải lỗi — chỉ là chưa dán link
        ///     sheet. Xét nó TRƯỚC vì lúc đó <c>Collect</c> luôn kèm một dòng lỗi "khong doc duoc json",
        ///     xét lỗi trước thì nhánh này không bao giờ chạy. Sau đó mới tới Error > Warn > Ok.
        /// </summary>
        public EzgStatus Status
        {
            get
            {
                if (_status?.Config == null) return EzgStatus.Warn;
                if (_status.Errors.Count > 0) return EzgStatus.Error;
                if (_status.PendingCount > 0) return EzgStatus.Warn;
                return EzgStatus.Ok;
            }
        }

        /// <summary>Đọc lại URL + chụp lại trạng thái. Luôn chạy dry-run — mở cửa sổ không được ghi gì.</summary>
        public void Reload()
        {
            _sheetUrl = MarketingSheetFetcher.CurrentSheetUrl;
            _projectPrefix = MarketingSheetFetcher.CurrentProjectPrefix;
            _status = MarketingConfigApplier.Collect(true);
            BuildGroups();
        }

        /// <summary>
        ///     Chỉ vẽ phần THÂN — tiêu đề, <see cref="Subtitle" /> và chip <see cref="Status" /> +
        ///     <see cref="Headline" /> do <c>EzgKitWindow</c> vẽ.
        /// </summary>
        public void Draw()
        {
            if (_status == null) Reload();

            // Ngoài scroll: nguồn dữ liệu, nút chạy và dải tóm tắt phải luôn với tới được.
            DrawSource();
            DrawActions();

            if (!string.IsNullOrEmpty(_message))
                EzgKitStyles.Banner(_message, _messageIsError ? EzgStatus.Error : EzgStatus.Ok);

            if (_status.Config == null)
            {
                EzgKitStyles.Banner(
                    "Chưa có dữ liệu. Dán link sheet ở trên rồi bấm \"Tải sheet + Setup\".",
                    EzgStatus.Warn);
                return;
            }

            DrawSummary();

            using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scroll.scrollPosition;

                DrawSinks();
                DrawTodos();
                DrawErrors();
                DrawReference();
            }
        }

        public bool RunAll() => SetupAll();

        #endregion

        #region Header

        private void DrawSource()
        {
            using (new EzgKitStyles.CardScope("Nguồn dữ liệu"))
            using (new EzgKitStyles.LabelWidthScope())
            {
                _sheetUrl = EditorGUILayout.TextField("Google Sheet URL", _sheetUrl);
                _projectPrefix = EditorGUILayout.TextField("Prefix cột dự án", _projectPrefix);
            }

            // Hướng dẫn nằm NGOÀI card ở trên để không lồng card trong card; gấp lại mặc định vì từ lần
            // thứ hai trở đi nó chỉ đẩy phần việc thật xuống dưới màn hình.
            EzgKitStyles.CollapsibleHelp("marketing-source", "Link sheet lấy ở đâu, prefix là gì?",
                HELP_SOURCE);
        }

        private void DrawActions()
        {
            // Cố ý chỉ hai nút: một nút chạy, một nút xem lại. Các biến thể (chỉ tải sheet, áp lại từ
            // JSON cũ) là việc của dev khi debug — để ở menu Ezg/Marketing, không bày ra đây.
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_sheetUrl)))
                {
                    if (EzgKitStyles.PrimaryButton("Tải sheet + Setup  (1 click)")) SetupAll();
                }

                if (EzgKitStyles.SecondaryButton("Làm mới", GUILayout.Width(110),
                        GUILayout.Height(EzgKitStyles.BUTTON_HEIGHT)))
                {
                    Reload();
                    _message = null;
                }
            }

            EditorGUILayout.Space(4);
        }

        /// <summary>
        ///     Dải tóm tắt: ba con số cần biết + công tắc lọc. Đây là chỗ trả lời "còn phải làm gì nữa
        ///     không" trong một cái liếc, không phải chỗ giải thích.
        /// </summary>
        private void DrawSummary()
        {
            var total = _status.Rows.Count;
            var pending = _status.PendingCount;
            var todos = _status.Todos.Count;

            using (new EditorGUILayout.HorizontalScope())
            {
                EzgKitStyles.Pill(pending == 0 ? EzgStatus.Ok : EzgStatus.Warn,
                    $"{total - pending}/{total} ô khớp sheet");
                EzgKitStyles.Pill(todos == 0 ? EzgStatus.Ok : EzgStatus.Warn, $"{todos} việc làm tay");

                if (_status.Errors.Count > 0)
                    EzgKitStyles.Pill(EzgStatus.Error, $"{_status.Errors.Count} lỗi");

                GUILayout.FlexibleSpace();

                _onlyPending = GUILayout.Toggle(_onlyPending, "Chỉ hiện ô đang lệch",
                    EditorStyles.miniButton, GUILayout.Width(150f), GUILayout.Height(18f));
            }

            EditorGUILayout.Space(2);
        }

        #endregion

        #region Body

        /// <summary>
        ///     Từng sink một, mỗi sink một card. Sink còn ô lệch thì mở sẵn, sink khớp hết thì gấp —
        ///     người dùng chỉ phải nhìn vào chỗ có việc.
        /// </summary>
        private void DrawSinks()
        {
            EzgKitStyles.SectionHeader("Đã ghi vào đâu trong project");

            var drewAny = false;

            foreach (var group in _groups)
            {
                if (_onlyPending && group.Pending == 0) continue;
                drewAny = true;

                var status = group.Pending > 0 ? EzgStatus.Warn : EzgStatus.Ok;
                var trailing = group.Pending > 0
                    ? $"{group.Pending} ô lệch"
                    : $"khớp ({group.Rows.Count} ô)";

                // Chưa có trong dict = người dùng chưa động vào -> mở/gấp theo trạng thái sink.
                // Khoá là Key chứ không phải Name: hai nhóm cùng tên phải mở/gấp độc lập.
                var open = _expanded.TryGetValue(group.Key, out var chosen) ? chosen : group.Pending > 0;

                using (new EzgKitStyles.CardScope())
                {
                    var next = EzgKitStyles.CardFoldout(open, group.Name, status, trailing);
                    if (next != open) _expanded[group.Key] = next;
                    if (!next) continue;

                    EzgKitStyles.Divider(2f);

                    foreach (var row in group.Rows)
                    {
                        if (!row.Matched)
                        {
                            EzgKitStyles.DiffRow(row.Field, row.OldValue, row.NewValue);
                            continue;
                        }

                        if (_onlyPending) continue;
                        EzgKitStyles.KeyValue(row.Field, row.Current, EzgStatus.Ok);
                    }
                }
            }

            if (drewAny) return;

            EditorGUILayout.LabelField(
                _onlyPending
                    ? "Không có ô nào lệch — tắt bộ lọc để xem toàn bộ bảng."
                    : "Chưa đối chiếu được ô nào.",
                EzgKitStyles.Hint);
        }

        /// <summary>Việc còn phải làm tay — nằm TRÊN khối tham khảo vì đây là việc, không phải thông tin.</summary>
        private void DrawTodos()
        {
            EzgKitStyles.SectionHeader("Còn phải làm tay");

            using (new EzgKitStyles.CardScope())
            {
                EzgKitStyles.Bullets(_status.Todos, "Không còn việc nào.");
            }
        }

        private void DrawErrors()
        {
            if (_status.Errors.Count == 0) return;

            using (new EzgKitStyles.CardScope("Lỗi", EzgStatus.Error))
            {
                EzgKitStyles.Bullets(_status.Errors);
            }
        }

        /// <summary>Vùng CHỈ ĐỌC. Gấp lại hết mặc định — có chữ đếm ở tiêu đề để biết bên trong có gì.</summary>
        private void DrawReference()
        {
            EzgKitStyles.SectionHeader("Tham khảo", "Giá trị theo sheet, chỉ để đối chiếu.");

            DrawIdentity();
            DrawDashboardOnly();
            DrawSkipped();
        }

        /// <summary>Khối nhận dạng app — thứ PM hỏi nhiều nhất, nhưng vẫn là số để tra chứ không phải việc.</summary>
        private void DrawIdentity()
        {
            var cfg = _status.Config;

            using (new EzgKitStyles.CardScope())
            {
                _identityOpen = EzgKitStyles.CardFoldout(_identityOpen, "Nhận dạng app (số theo sheet)",
                    EzgStatus.None, $"{IDENTITY_ROW_COUNT} dòng");
                if (!_identityOpen) return;

                EzgKitStyles.Divider(2f);

                // Package name cũng hiện ở thanh header của cửa sổ, nhưng ở ĐÓ là số đang nằm trong
                // PlayerSettings còn ở ĐÂY là số sheet ghi — hai thứ lệch nhau chính là thứ cần thấy.
                EzgKitStyles.KeyValue("Package name", cfg.packageName, EzgStatus.None,
                    "Số theo sheet. Header cửa sổ hiện số đang đặt trong PlayerSettings.");
                EzgKitStyles.KeyValue("Tên hiển thị", cfg.gameName);
                EzgKitStyles.KeyValue("Apple ID", cfg.appleId, EzgStatus.None,
                    emptyHint: "chưa có — chưa tạo app trên App Store Connect");
                EzgKitStyles.KeyValue("AppsFlyer dev key", cfg.appsflyerDevKey);
                EzgKitStyles.KeyValue("MAX SDK key", cfg.max.sdkKey);
                EzgKitStyles.KeyValue("Facebook app ID", cfg.facebook.appId);
                EzgKitStyles.KeyValue("Link Google Play", cfg.GooglePlayUrl);
                EzgKitStyles.KeyValue("Link App Store", cfg.AppStoreUrl, EzgStatus.None,
                    emptyHint: "chưa có — cần Apple ID");
                EzgKitStyles.KeyValue("Privacy policy", cfg.applovin.privacyPolicyUrl);
                EzgKitStyles.KeyValue("Terms of service", cfg.applovin.termsOfServiceUrl);
            }
        }

        /// <summary>
        ///     Số khai bên dashboard AppLovin MAX chứ app không nhúng. PM cần thấy để đối chiếu với
        ///     dashboard, nên vẫn hiện — nhưng gấp lại, vì 22 dòng chỉ-đọc luôn mở thì chiếm hết trang.
        /// </summary>
        private void DrawDashboardOnly()
        {
            var cfg = _status.Config;

            using (new EzgKitStyles.CardScope())
            {
                _dashboardOpen = EzgKitStyles.CardFoldout(_dashboardOpen,
                    "Khai bên dashboard MAX (app không nhúng)", EzgStatus.None,
                    $"{DASHBOARD_ROW_COUNT} dòng");
                if (!_dashboardOpen) return;

                EzgKitStyles.Divider(2f);

                // Bốn mạng quảng cáo tách bằng Divider trong CÙNG một card — không lồng card.
                EditorGUILayout.LabelField("Ad unit MAX", EzgKitStyles.SectionTitle);
                EzgKitStyles.KeyValue("Rewarded Android", cfg.max.android.rewarded);
                EzgKitStyles.KeyValue("Rewarded iOS", cfg.max.ios.rewarded);
                EzgKitStyles.KeyValue("Interstitial Android", cfg.max.android.interstitial);
                EzgKitStyles.KeyValue("Interstitial iOS", cfg.max.ios.interstitial);
                EzgKitStyles.KeyValue("Banner Android", cfg.max.android.banner, EzgStatus.None,
                    emptyHint: "sheet không có");
                EzgKitStyles.KeyValue("Banner iOS", cfg.max.ios.banner, EzgStatus.None,
                    emptyHint: "sheet không có");

                EzgKitStyles.Divider();
                EditorGUILayout.LabelField("Admob", EzgKitStyles.SectionTitle);
                EzgKitStyles.KeyValue("App ID Android", cfg.admob.android.appId);
                EzgKitStyles.KeyValue("App ID iOS", cfg.admob.ios.appId);
                EzgKitStyles.KeyValue("Rewarded Android", cfg.admob.android.rewarded);
                EzgKitStyles.KeyValue("Rewarded iOS", cfg.admob.ios.rewarded);
                EzgKitStyles.KeyValue("Interstitial Android", cfg.admob.android.interstitial);
                EzgKitStyles.KeyValue("Interstitial iOS", cfg.admob.ios.interstitial);

                EzgKitStyles.Divider();
                EditorGUILayout.LabelField("Unity Ads", EzgKitStyles.SectionTitle);
                EzgKitStyles.KeyValue("Game ID Android", cfg.unityAds.android.gameId);
                EzgKitStyles.KeyValue("Game ID iOS", cfg.unityAds.ios.gameId);
                EzgKitStyles.KeyValue("Rewarded Android", cfg.unityAds.android.rewarded);
                EzgKitStyles.KeyValue("Rewarded iOS", cfg.unityAds.ios.rewarded);
                EzgKitStyles.KeyValue("Interstitial Android", cfg.unityAds.android.interstitial);
                EzgKitStyles.KeyValue("Interstitial iOS", cfg.unityAds.ios.interstitial);

                EzgKitStyles.Divider();
                EditorGUILayout.LabelField("Facebook Audience Network", EzgKitStyles.SectionTitle);
                EzgKitStyles.KeyValue("Rewarded Android", cfg.facebook.android.rewarded);
                EzgKitStyles.KeyValue("Rewarded iOS", cfg.facebook.ios.rewarded);
                EzgKitStyles.KeyValue("Interstitial Android", cfg.facebook.android.interstitial);
                EzgKitStyles.KeyValue("Interstitial iOS", cfg.facebook.ios.interstitial);
            }
        }

        /// <summary>Sink không có trong dự án này. Không phải lỗi nên không tô màu cảnh báo.</summary>
        private void DrawSkipped()
        {
            if (_status.Skipped.Count == 0) return;

            using (new EzgKitStyles.CardScope())
            {
                _skippedOpen = EzgKitStyles.CardFoldout(_skippedOpen,
                    "Sink không có trong dự án này (bỏ qua, không phải lỗi)", EzgStatus.None,
                    $"{_status.Skipped.Count} sink");
                if (!_skippedOpen) return;

                EzgKitStyles.Divider(2f);
                EzgKitStyles.Bullets(_status.Skipped);
            }
        }

        #endregion

        #region Actions

        private void SaveSource() =>
            MarketingSheetFetcher.SaveSheetUrl((_sheetUrl ?? "").Trim(), (_projectPrefix ?? "").Trim());

        private bool SetupAll()
        {
            SaveSource();
            if (!MarketingSheetFetcher.Fetch(out var fetchReport))
            {
                _message = fetchReport;
                _messageIsError = true;
                return false;
            }

            _status = MarketingConfigApplier.Collect(false);
            BuildGroups();
            _message = fetchReport + $"Đã ghi vào project. Còn {_status.Todos.Count} việc làm tay.";
            _messageIsError = false;
            return true;
        }

        #endregion

        #region Helpers

        /// <summary>
        ///     Gom <c>Rows</c> theo sink, giữ nguyên thứ tự applier trả về (sink xuất hiện lại sau một
        ///     sink khác thì thành nhóm mới — đúng như bảng báo cáo text). Chỉ chạy khi
        ///     <see cref="_status" /> đổi, không chạy mỗi lượt vẽ.
        /// </summary>
        private void BuildGroups()
        {
            _groups.Clear();
            if (_status?.Rows == null) return;

            // Tên sink có thể lặp lại thành nhiều nhóm rời nhau, nên Name KHÔNG dùng làm khoá foldout
            // được (hai nhóm sẽ chung một ô trong _expanded). Nhóm đầu tiên mang tên đó giữ nguyên tên
            // làm khoá, các nhóm trùng sau gắn thêm chỉ số nhóm cho khác nhau.
            var seenNames = new HashSet<string>();

            SinkGroup current = null;
            foreach (var row in _status.Rows)
            {
                if (current == null || current.Name != row.Sink)
                {
                    current = new SinkGroup
                    {
                        Name = row.Sink,
                        Key = seenNames.Add(row.Sink) ? row.Sink : $"{row.Sink}#{_groups.Count}"
                    };
                    _groups.Add(current);
                }

                current.Rows.Add(row);
                if (!row.Matched) current.Pending++;
            }
        }

        #endregion
    }
}
#endif
