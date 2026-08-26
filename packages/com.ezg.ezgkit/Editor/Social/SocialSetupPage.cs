#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Ezg.Editor.Shared.EzgKit;
using Ezg.Editor.Shared.Readiness;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Networking;

namespace Ezg.Editor.Shared.Social
{
    /// <summary>
    ///     Tab <b>Social</b> — link cộng đồng / hỗ trợ / rating của dự án: điền Discord invite, link
    ///     support, email support → Lưu (<c>ProjectSettings/SocialConfig.json</c>) → Ghi vào
    ///     <c>GameConstant.cs</c>; phía dưới là bảng trạng thái Ready / Warning / Error của TOÀN BỘ
    ///     link đi vào build (kể cả fanpage/privacy/terms/store do tab Marketing ghi, link còn
    ///     hardcode trong script, webhook + bot token Discord của BugLogger).
    ///     <para>
    ///         Việc "link còn sống không" chỉ chạy khi bấm <i>Kiểm Discord</i> (API công khai của
    ///         Discord: invite → tên server, webhook → tên webhook). Không tự gọi mạng khi mở tab.
    ///     </para>
    ///     <para>
    ///         Cùng kỷ luật snapshot với các tab khác: <see cref="Reload" /> chụp trạng thái; kết quả
    ///         mạng và mọi thay đổi số widget được đổ vào ở ĐẦU <see cref="Draw" /> qua
    ///         <see cref="_reloadPending" />.
    ///     </para>
    /// </summary>
    internal class SocialSetupPage : IEzgKitPage
    {
        #region Fields

        private const string API_INVITE = "https://discord.com/api/v10/invites/";

        private SocialSource _source;

        /// <summary>Bản đã lưu trên đĩa — để biết ô nhập đang khác file (chưa Lưu).</summary>
        private SocialSource _saved;

        private ReadinessReport _report;
        private readonly List<ReadinessItem> _items = new();
        private readonly List<ReadinessItem> _todos = new();
        private DiscordLookup _lookup;
        private Vector2 _scroll;
        private string _message;
        private EzgStatus _messageStatus = EzgStatus.None;
        private bool _reloadPending;
        private bool _detailsOpen = true;

        /// <summary>Request đang chạy; mỗi request kèm callback xử lý kết quả. Xử lý tuần tự cho đơn giản.</summary>
        private readonly Queue<(UnityWebRequest Request, Action<UnityWebRequest> OnDone)> _pending = new();

        private UnityWebRequest _current;
        private Action<UnityWebRequest> _currentOnDone;
        private DiscordLookup _building;

        #endregion

        #region Page

        public string Title => "Social";

        public string Subtitle =>
            "Discord, link hỗ trợ, email, rating và các link đi vào build — điền, ghi vào GameConstant, kiểm trạng thái.";

        public string RunAllLabel => "Ghi link social vao GameConstant.cs";

        public string Headline
        {
            get
            {
                if (_report == null) return "Chưa đọc trạng thái.";
                int errors = 0, warns = 0;
                foreach (var item in _items)
                {
                    if (item.Status == EzgStatus.Error) errors++;
                    else if (item.Status == EzgStatus.Warn) warns++;
                }

                if (errors == 0 && warns == 0) return "Link đầy đủ, khớp GameConstant.";
                return $"{errors} lỗi · {warns} cảnh báo về link/social.";
            }
        }

        public EzgStatus Status
        {
            get
            {
                if (_report == null) return EzgStatus.Warn;
                var worst = EzgStatus.Ok;
                foreach (var item in _items)
                    if (item.Status > worst && item.Status != EzgStatus.None) worst = item.Status;
                return worst;
            }
        }

        public void Reload()
        {
            _saved = SocialSource.Load();
            // Giữ ô đang gõ nếu người dùng chưa Lưu mà bấm Làm mới: mất chữ vừa gõ là bực hơn thấy số cũ.
            if (_source == null || _source.SameAs(_savedBefore)) _source = _saved.Clone();
            _savedBefore = _saved.Clone();

            _report = new ReadinessReport();
            SocialChecks.Collect(_report, _source, _lookup);
            _items.Clear();
            _todos.Clear();
            foreach (var item in _report.Items)
            {
                if (item.Group != ReadinessGroup.Social) continue;
                _items.Add(item);
                if (item.IsPending) _todos.Add(item);
            }

            _todos.Sort((a, b) => b.Status.CompareTo(a.Status));
        }

