#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Ezg.Editor.Shared.EzgKit;
using UnityEditor;
using UnityEngine;

namespace Ezg.Editor.Shared.Firebase
{
    /// <summary>
    ///     Tab Firebase của cửa sổ setup (<see cref="EzgKitWindow" /> vẽ tiêu đề + <see cref="Subtitle" />
    ///     + chip <see cref="Status" />; page chỉ vẽ phần thân).
    ///     <para>
    ///         <b>Vùng cố định, nằm NGOÀI scroll</b> — cảnh báo và việc phải làm không được cuộn mất:
    ///     </para>
    ///     <list type="number">
    ///         <item>Banner đỏ khi config trong <c>Assets/</c> trỏ sang DỰ ÁN KHÁC.</item>
    ///         <item>Hàng nút hành động (Lưu / Dry Run / Tạo app), đặt trên đầu chứ không chôn giữa trang.</item>
    ///         <item>Đoạn giải thích chung, gấp lại mặc định.</item>
    ///     </list>
    ///     <para>
    ///         <b>Vùng cuộn</b> giữ nguyên ba nhóm đánh số, vì đó là BA loại thông tin phải xử lý khác nhau:
    ///     </para>
    ///     <list type="number">
    ///         <item>
    ///             <b>Chọn file service account</b> — CHỈ một thứ bắt buộc: file key .json. Project id
    ///             tự lấy từ <c>project_id</c> trong chính file đó (<see cref="AdoptKeyProjectId" />),
    ///             ô project id chỉ còn để sửa cho ca cross-project — mà ca đó cũng có nút
    ///             <see cref="ProbeProjects" /> dò sẵn danh sách nên vẫn không phải gõ. Apple ID và
    ///             mật khẩu keystore là tuỳ chọn thật: không suy ra được từ file key, bỏ trống vẫn chạy.
    ///         </item>
    ///         <item>
    ///             <b>Tool tự lấy</b> — đọc từ PlayerSettings / file config / file key. Tách thành từng
    ///             card có tiêu đề + trạng thái, chỉ hiện để soi chứ không cho sửa: sửa ở đây là lệch với
    ///             nguồn thật.
    ///         </item>
    ///         <item>
    ///             <b>Không có API</b> — vĩnh viễn phải làm tay ngoài Unity (APNs key, app record trên
    ///             App Store Connect / Play Console). Bày ra kèm link để "bấm 1 nút" không bị hiểu nhầm
    ///             là xong 100%.
    ///         </item>
    ///     </list>
    ///     <para>
    ///         Cuối trang là ghi chú roadmap (gấp lại mặc định — không phải việc của người đang dựng dự
    ///         án) và kết quả lần chạy gần nhất.
    ///     </para>
    ///     <para>
    ///         <b>Cơ chế snapshot — đường vẽ TUYỆT ĐỐI không đọc đĩa.</b> Cửa sổ đọc <see cref="Status" />
    ///         và <see cref="Headline" /> mỗi lượt OnGUI, mà mỗi property của
    ///         <see cref="FirebaseAppProvisioner" /> lại là một lượt <c>File.ReadAllText</c> +
    ///         <c>Regex.Match</c> không cache. Gọi thẳng trong lúc vẽ có hai cái giá: (1) hơn 20 lần đọc
    ///         file mỗi lượt vẽ, mà nav item có <c>hover.background</c> nên cửa sổ repaint theo chuyển
    ///         động chuột — nhích chuột một cái là ~70 lần đọc file; (2) nguy hiểm hơn, SỐ WIDGET vẽ ra
    ///         (banner đỏ có/không, icon trong <c>CardHeader</c>) phụ thuộc dữ liệu trên ĐĨA, mà đĩa có
    ///         thể đổi giữa lượt Layout và lượt Repaint ⇒ <c>ArgumentException: Getting control N's
    ///         position in a group with only M controls</c>.
    ///     </para>
    ///     <para>
    ///         Nên toàn bộ trạng thái đọc-từ-ngoài được chụp MỘT lần vào field trong
    ///         <see cref="RefreshSnapshot" /> (gọi ở cuối <see cref="Reload" />, trong <see cref="Save" />,
    ///         và ở đầu <see cref="Draw" /> khi <see cref="_snapshotDirty" /> — đầu Draw chứ không phải
    ///         giữa Draw, để lượt Layout và lượt Repaint của cùng một chu kỳ luôn thấy cùng một snapshot).
    ///     </para>
    ///     <para>
    ///         Cùng cơ chế đó cho hai thứ khác cũng đổi SỐ WIDGET: đọc lại file key khi user chọn file
    ///         mới (<see cref="_keyDirty" />) và danh sách project vừa dò được
    ///         (<see cref="_pendingProjectChoices" />) — cả hai chờ tới đầu lượt <see cref="Draw" /> kế
    ///         tiếp, không đổ vào giữa lượt đang vẽ.
    ///     </para>
    ///     <para>
    ///         Mật khẩu keystore chỉ nằm trong field của page này (để chạy <c>keytool</c> lấy SHA-1) —
    ///         KHÔNG lưu vào EditorPrefs, không ghi ra file, không đưa vào report.
    ///     </para>
    /// </summary>
    internal class FirebaseSetupPage : IEzgKitPage
    {
        #region Fields

        private FirebaseSource _source;
        private string _keyPath;
        private string _shaHash;
        private string _keystorePass;
        private string _lastReport;
        private Vector2 _scroll;

        /// <summary>Khối roadmap gấp lại mặc định: là ghi chú cho dev, không phải việc phải làm hôm nay.</summary>
        private bool _roadmapOpen;

        /// <summary>Đọc được gì từ file key — chụp trong <see cref="Reload" />, không đọc file mỗi lần vẽ.</summary>
        private string _keyEmail;

        private string _keyProjectId;
        private string _keyError;

