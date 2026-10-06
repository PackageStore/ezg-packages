#if UNITY_EDITOR
using System.Collections.Generic;
using Ezg.Editor.Shared.Setup;
using UnityEngine.UIElements;

namespace Ezg.Editor.Shared.EzgKit.Pages
{
    /// <summary>
    ///     Quảng cáo + quyền riêng tư: AdsConfig (debug, format, id MAX), define <c>MEDIATION_MAX</c> (thiếu là code
    ///     MAX của com.ezg.ads bị compile bỏ), AppLovinSettings (sdk key, AdMob app id), consent flow / ATT của MAX,
    ///     đối tượng người chơi (COPPA) và Facebook app id.
    /// </summary>
    internal sealed class AdsPage : SetupPage
    {
        #region Keys

        private const string DEFINE_MAX = "MEDIATION_MAX";

        private const string K_DEBUG = "debugAds";
        private const string K_FORMATS = "enabledFormats";
        private const string K_SDK = "maxSdkKey";
        private const string K_DEFINE = "mediationMax";
        private const string K_ADMOB_ANDROID = "admobAndroidAppId";
        private const string K_ADMOB_IOS = "admobIosAppId";
        private const string K_CONSENT = "consentFlowEnabled";
        private const string K_PRIVACY = "privacyPolicyUrl";
        private const string K_TERMS = "termsOfServiceUrl";
        private const string K_ATT = "attText";
        private const string K_AUDIENCE = "audience";
        private const string K_FB_APP = "facebookAppId";
        private const string K_FB_TOKEN = "facebookClientToken";

        /// <summary>Field AdsConfig: (key API, field asset, nhãn).</summary>
        private static readonly (string Key, string Field, string Label)[] UnitIds =
        {
            ("maxAndroidBannerId", "maxAndroidBannerId", "Android · Banner"),
            ("maxAndroidInterstitialId", "maxAndroidInterstitialId", "Android · Interstitial"),
            ("maxAndroidRewardedId", "maxAndroidRewardedId", "Android · Rewarded"),
            ("maxIosBannerId", "maxIosBannerId", "iOS · Banner"),
            ("maxIosInterstitialId", "maxIosInterstitialId", "iOS · Interstitial"),
            ("maxIosRewardedId", "maxIosRewardedId", "iOS · Rewarded"),
        };

        private static readonly (string Key, string Label)[] IronSource =
        {
            ("ironSourceAndroidKey", "ironSource app key Android"),
            ("ironSourceIosKey", "ironSource app key iOS"),
            ("ironSourceRewardedAndroidId", "ironSource rewarded Android"),
            ("ironSourceRewardedIosId", "ironSource rewarded iOS"),
        };

        internal static readonly string[] AudienceIds = { "all", "13plus", "children" };

        internal static readonly List<string> AudienceLabels = new()
        {
            "Mọi lứa tuổi (không nhắm trẻ em)", "13+ (không phục vụ trẻ dưới 13)", "Có trẻ em dưới 13 (Families / COPPA)",
        };

        #endregion

        #region UI state

        private Toggle _debug, _define, _consent;
        private readonly Dictionary<int, Toggle> _formats = new();
        private TextField _sdk, _admobAndroid, _admobIos, _privacy, _terms, _att, _fbApp, _fbToken;
        private readonly Dictionary<string, TextField> _units = new();
        private DropdownField _audience;

        #endregion

        internal override string Id => PageIds.ADS;

        internal override string Title => "Ads & Privacy";

        internal override string Description =>
            "Mediation MAX, AdMob, consent / ATT, đối tượng người chơi và Facebook — thứ quyết định ads có chạy và app có qua review.";

        internal override bool CanApply => true;

        internal override string ApplyWarning =>
            "Đổi define MEDIATION_MAX làm Unity recompile toàn project.";

        #region Core

