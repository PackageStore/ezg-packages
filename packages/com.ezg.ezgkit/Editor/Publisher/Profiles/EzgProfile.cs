#if UNITY_EDITOR
using System.IO;
using Ezg.Editor.Shared.Marketing;
using UnityEngine;

namespace Ezg.Editor.Shared.Publisher.Profiles
{
    /// <summary>
    ///     Ezg — bản "trong nhà", bộ SDK mặc định của code-template: Meta, AppsFlyer, Firebase, AppLovin MAX,
    ///     Unity IAP (+ SDK nền tảng Google Play plugins / Apple GameKit — mặc nhiên, xem <c>SdkInstallSpec.IsPlatform</c>). Là một profile như mọi publisher khác để
    ///     bấm "Chuyển sang Ezg" là cài lại đúng bộ này và gỡ SDK publisher khác bắt gắn thêm (GameAnalytics…).
    ///     <para>
    ///         ID của Ezg không cố định theo công ty mà theo TỪNG GAME, nằm trong Google Sheet marketing →
    ///         <c>ProjectSettings/MarketingConfig.json</c> (tab Marketing tải về). Nên <see cref="RequiredSdks" />
    ///         đọc JSON đó lúc được gọi: có giá trị thì thành ID "cấp sẵn" (switch về Ezg ghi lại được dev key
    ///         AppsFlyer của game), chưa tải sheet thì là ID "game tự điền" kèm chỉ dẫn sang tab Marketing.
    ///     </para>
    /// </summary>
    internal sealed class EzgProfile : IPublisherProfile
    {
        public string Id => "ezg";
        public string Title => "Ezg (trong nha)";
        public string DisplayName => "Ezg (bản mặc định trong nhà)";

        public string Subtitle =>
            "Bộ SDK mặc định của code-template. Chuyển về đây = cài lại SDK Ezg, gỡ SDK publisher khác đòi thêm, ghi lại ID từ sheet marketing.";

        public string GuideUrl => null;

        public string Intro =>
            "Đây là bộ SDK mà mọi dự án Ezg tự phát hành đều có: Meta + AppsFlyer (attribution, ID theo game trong sheet "
            + "marketing), Firebase (Analytics / Crashlytics / Remote Config / save-sync), AppLovin MAX (ads), Unity IAP, "
            + "Google Play plugins (Play Asset Delivery), Apple GameKit.\n\n"
            + "Sau khi đi CPI test với một publisher (họ bắt thay dev key, gắn thêm SDK của họ), bấm \"Chuyển sang Ezg\" "
            + "để về bản này: SDK đã gỡ được cài lại từ cache trên máy (switcher export .unitypackage trước khi gỡ), "
            + "SDK publisher đòi thêm bị gỡ, dev key AppsFlyer ghi lại theo MarketingConfig.json.";

        public SdkRequirement[] RequiredSdks
        {
            get
            {
                var marketing = LoadMarketing();
                var devKey = marketing?.appsflyerDevKey;
                var appleId = marketing?.appleId;
                var fbAppId = marketing?.facebook?.appId;
                var fbToken = marketing?.facebook?.clientToken;
                const string viaSheet = "Google Sheet marketing của game → tab Marketing > Tải sheet > ghi.";

                return new[]
                {
                    new SdkRequirement(SdkKind.Meta, "Attribution + event Meta cho campaign tự chạy của Ezg.",
                        Slot("appId", "App ID", fbAppId, viaSheet),
                        Slot("clientToken", "Client Token", fbToken, viaSheet)),
                    new SdkRequirement(SdkKind.AppsFlyer, "Attribution dưới tài khoản AppsFlyer của Ezg — dev key theo game trong sheet.",
                        Slot("devKey", "Dev key", devKey, viaSheet),
                        Slot("iosAppId", "iOS App Store ID", appleId, viaSheet + " (Apple ID 10 chữ số ở App Store Connect)")),
                    new SdkRequirement(SdkKind.Firebase, "Analytics, Crashlytics, Remote Config, save-sync của template (tab Firebase tạo app + tải config)."),
                    new SdkRequirement(SdkKind.AppLovinMax, "Mediation ads của template (key ghi từ sheet marketing)."),
                    new SdkRequirement(SdkKind.UnityIap, "Shop IAP của template (com.ezg.iap bọc com.unity.purchasing)."),
                    // Google Play plugins + Apple GameKit là SDK NỀN TẢNG (IsPlatform trong SdkCatalog) — mọi profile
                    // mặc nhiên giữ, không khai ở đây.
                };
            }
        }

        /// <summary>Sheet đã có giá trị → ID cấp sẵn (switch ghi được); chưa có → game tự điền qua tab Marketing.</summary>
        private static SdkIdSlot Slot(string key, string label, string value, string howTo) =>
            string.IsNullOrEmpty(value) ? SdkIdSlot.Own(key, label, howTo) : SdkIdSlot.Given(key, label, value, howTo);

        private static MarketingConfig LoadMarketing()
        {
            try
            {
                var path = MarketingConfig.JsonPath;
                return File.Exists(path) ? JsonUtility.FromJson<MarketingConfig>(File.ReadAllText(path)) : null;
            }
            catch (System.Exception)
            {
                return null;
            }
        }
    }
}
#endif