        /// <summary>
        ///     User vừa chọn file key khác → phải đọc lại file. Cùng lý do với
        ///     <see cref="_snapshotDirty" />: chỉ bật cờ trong lúc vẽ, việc đọc đĩa để đầu lượt
        ///     <see cref="Draw" /> kế tiếp làm.
        /// </summary>
        private bool _keyDirty;

        /// <summary>
        ///     Tính trong <see cref="RefreshSnapshot" />: file key đọc được, có project id, mà KHÁC
        ///     project đang khai. Không so trực tiếp trong lúc vẽ vì ô project id sửa được ngay giữa lượt
        ///     vẽ ⇒ số widget (dòng cảnh báo + nút) sẽ lệch giữa Layout và Repaint.
        /// </summary>
        private bool _keyProjectDiffers;

        /// <summary>
        ///     Danh sách project <see cref="ProbeProjects" /> dò được; null = chưa dò / chỉ có 1 (đã gán
        ///     thẳng, khỏi bắt chọn).
        /// </summary>
        private string[] _projectChoices;

        private int _projectIndex;

        /// <summary>
        ///     Kết quả nút "Dò project" đang chờ vào <see cref="_projectChoices" />. Nút nằm NGOÀI scroll
        ///     nên nó chạy TRƯỚC phần thân trong cùng một lượt vẽ; gán thẳng là lượt đó vẽ thêm một
        ///     popup so với lượt Layout của chính nó. Mảng rỗng = lệnh xoá danh sách.
        /// </summary>
        private string[] _pendingProjectChoices;

        /// <summary>
        ///     Trạng thái file config trong <c>Assets/</c> — chụp trong <see cref="RefreshSnapshot" />.
        ///     Đường vẽ chỉ được đọc mấy field này, không được gọi lại
        ///     <see cref="FirebaseAppProvisioner" /> (xem doc đầu class).
        /// </summary>
        private bool _androidConfigExists, _iosConfigExists;

        private string _localAndroidProjectId, _localIosProjectId;
        private string _localAndroidAppId, _localIosAppId;
        private string _xmlProjectId;

        /// <summary>Id lấy từ PlayerSettings — cũng chụp một lần, không hỏi lại mỗi lượt vẽ.</summary>
        private string _androidPackage, _iosBundle;

        /// <summary>Kết quả tính sẵn của <see cref="ComputeMismatch" />; null = không lệch.</summary>
        private string _mismatch;

        /// <summary>
        ///     User vừa sửa thứ mà snapshot phụ thuộc (project id) → chụp lại ở ĐẦU lượt
        ///     <see cref="Draw" /> kế tiếp. Chỉ bật cờ, không tính ngay giữa lúc vẽ: tính giữa chừng là
        ///     đẻ ra đúng cái lệch Layout≠Repaint mà snapshot sinh ra để tránh.
        /// </summary>
        private bool _snapshotDirty;

        /// <summary>
        ///     URL phụ thuộc project id, dựng sẵn trong <see cref="RefreshSnapshot" />: mỗi lượt vẽ dựng
        ///     lại là ~10 chuỗi nội suy + mảng tuple <c>params</c> vứt đi ngay.
        /// </summary>
        private string _urlFirebaseProject, _urlFirebaseSettings, _urlCloudMessaging;

        private string _urlServiceAccounts, _urlEnableManagementApi, _urlBilling;

        /// <summary>Dựng sẵn một lần: <c>Bullets</c> nhận IEnumerable nên đừng cấp phát mảng mỗi lượt vẽ.</summary>
        private static readonly string[] _roadmapItems =
        {
            "Nâng Blaze / link billing — Cloud Billing API, cần roles/billing.user.",
            "Tạo GA4 property + link vào Firebase — Analytics Admin API "
            + "(properties.create + properties.firebaseLinks.create).",
            "Đẩy default của GameRemoteConfig lên project — Remote Config REST API.",
        };

        #endregion

        #region Console URL

        /// <summary>
        ///     Link mở thẳng tới đúng trang cần bấm. Thiếu project id thì mở trang gốc — vẫn hơn là
        ///     không có nút.
        /// </summary>
        private static class ConsoleUrl
        {
            internal static string FirebaseProject(string id) =>
                string.IsNullOrEmpty(id)
                    ? "https://console.firebase.google.com/"
                    : $"https://console.firebase.google.com/project/{id}/overview";

            internal static string FirebaseSettings(string id) =>
                string.IsNullOrEmpty(id)
                    ? "https://console.firebase.google.com/"
                    : $"https://console.firebase.google.com/project/{id}/settings/general";

            internal static string CloudMessaging(string id) =>
                string.IsNullOrEmpty(id)
                    ? "https://console.firebase.google.com/"
                    : $"https://console.firebase.google.com/project/{id}/settings/cloudmessaging";

            internal static string ServiceAccounts(string id) =>
                string.IsNullOrEmpty(id)
                    ? "https://console.cloud.google.com/iam-admin/serviceaccounts"
                    : $"https://console.cloud.google.com/iam-admin/serviceaccounts?project={id}";

            internal static string EnableManagementApi(string id) =>
                string.IsNullOrEmpty(id)
                    ? "https://console.cloud.google.com/apis/library/firebase.googleapis.com"
                    : "https://console.cloud.google.com/apis/library/firebase.googleapis.com"
                      + $"?project={id}";

            internal static string Billing(string id) =>
                string.IsNullOrEmpty(id)
                    ? "https://console.cloud.google.com/billing"
                    : $"https://console.cloud.google.com/billing/linkedaccount?project={id}";

            internal const string AppleAuthKeys = "https://developer.apple.com/account/resources/authkeys/list";
            internal const string AppStoreConnect = "https://appstoreconnect.apple.com/apps";
            internal const string PlayConsole = "https://play.google.com/console";
            internal const string GoogleAnalytics = "https://analytics.google.com/analytics/web/";
        }

        #endregion

        #region Page

        public string Title => "Firebase";

        public string Subtitle =>
            "Tạo app Android + iOS trên project Firebase rồi tải google-services.json / "
            + "GoogleService-Info.plist về Assets/.";

