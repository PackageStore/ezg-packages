#if UNITY_EDITOR
namespace Ezg.Editor.Shared.Publisher.Profiles
{
    /// <summary>
    ///     Neptune (Flick Different) — CPI test. Nguồn: "CPI Test Guide for Partners" (Notion,
    ///     <see cref="GuideUrl" />), đọc 2026-08-26, mục SDK của PRE-TEST CHECKLIST. Ba SDK bắt buộc:
    ///     Meta (Facebook SDK), AppsFlyer (dev key CỦA NEPTUNE + event playtime), GameAnalytics (tài
    ///     khoản Neptune mời). Guide không đòi SDK nào khác — mọi SDK khác đang có trong project là
    ///     "thừa với Neptune" (tool chỉ báo).
    ///     <para>
    ///         Phần Google Play / Creative / Build của guide không nằm đây: tab này trả lời đúng một
    ///         câu "SDK gì, ID gì, thay ở đâu".
    ///     </para>
    /// </summary>
    internal sealed class NeptuneProfile : IPublisherProfile
    {
        #region Constants

        private const string URL_GUIDE =
            "https://app.notion.com/p/flickdifferent/CPI-Test-Guide-for-Partners-e77a45eaf6d382dbb5b581c090f6dd6c";

        private const string URL_META_DEV = "https://developers.facebook.com/apps/";
        private const string URL_META_BUSINESS = "https://business.facebook.com/settings/partners";
        private const string URL_META_GUIDE_PDF = "https://drive.google.com/file/d/1U2yUIj1Z1SmTQgvj8vZgY6ulgSWCuvlu/view";
        private const string URL_APPSFLYER = "https://hq1.appsflyer.com/";
        private const string URL_GA = "https://tool.gameanalytics.com/";

        #endregion

        #region Identity

        public string Id => "neptune";
        public string Title => "Neptune";
        public string DisplayName => "Neptune (Flick Different)";

        public string Subtitle =>
            "CPI test với Neptune — SDK họ yêu cầu, SDK đã gắn / thừa / cần gắn thêm, ID phải thay ở đâu.";

        public string GuideUrl => URL_GUIDE;

        public string Intro =>
            "Neptune chạy CPI test cho game trước khi quyết định hợp tác. Về SDK, guide của họ đòi đúng ba thứ: "
            + "Meta (Facebook SDK — app phải Live, cấp Partner ID của Neptune quyền Full control), AppsFlyer "
            + "(dùng DEV KEY CỦA NEPTUNE thay key Ezg, thêm custom event f_custom_playtime bắn khi app xuống "
            + "nền/thoát với 4 tham số), GameAnalytics (Neptune mời vào tổ chức của họ, game key tạo ở đó).\n\n"
            + "Nút \"Sinh lại SDK theo Neptune\" ghi các ID Neptune cấp sẵn vào project (hiện là dev key AppsFlyer). "
            + "ID game tự tạo (Meta app id, GA key) tool chỉ kiểm có hay chưa và chỉ chỗ điền.";

        #endregion

        #region SDK

        private static readonly SdkRequirement[] _sdks =
        {
            new SdkRequirement(SdkKind.Meta,
                    "Meta đo install/event của campaign — không có là CPI test không có số.",
                    SdkIdSlot.Own("appId", "App ID",
                        "developers.facebook.com > tạo App (tên, email, Privacy Policy URL, Data Deletion URL, icon 1024) > chuyển Live > Settings > Basic > App ID."),
                    SdkIdSlot.Own("clientToken", "Client Token",
                        "Meta App > Settings > Advanced > Security > Client token."),
                    SdkIdSlot.Given("partnerId", "Partner ID của Neptune", "3870082899724468",
                        "Business Settings > Partners > Add > dán Partner ID > gán Full control cho app. Ngoài Unity, không có API."))
                .WithLinks(("Meta for Developers", URL_META_DEV), ("Business Settings > Partners", URL_META_BUSINESS),
                    ("PDF Meta guide", URL_META_GUIDE_PDF)),

            new SdkRequirement(SdkKind.AppsFlyer,
                    "Attribution chạy dưới tài khoản AppsFlyer của Neptune — phải dùng dev key của họ.",
                    SdkIdSlot.Given("devKey", "Dev key", "NsZymPemYQycKKY8A826TU",
                        "Neptune cấp sẵn trong guide. Sau khi ghi, đổi cả appsflyerDevKey trong Google Sheet marketing để lần Tải sheet không đè ngược."),
                    SdkIdSlot.Own("iosAppId", "iOS App Store ID",
                        "Chỉ iOS. App Store Connect > App > App Information > Apple ID (10 chữ số). Android không cần."))
                .WithEvents(new RequiredEvent("f_custom_playtime", "App Exit / Background (OnApplicationPause / OnApplicationQuit)",
                    "playtime", "session_id", "current_stage", "no_ads"))
                .WithLinks(("AppsFlyer", URL_APPSFLYER)),

            new SdkRequirement(SdkKind.GameAnalytics,
                    "Neptune theo dõi hành vi người chơi trong test qua GA (tài khoản do họ mời).",
                    SdkIdSlot.Own("gameKey", "Game Key",
                        "Nhận invite email của Neptune > GA Dashboard > Create Game (Bundle ID, Android, Unity, Google Play) > Settings > Game keys."),
                    SdkIdSlot.Own("secretKey", "Secret Key", "Cùng chỗ với Game Key."))
                .WithLinks(("GameAnalytics", URL_GA)),
        };

        public SdkRequirement[] RequiredSdks => _sdks;

        #endregion
    }
}
#endif
