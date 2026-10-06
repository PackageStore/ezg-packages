#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Ezg.Editor.Shared.EzgKit;
using UnityEngine;

namespace Ezg.Editor.Shared.Social
{
    /// <summary>
    ///     Kiểm link Discord còn sống không (nút bấm ở trang Social — không tự chạy): invite qua
    ///     <c>discord.com/api/v10/invites/&lt;code&gt;?with_counts=true</c>, webhook qua GET chính URL webhook (không
    ///     cần auth). Chạy tuần tự, bất đồng bộ — Editor vẫn dùng được trong lúc chờ.
    /// </summary>
    internal static class DiscordProbe
    {
        private const string API_INVITE = "https://discord.com/api/v10/invites/";

        // JsonUtility gán qua reflection.
#pragma warning disable CS0649
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
        }
#pragma warning restore CS0649

        internal static bool Running { get; private set; }

        /// <summary>Bắt đầu kiểm; <paramref name="onDone" /> nhận kết quả (null = không có link nào để kiểm).</summary>
        internal static void Run(string invite, Action<DiscordLookup> onDone)
        {
            if (Running) return;
            var lookup = new DiscordLookup();
            var queue = new Queue<(string Url, Action<AsyncHttp.Response> Handle)>();

            var code = SocialChecks.InviteCode(invite);
            if (code != null)
            {
                lookup.InviteCode = code;
                queue.Enqueue((API_INVITE + code + "?with_counts=true", response =>
                {
                    if (!response.Ok)
                    {
                        lookup.InviteError = response.Code == 404 ? "404 Unknown Invite" : response.Error;
                        return;
                    }

                    var data = JsonUtility.FromJson<InviteResponse>(response.Body ?? "{}");
                    if (data?.guild == null)
                    {
                        lookup.InviteError = "phản hồi không có guild";
                        return;
                    }

                    lookup.InviteOk = true;
                    lookup.GuildName = data.guild.name;
                    lookup.MemberCount = data.approximate_member_count;
                }));
            }

            foreach (var url in SocialChecks.FindWebhooks())
            {
                var captured = url;
                queue.Enqueue((captured, response =>
                {
                    if (!response.Ok)
                    {
                        lookup.WebhookErrors[captured] = response.Code == 404 ? "404 Unknown Webhook" : response.Error;
                        return;
                    }

                    var data = JsonUtility.FromJson<WebhookResponse>(response.Body ?? "{}");
                    lookup.WebhookNames[captured] = string.IsNullOrEmpty(data?.name) ? "(không tên)" : data.name;
                }));
            }

            if (queue.Count == 0)
            {
                onDone?.Invoke(null);
                return;
            }

            Running = true;

            void Next()
            {
                if (queue.Count == 0)
                {
                    Running = false;
                    onDone?.Invoke(lookup);
                    return;
                }

                var (url, handle) = queue.Dequeue();
                AsyncHttp.Get(url, response =>
                {
                    try
                    {
                        handle(response);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }

                    Next();
                }, 15);
            }

            Next();
        }
    }
}
#endif