        internal override PageReport Detect()
        {
            var report = new PageReport();
            var ads = AdsConfigBridge.Find();
            if (ads == null)
            {
                report.Add(AdsConfigBridge.Installed ? EzgStatus.Warn : EzgStatus.None,
                    AdsConfigBridge.Installed ? "Không có AdsConfig.asset trong Resources." : "Dự án chưa cài com.ezg.ads.",
                    "Create > Ezg > Ads > Config trong một thư mục Resources.");
            }
            else
            {
                if (SerializedAsset.GetBool(ads, "debugAds") == true)
                    report.Add(EzgStatus.Warn, "debugAds đang BẬT — ad tự thành công, không có doanh thu.", "Tắt trước khi build store.");
                else report.Ok();

                var key = SerializedAsset.GetString(ads, "maxAndroidSdkKey");
                if (string.IsNullOrEmpty(key)) report.Add(EzgStatus.Warn, "Chưa có MAX SDK key.", "Lấy ở MAX dashboard > Account > Keys (hoặc tải sheet ở trang Marketing).");
                else report.Ok();

                var missingUnits = 0;
                foreach (var (_, field, _) in UnitIds)
                    if (string.IsNullOrEmpty(SerializedAsset.GetString(ads, field)) && !field.Contains("Banner"))
                        missingUnits++;
                if (missingUnits > 0) report.Add(EzgStatus.Warn, $"{missingUnits} ad unit (inter / rewarded) còn trống.", "Tạo ad unit trên MAX dashboard.");
                else report.Ok();

                if (!string.IsNullOrEmpty(key) && !DefineSymbols.HasOnAll(DEFINE_MAX))
                    report.Add(EzgStatus.Error, "Thiếu define MEDIATION_MAX — toàn bộ code MAX của com.ezg.ads bị compile bỏ.", "Bật ô MEDIATION_MAX rồi Áp dụng.");
            }

            if (AppLovinBridge.Installed)
            {
                var settings = AppLovinBridge.FindSettings();
                if (settings == null) report.Add(EzgStatus.Warn, "Chưa có AppLovinSettings.asset.", "Áp dụng ở trang này sẽ tạo (qua AppLovinSettings.Instance).");
                else
                {
                    var android = SerializedAsset.GetString(settings, "adMobAndroidAppId");
                    var ios = SerializedAsset.GetString(settings, "adMobIosAppId");
                    if (string.IsNullOrEmpty(android) || string.IsNullOrEmpty(ios))
                        report.Add(EzgStatus.Warn, "Thiếu AdMob app id — adapter Google không fill.", "Điền AdMob app id (ca-app-pub-…~…).");
                    else report.Ok();
                }

                if (!AppLovinBridge.ConsentEnabled)
                    report.Add(EzgStatus.Warn, "Consent flow của MAX đang tắt (GDPR / UMP + ATT).", "Bật consent flow và điền link Privacy / Terms.");
                else if (string.IsNullOrEmpty(AppLovinBridge.ConsentPrivacyUrl))
                    report.Add(EzgStatus.Warn, "Consent flow thiếu link Privacy policy.", "Điền link Privacy policy.");
                else report.Ok();
            }

            var audience = EzgKitState.Answer(Id, K_AUDIENCE, string.Empty);
            if (string.IsNullOrEmpty(audience))
                report.Add(EzgStatus.Warn, "Chưa chọn đối tượng người chơi (target audience).", "Chọn ở card Quyền riêng tư — quyết định COPPA / Families.");
            else if (audience == "children")
                report.Add(EzgStatus.Warn, "Game có trẻ em dưới 13: phải bỏ quảng cáo cá nhân hoá, xin quyền AD_ID đúng chính sách Families, rà Facebook SDK.",
                    "Đọc chính sách Google Play Families + Apple Kids trước khi phát hành.");
            else report.Ok();

            if (FacebookBridge.Installed && FacebookBridge.Find() != null)
            {
                if (string.IsNullOrEmpty(FacebookBridge.AppId)) report.Add(EzgStatus.Warn, "FacebookSettings chưa có app id.", "Điền Facebook app id + client token.");
                else if (!FacebookBridge.HasInitCall())
                    report.Add(EzgStatus.Warn, "Có Facebook SDK nhưng code game không gọi FB.Init — attribution Meta không chạy.", "Gọi FB.Init lúc boot (hoặc gỡ SDK nếu không dùng).");
                else report.Ok();
            }

            return report.Resolve(ads == null
                ? "Chưa có AdsConfig."
                : SerializedAsset.GetBool(ads, "debugAds") == true ? "Đang chạy debug ads." : "Ads thật.");
        }

