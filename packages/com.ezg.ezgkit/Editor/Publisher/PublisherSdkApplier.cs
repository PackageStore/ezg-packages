#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace Ezg.Editor.Shared.Publisher
{
    /// <summary>
    ///     Phần "ghi ID" của nút "Chuyển sang {publisher}": ghi mọi ID PUBLISHER CẤP SẴN
    ///     (<see cref="SdkIdSlot.PublisherValue" />) mà <see cref="PublisherIdWriter" /> biết chỗ ghi — dev key
    ///     AppsFlyer của Neptune vào <c>GameConstant</c>, app id Meta từ sheet marketing vào FacebookSettings…
    ///     ID game tự tạo (Meta app id của Neptune, GA key) KHÔNG ghi ở đây — dev gõ vào ô trên
    ///     <see cref="PublisherPage" /> rồi bấm "Điền". ID ngoài Unity (Partner ID) chỉ hiện.
    ///     <para>Chỉ ghi ID của SDK ĐÃ gắn: SDK vừa được kế hoạch cài thì file config chưa tồn tại lúc này.</para>
    ///     <para>Gọi qua <c>ReadinessActions.Defer</c>. Chuỗi trong <paramref name="changes" /> không dấu (đi vào dialog).</para>
    /// </summary>
    internal static class PublisherSdkApplier
    {
        /// <summary>
        ///     Ghi các ID publisher cấp. <paramref name="dryRun" /> = chỉ liệt kê. Trả về false khi không có
        ///     gì ghi được (profile không có ID cấp sẵn ghi máy được, thiếu file) — lý do trong <paramref name="error" />.
        /// </summary>
        internal static bool Apply(IPublisherProfile profile, List<SdkReport> reports, bool dryRun,
            out List<string> changes, out string error)
        {
            changes = new List<string>();
            error = null;

            var entries = new List<PublisherIdWriter.Entry>();
            foreach (var report in reports)
            {
                if (!report.Required || !report.Installed) continue;
                foreach (var slot in report.Slots)
                    if (slot.Wanted != null && PublisherIdWriter.CanWrite(report.Kind, slot.Key))
                        entries.Add(new PublisherIdWriter.Entry(report.Kind, slot.Key, slot.Wanted));
            }

            if (entries.Count == 0)
            {
                error = $"{profile.DisplayName} khong co ID cap san nao tool ghi duoc bang may (tren SDK da gan).";
                return false;
            }

            if (!PublisherIdWriter.Write(entries, dryRun, out changes, out error)) return false;
            if (dryRun) return true;

            var state = PublisherState.Load();
            state.activePublisher = profile.Id;
            state.appliedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");
            state.Save();
            changes.Add($"PublisherConfig.json: activePublisher = {profile.Id}");
            return true;
        }
    }
}
#endif