        private SocialSource _savedBefore;

        public void Draw()
        {
            if (_reloadPending)
            {
                _reloadPending = false;
                Reload();
            }

            if (_report == null) Reload();

            DrawActionsBar();

            using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scroll.scrollPosition;
                DrawInputs();
                DrawTodos();
                DrawDetails();
                DrawManualSteps();
            }
        }

        /// <summary>Luồng chạy hết: lưu JSON + ghi GameConstant. Không có gì để điền thì bỏ qua (true).</summary>
        public bool RunAll()
        {
            if (_source == null) Reload();
            if (string.IsNullOrEmpty(_source.discordInvite) && string.IsNullOrEmpty(_source.supportUrl)
                && string.IsNullOrEmpty(_source.supportEmail))
            {
                _message = "Bo qua: SocialConfig.json chua co link nao de ghi.";
                _messageStatus = EzgStatus.Warn;
                return true;
            }

            return SaveAndApply();
        }

        #endregion

        #region Draw

        private void DrawActionsBar()
        {
            var dirty = !_source.SameAs(_saved);
            EzgKitStyles.Banner(Headline + (dirty ? "  ·  Ô nhập đang khác file — bấm Lưu." : ""),
                dirty ? EzgStatus.Warn : Status);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (EzgKitStyles.PrimaryButton("Lưu + Ghi vào GameConstant.cs", GUILayout.Width(240f)))
                    ReadinessActions.Defer(() =>
                    {
                        SaveAndApply();
                        InternalEditorUtility.RepaintAllViews();
                    });

                if (EzgKitStyles.SecondaryButton("Chỉ lưu JSON", GUILayout.Width(120f)))
                    ReadinessActions.Defer(() =>
                    {
                        _source.Save();
                        _message = "Da luu ProjectSettings/SocialConfig.json (chua ghi GameConstant).";
                        _messageStatus = EzgStatus.Ok;
                        _reloadPending = true;
                        InternalEditorUtility.RepaintAllViews();
                    });

                using (new EditorGUI.DisabledScope(_current != null))
                {
                    if (EzgKitStyles.SecondaryButton(_current != null ? "Đang kiểm…" : "Kiểm Discord (invite + webhook)",
                            GUILayout.Width(230f)))
                        StartDiscordCheck();
                }

                GUILayout.FlexibleSpace();
            }

            if (!string.IsNullOrEmpty(_message))
            {
                var previous = GUI.contentColor;
                GUI.contentColor = EzgKitStyles.ColorOf(_messageStatus);
                EditorGUILayout.LabelField(_message, EditorStyles.wordWrappedMiniLabel);
                GUI.contentColor = previous;
            }

