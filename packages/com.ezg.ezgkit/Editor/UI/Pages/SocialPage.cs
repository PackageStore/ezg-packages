#if UNITY_EDITOR
using System.Collections.Generic;
using Ezg.Editor.Shared.Readiness;
using Ezg.Editor.Shared.Social;
using Ezg.Editor.Shared.Setup;
using UnityEngine.UIElements;

namespace Ezg.Editor.Shared.EzgKit.Pages
{
    /// <summary>
    ///     Social (Nâng cao): link cộng đồng / hỗ trợ (Discord invite, trang support, email) → const trong
    ///     GameConstant.cs, cộng bộ quét link store / fanpage / Discord còn hardcode ngoài GameConstant và bot token
    ///     Discord bị lộ trong source / asset.
    /// </summary>
    internal sealed class SocialPage : SetupPage
    {
        private const string K_DISCORD = "discordInvite";
        private const string K_SUPPORT = "supportUrl";
        private const string K_EMAIL = "supportEmail";

        private TextField _discord, _support, _email;
        private DiscordLookup _lookup;

        internal override string Id => PageIds.SOCIAL;

        internal override string Title => "Social";

        internal override string Description =>
            "Discord / trang hỗ trợ / email hỗ trợ, quét link hardcode và token Discord bị lộ trong source.";

        internal override string Group => GROUP_ADVANCED;

        internal override bool CanApply => true;

        internal override string ApplyWarning => "Ghi GameConstant.cs → Unity recompile.";

        #region Core

        private ReadinessReport Collect()
        {
            var report = new ReadinessReport();
            SocialChecks.Collect(report, SocialSource.Load(), _lookup);
            return report;
        }

        internal override PageReport Detect()
        {
            var page = new PageReport();
            foreach (var item in Collect().Items)
            {
                if (item.Status == EzgStatus.Ok) page.Ok();
                else if (item.IsPending) page.Add(item.Status, item.Label + (string.IsNullOrEmpty(item.Note) ? string.Empty : " — " + item.Note), item.Fix);
            }

            var source = SocialSource.Load();
            return page.Resolve(string.IsNullOrEmpty(source.discordInvite) && string.IsNullOrEmpty(source.supportUrl)
                ? "Chưa khai link cộng đồng / hỗ trợ."
                : "Đã khai link cộng đồng / hỗ trợ.");
        }

        internal override JsonObject GetValues(bool maskSecrets)
        {
            var source = SocialSource.Load();
            return new JsonObject()
                .Set(K_DISCORD, source.discordInvite ?? string.Empty)
                .Set(K_SUPPORT, source.supportUrl ?? string.Empty)
                .Set(K_EMAIL, source.supportEmail ?? string.Empty);
        }

        internal override ApplyResult Apply(JsonObject values, bool dryRun)
        {
            var result = new ApplyResult { DryRun = dryRun };
            var source = SocialSource.Load();
            var next = source.Clone();
            if (values.Has(K_DISCORD)) next.discordInvite = values.Str(K_DISCORD).Trim();
            if (values.Has(K_SUPPORT)) next.supportUrl = values.Str(K_SUPPORT).Trim();
            if (values.Has(K_EMAIL)) next.supportEmail = values.Str(K_EMAIL).Trim();

            if (!string.IsNullOrEmpty(next.discordInvite) && SocialChecks.InviteCode(next.discordInvite) == null)
                return ApplyResult.Fail("Link Discord phải dạng discord.gg/<code> hoặc discord.com/invite/<code>.");
            var urlError = Validate.Url(next.supportUrl);
            if (urlError != null) return ApplyResult.Fail("Support link: " + urlError);
            var emailError = Validate.Email(next.supportEmail);
            if (emailError != null) return ApplyResult.Fail("Support email: " + emailError);

            result.Rows.Add(new ChangeRow("SocialConfig.json", K_DISCORD, source.discordInvite, next.discordInvite, (source.discordInvite ?? "") == (next.discordInvite ?? "")));
            result.Rows.Add(new ChangeRow("SocialConfig.json", K_SUPPORT, source.supportUrl, next.supportUrl, (source.supportUrl ?? "") == (next.supportUrl ?? "")));
            result.Rows.Add(new ChangeRow("SocialConfig.json", K_EMAIL, source.supportEmail, next.supportEmail, (source.supportEmail ?? "") == (next.supportEmail ?? "")));
            if (!dryRun && !source.SameAs(next)) next.Save();

            if (!SocialChecks.Apply(next, dryRun, out var changes, out var error))
            {
                result.Notes.Add(error);
                return result;
            }

            // SocialChecks.Apply trả câu dạng "Const: cũ -> mới" / "Const: giu nguyen (x)" / "Const: THEM MOI = x".
            foreach (var change in changes)
            {
                var parts = change.Split(new[] { ':' }, 2);
                var field = parts[0].Trim();
                var rest = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                if (rest.StartsWith("giu nguyen"))
                    result.Rows.Add(new ChangeRow("GameConstant.cs", field, rest, rest, true));
                else if (rest.StartsWith("THEM MOI"))
                    result.Rows.Add(new ChangeRow("GameConstant.cs", field, "(chưa có const)", rest.Substring("THEM MOI".Length).TrimStart(' ', '=').Trim(), false));
                else
                {
                    var arrow = rest.IndexOf("->", System.StringComparison.Ordinal);
                    result.Rows.Add(arrow < 0
                        ? new ChangeRow("GameConstant.cs", field, string.Empty, rest, false)
                        : new ChangeRow("GameConstant.cs", field, rest.Substring(0, arrow).Trim(), rest.Substring(arrow + 2).Trim(), false));
                }
            }

            return result;
        }

