#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Ezg.Editor.Shared.EzgKit;
using UnityEditor;
using UnityEditor.Build;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Networking;

namespace Ezg.Editor.Shared.Readiness
{
    /// <summary>
    ///     Tab <b>Readiness</b> — bảng "triển khai được chưa" cho PM: IAP / Firebase / SDK / Store, mỗi
    ///     mục một trong ba mức Ready (xanh) · Warning (vàng) · Error (đỏ). Mở cửa sổ là thấy, không
    ///     phải hỏi dev, không phải mở console nào.
    ///     <para>
    ///         <b>Việc phải làm nằm trên, chi tiết nằm dưới:</b> khối đầu chỉ liệt kê mục đỏ/vàng kèm
    ///         câu hành động; bảng đầy đủ theo nhóm gấp/mở ở dưới; cuối cùng là các việc tay ngoài
    ///         Unity mà tool không kiểm được (SKU trên console, Remote Config key, rules).
    ///     </para>
    ///     <para>
    ///         Chỉ ĐỌC. Đúng một thứ gọi ra ngoài là nút <i>Tra App Store</i> (lookup công khai của
    ///         Apple cho id trong <c>GameConstant.IOSAppId</c>) — là nút bấm, không tự chạy khi mở tab,
    ///         và kết quả về được đổ vào snapshot ở ĐẦU lượt Draw kế tiếp (<see cref="_reloadPending" />),
    ///         không sửa dữ liệu giữa lượt vẽ (xem quy tắc snapshot trong <c>FirebaseSetupPage</c>).
    ///     </para>
    /// </summary>
    internal class ReadinessPage : IEzgKitPage
    {
        #region Fields

        private const string LOOKUP_URL = "https://itunes.apple.com/lookup?id=";
        private const string APPSFLYER_IOS_LABEL = "AppsFlyer iOS App Store ID";

        private ReadinessReport _report;
        private AppStoreLookup _lookup;
        private UnityWebRequest _request;
        private string _requestId;
        private bool _reloadPending;
        private Vector2 _scroll;
        private string _message;
        private EzgStatus _messageStatus = EzgStatus.None;

        /// <summary>Id iOS đang khai trong GameConstant — chụp trong Reload để quyết có vẽ nút Tra hay không.</summary>
        private string _iosAppId;

        /// <summary>Nhóm nào đang mở. Nhóm chưa có trong dict thì mở nếu còn việc, gấp nếu xanh hết.</summary>
        private readonly Dictionary<ReadinessGroup, bool> _open = new();

        /// <summary>Gom sẵn theo nhóm trong Reload để vòng vẽ không lọc lại list mỗi lượt.</summary>
        private readonly Dictionary<ReadinessGroup, List<ReadinessItem>> _byGroup = new();

        private readonly Dictionary<ReadinessGroup, EzgStatus> _groupStatus = new();
        private readonly Dictionary<ReadinessGroup, string> _groupTrailing = new();
        private readonly List<ReadinessItem> _todos = new();

        private static readonly ReadinessGroup[] _groups =
            (ReadinessGroup[])Enum.GetValues(typeof(ReadinessGroup));

        private static readonly (string Label, string Url)[] _skuLinks =
        {
            ("App Store Connect", "https://appstoreconnect.apple.com/apps"),
            ("Play Console", "https://play.google.com/console/"),
        };

        #endregion

        #region Page

        public string Title => "Readiness";

        public string Subtitle =>
            "Trạng thái sẵn sàng phát hành — IAP, Firebase, SDK, Store. Ready / Warning / Error, chỉ đọc.";

        /// <summary>Không có bước ghi nào — tab này không tham gia luồng chạy hết.</summary>
        public string RunAllLabel => null;

        public string Headline
        {
            get
            {
                if (_report == null) return "Chưa đọc trạng thái.";
                if (_report.Errors == 0 && _report.Warns == 0) return $"Sẵn sàng — {_report.Oks} mục xanh.";
                return $"{_report.Errors} lỗi · {_report.Warns} cảnh báo · {_report.Oks} sẵn sàng.";
            }
        }

        public EzgStatus Status
        {
            get
            {
                if (_report == null) return EzgStatus.Warn;
                if (_report.Errors > 0) return EzgStatus.Error;
                return _report.Warns > 0 ? EzgStatus.Warn : EzgStatus.Ok;
            }
        }

