#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Ezg.Editor.Shared.EzgKit;
using Ezg.Editor.Shared.Marketing;
using Ezg.Editor.Shared.Readiness;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Ezg.Editor.Shared.Social
{
    /// <summary>Kết quả tra Discord (nút bấm ở tab Social — không tự chạy khi mở tab).</summary>
    internal sealed class DiscordLookup
    {
        internal string InviteCode;
        internal bool InviteOk;
        internal string GuildName;
        internal int MemberCount;
        internal string InviteError;

        /// <summary>Webhook → tên/kênh (GET webhook không cần auth). Key = url webhook.</summary>
        internal readonly Dictionary<string, string> WebhookNames = new();

        internal readonly Dictionary<string, string> WebhookErrors = new();
    }

    /// <summary>
    ///     Kiểm link cộng đồng / hỗ trợ / rating đang THẬT SỰ nằm trong build — đọc const trong
    ///     <c>GameConstant.cs</c> bằng regex, so với <see cref="SocialSource" /> (JSON) và sheet
    ///     marketing, rồi quét toàn bộ script tìm link store/fanpage/support còn hardcode ngoài
    ///     GameConstant — đó chính là cách link của game khác đi theo template mà không ai thấy.
    ///     <para>
    ///         Kết quả đổ vào <see cref="ReadinessReport" /> nhóm <see cref="ReadinessGroup.Social" />,
    ///         nên tab Readiness cũng có nhóm này; tab Social vẽ cùng dữ liệu kèm ô nhập.
    ///     </para>
    /// </summary>
    internal static class SocialChecks
    {
        #region Constants

        private const ReadinessGroup G = ReadinessGroup.Social;

        internal const string CONST_DISCORD = "LinkDiscord";
        internal const string CONST_SUPPORT = "LinkSupport";
        internal const string CONST_SUPPORT_EMAIL = "SupportEmail";
        private const string CONST_FACEBOOK = "LinkFacebook";
        private const string CONST_PRIVACY = "LinkPrivacyPolicy";
        private const string CONST_TERMS = "LinkTermsOfService";
        private const string CONST_STORE_ANDROID = "LinkStoreFree";
        private const string CONST_STORE_IOS = "LinkStoreIos";
        private const string CONST_IOS_APP_ID = "IOSAppId";

        private const string PLAY_REVIEW_PLUGIN = "Assets/GooglePlayPlugins/com.google.play.review";
        private const string RATING_CSV_NAME = "RatingConfig";

        private const string URL_DISCORD_DEV = "https://discord.com/developers/applications";
        private const string URL_GOOGLE_FORMS = "https://docs.google.com/forms/";

        /// <summary>Link nào KHÔNG được hardcode ngoài GameConstant — bắt theo host.</summary>
        private static readonly Regex _hardcodedLink = new(
            "\"(https?://(?:play\\.google\\.com/store|apps\\.apple\\.com/|forms\\.gle/|(?:www\\.)?facebook\\.com/|discord\\.gg/|discord\\.com/invite/)[^\"]*)\"",
            RegexOptions.Compiled);

        /// <summary>Bot token Discord dạng <c>base64(id).timestamp.hmac</c> — nằm trong source là lộ.</summary>
        private static readonly Regex _botToken = new("\"[A-Za-z0-9_-]{23,28}\\.[A-Za-z0-9_-]{6,7}\\.[A-Za-z0-9_-]{25,}\"",
            RegexOptions.Compiled);

        private static readonly Regex _webhook = new("https://discord\\.com/api/webhooks/\\d+/[A-Za-z0-9_-]+",
            RegexOptions.Compiled);

        private static readonly Regex _email = new("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", RegexOptions.Compiled);

        #endregion

        #region Entry

        /// <summary>Đường dẫn tuyệt đối GameConstant.cs, null nếu dự án không có.</summary>
        internal static string FindGameConstant()
        {
            foreach (var guid in AssetDatabase.FindAssets("GameConstant t:Script"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path) == "GameConstant.cs")
                    return Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            }

            return null;
        }

        /// <summary>Giá trị const string trong text GameConstant; null = const không tồn tại, "" = rỗng.</summary>
        internal static string ReadConst(string text, string name)
        {
            if (text == null) return null;
            var match = Regex.Match(text, "public const string " + name + "\\s*=\\s*\"([^\"]*)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        internal static void Collect(ReadinessReport report, SocialSource source, DiscordLookup lookup)
        {
            var gameConstant = FindGameConstant();
            var text = gameConstant == null ? null : File.ReadAllText(gameConstant);
            var marketing = LoadMarketing();
            var socialTab = ReadinessActions.KitTab("Mở tab Social", EzgKitWindow.Tab.Social);
            var marketingTab = ReadinessActions.KitTab("Mở tab Marketing", EzgKitWindow.Tab.Marketing);

            if (text == null)
            {
                report.Add(new ReadinessItem(G, "GameConstant.cs", null, EzgStatus.Warn,
                    "Không có GameConstant.cs — không có chỗ chứa link cho code game đọc.",
                    "Tạo Features/_Shared/Config/GameConstant.cs theo template (const LinkDiscord/LinkSupport/SupportEmail/LinkFacebook/…)."));
            }
            else
            {
                CheckOwnedLink(report, text, gameConstant, source, lookup, socialTab);
                CheckMarketingLinks(report, text, gameConstant, marketing, marketingTab);
            }

            CheckRating(report, text, gameConstant);
            CheckHardcodedLinks(report, gameConstant, socialTab);
            CheckDiscordSecrets(report, lookup);
        }

        #endregion

        #region Owned by Social tab

        private static void CheckOwnedLink(ReadinessReport report, string text, string gameConstant,
            SocialSource source, DiscordLookup lookup, (string, Action) socialTab)
        {
            (string, Action) open(string name) =>
                ReadinessActions.OpenScript("Mở GameConstant.cs", gameConstant, name);

            // Discord
            var discord = ReadConst(text, CONST_DISCORD);
            var code = InviteCode(discord);
            if (discord == null)
                report.Add(MissingConst(CONST_DISCORD, gameConstant, socialTab));
            else if (string.IsNullOrEmpty(discord))
                report.Add(new ReadinessItem(G, "Discord invite", null, EzgStatus.Warn,
                        "Nút Discord trong game không mở gì.",
                        "Tab Social > điền link mời (Discord > Server > Invite People > Edit invite link: Never expire) > Lưu + Ghi.")
                    .With(socialTab, open(CONST_DISCORD)));
            else if (code == null)
                report.Add(new ReadinessItem(G, "Discord invite", discord, EzgStatus.Error,
                        "Không phải link mời Discord (cần discord.gg/<code> hoặc discord.com/invite/<code>).",
                        "Tab Social > sửa lại link mời > Lưu + Ghi.")
                    .With(socialTab, open(CONST_DISCORD)));
            else if (lookup != null && lookup.InviteCode == code && !string.IsNullOrEmpty(lookup.InviteError))
                report.Add(new ReadinessItem(G, "Discord invite", discord, EzgStatus.Error,
                        "Discord trả lỗi cho link này: " + lookup.InviteError + " (hết hạn / bị xoá?).",
                        "Tạo invite mới KHÔNG hết hạn (Edit invite link > Expire after: Never) rồi Tab Social > Lưu + Ghi.")
                    .With(socialTab, open(CONST_DISCORD)));
            else if (lookup != null && lookup.InviteCode == code && lookup.InviteOk)
                report.Add(new ReadinessItem(G, "Discord invite", discord, EzgStatus.Ok,
                    $"Server \"{lookup.GuildName}\" · ~{lookup.MemberCount} thành viên"));
            else if (source != null && !string.IsNullOrEmpty(source.discordInvite) && source.discordInvite != discord)
                report.Add(new ReadinessItem(G, "Discord invite", discord, EzgStatus.Warn,
                        $"SocialConfig.json có `{source.discordInvite}` nhưng chưa ghi vào GameConstant.",
                        "Tab Social > Ghi vào GameConstant.cs.")
                    .With(socialTab));
            else
                report.Add(new ReadinessItem(G, "Discord invite", discord, EzgStatus.Ok,
                        "Chưa xác minh với Discord — bấm \"Kiểm Discord\" ở tab Social để chắc link còn sống.")
                    .With(socialTab));

            // Support URL
            var support = ReadConst(text, CONST_SUPPORT);
            if (support == null)
                report.Add(MissingConst(CONST_SUPPORT, gameConstant, socialTab));
            else if (string.IsNullOrEmpty(support))
                report.Add(new ReadinessItem(G, "Support link", null, EzgStatus.Warn,
                        "Nút Support trong Settings không mở gì.",
                        "Tab Social > điền link form/trang hỗ trợ (Google Form, Zendesk…) > Lưu + Ghi.",
                        ("Google Forms", URL_GOOGLE_FORMS))
                    .With(socialTab, open(CONST_SUPPORT)));
            else if (!IsHttp(support))
                report.Add(new ReadinessItem(G, "Support link", support, EzgStatus.Error,
                        "Không phải URL http(s).", "Tab Social > sửa link > Lưu + Ghi.")
                    .With(socialTab, open(CONST_SUPPORT)));
            else if (source != null && !string.IsNullOrEmpty(source.supportUrl) && source.supportUrl != support)
                report.Add(new ReadinessItem(G, "Support link", support, EzgStatus.Warn,
                        $"SocialConfig.json có `{source.supportUrl}` nhưng chưa ghi vào GameConstant.",
                        "Tab Social > Ghi vào GameConstant.cs.")
                    .With(socialTab));
            else
                report.Add(new ReadinessItem(G, "Support link", support,
                        source == null || string.IsNullOrEmpty(source.supportUrl) ? EzgStatus.Warn : EzgStatus.Ok,
                        source == null || string.IsNullOrEmpty(source.supportUrl)
                            ? "GameConstant có link nhưng SocialConfig.json chưa có — không rõ link này của dự án này hay đi theo template."
                            : null,
                        source == null || string.IsNullOrEmpty(source.supportUrl)
                            ? "Tab Social > xác nhận link đúng là của dự án này (điền lại vào ô Support) > Lưu."
                            : null)
                    .With(socialTab));

            // Support email
            var email = ReadConst(text, CONST_SUPPORT_EMAIL);
            if (email == null)
                report.Add(MissingConst(CONST_SUPPORT_EMAIL, gameConstant, socialTab));
            else if (string.IsNullOrEmpty(email))
                report.Add(new ReadinessItem(G, "Support email", null, EzgStatus.Warn,
                        "Nút Gmail trong Settings chỉ mở Gmail trống, không tới địa chỉ nào.",
                        "Tab Social > điền email hỗ trợ > Lưu + Ghi (nút sẽ mở mailto: tới email này).")
                    .With(socialTab, open(CONST_SUPPORT_EMAIL)));
            else if (!_email.IsMatch(email))
                report.Add(new ReadinessItem(G, "Support email", email, EzgStatus.Error,
                        "Không phải địa chỉ email.", "Tab Social > sửa email > Lưu + Ghi.")
                    .With(socialTab, open(CONST_SUPPORT_EMAIL)));
            else if (source != null && !string.IsNullOrEmpty(source.supportEmail) && source.supportEmail != email)
                report.Add(new ReadinessItem(G, "Support email", email, EzgStatus.Warn,
                        $"SocialConfig.json có `{source.supportEmail}` nhưng chưa ghi vào GameConstant.",
                        "Tab Social > Ghi vào GameConstant.cs.")
                    .With(socialTab));
            else
                report.Add(new ReadinessItem(G, "Support email", email, EzgStatus.Ok).With(socialTab));
        }

        private static ReadinessItem MissingConst(string name, string gameConstant, (string, Action) socialTab) =>
            new ReadinessItem(G, name, null, EzgStatus.Warn,
                    $"GameConstant.cs chưa có `public const string {name}` — tab Social không có chỗ ghi.",
                    $"Bấm \"Ghi vào GameConstant.cs\" ở tab Social: tool tự thêm const `{name}` ngay sau `LinkFacebook`.")
                .With(socialTab, ReadinessActions.OpenScript("Mở GameConstant.cs", gameConstant, "LinkFacebook"));

        #endregion

        #region Owned by Marketing sheet (read-only here)

        private static void CheckMarketingLinks(ReadinessReport report, string text, string gameConstant,
            MarketingConfig marketing, (string, Action) marketingTab)
        {
            var androidId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) ?? "";
            (string, Action) open(string name) =>
                ReadinessActions.OpenScript("Mở GameConstant.cs", gameConstant, name);

            // Fanpage
            var facebook = ReadConst(text, CONST_FACEBOOK);
            var sheetFacebook = marketing?.links?.facebookPage;
            if (string.IsNullOrEmpty(facebook))
                report.Add(new ReadinessItem(G, "Fanpage (LinkFacebook)", null, EzgStatus.Warn,
                        "Rỗng — nút fanpage (nếu có) không mở gì.",
                        "Điền links.facebookPage trong sheet marketing rồi tab Marketing > ghi.")
                    .With(marketingTab, open(CONST_FACEBOOK)));
            else if (!string.IsNullOrEmpty(sheetFacebook) && sheetFacebook != facebook)
                report.Add(new ReadinessItem(G, "Fanpage (LinkFacebook)", facebook, EzgStatus.Warn,
                        $"Khác sheet marketing (`{sheetFacebook}`).", "Tab Marketing > ghi lại GameConstant.")
                    .With(marketingTab, open(CONST_FACEBOOK)));
            else if (string.IsNullOrEmpty(sheetFacebook))
                report.Add(new ReadinessItem(G, "Fanpage (LinkFacebook)", facebook, EzgStatus.Warn,
                        "Sheet marketing chưa có fanpage nên không đối chiếu được — link này có thể của game khác đi theo template.",
                        "Điền links.facebookPage trong sheet marketing rồi tab Marketing > ghi.")
                    .With(marketingTab, open(CONST_FACEBOOK)));
            else
                report.Add(new ReadinessItem(G, "Fanpage (LinkFacebook)", facebook, EzgStatus.Ok).With(marketingTab));

            // Privacy / Terms — Apple/Google soi khi review, MAX consent flow cũng dùng.
            foreach (var (name, label, sheet) in new[]
                     {
                         (CONST_PRIVACY, "Privacy policy", marketing?.applovin?.privacyPolicyUrl),
                         (CONST_TERMS, "Terms of service", marketing?.applovin?.termsOfServiceUrl),
                     })
            {
                var value = ReadConst(text, name);
                if (string.IsNullOrEmpty(value) || !IsHttp(value))
                    report.Add(new ReadinessItem(G, label, value, EzgStatus.Error,
                            "Store review yêu cầu link công khai; MAX consent flow cũng mở link này.",
                            "Điền applovin.privacyPolicyUrl / termsOfServiceUrl trong sheet marketing rồi tab Marketing > ghi.")
                        .With(marketingTab, open(name)));
                else if (!string.IsNullOrEmpty(sheet) && sheet != value)
                    report.Add(new ReadinessItem(G, label, value, EzgStatus.Warn,
                            $"Khác sheet marketing (`{sheet}`).", "Tab Marketing > ghi lại GameConstant.")
                        .With(marketingTab, open(name)));
                else
                    report.Add(new ReadinessItem(G, label, value, EzgStatus.Ok).With(marketingTab));
            }

            // Store links — id trong link phải là CHÍNH app này.
            var storeAndroid = ReadConst(text, CONST_STORE_ANDROID);
            if (string.IsNullOrEmpty(storeAndroid))
                report.Add(new ReadinessItem(G, "Link store Android", null, EzgStatus.Warn,
                        "Rỗng — nút rate/update Android không mở gì.", "Tab Marketing > ghi (tự dựng từ package name).")
                    .With(marketingTab, open(CONST_STORE_ANDROID)));
            else if (!string.IsNullOrEmpty(androidId) && !storeAndroid.Contains("id=" + androidId))
                report.Add(new ReadinessItem(G, "Link store Android", storeAndroid, EzgStatus.Error,
                        $"Link trỏ app khác — package name hiện tại là `{androidId}`.",
                        "Tab Marketing > ghi lại GameConstant (LinkStoreFree dựng từ package name).")
                    .With(marketingTab, open(CONST_STORE_ANDROID)));
            else
                report.Add(new ReadinessItem(G, "Link store Android", storeAndroid, EzgStatus.Ok).With(marketingTab));

            var storeIos = ReadConst(text, CONST_STORE_IOS);
            var iosAppId = ReadConst(text, CONST_IOS_APP_ID);
            var linkId = Match(storeIos, "/id(\\d+)");
            if (string.IsNullOrEmpty(storeIos))
                report.Add(new ReadinessItem(G, "Link store iOS", null, EzgStatus.Warn,
                        "Rỗng — rating iOS (fallback) và nút update không mở gì.",
                        "Điền appleId trong sheet marketing rồi tab Marketing > ghi (link dựng từ Apple ID).")
                    .With(marketingTab, open(CONST_STORE_IOS)));
            else if (!string.IsNullOrEmpty(iosAppId) && linkId != null && linkId != iosAppId)
                report.Add(new ReadinessItem(G, "Link store iOS", storeIos, EzgStatus.Error,
                        $"Id trong link (`{linkId}`) khác IOSAppId (`{iosAppId}`) — một trong hai là của app khác đi theo template.",
                        "Điền appleId THẬT (ASC > App Information) trong sheet marketing rồi tab Marketing > ghi — cả IOSAppId và LinkStoreIos sẽ cùng theo sheet.")
                    .With(marketingTab, open(CONST_STORE_IOS)));
            else
                report.Add(new ReadinessItem(G, "Link store iOS", storeIos,
                        string.IsNullOrEmpty(marketing?.appleId) ? EzgStatus.Warn : EzgStatus.Ok,
                        string.IsNullOrEmpty(marketing?.appleId) ? "Sheet marketing chưa có appleId nên chưa xác minh được id trong link." : null,
                        string.IsNullOrEmpty(marketing?.appleId) ? "Điền appleId trong sheet marketing rồi tab Marketing > ghi." : null)
                    .With(marketingTab));
        }

        #endregion

        #region Rating

        private static void CheckRating(ReadinessReport report, string text, string gameConstant)
        {
            var pluginExists = AssetDatabase.IsValidFolder(PLAY_REVIEW_PLUGIN);
            report.Add(new ReadinessItem(G, "Rating Android (in-app review)",
                    pluginExists ? "Google Play In-App Review" : null,
                    pluginExists ? EzgStatus.Ok : EzgStatus.Warn,
                    pluginExists ? null : "Thiếu plugin com.google.play.review — ReviewClient không compile / không mở hộp đánh giá.",
                    pluginExists ? null : "Cài Google Play Plugins for Unity (In-App Review) vào Assets/GooglePlayPlugins.",
                    ("Play In-App Review", "https://developer.android.com/guide/playcore/in-app-review/unity")));

            // iOS: RequestStoreReview + fallback LinkStoreIos (đã kiểm ở trên).
            report.Add(new ReadinessItem(G, "Rating iOS", "SKStoreReviewController → fallback LinkStoreIos",
                EzgStatus.None, "Fallback dùng Link store iOS — trạng thái xem dòng đó."));

            // Tần suất hỏi lại.
            string csvPath = null;
            foreach (var guid in AssetDatabase.FindAssets(RATING_CSV_NAME + " t:TextAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path) == RATING_CSV_NAME + ".csv")
                {
                    csvPath = path;
                    break;
                }
            }

            if (csvPath == null) return;
            var csv = File.ReadAllText(Path.Combine(Application.dataPath, "..", csvPath));
            var seconds = Match(csv, "time_next_rating\\s*[\\r\\n]+\\s*(\\d+)");
            var item = new ReadinessItem(G, "Rating: hỏi lại sau", seconds == null ? null : $"{seconds}s (~{SecondsToHuman(seconds)})",
                seconds == null ? EzgStatus.Warn : EzgStatus.None,
                seconds == null ? "RatingConfig.csv không có time_next_rating." : "Đổi trong CSV rồi import lại.",
                seconds == null ? "Mở RatingConfig.csv, thêm dòng time_next_rating." : null);
            report.Add(item.With(ReadinessActions.SelectAsset("Mở RatingConfig.csv", csvPath)));
        }

        private static string SecondsToHuman(string seconds)
        {
            if (!long.TryParse(seconds, out var s)) return "?";
            if (s >= 86400) return $"{s / 86400.0:0.#} ngày";
            if (s >= 3600) return $"{s / 3600.0:0.#} giờ";
            return $"{s / 60.0:0.#} phút";
        }

        #endregion

        #region Scan source

        /// <summary>
        ///     Link store/fanpage/support/discord nằm ngoài GameConstant là link không ai kiểm — quét
        ///     mọi script runtime trong Assets (bỏ Editor/ và GameConstant.cs).
        /// </summary>
        private static void CheckHardcodedLinks(ReadinessReport report, string gameConstant, (string, Action) socialTab)
        {
            var root = Application.dataPath;
            var hits = 0;
            // Quét trên SourceIndex (text .cs cache trong RAM) — không đọc đĩa mỗi Reload.
            foreach (var source in SourceIndex.Files)
            {
                var file = source.Absolute;
                var normalized = file.Replace('\\', '/');
                if (source.IsEditor || normalized.EndsWith("/GameConstant.cs")) continue;

                var text = source.Text;
                if (!text.Contains("http")) continue;
                var lines = text.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    var trimmed = lines[i].TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("*")) continue;
                    var match = _hardcodedLink.Match(trimmed);
                    if (!match.Success) continue;

                    hits++;
                    var url = match.Groups[1].Value;
                    var relative = normalized.Substring(root.Length + 1);
                    report.Add(new ReadinessItem(G, $"Link hardcode: {Path.GetFileName(file)}:{i + 1}", url, EzgStatus.Warn,
                            $"Link nằm ngoài GameConstant ({relative}) — không tool nào kiểm, dễ là link của game khác đi theo template.",
                            "Chuyển link vào một const trong GameConstant (LinkStoreFree/LinkStoreIos/LinkFacebook/LinkSupport/LinkDiscord) và đọc từ đó.")
                        .With(ReadinessActions.OpenScript("Mở đúng dòng", file, Regex.Escape(url))));
                }
            }

            if (hits == 0)
                report.Add(new ReadinessItem(G, "Link hardcode ngoài GameConstant", "0", EzgStatus.Ok,
                    "Mọi link store/fanpage/support/discord trong script runtime đều đi qua GameConstant."));
        }

        /// <summary>
        ///     Webhook Discord (bug report / feedback) và bot token. Webhook nằm trong source là chấp
        ///     nhận được (ai có cũng chỉ post được vào kênh đó); bot token thì KHÔNG — nó là quyền của
        ///     cả bot trên mọi server bot tham gia.
        /// </summary>
        private static void CheckDiscordSecrets(ReadinessReport report, DiscordLookup lookup)
        {
            var root = Application.dataPath;
            var webhooks = new List<(string Url, string File)>();
            var tokens = new List<(string File, int Line)>();

            foreach (var source in SourceIndex.Files)
            {
                if (source.IsEditor) continue;
                var file = source.Absolute;
                var text = source.Text;
                if (!text.Contains("discord")) continue;
                foreach (Match m in _webhook.Matches(text)) webhooks.Add((m.Value, file));

                var lines = text.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    var trimmed = lines[i].TrimStart();
                    if (trimmed.StartsWith("//")) continue;
                    if (_botToken.IsMatch(trimmed) && !trimmed.Contains("webhooks/")) tokens.Add((file, i + 1));
                }
            }

            if (webhooks.Count == 0)
                report.Add(new ReadinessItem(G, "Discord webhook (bug report / feedback)", null, EzgStatus.None,
                    "Không có webhook Discord trong source — dự án không dùng BugLogger Discord."));
            else
                foreach (var (url, file) in webhooks)
                {
                    var shortUrl = url.Substring(0, Math.Min(url.Length, 52)) + "…";
                    string note = null;
                    var status = EzgStatus.Ok;
                    if (lookup != null && lookup.WebhookErrors.TryGetValue(url, out var error))
                    {
                        status = EzgStatus.Error;
                        note = "Discord trả lỗi: " + error + " — webhook đã bị xoá/thu hồi, bug report gửi vào hư không.";
                    }
                    else if (lookup != null && lookup.WebhookNames.TryGetValue(url, out var name))
                        note = "Webhook \"" + name + "\"";
                    else
                        note = "Chưa xác minh — bấm \"Kiểm Discord\" ở tab Social.";

                    report.Add(new ReadinessItem(G, $"Discord webhook: {Path.GetFileName(file)}", shortUrl, status, note,
                            status == EzgStatus.Error ? "Tạo webhook mới trên kênh Discord (Channel > Integrations > Webhooks) rồi thay trong file." : null)
                        .With(ReadinessActions.OpenScript("Mở file", file, "api/webhooks")));
                }

            foreach (var (file, line) in tokens)
                report.Add(new ReadinessItem(G, $"Bot token Discord trong source: {Path.GetFileName(file)}:{line}", "***",
                        EzgStatus.Error,
                        "Token bot nằm trong script = ai có build/repo cũng điều khiển được bot trên MỌI server nó tham gia.",
                        "Thu hồi token (Discord Developer Portal > Bot > Reset Token), rồi chuyển việc tạo thread sang server/Cloudflare Worker hoặc nạp token từ remote config — KHÔNG để trong source.",
                        ("Developer Portal", URL_DISCORD_DEV))
                    .With(ReadinessActions.OpenScript("Mở đúng dòng", file, "_botToken|Bot ")));
        }

        #endregion

        #region Apply (ghi GameConstant)

        /// <summary>
        ///     Ghi ba const social vào GameConstant.cs. Const chưa có thì CHÈN ngay sau dòng
        ///     <c>LinkFacebook</c> (mỏ neo ổn định của template). Giá trị rỗng trong JSON KHÔNG xoá const
        ///     đang có (chỉ chèn const mới rỗng khi thiếu hẳn). Trả về danh sách thay đổi để hiện; lỗi
        ///     thì trả false + message.
        /// </summary>
        internal static bool Apply(SocialSource source, bool dryRun, out List<string> changes, out string error)
        {
            changes = new List<string>();
            error = null;

            var path = FindGameConstant();
            if (path == null || !File.Exists(path))
            {
                error = "Khong tim thay GameConstant.cs trong du an.";
                return false;
            }

            var bytes = File.ReadAllBytes(path);
            var hadBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var text = new UTF8Encoding(false).GetString(bytes, hadBom ? 3 : 0, bytes.Length - (hadBom ? 3 : 0));

            foreach (var (name, value) in new[]
                     {
                         (CONST_DISCORD, source.discordInvite ?? ""),
                         (CONST_SUPPORT, source.supportUrl ?? ""),
                         (CONST_SUPPORT_EMAIL, source.supportEmail ?? ""),
                     })
            {
                var regex = new Regex("(public const string " + name + "\\s*=\\s*\")([^\"]*)(\";)");
                var match = regex.Match(text);
                if (match.Success)
                {
                    var current = match.Groups[2].Value;
                    if (current == value)
                    {
                        changes.Add($"{name}: giu nguyen ({Display(value)})");
                        continue;
                    }

                    // JSON rỗng thì GIỮ giá trị đang có, không xoá trắng — cùng luật với applier Marketing:
                    // ô chưa điền không được hiểu là "xoá". Muốn xoá thật thì sửa tay trong GameConstant.
                    if (string.IsNullOrEmpty(value))
                    {
                        changes.Add($"{name}: giu nguyen {Display(current)} (JSON rong)");
                        continue;
                    }

                    changes.Add($"{name}: {Display(current)} -> {Display(value)}");
                    text = text.Remove(match.Groups[2].Index, current.Length).Insert(match.Groups[2].Index, value);
                    continue;
                }

                // Chưa có const → chèn sau LinkFacebook (giữ indent của dòng đó).
                var anchor = Regex.Match(text, "([ \\t]*)public const string LinkFacebook\\s*=[^;]*;[^\\n]*\\n");
                if (!anchor.Success)
                {
                    error = $"GameConstant.cs khong co const {name} va cung khong co LinkFacebook de chen sau — them tay:\n"
                            + $"public const string {name} = \"\";";
                    return false;
                }

                var indent = anchor.Groups[1].Value;
                var insertAt = anchor.Index + anchor.Length;
                text = text.Insert(insertAt, $"{indent}public const string {name} = \"{value}\";\n");
                changes.Add($"{name}: THEM MOI = {Display(value)}");
            }

            if (dryRun) return true;

            File.WriteAllText(path, text, new UTF8Encoding(hadBom));
            var assetPath = "Assets" + path.Replace('\\', '/').Substring(Application.dataPath.Length);
            AssetDatabase.ImportAsset(assetPath);
            return true;
        }

        private static string Display(string value) => string.IsNullOrEmpty(value) ? "(rong)" : value;

        #endregion

        #region Helpers

        /// <summary>Mã mời từ link discord.gg/<c>code</c> hoặc discord.com/invite/<c>code</c>; null = không phải link mời.</summary>
        internal static string InviteCode(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var m = Regex.Match(url, "^https?://(?:www\\.)?(?:discord\\.gg|discord\\.com/invite|discordapp\\.com/invite)/([A-Za-z0-9-]+)/?(?:\\?.*)?$");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>Mọi webhook Discord trong script runtime — cho nút Kiểm Discord.</summary>
        internal static List<string> FindWebhooks()
        {
            var result = new List<string>();
            foreach (var source in SourceIndex.Files)
            {
                if (source.IsEditor) continue;
                var text = source.Text;
                if (!text.Contains("api/webhooks")) continue;
                foreach (Match m in _webhook.Matches(text))
                    if (!result.Contains(m.Value)) result.Add(m.Value);
            }

            return result;
        }

        private static bool IsHttp(string url) =>
            !string.IsNullOrEmpty(url)
            && (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

        private static string Match(string text, string pattern)
        {
            if (text == null) return null;
            var match = Regex.Match(text, pattern);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        private static MarketingConfig LoadMarketing()
        {
            try
            {
                var path = MarketingConfig.JsonPath;
                return File.Exists(path) ? JsonUtility.FromJson<MarketingConfig>(File.ReadAllText(path)) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion
    }
}
#endif
