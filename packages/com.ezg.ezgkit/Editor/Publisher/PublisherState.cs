#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Ezg.Editor.Shared.Publisher
{
    /// <summary>
    ///     Dự án đang đi với nhà phát hành nào — ghi khi bấm "Sinh lại SDK theo nhà phát hành". Header bar
    ///     đọc để hiện ô "Phát hành": dev key AppsFlyer trong build là của tài khoản nào phải thấy ở mọi tab.
    ///     <para>
    ///         Nằm ở <c>ProjectSettings/PublisherConfig.json</c> cùng lý do với <c>SocialConfig.json</c>:
    ///         tool đi chung qua code-template, dữ liệu mỗi dự án một khác. Nên commit.
    ///     </para>
    /// </summary>
    [Serializable]
    internal class PublisherState
    {
        /// <summary>Id profile (<see cref="IPublisherProfile.Id" />) đã áp lần cuối. Rỗng = đang theo Ezg.</summary>
        public string activePublisher;

        /// <summary>Thời điểm áp lần cuối (UTC) — chỉ để hiện.</summary>
        public string appliedAtUtc;

        internal static string JsonPath =>
            Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                "ProjectSettings/PublisherConfig.json");

        internal static PublisherState Load()
        {
            if (!File.Exists(JsonPath)) return new PublisherState();

            try
            {
                return JsonUtility.FromJson<PublisherState>(File.ReadAllText(JsonPath)) ?? new PublisherState();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Publisher] PublisherConfig.json hong: {exception.Message}");
                return new PublisherState();
            }
        }

        internal void Save() =>
            File.WriteAllText(JsonPath, JsonUtility.ToJson(this, true), new UTF8Encoding(false));
    }
}
#endif