            EzgKitStyles.CollapsibleHelp("social-scope", "Tab này ghi gì, kiểm gì?",
                "GHI: ba const LinkDiscord / LinkSupport / SupportEmail trong GameConstant.cs (chưa có thì "
                + "tự thêm sau LinkFacebook). Code game (Settings: nút Discord / Support / Gmail) đọc từ đó.\n\n"
                + "KIỂM (chỉ đọc): mọi link đi vào build — fanpage, privacy, terms, link store (do tab "
                + "Marketing ghi từ sheet), rating Android/iOS, link còn HARDCODE trong script ngoài "
                + "GameConstant (đây là cách link của game khác đi theo template), webhook + bot token "
                + "Discord của BugLogger.\n\n"
                + "Kiểm Discord gọi API công khai: invite → tên server + số thành viên (link chết thì báo "
                + "đỏ), webhook → tên webhook (đã thu hồi thì báo đỏ). Không gọi khi mở tab.");
        }

        private void DrawInputs()
        {
            EzgKitStyles.SectionHeader("1. Điền link của dự án",
                "Lưu vào ProjectSettings/SocialConfig.json, rồi Ghi để vào GameConstant.cs.");

            _source.discordInvite = SetupGui.ManualField("Discord invite", _source.discordInvite,
                "Discord > server của game > chuột phải tên server > Invite People > Edit invite link > "
                + "Expire after: Never, Max uses: No limit > Generate. Copy link dạng https://discord.gg/xxxx. "
                + "Link có hạn là sau 7 ngày nút Discord trong game chết.",
                SetupGui.FieldNeed.Required, ("Mở Discord", "https://discord.com/channels/@me"));

            _source.supportUrl = SetupGui.ManualField("Support link", _source.supportUrl,
                "Form/trang hỗ trợ người chơi — Google Form (docs.google.com/forms > Send > link ngắn "
                + "forms.gle) hoặc trang support của studio. Nút Support trong Settings mở link này.",
                SetupGui.FieldNeed.Required, ("Google Forms", "https://docs.google.com/forms/"));

            _source.supportEmail = SetupGui.ManualField("Support email", _source.supportEmail,
                "Email nhận phản hồi. Nút Gmail trong Settings sẽ mở mailto: tới email này; để trống thì "
                + "nút chỉ mở Gmail trống như cũ.",
                SetupGui.FieldNeed.Optional);
        }

        private void DrawTodos()
        {
            EzgKitStyles.SectionHeader("2. Việc phải làm",
                _todos.Count == 0 ? null : "Đỏ trước, vàng sau. Mỗi dòng một hành động cụ thể.");

            using (new EzgKitStyles.CardScope())
            {
                if (_todos.Count == 0)
                {
                    EditorGUILayout.LabelField("Không còn việc — mọi link tool kiểm được đều xanh.", EzgKitStyles.Hint);
                    return;
                }

                for (var i = 0; i < _todos.Count; i++)
                {
                    var item = _todos[i];
                    if (i > 0) EzgKitStyles.Divider(2f);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EzgKitStyles.StatusIcon(item.Status);
                        GUILayout.Label(item.Label, EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                        if (!string.IsNullOrEmpty(item.Value))
                            EditorGUILayout.SelectableLabel(item.Value, EzgKitStyles.ValueStyle,
                                GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    }

                    if (!string.IsNullOrEmpty(item.Note)) Indented(item.Note, EzgKitStyles.Hint);
                    if (!string.IsNullOrEmpty(item.Fix)) Indented("→ " + item.Fix, EditorStyles.wordWrappedLabel);
                    ReadinessPage.DrawActions(item);
                }
            }
        }

        private void DrawDetails()
        {
            EzgKitStyles.SectionHeader("3. Toàn bộ link đi vào build");

            using (new EzgKitStyles.CardScope())
            {
                var pending = _todos.Count;
                var next = EzgKitStyles.CardFoldout(_detailsOpen, "Chi tiết", Status,
                    pending == 0 ? $"{_items.Count} mục" : $"{_items.Count} mục · {pending} cần xử lý");
                if (next != _detailsOpen) _detailsOpen = next;
                if (!next) return;

                foreach (var item in _items)
                {
                    EzgKitStyles.KeyValue(item.Label, item.Value, item.Status, item.Note,
                        item.Status == EzgStatus.None ? "không áp dụng" : "chưa có");
                    if (!item.IsPending) continue;
                    ReadinessPage.DrawActions(item);
                }
            }
        }

        private static void DrawManualSteps()
        {
            EzgKitStyles.SectionHeader("4. Việc tay ngoài Unity");

            SetupGui.ManualStep("Nối nút Discord trong màn Settings",
                "SettingsController đã có OnDiscord() đọc GameConstant.LinkDiscord; prefab screen_settings "
                + "cần một nút gọi hàm này (nút Support / Gmail đã nối sẵn). Tool không tự sửa prefab UI.");

            SetupGui.ManualStep("Bật kênh nhận bug report / feedback trên Discord",
                "Webhook trong BugLogger phải trỏ vào kênh của CHÍNH dự án; kênh forum cần tag. Kiểm bằng nút "
                + "Kiểm Discord ở trên — webhook đã thu hồi sẽ báo đỏ.",
                ("Discord", "https://discord.com/channels/@me"));
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

        private bool SaveAndApply()
        {
            try
            {
                _source.Save();
                if (!SocialChecks.Apply(_source, false, out var changes, out var error))
                {
                    _message = "Ghi GameConstant that bai: " + error;
                    _messageStatus = EzgStatus.Error;
                    _reloadPending = true;
                    return false;
                }

                _message = "Da luu JSON + ghi GameConstant.cs: " + string.Join(" · ", changes)
                           + ". Unity se recompile.";
                _messageStatus = EzgStatus.Ok;
                _reloadPending = true;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                _message = "Loi: " + exception.Message;
                _messageStatus = EzgStatus.Error;
                return false;
            }
        }

        /// <summary>Xếp hàng: 1 request invite (nếu có) + 1 request mỗi webhook; chạy tuần tự qua EditorApplication.update.</summary>
        private void StartDiscordCheck()
        {
            if (_current != null) return;

            _building = new DiscordLookup();
            _pending.Clear();

            var gameConstant = SocialChecks.FindGameConstant();
            var text = gameConstant == null ? null : System.IO.File.ReadAllText(gameConstant);
            var invite = SocialChecks.ReadConst(text, SocialChecks.CONST_DISCORD);
            if (string.IsNullOrEmpty(invite)) invite = _source.discordInvite;
            var code = SocialChecks.InviteCode(invite);
            if (code != null)
            {
                _building.InviteCode = code;
                _pending.Enqueue((UnityWebRequest.Get(API_INVITE + code + "?with_counts=true"), req =>
                {
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        _building.InviteError = req.responseCode == 404 ? "404 Unknown Invite" : req.error;
                        return;
                    }

                    var data = JsonUtility.FromJson<InviteResponse>(req.downloadHandler.text);
                    if (data?.guild == null)
                    {
                        _building.InviteError = "phan hoi khong co guild";
                        return;
                    }

                    _building.InviteOk = true;
                    _building.GuildName = data.guild.name;
                    _building.MemberCount = data.approximate_member_count;
                }));
            }

            foreach (var url in SocialChecks.FindWebhooks())
            {
                var captured = url;
                _pending.Enqueue((UnityWebRequest.Get(captured), req =>
                {
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        _building.WebhookErrors[captured] = req.responseCode == 404 ? "404 Unknown Webhook" : req.error;
                        return;
                    }

                    var data = JsonUtility.FromJson<WebhookResponse>(req.downloadHandler.text);
                    _building.WebhookNames[captured] = string.IsNullOrEmpty(data?.name) ? "(khong ten)" : data.name;
                }));
            }

            if (_pending.Count == 0)
            {
                _message = "Khong co link Discord nao de kiem (chua dien invite, khong co webhook).";
                _messageStatus = EzgStatus.Warn;
                return;
            }

            _message = $"Dang kiem {_pending.Count} link Discord…";
            _messageStatus = EzgStatus.None;
            EditorApplication.update += Poll;
            Next();
        }

        private void Next()
        {
            if (_pending.Count == 0)
            {
                EditorApplication.update -= Poll;
                _lookup = _building;
                _building = null;
                _message = $"Kiem xong: invite {(_lookup.InviteCode == null ? "khong co" : _lookup.InviteOk ? "OK" : "LOI")}, "
                           + $"webhook OK {_lookup.WebhookNames.Count} / loi {_lookup.WebhookErrors.Count}.";
                _messageStatus = _lookup.WebhookErrors.Count > 0 || (!string.IsNullOrEmpty(_lookup.InviteError))
                    ? EzgStatus.Error
                    : EzgStatus.Ok;
                _reloadPending = true;
                InternalEditorUtility.RepaintAllViews();
                return;
            }

            var (request, onDone) = _pending.Dequeue();
            _current = request;
            _currentOnDone = onDone;
            _current.timeout = 15;
            _current.SendWebRequest();
        }

        private void Poll()
        {
            if (_current == null)
            {
                Next();
                return;
            }

            if (!_current.isDone) return;

            try
            {
                _currentOnDone(_current);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                _current.Dispose();
                _current = null;
                _currentOnDone = null;
            }

            Next();
        }

        [Serializable]
        private sealed class InviteResponse
        {
            public InviteGuild guild;
            public int approximate_member_count;
        }

        [Serializable]
        private sealed class InviteGuild
        {
            public string name;
        }

        [Serializable]
        private sealed class WebhookResponse
        {
            public string name;
            public string channel_id;
        }

        #endregion
    }
}
#endif