        public string RunAllLabel => "Tao app Firebase + tai google-services.json / plist";

        public string Headline
        {
            get
            {
                // File key giờ là thứ DUY NHẤT bắt buộc (project id suy ra từ nó) nên nó nói trước.
                if (!string.IsNullOrEmpty(_keyError)) return "Chưa có file key: " + _keyError;

                if (string.IsNullOrEmpty(_source?.projectId))
                    return "File key không có project id — điền tay hoặc bấm \"Dò project khả dụng\".";

                if (!_androidConfigExists || !_iosConfigExists)
                    return "Thiếu config: "
                           + (_androidConfigExists ? "" : "google-services.json ")
                           + (_iosConfigExists ? "" : "GoogleService-Info.plist");

                return _mismatch ?? $"Đã có config cho cả 2 nền, project {_source.projectId}.";
            }
        }

        /// <summary>
        ///     Ưu tiên Error &gt; Warn &gt; Ok. Config trỏ sang dự án khác là ca NGUY HIỂM nhất (file vẫn
        ///     tồn tại, build vẫn chạy, số liệu bắn sang project người ta) nên phải là đỏ, không phải vàng.
        ///     <para>Trong nhóm Warn thì thiếu file key đứng trước: nó là đầu vào duy nhất bắt buộc.</para>
        /// </summary>
        public EzgStatus Status
        {
            get
            {
                if (_mismatch != null) return EzgStatus.Error;
                if (!string.IsNullOrEmpty(_keyError)) return EzgStatus.Warn;
                if (string.IsNullOrEmpty(_source?.projectId)) return EzgStatus.Warn;

                if (!_androidConfigExists || !_iosConfigExists) return EzgStatus.Warn;

                return EzgStatus.Ok;
            }
        }

        /// <summary>
        ///     Chụp MỘT lần toàn bộ thứ phải đọc từ ngoài (PlayerSettings + file config dưới đĩa) rồi
        ///     tính sẵn <see cref="_mismatch" /> và mấy URL phụ thuộc project id. Mỗi property của
        ///     <see cref="FirebaseAppProvisioner" /> ở đây được gọi ĐÚNG một lần; sau đó đường vẽ chỉ
        ///     đọc field. Gọi lại khi trạng thái có thể đã đổi: xong <see cref="Reload" />, xong
        ///     <see cref="Save" />, và đầu <see cref="Draw" /> khi user vừa sửa project id hoặc vừa đổi
        ///     file key.
        /// </summary>
        private void RefreshSnapshot()
        {
            _androidPackage = FirebaseAppProvisioner.AndroidPackage;
            _iosBundle = FirebaseAppProvisioner.IosBundle;

            _androidConfigExists = FirebaseAppProvisioner.AndroidConfigExists;
            _iosConfigExists = FirebaseAppProvisioner.IosConfigExists;

            _localAndroidProjectId = FirebaseAppProvisioner.LocalAndroidConfigProjectId;
            _localIosProjectId = FirebaseAppProvisioner.LocalIosConfigProjectId;
            _localAndroidAppId = FirebaseAppProvisioner.LocalAndroidAppId;
            _localIosAppId = FirebaseAppProvisioner.LocalIosAppId;
            _xmlProjectId = FirebaseAppProvisioner.GeneratedXmlProjectId;

            _mismatch = ComputeMismatch();

            // Chốt luôn ở đây: dòng "key thuộc project khác" + nút đổi chỉ được xuất hiện/biến mất theo
            // snapshot, không theo ô text đang gõ dở.
            _keyProjectDiffers = !string.IsNullOrEmpty(_keyProjectId)
                                 && _keyProjectId != _source?.projectId;

            var projectId = _source?.projectId;
            _urlFirebaseProject = ConsoleUrl.FirebaseProject(projectId);
            _urlFirebaseSettings = ConsoleUrl.FirebaseSettings(projectId);
            _urlCloudMessaging = ConsoleUrl.CloudMessaging(projectId);
            _urlServiceAccounts = ConsoleUrl.ServiceAccounts(projectId);
            _urlEnableManagementApi = ConsoleUrl.EnableManagementApi(projectId);
            _urlBilling = ConsoleUrl.Billing(projectId);
        }

        /// <summary>
        ///     Config đang nằm trong <c>Assets/</c> có thể là của DỰ ÁN KHÁC (đi theo code-template) —
        ///     file vẫn tồn tại, build vẫn chạy, số liệu bắn sang project người ta. So project id offline
        ///     để bắt đúng ca đó ngay lúc mở cửa sổ, không cần gọi API.
        ///     <para>Chỉ được gọi từ <see cref="RefreshSnapshot" /> — kết quả nằm ở <see cref="_mismatch" />.</para>
        /// </summary>
        private string ComputeMismatch()
        {
            var expected = _source?.projectId;
            if (string.IsNullOrEmpty(expected)) return null;

            var android = _localAndroidProjectId;
            if (!string.IsNullOrEmpty(android) && android != expected)
                return $"google-services.json đang trỏ sang project '{android}' chứ không phải "
                       + $"'{expected}' — build sẽ bắn số liệu sang dự án khác.";

            var ios = _localIosProjectId;
            if (!string.IsNullOrEmpty(ios) && ios != expected)
                return $"GoogleService-Info.plist đang trỏ sang project '{ios}' chứ không phải "
                       + $"'{expected}'.";

            // File xml mới là thứ đi vào bản build Android, không phải file json.
            var xml = _xmlProjectId;
            if (!string.IsNullOrEmpty(xml) && xml != expected)
                return $"google-services.xml trong FirebaseApp.androidlib còn trỏ sang project "
                       + $"'{xml}'. Chọn Assets/google-services.json > Reimport để generator của "
                       + "Firebase chạy lại.";

            return null;
        }

