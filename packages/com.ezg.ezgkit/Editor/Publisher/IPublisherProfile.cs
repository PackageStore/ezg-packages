#if UNITY_EDITOR
namespace Ezg.Editor.Shared.Publisher
{
    /// <summary>
    ///     Một NHÀ PHÁT HÀNH mà dự án có thể đi cùng (Neptune, SayGame, …). Với dev, câu hỏi duy nhất
    ///     khi đi với một publisher là về SDK: <b>họ đòi SDK gì · mình đã gắn cái nào · cái nào thừa ·
    ///     cái nào phải gắn thêm · và ID nào phải thay ở đâu</b>. Profile trả lời câu đó bằng
    ///     <see cref="RequiredSdks" />; phần còn lại (tool tự dò SDK đang có trong project, đọc ID hiện
    ///     tại, chỉ chỗ sửa) là việc của <see cref="SdkCatalog" /> — dùng chung cho mọi publisher.
    ///     <para>
    ///         Tách khỏi bốn tab "Setup Ezg": bốn tab đó dựng dự án theo hạ tầng Ezg; publisher thì mỗi
    ///         nhà một bộ SDK/ID, và cùng một dự án có thể lần lượt đi test với vài nhà. Thêm publisher
    ///         mới = một lớp implement interface này + đăng ký ở <see cref="PublisherRegistry" />.
    ///     </para>
    /// </summary>
    internal interface IPublisherProfile
    {
        /// <summary>Khoá ổn định dùng trong JSON trạng thái (ASCII, không đổi sau khi phát hành).</summary>
        string Id { get; }

        /// <summary>Nhãn tab. ASCII-only để cột nav không phụ thuộc font của Editor.</summary>
        string Title { get; }

        /// <summary>Tên đầy đủ hiện trong thân trang / báo cáo (ví dụ "Neptune (Flick Different)").</summary>
        string DisplayName { get; }

        /// <summary>Một câu "tab này để làm gì", hiện dưới tiêu đề trang.</summary>
        string Subtitle { get; }

        /// <summary>Tài liệu gốc của nhà phát hành (Notion / Drive). null = chưa có tài liệu.</summary>
        string GuideUrl { get; }

        /// <summary>Đoạn giới thiệu quy trình, gấp lại mặc định trong trang.</summary>
        string Intro { get; }

        /// <summary>
        ///     SDK publisher yêu cầu. SDK có trong project mà KHÔNG nằm ở đây là "thừa với publisher này" —
        ///     <see cref="SdkSwitcher" /> sẽ gỡ khi chuyển sang publisher này (trừ khi code game còn gọi
        ///     thẳng SDK đó). Rỗng = chưa có tài liệu → không gỡ gì.
        ///     <para>
        ///         Có thể đọc file khi được gọi (profile Ezg lấy ID từ MarketingConfig.json) — page cache kết
        ///         quả trong Reload, KHÔNG gọi từ getter Status/Headline.
        ///     </para>
        /// </summary>
        SdkRequirement[] RequiredSdks { get; }
    }

    /// <summary>SDK mà tool biết dò trong project. Thứ tự = thứ tự hiện trong bảng.</summary>
    internal enum SdkKind
    {
        Meta,
        AppsFlyer,
        GameAnalytics,
        Firebase,
        AppLovinMax,
        UnityIap,
        GooglePlayPlugins,
        AppleGameKit,
    }

    /// <summary>Một SDK publisher yêu cầu + các ID phải điền cho SDK đó.</summary>
    internal sealed class SdkRequirement
    {
        internal SdkKind Sdk;

        /// <summary>Vì sao publisher cần SDK này (một câu — để dev không tranh cãi "bỏ được không").</summary>
        internal string Why;

        internal SdkIdSlot[] Ids;

        /// <summary>Custom event publisher bắt buộc trong SDK này (ví dụ AppsFlyer <c>f_custom_playtime</c>). Có thể null.</summary>
        internal RequiredEvent[] Events;

        internal (string Label, string Url)[] Links;

        internal SdkRequirement(SdkKind sdk, string why, params SdkIdSlot[] ids)
        {
            Sdk = sdk;
            Why = why;
            Ids = ids;
        }

        internal SdkRequirement WithEvents(params RequiredEvent[] events)
        {
            Events = events;
            return this;
        }

        internal SdkRequirement WithLinks(params (string Label, string Url)[] links)
        {
            Links = links;
            return this;
        }
    }

    /// <summary>
    ///     Một ID của SDK. <see cref="Key" /> là khoá <see cref="SdkCatalog" /> hiểu (biết ID đó đang
    ///     nằm ở file nào trong project, đọc ra và chỉ chỗ sửa); khoá lạ = ID "ngoài Unity" — tool chỉ
    ///     bày giá trị + hướng dẫn, không đọc/ghi.
    ///     <para>
    ///         <see cref="PublisherValue" /> khác null = giá trị PUBLISHER CẤP, dùng chung mọi game (dev key
    ///         AppsFlyer của họ, Partner ID) → nút "Sinh lại SDK" ghi được nếu catalog biết chỗ ghi.
    ///         null = GAME TỰ TẠO trên console (Meta app id, GA game key) → tool chỉ kiểm có/không.
    ///     </para>
    /// </summary>
    internal sealed class SdkIdSlot
    {
        internal string Key;
        internal string Label;
        internal string PublisherValue;
        internal string HowToGet;

        internal SdkIdSlot(string key, string label, string publisherValue, string howToGet)
        {
            Key = key;
            Label = label;
            PublisherValue = publisherValue;
            HowToGet = howToGet;
        }

        /// <summary>ID publisher cấp sẵn.</summary>
        internal static SdkIdSlot Given(string key, string label, string value, string howTo = null) =>
            new(key, label, value, howTo);

        /// <summary>ID game tự tạo trên console của SDK.</summary>
        internal static SdkIdSlot Own(string key, string label, string howTo) => new(key, label, null, howTo);
    }

    internal sealed class RequiredEvent
    {
        internal string Name;
        internal string[] Parameters;
        internal string When;

        internal RequiredEvent(string name, string when, params string[] parameters)
        {
            Name = name;
            When = when;
            Parameters = parameters;
        }
    }

    /// <summary>
    ///     Danh sách nhà phát hành. Thứ tự = thứ tự tab trong nhóm "Nhà phát hành" của cửa sổ.
    ///     Thêm nhà phát hành mới: viết profile trong <c>Profiles/</c> rồi thêm vào mảng này — và thêm
    ///     một mục vào <c>EzgKitWindow.Tab</c> nếu muốn có menu mở thẳng tab đó.
    /// </summary>
    internal static class PublisherRegistry
    {
        internal static readonly IPublisherProfile[] Profiles =
        {
            // Ezg đứng đầu: bản "trong nhà" — bấm về là cài lại bộ SDK mặc định của template.
            new Profiles.EzgProfile(),
            new Profiles.NeptuneProfile(),
            new Profiles.SayGameProfile(),
        };

        internal static IPublisherProfile Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var profile in Profiles)
                if (profile.Id == id) return profile;
            return null;
        }
    }
}
#endif