        public void Reload()
        {
            _report = ReadinessChecks.Collect(_lookup);
            _iosAppId = null;
            _todos.Clear();
            foreach (var group in _groups)
            {
                if (!_byGroup.TryGetValue(group, out var list)) _byGroup[group] = list = new List<ReadinessItem>();
                list.Clear();
            }

            foreach (var item in _report.Items)
            {
                _byGroup[item.Group].Add(item);
                if (item.Status is EzgStatus.Error or EzgStatus.Warn) _todos.Add(item);
                if (item.Label == APPSFLYER_IOS_LABEL) _iosAppId = item.Value;
            }

            // Đỏ trước vàng sau: người đọc xử lý theo đúng thứ tự đó.
            _todos.Sort((a, b) => b.Status.CompareTo(a.Status));

            foreach (var group in _groups)
            {
                var worst = EzgStatus.None;
                var pending = 0;
                var counted = 0;
                foreach (var item in _byGroup[group])
                {
                    if (item.Status == EzgStatus.None) continue;
                    counted++;
                    if (item.Status > worst) worst = item.Status;
                    if (item.Status is EzgStatus.Error or EzgStatus.Warn) pending++;
                }

                _groupStatus[group] = worst;
                _groupTrailing[group] = pending == 0 ? $"{counted} mục" : $"{counted} mục · {pending} cần xử lý";
                if (!_open.ContainsKey(group)) _open[group] = pending > 0;
            }
        }

        public void Draw()
        {
            // Snapshot đổi ở ĐẦU lượt vẽ, không đổi giữa lượt — số widget phải giống nhau giữa Layout và Repaint.
            if (_reloadPending)
            {
                _reloadPending = false;
                Reload();
            }

            if (_report == null) Reload();

            DrawFixedTop();

            using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scroll.scrollPosition;
                DrawTodos();
                DrawGroups();
                DrawManualSteps();
            }
        }

        public bool RunAll() => true;

        #endregion

        #region Draw

        private void DrawFixedTop()
        {
            EzgKitStyles.Banner(Headline + (_report.Errors > 0
                    ? "  Mục đỏ là build ra sai/hỏng — sửa trước khi build store."
                    : _report.Warns > 0
                        ? "  Chạy được, còn việc trước khi lên store."
                        : "  Có thể build store."),
                Status);

            using (new EditorGUILayout.HorizontalScope())
            {
                var canLookup = !string.IsNullOrEmpty(_iosAppId) && _request == null;
                using (new EditorGUI.DisabledScope(!canLookup))
                {
                    if (EzgKitStyles.SecondaryButton(
                            _request != null ? "Đang tra App Store…" : "Tra App Store id (AppsFlyer iOS)",
                            GUILayout.Width(240f), GUILayout.Height(24f)))
                        StartLookup(_iosAppId);
                }

                if (EzgKitStyles.SecondaryButton("Copy báo cáo cho PM", GUILayout.Width(180f), GUILayout.Height(24f)))
                    CopyReport();

                GUILayout.FlexibleSpace();
            }

            if (!string.IsNullOrEmpty(_message))
            {
                var previous = GUI.contentColor;
                GUI.contentColor = EzgKitStyles.ColorOf(_messageStatus);
                EditorGUILayout.LabelField(_message, EditorStyles.miniLabel);
                GUI.contentColor = previous;
            }

            EzgKitStyles.CollapsibleHelp("readiness-scope", "Tool kiểm gì — và KHÔNG kiểm được gì?",
                "Kiểm từ chính project: file config Firebase, catalog SKU của shop, key/ad-unit trong "
                + "AdsConfig / AppLovinSettings / FacebookSettings, GameConstant, keystore, script build iOS.\n\n"
                + "KHÔNG thấy được console: SKU đã tạo trên Play/ASC chưa, Remote Config key đã lên chưa, "
                + "Firestore rules, APNs key. Những thứ đó nằm ở mục \"Việc tay ngoài Unity\" cuối trang — "
                + "xanh hết ở đây KHÔNG có nghĩa là store đã sẵn.\n\n"
                + "Đỏ = build ra là hỏng hoặc bắn số liệu sang dự án khác. Vàng = chạy được nhưng còn việc "
                + "trước khi phát hành. Xám = không áp dụng cho dự án này.\n\n"
                + "Mỗi mục đỏ/vàng có dòng \"→ cách sửa\" và nút: ▸ = làm trong Unity (chọn asset, mở script "
                + "đúng dòng, mở Project Settings, mở tab khác của kit, hoặc sửa luôn — nút sửa luôn hỏi lại "
                + "trước khi ghi); ↗ = mở trang web (console của store / SDK).");
        }