        internal override JsonObject GetValues(bool maskSecrets)
        {
            var ads = AdsConfigBridge.Find();
            var settings = AppLovinBridge.FindSettings();
            var key = SerializedAsset.GetString(ads, "maxAndroidSdkKey") ?? string.Empty;
            var values = new JsonObject()
                .Set(K_DEBUG, SerializedAsset.GetBool(ads, "debugAds") ?? true)
                .Set(K_FORMATS, SerializedAsset.GetInt(ads, "enabledFormats") ?? 7)
                .Set(K_SDK, maskSecrets ? Mask.Secret(key) : key)
                .Set(K_DEFINE, DefineSymbols.HasOnAll(DEFINE_MAX));
            foreach (var (k, field, _) in UnitIds) values.Set(k, SerializedAsset.GetString(ads, field) ?? string.Empty);
            foreach (var (k, _) in IronSource) values.Set(k, SerializedAsset.GetString(ads, k) ?? string.Empty);
            values.Set(K_ADMOB_ANDROID, SerializedAsset.GetString(settings, "adMobAndroidAppId") ?? string.Empty)
                .Set(K_ADMOB_IOS, SerializedAsset.GetString(settings, "adMobIosAppId") ?? string.Empty)
                .Set(K_CONSENT, AppLovinBridge.ConsentEnabled)
                .Set(K_PRIVACY, AppLovinBridge.ConsentPrivacyUrl ?? AppSecretsSink.Read(AppSecretsSink.F_PRIVACY) ?? string.Empty)
                .Set(K_TERMS, AppLovinBridge.ConsentTermsUrl ?? AppSecretsSink.Read(AppSecretsSink.F_TERMS) ?? string.Empty)
                .Set(K_ATT, AppLovinBridge.AttText ?? string.Empty)
                .Set(K_AUDIENCE, EzgKitState.Answer(Id, K_AUDIENCE, string.Empty))
                .Set(K_FB_APP, FacebookBridge.AppId ?? string.Empty);
            var token = FacebookBridge.ClientToken ?? string.Empty;
            values.Set(K_FB_TOKEN, maskSecrets ? Mask.Secret(token) : token);
            return values;
        }

        internal override ApplyResult Apply(JsonObject values, bool dryRun)
        {
            var result = new ApplyResult { DryRun = dryRun };

            string Str(string key)
            {
                if (!values.Has(key)) return null;
                var v = values.Str(key).Trim();
                return v.IndexOf('•') >= 0 ? null : v; // giá trị che — không ghi
            }

            foreach (var (key, validate) in new (string, System.Func<string, string>)[]
                     {
                         (K_ADMOB_ANDROID, Validate.AdmobAppId), (K_ADMOB_IOS, Validate.AdmobAppId),
                         (K_PRIVACY, v => Validate.Url(v)), (K_TERMS, v => Validate.Url(v)),
                     })
            {
                var value = Str(key);
                var error = value == null ? null : validate(value);
                if (error != null) return ApplyResult.Fail($"{key}: {error}");
            }

            // AdsConfig
            var ads = AdsConfigBridge.Find();
            if (ads != null)
            {
                var list = new List<(string, object, bool)>();
                if (values.Has(K_DEBUG)) list.Add(("debugAds", values.Bool(K_DEBUG), false));
                if (values.Has(K_FORMATS)) list.Add(("enabledFormats", values.Int(K_FORMATS), false));
                var sdk = Str(K_SDK);
                if (!string.IsNullOrEmpty(sdk))
                {
                    list.Add(("maxAndroidSdkKey", sdk, true));
                    list.Add(("maxIosSdkKey", sdk, true));
                }

                foreach (var (key, field, _) in UnitIds) list.Add((field, Str(key), false));
                foreach (var (key, _) in IronSource) list.Add((key, Str(key), false));
                SerializedAsset.Write(ads, "AdsConfig", list, dryRun, result.Rows);
            }
            else if (AdsConfigBridge.Installed) result.Notes.Add("Không có AdsConfig.asset — tạo bằng Create > Ezg > Ads > Config trong Resources rồi Áp dụng lại.");

            // Define
            if (values.Has(K_DEFINE)) DefineSymbols.Set(DEFINE_MAX, values.Bool(K_DEFINE), dryRun, result.Rows);

            // AppLovinSettings
            if (AppLovinBridge.Installed)
            {
                var admobAndroid = Str(K_ADMOB_ANDROID);
                var admobIos = Str(K_ADMOB_IOS);
                var sdk = Str(K_SDK);
                var wantSettings = !string.IsNullOrEmpty(admobAndroid) || !string.IsNullOrEmpty(admobIos) || !string.IsNullOrEmpty(sdk);
                var settings = AppLovinBridge.FindSettings();
                if (settings == null && wantSettings)
                {
                    result.Rows.Add(new ChangeRow("AppLovinSettings", "(asset)", "chưa có", "tạo qua AppLovinSettings.Instance", false));
                    if (!dryRun) settings = AppLovinBridge.FindOrCreateSettings();
                }

                if (settings != null)
                    SerializedAsset.Write(settings, "AppLovinSettings", new List<(string, object, bool)>
                    {
                        ("sdkKey", string.IsNullOrEmpty(sdk) ? null : sdk, true),
                        ("adMobAndroidAppId", admobAndroid, false),
                        ("adMobIosAppId", admobIos, false),
                    }, dryRun, result.Rows);

                if (values.Has(K_CONSENT) || Str(K_PRIVACY) != null || Str(K_TERMS) != null || Str(K_ATT) != null)
                {
                    bool? consent = values.Has(K_CONSENT) ? values.Bool(K_CONSENT) : null;
                    if (!AppLovinBridge.WriteConsent(consent, Str(K_PRIVACY), Str(K_TERMS), Str(K_ATT), dryRun, result.Rows, out var error))
                        result.Notes.Add(error);
                }
            }

            // Facebook
            if (FacebookBridge.Installed)
            {
                var fb = FacebookBridge.Find();
                var appId = Str(K_FB_APP);
                var token = Str(K_FB_TOKEN);
                if (fb != null)
                    SerializedAsset.Write(fb, "FacebookSettings", new List<(string, object, bool)>
                    {
                        ("appIds[0]", string.IsNullOrEmpty(appId) ? null : appId, false),
                        ("clientTokens[0]", string.IsNullOrEmpty(token) ? null : token, true),
                    }, dryRun, result.Rows);
                else if (!string.IsNullOrEmpty(appId))
                    result.Notes.Add("Chưa có FacebookSettings.asset — mở Facebook > Edit Settings một lần để SDK tạo, rồi Áp dụng lại.");
            }

            // Audience (quyết định, lưu ở state)
            var audience = Str(K_AUDIENCE);
            if (!string.IsNullOrEmpty(audience))
            {
                var current = EzgKitState.Answer(Id, K_AUDIENCE, string.Empty);
                result.Rows.Add(new ChangeRow("EzgKitSetup.json", K_AUDIENCE, current, audience, current == audience));
                if (!dryRun && current != audience) EzgKitState.SetAnswer(Id, K_AUDIENCE, audience);
                if (audience == "children")
                    result.Notes.Add("Có trẻ em dưới 13: tắt quảng cáo cá nhân hoá, khai Families trên Play Console, rà quyền AD_ID và Facebook SDK.");
            }

            if (!dryRun) UnityEditor.AssetDatabase.SaveAssets();
            return result;
        }