        public void Reload()
        {
            _source = FirebaseSource.Load();
            _keyPath = FirebaseSource.KeyPath;
            if (string.IsNullOrEmpty(_keystorePass)) _keystorePass = PlayerSettings.Android.keystorePass;
            ReloadKeyInfo();
            AdoptKeyProjectId();
            RefreshSnapshot();
            _keyDirty = false;
            _snapshotDirty = false;
        }

        /// <summary>
        ///     Project id nằm sẵn trong file key nên KHÔNG bắt gõ lại: ô trống thì tự điền.
        ///     <para>
        ///         Chỉ điền khi đang trống — có giá trị rồi thì KHÔNG ghi đè, vì service account hoàn
        ///         toàn có thể được gán role trên project khác project sinh ra nó (ca cross-project, xem
        ///         <see cref="DrawKeyInfo" />). Ca đó được lo bằng dòng cảnh báo + nút "Dùng project của
        ///         key" trong <see cref="DrawKeyProjectRow" />, không phải bằng việc âm thầm sửa.
        ///     </para>
        ///     Phải gọi SAU <see cref="ReloadKeyInfo" /> và TRƯỚC <see cref="RefreshSnapshot" />.
        /// </summary>
        private void AdoptKeyProjectId()
        {
            if (_source == null) return;
            if (string.IsNullOrEmpty(_keyProjectId)) return;
            if (!string.IsNullOrEmpty(_source.projectId)) return;

            _source.projectId = _keyProjectId;
            _snapshotDirty = true;
        }

        /// <summary>Chỉ đọc file key để hiện thông tin — KHÔNG gọi API, không xin token.</summary>
        private void ReloadKeyInfo()
        {
            _keyEmail = null;
            _keyProjectId = null;
            _keyError = null;

            if (string.IsNullOrEmpty(_keyPath))
            {
                _keyError = "Chưa chọn file key.";
                return;
            }

            if (!FirebaseServiceAccount.TryLoad(_keyPath, out var key, out var error))
            {
                _keyError = error;
                return;
            }

            _keyEmail = key.client_email;
            _keyProjectId = key.project_id;
        }

        public void Draw()
        {
            if (_source == null) Reload();

            // TRƯỚC mọi widget: mọi thứ đọc-từ-ngoài + mọi thứ nút bấm để lại đều được tiêu thụ ở đây thì
            // lượt Layout và lượt Repaint của cùng một chu kỳ chắc chắn thấy cùng một dữ liệu, nên số
            // control vẽ ra không đổi giữa hai lượt.
            if (_keyDirty)
            {
                _keyDirty = false;
                ReloadKeyInfo();
                AdoptKeyProjectId();

                // _keyError / _keyProjectDiffers vừa đổi, mà cả hai đều quyết định số widget ⇒ chụp lại
                // kể cả khi project id không đổi.
                _snapshotDirty = true;
            }

            if (_pendingProjectChoices != null)
            {
                _projectChoices = _pendingProjectChoices.Length == 0 ? null : _pendingProjectChoices;
                _pendingProjectChoices = null;
            }

            if (_snapshotDirty)
            {
                RefreshSnapshot();
                _snapshotDirty = false;
            }

            // Ngoài scroll: cảnh báo hỏng + hàng nút. Hai thứ này mà phải cuộn mới thấy là hỏng cả tab.
            DrawAlert();
            DrawActions();
            DrawIntro();

            // ScrollViewScope chứ không BeginScrollView/EndScrollView tay: Dispose chạy trong finally nên
            // exception ném từ mấy hàm vẽ bên dưới không để lại LayoutGroup mở (cửa sổ sẽ spam
            // "Mismatched LayoutGroup" và vẽ rác cho tới khi đóng mở lại).
            using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scroll.scrollPosition;

                DrawManualInputs();
                DrawAutoInfo();
                DrawNoApiSteps();
                DrawRoadmap();
                DrawReport();
            }
        }

        public bool RunAll() => SaveAndRun(false);

        #endregion

        #region Draw - fixed top

        private void DrawAlert()
        {
            if (_mismatch != null) EzgKitStyles.Banner(_mismatch, EzgStatus.Error);
        }

        /// <summary>
        ///     Hàng nút nằm ĐẦU trang: người mở tab này là để chạy, không phải để đọc. Nút tạo bị khoá
        ///     khi thiếu project id / file key hỏng — bấm vào cũng chỉ ra lỗi.
        ///     <para>
        ///         Mọi nút gọi mạng đều nằm ở ĐÂY, ngoài scroll: request là ĐỒNG BỘ, chạy nó từ một
        ///         widget nằm sâu trong thân trang là block ngay giữa lúc vẽ.
        ///     </para>
        /// </summary>
        private void DrawActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (EzgKitStyles.SecondaryButton("Lưu", GUILayout.Width(90),
                        GUILayout.Height(EzgKitStyles.BUTTON_HEIGHT))) Save();

                // Chỉ cần file key đọc được là dò được — không cần project id (dò chính là để có nó).
                using (new EditorGUI.DisabledScope(!string.IsNullOrEmpty(_keyError)))
                {
                    if (EzgKitStyles.SecondaryButton("Dò project khả dụng")) ProbeProjects();
                }