        #endregion

        #region UI

        internal override void Build(VisualElement body, IPageHost host)
        {
            var values = GetValues(false);
            var links = Ui.Card(body, "Link cộng đồng / hỗ trợ",
                "Lưu ở ProjectSettings/SocialConfig.json, ghi vào const LinkDiscord / LinkSupport / SupportEmail trong GameConstant.cs (thiếu const thì chèn sau LinkFacebook).");
            _discord = Ui.TextRow(links, "Discord invite", values.Str(K_DISCORD), "Invite KHÔNG hết hạn (Edit invite link > Expire after: Never).",
                v => string.IsNullOrEmpty(v) || SocialChecks.InviteCode(v) != null ? null : "Cần discord.gg/<code> hoặc discord.com/invite/<code>.");
            _support = Ui.TextRow(links, "Trang hỗ trợ", values.Str(K_SUPPORT), "Google Form, Zendesk, trang web…", v => Validate.Url(v));
            _email = Ui.TextRow(links, "Email hỗ trợ", values.Str(K_EMAIL), "Nút Gmail trong Settings mở mailto: tới đây.", Validate.Email);
            Ui.Button(Ui.Row(links, "ezg-actions"), DiscordProbe.Running ? "Đang kiểm Discord…" : "Kiểm Discord", () =>
            {
                DiscordProbe.Run(_discord.value, lookup =>
                {
                    if (lookup == null)
                    {
                        host.Toast("Không có link Discord nào để kiểm (chưa có invite, không có webhook).", EzgStatus.Warn);
                        return;
                    }

                    _lookup = lookup;
                    host.RefreshAll();
                    host.Rebuild();
                    host.Toast($"Kiểm xong: invite {(lookup.InviteCode == null ? "không có" : lookup.InviteOk ? "OK" : "LỖI")}, webhook OK {lookup.WebhookNames.Count} / lỗi {lookup.WebhookErrors.Count}.",
                        lookup.WebhookErrors.Count > 0 || !string.IsNullOrEmpty(lookup.InviteError) ? EzgStatus.Error : EzgStatus.Ok);
                });
            }, "secondary", "Gọi API Discord kiểm invite còn sống + webhook còn hợp lệ (không cần đăng nhập).");

            var scan = Ui.Card(body, "Kiểm tra", "Link store / fanpage / rating, link hardcode ngoài GameConstant, webhook và bot token Discord.");
            var items = new List<ReadinessItem>(Collect().Items);
            items.Sort((a, b) => b.Status.CompareTo(a.Status));
            foreach (var item in items) OverviewPage.ReadinessRow(scan, item);
        }

        internal override JsonObject CollectUi() =>
            new JsonObject()
                .Set(K_DISCORD, _discord.value)
                .Set(K_SUPPORT, _support.value)
                .Set(K_EMAIL, _email.value);

        #endregion
    }
}
#endif