        #endregion

        #region UI

        internal override void Build(VisualElement body, IPageHost host)
        {
            var values = GetValues(false);
            var ads = AdsConfigBridge.Find();

            var adsCard = Ui.Card(body, "AdsConfig (com.ezg.ads)",
                ads == null ? "Không tìm thấy AdsConfig.asset — tạo bằng Create > Ezg > Ads > Config trong Resources." : UnityEditor.AssetDatabase.GetAssetPath(ads));
            _debug = Ui.ToggleRow(adsCard, "Debug ads", values.Bool(K_DEBUG),
                "BẬT: không init mediation, mọi ad tự thành công (dev). PHẢI tắt cho bản store.");
            var formatsRow = Ui.Row(null, "ezg-wrap");
            _formats.Clear();
            var flags = values.Int(K_FORMATS);
            foreach (var (bit, label) in AdsConfigBridge.Formats)
            {
                var toggle = new Toggle(label) { value = (flags & bit) != 0 };
                toggle.AddToClassList("ezg-inline-toggle");
                formatsRow.Add(toggle);
                _formats[bit] = toggle;
            }

            var formatsWrap = new VisualElement();
            adsCard.Add(Ui.Text("Format đang dùng", "ezg-field-label"));
            formatsWrap.Add(formatsRow);
            adsCard.Add(formatsWrap);
            Ui.Hint(adsCard, "Format bật mà thiếu ad unit id thì tự tắt lúc chạy.");

            var max = Ui.Card(body, "AppLovin MAX", "SDK key dùng chung hai nền tảng; ad unit lấy ở MAX dashboard > Mediation > Ad Units.");
            _sdk = Ui.TextRow(max, "MAX SDK key", values.Str(K_SDK), null, null, true);
            _define = Ui.ToggleRow(max, "Define MEDIATION_MAX", values.Bool(K_DEFINE),
                "com.ezg.ads guard toàn bộ code MAX bằng define này — thiếu là ads không bao giờ chạy (Android + iOS).");
            _units.Clear();
            foreach (var (key, _, label) in UnitIds) _units[key] = Ui.TextRow(max, label, values.Str(key));
            if (AppLovinBridge.Installed)
            {
                _admobAndroid = Ui.TextRow(max, "AdMob app id Android", values.Str(K_ADMOB_ANDROID), "ca-app-pub-XXXX~YYYY (AppLovinSettings).", Validate.AdmobAppId);
                _admobIos = Ui.TextRow(max, "AdMob app id iOS", values.Str(K_ADMOB_IOS), null, Validate.AdmobAppId);
            }
            else Ui.Message(max, "Chưa cài AppLovin MAX SDK — chỉ ghi được AdsConfig.", EzgStatus.Warn);

            var iron = Ui.Fold(max, "ironSource / LevelPlay (tuỳ chọn)", false);
            foreach (var (key, label) in IronSource) _units[key] = Ui.TextRow(iron, label, values.Str(key));

            var privacy = Ui.Card(body, "Quyền riêng tư", "Consent flow của MAX (GDPR / UMP + ATT iOS) và đối tượng người chơi.");
            if (AppLovinBridge.Installed)
            {
                _consent = Ui.ToggleRow(privacy, "Bật consent flow MAX", values.Bool(K_CONSENT), "MAX hiện hộp đồng ý GDPR (UMP) và ATT theo vùng.");
                _privacy = Ui.TextRow(privacy, "Privacy policy URL", values.Str(K_PRIVACY), null, v => Validate.Url(v));
                _terms = Ui.TextRow(privacy, "Terms of service URL", values.Str(K_TERMS), null, v => Validate.Url(v));
                _att = Ui.TextRow(privacy, "Câu hỏi ATT (tiếng Anh)", values.Str(K_ATT),
                    "Trống = câu mặc định của MAX. Ghi ở đây là bật override của MAX.");
            }

            var audienceIndex = System.Array.IndexOf(AudienceIds, values.Str(K_AUDIENCE));
            _audience = Ui.DropdownRow(privacy, "Đối tượng người chơi", AudienceLabels, audienceIndex < 0 ? 0 : audienceIndex,
                audienceIndex < 0 ? "Chưa chọn — chọn rồi Áp dụng để lưu quyết định." : null);

            if (FacebookBridge.Installed)
            {
                var fb = Ui.Card(body, "Facebook SDK", FacebookBridge.Find() == null
                    ? "Chưa có FacebookSettings.asset (mở Facebook > Edit Settings một lần để SDK tạo)."
                    : "FacebookSettings.asset — app id + client token.");
                _fbApp = Ui.TextRow(fb, "Facebook app id", values.Str(K_FB_APP));
                _fbToken = Ui.TextRow(fb, "Client token", values.Str(K_FB_TOKEN), null, null, true);
                if (!FacebookBridge.HasInitCall())
                    Ui.Message(fb, "Code game chưa gọi FB.Init — có app id cũng không có attribution Meta.", EzgStatus.Warn);
            }
        }