                if (EzgKitStyles.SecondaryButton("Kiểm tra (Dry Run)")) SaveAndRun(true);

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_source.projectId)
                                                   || !string.IsNullOrEmpty(_keyError)))
                {
                    if (EzgKitStyles.PrimaryButton("Tạo app + tải config")) SaveAndRun(false);
                }
            }

            EditorGUILayout.LabelField(
                "Dò project và Dry Run đều chỉ GET — không tạo gì, không ghi file. Nút tạo cũng chạy "
                + "dry-run trước rồi hỏi lại.",
                EzgKitStyles.Hint);
        }

        private static void DrawIntro() =>
            EzgKitStyles.CollapsibleHelp("firebase-intro",
                "Tab này làm gì, vì sao phải chạy sau Marketing?",
                "Tạo app Android + iOS trên project Firebase rồi tải google-services.json / "
                + "GoogleService-Info.plist về Assets/.\n\n"
                + "Đầu vào duy nhất là file service account key (.json): project id nằm sẵn trong file "
                + "đó nên tool tự lấy, không phải gõ.\n\n"
                + "Chạy lại vô hại: app nhận diện theo packageName / bundleId, đã có thì dùng lại chứ "
                + "không tạo trùng. Nhưng Firebase KHÔNG cho sửa hai id đó sau khi tạo, nên chạy tab "
                + "Marketing trước (nó ghi id vào PlayerSettings).");

        #endregion

        #region Draw - manual inputs

        /// <summary>
        ///     File key đứng ĐẦU vì nó là đầu vào duy nhất bắt buộc — mọi thứ khác trong mục này hoặc suy
        ///     ra từ nó (project id), hoặc là tuỳ chọn thật (Apple ID, mật khẩu keystore).
        /// </summary>
        private void DrawManualInputs()
        {
            EzgKitStyles.SectionHeader("1. Chọn file service account",
                "Chọn đúng một file .json là đủ — project id nằm sẵn trong file. Mấy ô còn lại tuỳ chọn.");

            var keyPath = SetupGui.ManualFilePathField("File key (.json)", _keyPath,
                "Google Cloud console > IAM & Admin > Service Accounts > Create service account > gán "
                + "role \"Firebase Admin\" (roles/firebase.admin) > tab Keys > Add key > Create new key > "
                + "JSON. Lưu file NGOÀI repo (vd ~/.config/ezg/firebase-admin.json) — tool từ chối file "
                + "nằm trong project vì sớm muộn cũng bị commit. Máy đã dùng gcloud thì tool tự lấy từ "
                + "biến môi trường GOOGLE_APPLICATION_CREDENTIALS.",
                "Chon service account key", "json", SetupGui.FieldNeed.Required,
                ("Trang Service Accounts", _urlServiceAccounts),
                ("Bật Firebase Management API", _urlEnableManagementApi));

            // Đổi file key thì phải đọc lại file (và có thể tự điền project id), nhưng KHÔNG đọc đĩa ở
            // đây: chỉ bật cờ để đầu lượt Draw sau xử lý — cùng lý do với _snapshotDirty.
            if (keyPath != _keyPath)
            {
                _keyPath = keyPath;
                _keyDirty = true;
            }

            // Không còn là ô phải gõ (optional: true): giá trị mặc định lấy từ file key ở trên. Vẫn cho
            // sửa vì service account có thể được gán role trên project khác.
            var projectId = SetupGui.ManualField("Project id", _source.projectId,
                "Tự lấy từ \"project_id\" trong file key ở trên — chỉ sửa khi service account được gán "
                + "role trên project KHÁC (bấm \"Dò project khả dụng\" ở hàng nút trên cùng để chọn, "
                + "khỏi gõ). Là dạng chữ (vd i001-backyard-empire), KHÔNG phải Project number dạng số.",
                SetupGui.FieldNeed.Derived,
                ("Mở Firebase console", _urlFirebaseProject),
                ("Mở Project settings", _urlFirebaseSettings));

            // Banner đỏ + URL đều phụ thuộc project id, nhưng KHÔNG tính lại ở đây: chỉ bật cờ để đầu
            // lượt Draw sau chụp lại. Vẫn phản hồi ngay dưới mắt user, mà không đổi snapshot giữa chừng.
            if (projectId != _source.projectId)
            {
                _source.projectId = projectId;
                _snapshotDirty = true;
            }

            DrawKeyProjectRow();
            DrawProjectChoices();

            EditorGUILayout.LabelField(
                "Hai ô dưới KHÔNG suy ra được từ file key, và là tuỳ chọn thật: bỏ trống vẫn tạo app + "
                + "tải config bình thường.", EzgKitStyles.Hint);

            // Nhãn KHÔNG kèm "— tuỳ chọn": SetupGui tự thêm hậu tố "(tuỳ chọn)" theo cờ optional.
            _source.appStoreId = SetupGui.ManualField("Apple ID (số)", _source.appStoreId,
                "App Store Connect > app của bạn > App Information > \"Apple ID\" (dãy số). Chỉ để "
                + "Firebase link sang App Store; để trống vẫn tạo được app iOS.",
                SetupGui.FieldNeed.Optional,
                ("Mở App Store Connect", ConsoleUrl.AppStoreConnect));

            _keystorePass = SetupGui.ManualPasswordField("Mật khẩu keystore", _keystorePass,
                "Chỉ cần nếu muốn đăng ký SHA-1 (Google Sign-In / Play Integrity / Dynamic Links). "
                + "Analytics + Crashlytics KHÔNG cần. Mật khẩu chỉ nằm trong ô này để chạy keytool — "
                + "không lưu vào EditorPrefs, không ghi ra file, không vào report.");

            DrawOptionalNames();
        }

        /// <summary>
        ///     Key thuộc project khác project đang khai: KHÔNG tự ghi đè (ca cross-project là hợp lệ),
        ///     chỉ nói ra + cho đổi bằng một cú bấm.
        ///     <para>Điều kiện đọc từ <see cref="_keyProjectDiffers" /> — snapshot, không so tại chỗ.</para>
        /// </summary>
        private void DrawKeyProjectRow()
        {
            if (!_keyProjectDiffers) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EzgKitStyles.ICON_WIDTH + 4f);

                var previous = GUI.contentColor;
                GUI.contentColor = EzgKitStyles.WarnColor;
                EditorGUILayout.LabelField($"File key thuộc project \"{_keyProjectId}\".",
                    EditorStyles.miniLabel);
                GUI.contentColor = previous;

                if (GUILayout.Button("Dùng project của key", EditorStyles.miniButton,
                        GUILayout.Width(160)))
                    SetProjectId(_keyProjectId);
            }
        }

        /// <summary>Danh sách <see cref="ProbeProjects" /> dò được — chỉ hiện khi có từ 2 lựa chọn.</summary>
        private void DrawProjectChoices()
        {
            if (_projectChoices == null) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EzgKitStyles.ICON_WIDTH + 4f);
                using (new EzgKitStyles.LabelWidthScope())
                {
                    var picked = EditorGUILayout.Popup("Project dò được", _projectIndex, _projectChoices);
                    if (picked == _projectIndex) return;

                    _projectIndex = picked;
                    SetProjectId(_projectChoices[picked]);
                }
            }
        }

        /// <summary>Tên app + cờ tạo project: có mặc định hợp lý nên gom lại, không cần hướng dẫn dài.</summary>
        private void DrawOptionalNames()
        {
            using (new EzgKitStyles.CardScope("Tên app & tuỳ chọn tạo project"))
            using (new EzgKitStyles.LabelWidthScope())
            {
                _source.androidDisplayName = EditorGUILayout.TextField("Tên app Android",
                    _source.androidDisplayName);
                _source.iosDisplayName = EditorGUILayout.TextField("Tên app iOS", _source.iosDisplayName);
                EditorGUILayout.LabelField(" ",
                    "Để trống thì lấy theo Product Name — xem dòng \"Tên app sẽ dùng\" ở mục 2.",
                    EzgKitStyles.Hint);

                _source.createProjectIfMissing = EditorGUILayout.Toggle("Tạo project nếu chưa có",
                    _source.createProjectIfMissing);

                if (_source.createProjectIfMissing)
                    EzgKitStyles.Banner(
                        "Cần quyền cấp tổ chức (roles/resourcemanager.projectCreator) và project id là "
                        + "vĩnh viễn — không xoá sạch được. Chỉ bật khi thật sự cần.", EzgStatus.Warn);
            }
        }

        #endregion

        #region Draw - auto info

        /// <summary>
        ///     Bốn khối chỉ-đọc, mỗi khối một card có tiêu đề + trạng thái: nhìn tiêu đề là biết khối nào
        ///     đang ổn, khỏi phải đọc từng dòng.
        /// </summary>
        private void DrawAutoInfo()
        {
            EzgKitStyles.SectionHeader("2. Tool tự lấy — chỉ để xem",
                "Đọc từ PlayerSettings / file config / file key. Muốn đổi thì sửa ở nguồn, không sửa ở đây.");

            using (new EzgKitStyles.CardScope("Id sẽ dùng để tạo app", IdentityStatus))
            {
                DrawIdentityInfo();
            }

            using (new EzgKitStyles.CardScope("Service account",
                       string.IsNullOrEmpty(_keyError) ? EzgStatus.Ok : EzgStatus.Warn))
            {
                DrawKeyInfo();
            }

            using (new EzgKitStyles.CardScope("File config đang có trong Assets/", ConfigStatus))
            {
                DrawConfigInfo();
            }

            // Trạng thái card cố định Ok và trailing luôn có chữ ⇒ CardHeader vẽ đúng bấy nhiêu control
            // dù _shaHash rỗng hay không (None thì không vẽ icon, Ok thì có — số control đổi theo dữ
            // liệu là mầm của lỗi group). SHA là TUỲ CHỌN nên cũng không được tô Warn: bày cảnh báo cho
            // thứ không bắt buộc là dạy người dùng bỏ qua cảnh báo. Tình trạng nói bằng chữ bên phải.
            using (new EzgKitStyles.CardScope("SHA-1 / SHA-256", EzgStatus.Ok,
                       string.IsNullOrEmpty(_shaHash) ? "chưa có — tuỳ chọn" : "đã có"))
            {
                DrawShaRow();
            }
        }

        private EzgStatus IdentityStatus =>
            string.IsNullOrEmpty(_androidPackage) || string.IsNullOrEmpty(_iosBundle)
                ? EzgStatus.Warn
                : EzgStatus.Ok;

        private EzgStatus ConfigStatus
        {
            get
            {
                if (_mismatch != null) return EzgStatus.Error;

                return _androidConfigExists && _iosConfigExists ? EzgStatus.Ok : EzgStatus.Warn;
            }
        }

        private void DrawIdentityInfo()
        {
            var android = _androidPackage;
            var ios = _iosBundle;

            SetupGui.InfoRow("Android package", android,
                string.IsNullOrEmpty(android) ? EzgStatus.Warn : EzgStatus.Ok,
                "Id app Firebase sẽ mang. Do tab Marketing ghi vào PlayerSettings.");
            SetupGui.InfoRow("iOS bundle", ios,
                string.IsNullOrEmpty(ios) ? EzgStatus.Warn : EzgStatus.Ok);
            SetupGui.InfoRow("Tên app sẽ dùng",
                $"{_source.ResolvedAndroidName}  |  {_source.ResolvedIosName}", EzgStatus.Ok);
        }

        private void DrawKeyInfo()
        {
            if (!string.IsNullOrEmpty(_keyError))
            {
                SetupGui.InfoRow("Service account", _keyError, EzgStatus.Warn);
                return;
            }

            SetupGui.InfoRow("Service account", _keyEmail, EzgStatus.Ok);

            var sameProject = _keyProjectId == _source.projectId;
            SetupGui.InfoRow("Key thuộc project", _keyProjectId,
                sameProject ? EzgStatus.Ok : EzgStatus.None,
                sameProject
                    ? null
                    : "Khác project đang khai — vẫn chạy được nếu service account này đã được gán role "
                      + "trên project đó. Muốn quay về project của key thì bấm \"Dùng project của key\" "
                      + "ở mục 1.");
        }

        private void DrawConfigInfo()
        {
            var expected = _source.projectId;

            DrawConfigRow("google-services.json", _androidConfigExists, _localAndroidProjectId, expected);
            SetupGui.InfoRow("  App id Android", _localAndroidAppId);

            DrawConfigRow("GoogleService-Info.plist", _iosConfigExists, _localIosProjectId, expected);
            SetupGui.InfoRow("  App id iOS", _localIosAppId);

            var xml = _xmlProjectId;
            SetupGui.InfoRow("XML trong androidlib", xml ?? "chưa sinh",
                XmlState(xml, expected),
                "Đây mới là project id THẬT SỰ đi vào bản build Android — generator của Firebase sinh "
                + "lại từ google-services.json mỗi lần import.");
        }

        private static void DrawConfigRow(string label, bool exists, string projectId, string expected)
        {
            if (!exists)
            {
                SetupGui.InfoRow(label, "chưa có", EzgStatus.Warn);
                return;
            }

            var matches = !string.IsNullOrEmpty(projectId) && projectId == expected;
            SetupGui.InfoRow(label, $"project {projectId}",
                matches ? EzgStatus.Ok : EzgStatus.Warn,
                matches ? null : $"Không khớp project đang khai ('{expected}').");
        }

        private static EzgStatus XmlState(string xml, string expected)
        {
            if (string.IsNullOrEmpty(xml)) return EzgStatus.None;
            return xml == expected ? EzgStatus.Ok : EzgStatus.Warn;
        }

        /// <summary>
        ///     SHA nằm ở vùng "tự lấy" vì keytool đọc được từ keystore của project; ô vẫn cho sửa để dán
        ///     tay khi keystore nằm ngoài project (vd bản Play App Signing lấy trên Play Console).
        /// </summary>
        private void DrawShaRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EzgKitStyles.LabelWidthScope())
                {
                    _shaHash = EditorGUILayout.TextField("SHA-1 / SHA-256", _shaHash);
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_keystorePass)))
                {
                    if (GUILayout.Button("Lấy từ keystore", GUILayout.Width(140))) ReadShaFromKeystore();
                }
            }

            EditorGUILayout.LabelField(
                string.IsNullOrEmpty(_keystorePass)
                    ? "Điền mật khẩu keystore ở mục 1 để tự lấy. Bỏ trống cả hai thì tool không đăng ký SHA."
                    : $"Keystore: {PlayerSettings.Android.keystoreName}",
                EzgKitStyles.Hint);
        }

        #endregion

        #region Draw - no API

        private void DrawNoApiSteps()
        {
            EzgKitStyles.SectionHeader("3. Không có API — phải làm tay ngoài Unity",
                "Cấp thêm quyền cũng không mở ra được: Google/Apple không có endpoint cho mấy việc này.");

            SetupGui.ManualStep("APNs auth key (.p8) cho push iOS",
                "Firebase không có endpoint upload APNs key, Apple cũng không cho tạo key qua API. "
                + "Tạo key ở Apple Developer > Keys, rồi upload ở Firebase > Project settings > Cloud "
                + "Messaging (kèm Key ID + Team ID). Không có thì analytics/crashlytics vẫn chạy, chỉ "
                + "push iOS là không.",
                ("Apple Developer > Keys", ConsoleUrl.AppleAuthKeys),
                ("Firebase > Cloud Messaging", _urlCloudMessaging));

            SetupGui.ManualStep("Tạo app record trên App Store Connect",
                "App Store Connect API có POST /v1/bundleIds (đăng ký bundle id) nhưng KHÔNG có "
                + "POST /v1/apps — bản ghi app phải tạo bằng tay.",
                ("Mở App Store Connect", ConsoleUrl.AppStoreConnect));

            SetupGui.ManualStep("Tạo app mới trên Play Console",
                "Play Developer API chỉ quản app đã tồn tại (upload bản, track, listing). Tạo app mới "
                + "là việc của Play Console.",
                ("Mở Play Console", ConsoleUrl.PlayConsole));
        }

        /// <summary>
        ///     Ghi chú roadmap cho dev — GẤP LẠI mặc định: người đang dựng dự án không làm gì được với
        ///     danh sách này, để nó mở sẵn chỉ làm loãng phần việc thật ở trên.
        /// </summary>
        private void DrawRoadmap()
        {
            using (new EzgKitStyles.CardScope())
            {
                _roadmapOpen = EzgKitStyles.CardFoldout(_roadmapOpen, "Tự động được nhưng tool chưa làm",
                    EzgStatus.None, "3 việc");

                if (!_roadmapOpen) return;

                EzgKitStyles.Divider(2f);
                EditorGUILayout.LabelField(
                    "Mấy việc này CÓ API — thiếu quyền cho service account và thiếu code, không phải chặn "
                    + "cứng. Đang phải làm tay:", EzgKitStyles.Hint);

                EzgKitStyles.Bullets(_roadmapItems);
                SetupGui.Links(
                    ("Trang Billing", _urlBilling),
                    ("Google Analytics", ConsoleUrl.GoogleAnalytics));
            }
        }

        #endregion

        #region Draw - report

        private void DrawReport()
        {
            if (string.IsNullOrEmpty(_lastReport)) return;

            // MinHeight chứ không ExpandHeight: trong scroll view, ô co giãn làm layout nhảy mỗi lượt vẽ.
            using (new EzgKitStyles.CardScope("Kết quả lần chạy gần nhất"))
            {
                EditorGUILayout.TextArea(_lastReport, GUILayout.MinHeight(80));
            }
        }

        #endregion

        #region Actions

        private void Save()
        {
            FirebaseSource.KeyPath = _keyPath;
            _source.Save();
            ReloadKeyInfo();
            AdoptKeyProjectId();

            // projectId vừa ghi xuống là thứ _mismatch + mấy URL phụ thuộc vào ⇒ chụp lại luôn.
            RefreshSnapshot();
            _keyDirty = false;
            _snapshotDirty = false;

            _lastReport = $"Da luu {FirebaseSource.JsonPath}";
        }

        /// <summary>
        ///     Đổi project id từ code (nút "Dùng project của key" / popup dò được). Luôn đi qua đây để
        ///     không quên <see cref="_snapshotDirty" /> — quên là banner đỏ và mấy URL console còn trỏ
        ///     theo project cũ.
        /// </summary>
        private void SetProjectId(string projectId)
        {
            if (_source == null || _source.projectId == projectId) return;

            _source.projectId = projectId;
            _snapshotDirty = true;
        }

        /// <summary>
        ///     Hỏi Google xem service account này với tới được project nào. Chỉ GET — không tạo gì, không
        ///     ghi file, và TUYỆT ĐỐI không <c>AssetDatabase.Refresh()</c> (Refresh kéo theo recompile →
        ///     domain reload, mất sạch state của trang giữa lúc user đang thao tác).
        ///     <para>
        ///         Chỉ được gọi từ hàng nút ngoài scroll: request là đồng bộ, và kết quả đi qua
        ///         <see cref="_pendingProjectChoices" /> chứ không vào thẳng danh sách đang vẽ.
        ///     </para>
        /// </summary>
        private void ProbeProjects()
        {
            // Provisioner đọc key từ FirebaseSource.KeyPath, mà ô file có thể vừa đổi và chưa Lưu.
            // Ghi mỗi đường dẫn (EditorPrefs theo máy) — không đụng ProjectSettings/FirebaseSource.json.
            FirebaseSource.KeyPath = _keyPath;

            if (!FirebaseAppProvisioner.TryListProjects(out var projects, out var error))
            {
                _pendingProjectChoices = Array.Empty<string>();
                _lastReport = error;
                return;
            }

            if (projects.Count == 1)
            {
                _pendingProjectChoices = Array.Empty<string>();
                SetProjectId(projects[0]);
                _lastReport = $"Service account chi thay 1 project -> da chon '{projects[0]}'.";
                return;
            }

            _pendingProjectChoices = projects.ToArray();
            _projectIndex = Math.Max(0, Array.IndexOf(_pendingProjectChoices, _source.projectId));
            _lastReport = $"Do duoc {projects.Count} project: {string.Join(", ", projects)}.\n"
                          + "Chon o dong 'Project do duoc' ngay duoi o Project id.";
        }

        private bool SaveAndRun(bool dryRun)
        {
            Save();

            if (dryRun) return FirebaseAppProvisioner.Run(true, _shaHash, out _lastReport);

            if (!FirebaseAppProvisioner.Run(true, _shaHash, out var plan))
            {
                _lastReport = plan;
                return false;
            }

            if (!EditorUtility.DisplayDialog("Firebase - xac nhan",
                    plan + "\n\nLuu y: packageName / bundleId cua app Firebase KHONG sua duoc sau khi tao.",
                    "Lam di", "Huy"))
            {
                _lastReport = plan + "\n(Da huy)";
                return false;
            }

            var ok = FirebaseAppProvisioner.Run(false, _shaHash, out _lastReport);
            Reload();
            return ok;
        }

        /// <summary>
        ///     Chạy <c>keytool -list</c> trên keystore đang khai trong PlayerSettings để lấy SHA-1.
        ///     Mật khẩu truyền qua tham số dòng lệnh của tiến trình con và không đi đâu khác.
        /// </summary>
        private void ReadShaFromKeystore()
        {
            var keystore = PlayerSettings.Android.keystoreName;
            var alias = PlayerSettings.Android.keyaliasName;

            if (string.IsNullOrEmpty(keystore))
            {
                _lastReport = "PlayerSettings chua khai keystore (Publishing Settings > Custom Keystore).";
                return;
            }

            // Unity serialize keystore trong project voi tien to '{inproject}: '.
            var marker = keystore.IndexOf("}: ", StringComparison.Ordinal);
            if (marker >= 0) keystore = keystore.Substring(marker + 3);
            if (!Path.IsPathRooted(keystore))
                keystore = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), keystore);

            if (!File.Exists(keystore))
            {
                _lastReport = $"Khong thay keystore: {keystore}";
                return;
            }

            if (string.IsNullOrEmpty(_keystorePass))
            {
                _lastReport = "Chua co mat khau keystore.";
                return;
            }

            var keytool = FindKeytool();
            if (keytool == null)
            {
                _lastReport = "Khong thay keytool. Cai JDK hoac dat JAVA_HOME (Unity co JDK rieng: "
                              + "Preferences > External Tools > Android JDK).";
                return;
            }

            try
            {
                var arguments = $"-list -v -keystore \"{keystore}\" -storepass \"{_keystorePass}\"";
                if (!string.IsNullOrEmpty(alias)) arguments += $" -alias \"{alias}\"";

                var info = new ProcessStartInfo(keytool, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(info);
                if (process == null)
                {
                    _lastReport = "Khong chay duoc keytool.";
                    return;
                }

                var output = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit(15000);

                var match = Regex.Match(output, @"SHA1:\s*([0-9A-Fa-f:]{47,})");
                if (!match.Success)
                {
                    // stderr cua keytool khong chua mat khau, chi la thong diep loi.
                    _lastReport = "keytool khong tra ve SHA1. Thuong la sai mat khau hoac sai alias.\n"
                                  + stderr.Trim();
                    return;
                }

                _shaHash = match.Groups[1].Value;
                _lastReport = $"Da lay SHA-1: {_shaHash}";
            }
            catch (Exception exception)
            {
                _lastReport = $"Chay keytool that bai: {exception.Message}";
            }
        }

        private static string FindKeytool()
        {
            var executable = Application.platform == RuntimePlatform.WindowsEditor ? "keytool.exe" : "keytool";

            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            var candidates = new[]
            {
                string.IsNullOrEmpty(javaHome) ? null : Path.Combine(javaHome, "bin", executable),
                Path.Combine(EditorPrefs.GetString("JdkPath", string.Empty), "bin", executable),
                "/usr/bin/" + executable,
            };

            foreach (var candidate in candidates)
                if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                    return candidate;

            // Khong tim thay duong dan tuyet doi -> de OS tu do trong PATH.
            return executable;
        }

        #endregion
    }
}
#endif
