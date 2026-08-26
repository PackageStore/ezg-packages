#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Ezg.Editor.Shared.Social
{
    /// <summary>
    ///     Link cộng đồng / hỗ trợ của dự án — nguồn cho tab Social ghi vào <c>GameConstant.cs</c>
    ///     (<c>LinkDiscord</c>, <c>LinkSupport</c>, <c>SupportEmail</c>).
    ///     <para>
    ///         Nằm ở <c>ProjectSettings/SocialConfig.json</c> cùng lý do với <c>MarketingConfig.json</c>:
    ///         tool dùng chung qua code-template, dữ liệu thì mỗi dự án một khác — để trong Assets là
    ///         merge template một lần là link của dự án này đè lên dự án kia. Ba link còn lại (fanpage,
    ///         privacy, terms, store) thuộc sheet marketing → tab Marketing ghi; tab Social chỉ ĐỌC để
    ///         kiểm.
    ///     </para>
    /// </summary>
    [Serializable]
    internal class SocialSource
    {
        #region Fields

        /// <summary>Link mời Discord (discord.gg/xxx hoặc discord.com/invite/xxx).</summary>
        public string discordInvite;

        /// <summary>Form / trang hỗ trợ người chơi (Google Form, Zendesk, trang web…).</summary>
        public string supportUrl;

        /// <summary>Email hỗ trợ — nút Gmail trong Settings mở <c>mailto:</c> tới đây.</summary>
        public string supportEmail;

        #endregion

        #region Paths

        internal static string JsonPath =>
            Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                "ProjectSettings/SocialConfig.json");

        #endregion

        #region Load / Save

        internal static SocialSource Load()
        {
            if (!File.Exists(JsonPath)) return new SocialSource();

            try
            {
                return JsonUtility.FromJson<SocialSource>(File.ReadAllText(JsonPath)) ?? new SocialSource();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Social] SocialConfig.json hong: {exception.Message}");
                return new SocialSource();
            }
        }

        internal void Save() =>
            File.WriteAllText(JsonPath, JsonUtility.ToJson(this, true), new UTF8Encoding(false));

        internal SocialSource Clone() => (SocialSource)MemberwiseClone();

        internal bool SameAs(SocialSource other) =>
            other != null
            && (discordInvite ?? "") == (other.discordInvite ?? "")
            && (supportUrl ?? "") == (other.supportUrl ?? "")
            && (supportEmail ?? "") == (other.supportEmail ?? "");

        #endregion
    }
}
#endif