        private void DrawTodos()
        {
            EzgKitStyles.SectionHeader("Việc phải làm",
                _todos.Count == 0 ? null : "Đỏ trước, vàng sau. Mỗi dòng là một hành động cụ thể.");

            using (new EzgKitStyles.CardScope())
            {
                if (_todos.Count == 0)
                {
                    EditorGUILayout.LabelField("Không còn việc — mọi mục tool kiểm được đều xanh.", EzgKitStyles.Hint);
                    return;
                }

                for (var i = 0; i < _todos.Count; i++)
                {
                    var item = _todos[i];
                    if (i > 0) EzgKitStyles.Divider(2f);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EzgKitStyles.StatusIcon(item.Status);
                        GUILayout.Label($"[{ReadinessReport.GroupTitle(item.Group)}]  {item.Label}",
                            EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                        if (!string.IsNullOrEmpty(item.Value))
                            EditorGUILayout.SelectableLabel(item.Value, EzgKitStyles.ValueStyle,
                                GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    }

                    if (!string.IsNullOrEmpty(item.Note)) Indented(item.Note, EzgKitStyles.Hint);
                    if (!string.IsNullOrEmpty(item.Fix)) Indented("→ " + item.Fix, EditorStyles.wordWrappedLabel);
                    DrawActions(item);
                }
            }
        }

        private void DrawGroups()
        {
            EzgKitStyles.SectionHeader("Chi tiết theo nhóm");

            foreach (var group in _groups)
            {
                var items = _byGroup[group];
                if (items.Count == 0) continue;

                using (new EzgKitStyles.CardScope())
                {
                    var open = _open[group];
                    var next = EzgKitStyles.CardFoldout(open, ReadinessReport.GroupTitle(group),
                        _groupStatus[group], _groupTrailing[group]);
                    if (next != open) _open[group] = next;
                    if (!next) continue;

                    foreach (var item in items)
                    {
                        EzgKitStyles.KeyValue(item.Label, item.Value, item.Status, item.Note,
                            item.Status == EzgStatus.None ? "không áp dụng" : "chưa có");
                        // Chỉ mục còn việc mới bày nút ở bảng chi tiết: 30 dòng xanh mỗi dòng 2 nút là
                        // nhiễu, và việc "đi sửa" đã có đủ ở khối Việc phải làm phía trên.
                        if (!item.IsPending) continue;
                        if (!string.IsNullOrEmpty(item.Fix)) Indented("→ " + item.Fix, EditorStyles.wordWrappedLabel);
                        DrawActions(item);
                    }

                    if (group == ReadinessGroup.Iap && _report.Skus.Count > 0)
                    {
                        EzgKitStyles.Divider(2f);
                        EditorGUILayout.LabelField($"SKU phải tồn tại trên store ({_report.Skus.Count}):",
                            EditorStyles.boldLabel);
                        EzgKitStyles.Bullets(_report.Skus);
                    }
                }
            }
        }

        private static void DrawManualSteps()
        {
            EzgKitStyles.SectionHeader("Việc tay ngoài Unity",
                "Không có API để tool kiểm — PM/dev tự tick sau khi làm trên console.");

            SetupGui.ManualStep("Tạo SKU trên Play Console + App Store Connect",
                "Danh sách SKU nằm ở nhóm IAP phía trên (và trong báo cáo copy). Loại Consumable / "
                + "Non-Consumable trên store PHẢI khớp cờ isNonConsumable của catalog — lệch loại là ô giá "
                + "trống và bấm mua fail.", _skuLinks);

            SetupGui.ManualStep("Remote Config: tạo key trên Firebase console",
                "GameRemoteConfig chỉ ĐỌC key; key chưa có trên console thì game chạy default trong code. "
                + "Không có API kiểm từ Editor mà không cần service account.",
                ("Firebase console", "https://console.firebase.google.com/"));

            SetupGui.ManualStep("Firestore rules + Auth provider + APNs key",
                "Save-sync / admin tool cần Firestore rules và provider Auth (Google, Game Center) bật trên "
                + "console; Messaging iOS cần APNs key upload vào Cloud Messaging.",
                ("Firebase console", "https://console.firebase.google.com/"));

            SetupGui.ManualStep("Sandbox test IAP trên máy thật",
                "Mua thử từng SKU bằng tài khoản tester (Play: License testers; ASC: Sandbox testers). "
                + "UGS chưa link hay SKU lệch loại chỉ lộ ra ở bước này.", _skuLinks);
        }

        /// <summary>
        ///     Hàng nút của một mục: nút Editor (chọn asset / mở script / mở settings / sửa luôn) trước,
        ///     link web sau. Nút Editor chạy hoãn qua <see cref="ReadinessActions.Defer" /> — không đổi
        ///     Selection hay mở cửa sổ giữa lượt vẽ. Nút "sửa luôn" tự hỏi xác nhận bên trong action.
        /// </summary>
        internal static void DrawActions(ReadinessItem item)
        {
            var hasActions = item.Actions is { Length: > 0 };
            var hasLinks = item.Links is { Length: > 0 };
            if (!hasActions && !hasLinks) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EzgKitStyles.ICON_WIDTH + 4f);
                if (hasActions)
                    foreach (var action in item.Actions)
                    {
                        if (action.Run == null) continue;
                        if (GUILayout.Button("▸ " + action.Label, EditorStyles.miniButton, GUILayout.MaxWidth(220f)))
                            ReadinessActions.Defer(action.Run);
                    }

                if (hasLinks)
                    foreach (var link in item.Links)
                    {
                        if (string.IsNullOrEmpty(link.Url)) continue;
                        if (GUILayout.Button("↗ " + link.Label, EditorStyles.miniButton, GUILayout.MaxWidth(200f)))
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

        private void CopyReport()
        {
            var text = _report.ToText(PlayerSettings.productName,
                PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android),
                PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS),
                "v" + PlayerSettings.bundleVersion);
            EditorGUIUtility.systemCopyBuffer = text;
            _message = $"Đã copy báo cáo ({_report.Items.Count} mục) — dán vào Discord/Slack.";
            _messageStatus = EzgStatus.Ok;
        }