        internal override JsonObject CollectUi()
        {
            var flags = 0;
            foreach (var pair in _formats)
                if (pair.Value.value)
                    flags |= pair.Key;

            var values = new JsonObject()
                .Set(K_DEBUG, _debug.value)
                .Set(K_FORMATS, flags)
                .Set(K_SDK, _sdk.value)
                .Set(K_DEFINE, _define.value);
            foreach (var pair in _units) values.Set(pair.Key, pair.Value.value);
            if (_admobAndroid != null) values.Set(K_ADMOB_ANDROID, _admobAndroid.value);
            if (_admobIos != null) values.Set(K_ADMOB_IOS, _admobIos.value);
            if (_consent != null) values.Set(K_CONSENT, _consent.value);
            if (_privacy != null) values.Set(K_PRIVACY, _privacy.value);
            if (_terms != null) values.Set(K_TERMS, _terms.value);
            if (_att != null) values.Set(K_ATT, _att.value);
            values.Set(K_AUDIENCE, AudienceIds[UnityEngine.Mathf.Clamp(_audience.index, 0, AudienceIds.Length - 1)]);
            if (_fbApp != null) values.Set(K_FB_APP, _fbApp.value);
            if (_fbToken != null) values.Set(K_FB_TOKEN, _fbToken.value);
            return values;
        }

        #endregion
    }
}
#endif