        /// <summary>
        ///     Tra id trên App Store lookup công khai. Bất đồng bộ qua <see cref="EditorApplication.update" />:
        ///     không block Editor, và kết quả chỉ được đổ vào snapshot ở đầu lượt Draw kế tiếp.
        /// </summary>
        private void StartLookup(string appId)
        {
            if (_request != null) return;

            _requestId = appId;
            _request = UnityWebRequest.Get(LOOKUP_URL + appId);
            _request.timeout = 15;
            _request.SendWebRequest();
            _message = null;
            EditorApplication.update += PollLookup;
        }

        private void PollLookup()
        {
            if (_request == null)
            {
                EditorApplication.update -= PollLookup;
                return;
            }

            if (!_request.isDone) return;
            EditorApplication.update -= PollLookup;

            var lookup = new AppStoreLookup { QueriedId = _requestId };
            try
            {
                if (_request.result != UnityWebRequest.Result.Success)
                    lookup.Error = _request.error;
                else
                {
                    var response = JsonUtility.FromJson<LookupResponse>(_request.downloadHandler.text);
                    lookup.Found = response != null && response.resultCount > 0 && response.results != null
                                   && response.results.Length > 0;
                    if (lookup.Found)
                    {
                        lookup.TrackName = response.results[0].trackName;
                        lookup.BundleId = response.results[0].bundleId;
                        lookup.Seller = response.results[0].sellerName;
                    }
                }
            }
            catch (Exception exception)
            {
                lookup.Error = exception.Message;
            }
            finally
            {
                _request.Dispose();
                _request = null;
            }

            _lookup = lookup;
            _message = !string.IsNullOrEmpty(lookup.Error) ? "Tra App Store thất bại: " + lookup.Error
                : !lookup.Found ? $"App Store không có app nào cho id {lookup.QueriedId}."
                : $"id {lookup.QueriedId} = \"{lookup.TrackName}\" ({lookup.BundleId}) — xem mục AppsFlyer iOS.";
            _messageStatus = !string.IsNullOrEmpty(lookup.Error) || !lookup.Found ? EzgStatus.Warn : EzgStatus.Ok;
            _reloadPending = true;
            InternalEditorUtility.RepaintAllViews();
        }

        [Serializable]
        private sealed class LookupResponse
        {
            public int resultCount;
            public LookupResult[] results;
        }

        [Serializable]
        private sealed class LookupResult
        {
            public string trackName;
            public string bundleId;
            public string sellerName;
        }

        #endregion
    }
}
#endif
