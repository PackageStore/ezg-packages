#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Ezg.Editor.Shared.EzgKit;
using Ezg.Editor.Shared.Readiness;
using Ezg.Editor.Shared.Social;
using UnityEditor;
using UnityEngine;

namespace Ezg.Editor.Shared.Publisher
{
    /// <summary>Một ID của SDK sau khi đối chiếu project với yêu cầu publisher.</summary>
    internal sealed class SlotReport
    {
        internal string Key;
        internal string Label;

        /// <summary>Giá trị đang có trong project; null = tool không đọc được (ID ngoài Unity).</summary>
        internal string Current;

        /// <summary>Giá trị publisher cấp; null = game tự tạo.</summary>
        internal string Wanted;

        internal EzgStatus Status;

        /// <summary>Chỗ thay trong project (file / menu). null = ngoài Unity.</summary>
        internal string Where;

        internal string HowToGet;
        internal string Note;
        internal (string Label, Action Run)[] Actions;

        /// <summary>Const trong GameConstant.cs mà applier ghi được; null = không tự ghi.</summary>
        internal string GameConstantName;
    }

    internal sealed class EventReport
    {
        internal string Name;
        internal EzgStatus Status;
        internal string Value;
        internal string Note;
        internal string Fix;
        internal (string Label, Action Run)[] Actions;
    }

    /// <summary>Một SDK trong bảng: có trong project không, publisher có đòi không, ID/event thế nào.</summary>
    internal sealed class SdkReport
    {
        internal SdkKind Kind;
        internal string Name;
        internal bool Installed;

        /// <summary>Version hoặc đường dẫn cho biết SDK nằm đâu — để "gỡ" biết gỡ cái gì.</summary>
        internal string Location;

        internal bool Required;

        /// <summary>SDK nền tảng (Game Center, Play Asset Delivery…) — đi với mọi bản build, không thuộc publisher nào, không bao giờ là "thừa".</summary>
        internal bool IsPlatform;
        internal string Why;
        internal string InstallHint;
        internal (string Label, string Url)[] Links;
        internal readonly List<SlotReport> Slots = new();
        internal readonly List<EventReport> Events = new();

        /// <summary>Đỏ = thiếu SDK bắt buộc hoặc ID sai; vàng = ID game chưa điền; xanh = đủ; xám = thừa.</summary>
        internal EzgStatus Status
        {
            get
            {
                if (!Required) return EzgStatus.None;
                if (!Installed) return EzgStatus.Error;
                var worst = EzgStatus.Ok;
                foreach (var slot in Slots)
                    if (slot.Status > worst) worst = slot.Status;
                foreach (var ev in Events)
                    if (ev.Status > worst) worst = ev.Status;
                return worst;
            }
        }
    }

    /// <summary>
    ///     Danh mục SDK tool biết dò trong project + biết ID của SDK đó nằm ở file nào. Dùng chung cho
    ///     mọi publisher: profile chỉ nói "cần SDK X với ID key K = giá trị V", catalog lo phần "X đang có
    ///     chưa, K hiện là gì, sửa ở đâu".
    ///     <para>
    ///         Cùng kỷ luật với <see cref="ReadinessChecks" />: KHÔNG tham chiếu assembly game — đọc YAML/
    ///         JSON/.cs bằng regex, PlayerSettings qua API Editor. Không gọi mạng, không ghi (ghi là việc
    ///         của <see cref="PublisherSdkApplier" />).
    ///     </para>
    /// </summary>
    internal static class SdkCatalog
    {
        #region Constants

        private const string FACEBOOK_DIR = "Assets/FacebookSDK";
        private const string FACEBOOK_SETTINGS_PATH = "Assets/FacebookSDK/SDK/Resources/FacebookSettings.asset";
        private const string ANDROID_MANIFEST = "Assets/Plugins/Android/AndroidManifest.xml";
        private const string GA_DIR = "Assets/GameAnalytics";
        private const string GA_SETTINGS_PATH = "Assets/Resources/GameAnalytics/Settings.asset";
        private const string FIREBASE_DIR = "Assets/Firebase";
        private const string ANDROID_FIREBASE_JSON = "Assets/google-services.json";
        private const string MAX_DIR = "Assets/MaxSdk";
        private const string PLAY_PLUGINS_DIR = "Assets/GooglePlayPlugins";
        private const string PACKAGES_MANIFEST = "Packages/manifest.json";
        private const string ADS_CONFIG_NAME = "AdsConfig";

        private const string CONST_APPSFLYER = "AppsFlyerId";
        private const string CONST_IOS_APP_ID = "IOSAppId";

        private const string URL_FACEBOOK_SDK = "https://developers.facebook.com/docs/unity/downloads";
        private const string URL_APPSFLYER_UNITY = "https://dev.appsflyer.com/hc/docs/unity-plugin";
        private const string URL_GA_UNITY = "https://docs.gameanalytics.com/integrations/sdk/unity";

        private static readonly SdkKind[] _kinds = (SdkKind[])Enum.GetValues(typeof(SdkKind));

        /// <summary>Spec là dữ liệu bất biến — dựng một lần, không cấp phát lại mỗi lượt vẽ.</summary>
        private static readonly Dictionary<SdkKind, SdkInstallSpec> _specs = new();

        #endregion

        #region Entry

        /// <summary>
        ///     Bảng SDK cho <paramref name="profile" />: mọi SDK publisher đòi (có hay chưa) + mọi SDK đang
        ///     có trong project mà publisher không đòi. SDK vừa không đòi vừa không có thì không liệt kê.
        /// </summary>
        internal static List<SdkReport> Collect(IPublisherProfile profile)
        {
            var reports = new List<SdkReport>();
            var required = new Dictionary<SdkKind, SdkRequirement>();
            foreach (var requirement in profile.RequiredSdks) required[requirement.Sdk] = requirement;

            var manifest = ReadProjectFile(PACKAGES_MANIFEST) ?? "";
            foreach (var kind in _kinds)
            {
                var report = new SdkReport { Kind = kind, Name = NameOf(kind) };
                try
                {
                    Detect(report, manifest);
                    var spec = SpecOf(kind);
                    report.IsPlatform = spec.IsPlatform;

                    // SDK nền tảng: profile không khai vẫn là "bắt buộc" — bản build nào cũng có, không phải của publisher.
                    if (!required.ContainsKey(kind) && spec.IsPlatform && profile.RequiredSdks.Length > 0)
                    {
                        report.Required = true;
                        report.Why = spec.PlatformNote;
                    }
                    else if (required.TryGetValue(kind, out var requirement))
                    {
                        report.Required = true;
                        report.Why = requirement.Why;
                        report.Links = requirement.Links;
                        if (report.Installed)
                        {
                            foreach (var slot in requirement.Ids ?? Array.Empty<SdkIdSlot>()) report.Slots.Add(ReadSlot(kind, slot));
                            if (requirement.Events is { Length: > 0 })
                                foreach (var ev in requirement.Events) report.Events.Add(CheckEvent(kind, ev));
                        }
                        else
                            // Chưa cài thì vẫn bày ID publisher cấp để dev biết trước phải chuẩn bị gì.
                            foreach (var slot in requirement.Ids ?? Array.Empty<SdkIdSlot>())
                                report.Slots.Add(new SlotReport
                                {
                                    Key = slot.Key, Label = slot.Label, Wanted = slot.PublisherValue,
                                    HowToGet = slot.HowToGet, Status = EzgStatus.None, Where = WhereOf(kind, slot.Key),
                                });
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    report.Installed = true;
                    report.Location = "lỗi đọc: " + exception.Message;
                }

                if (report.Required || report.Installed) reports.Add(report);
            }

            return reports;
        }

        internal static string NameOf(SdkKind kind) =>
            kind switch
            {
                SdkKind.Meta => "Meta (Facebook SDK)",
                SdkKind.AppsFlyer => "AppsFlyer",
                SdkKind.GameAnalytics => "GameAnalytics",
                SdkKind.Firebase => "Firebase",
                SdkKind.AppLovinMax => "AppLovin MAX (ads)",
                SdkKind.UnityIap => "Unity IAP",
                SdkKind.GooglePlayPlugins => "Google Play plugins (PAD)",
                SdkKind.AppleGameKit => "Apple GameKit",
                _ => kind.ToString(),
            };

        #endregion

        #region Detect

        private static void Detect(SdkReport report, string manifest)
        {
            switch (report.Kind)
            {
                case SdkKind.Meta:
                    report.Installed = DirExists(FACEBOOK_DIR);
                    report.Location = report.Installed ? FACEBOOK_DIR : null;
                    report.InstallHint = "Tải Facebook SDK for Unity (.unitypackage) và import; sau đó Facebook > Edit Settings.";
                    report.Links ??= new[] { ("Facebook SDK for Unity", URL_FACEBOOK_SDK) };
                    break;
                case SdkKind.AppsFlyer:
                {
                    var pkg = Match(manifest, "\"appsflyer-unity-plugin\"\\s*:\\s*\"([^\"]+)\"");
                    var script = FindScript("AppsFlyer");
                    report.Installed = pkg != null || script != null;
                    report.Location = pkg != null ? "manifest: " + Regex.Replace(pkg, ".*#", "") : script;
                    report.InstallHint = "Package Manager > Add from git URL: https://github.com/AppsFlyerSDK/appsflyer-unity-plugin.git#v6.x";
                    report.Links ??= new[] { ("AppsFlyer Unity plugin", URL_APPSFLYER_UNITY) };
                    break;
                }
                case SdkKind.GameAnalytics:
                    report.Installed = DirExists(GA_DIR) || FindScript("GameAnalytics") != null;
                    report.Location = report.Installed ? GA_DIR : null;
                    report.InstallHint = "Tải GameAnalytics Unity SDK (.unitypackage), import, rồi Assets > GameAnalytics > Select Settings.";
                    report.Links ??= new[] { ("GA Unity SDK", URL_GA_UNITY) };
                    break;
                case SdkKind.Firebase:
                {
                    var ezg = Match(manifest, "\"com\\.ezg\\.firebase\"\\s*:\\s*\"([^\"]+)\"");
                    report.Installed = DirExists(FIREBASE_DIR) || ezg != null;
                    var project = Match(ReadProjectFile(ANDROID_FIREBASE_JSON), "\"project_id\"\\s*:\\s*\"([^\"]+)\"");
                    report.Location = report.Installed ? $"{FIREBASE_DIR}{(ezg != null ? " · com.ezg.firebase " + ezg : "")}{(project != null ? " · project " + project : "")}" : null;
                    break;
                }
                case SdkKind.AppLovinMax:
                    report.Installed = DirExists(MAX_DIR) || Match(manifest, "\"com\\.ezg\\.ads\"\\s*:\\s*\"([^\"]+)\"") != null;
                    report.Location = report.Installed ? MAX_DIR : null;
                    break;
                case SdkKind.UnityIap:
                {
                    var version = Match(manifest, "\"com\\.unity\\.purchasing\"\\s*:\\s*\"([^\"]+)\"");
                    report.Installed = version != null;
                    report.Location = version == null ? null : "com.unity.purchasing " + version;
                    break;
                }
                case SdkKind.GooglePlayPlugins:
                    report.Installed = DirExists(PLAY_PLUGINS_DIR);
                    report.Location = report.Installed ? PLAY_PLUGINS_DIR : null;
                    break;
                case SdkKind.AppleGameKit:
                {
                    var version = Match(manifest, "\"com\\.apple\\.unityplugin\\.gamekit\"\\s*:\\s*\"([^\"]+)\"");
                    report.Installed = version != null;
                    report.Location = version == null ? null : "com.apple.unityplugin.gamekit " + version;
                    break;
                }
            }
        }

        #endregion

        #region ID slots

        /// <summary>Chỗ sửa một ID — chuỗi cho người đọc, dùng cả khi SDK chưa cài.</summary>
        private static string WhereOf(SdkKind kind, string key) =>
            (kind, key) switch
            {
                (SdkKind.Meta, "appId") or (SdkKind.Meta, "clientToken") =>
                    "FacebookSettings.asset (Facebook > Edit Settings) → Regenerate Android Manifest. Hoặc sheet marketing facebook.appId/clientToken → tab Marketing ghi.",
                (SdkKind.AppsFlyer, "devKey") => "GameConstant.AppsFlyerId (+ appsflyerDevKey trong MarketingConfig.json & Google Sheet marketing).",
                (SdkKind.AppsFlyer, "iosAppId") => "GameConstant.IOSAppId (+ appleId trong sheet marketing).",
                (SdkKind.GameAnalytics, "gameKey") or (SdkKind.GameAnalytics, "secretKey") =>
                    "Assets/Resources/GameAnalytics/Settings.asset (Assets > GameAnalytics > Select Settings).",
                _ => null,
            };

        private static SlotReport ReadSlot(SdkKind kind, SdkIdSlot slot)
        {
            var report = new SlotReport
            {
                Key = slot.Key, Label = slot.Label, Wanted = slot.PublisherValue, HowToGet = slot.HowToGet,
                Where = WhereOf(kind, slot.Key),
            };

            switch (kind, slot.Key)
            {
                case (SdkKind.Meta, "appId"):
                case (SdkKind.Meta, "clientToken"):
                {
                    var settings = ReadProjectFile(FACEBOOK_SETTINGS_PATH);
                    var pattern = slot.Key == "appId" ? "appIds:\\s*\\n\\s*-\\s*(\\S+)" : "clientTokens:\\s*\\n\\s*-\\s*(\\S+)";
                    report.Current = Match(settings, pattern);
                    report.Actions = new[]
                    {
                        ReadinessActions.SelectAsset("Chọn FacebookSettings", FACEBOOK_SETTINGS_PATH),
                        ReadinessActions.KitTab("Mở tab Marketing", EzgKitWindow.Tab.Marketing),
                    };
                    if (slot.Key == "appId" && !string.IsNullOrEmpty(report.Current))
                    {
                        var manifestId = Match(ReadProjectFile(ANDROID_MANIFEST),
                            "com\\.facebook\\.sdk\\.ApplicationId\"\\s+android:value=\"fb(\\d+)\"");
                        report.Note = manifestId == null
                            ? "AndroidManifest.xml chưa có meta-data ApplicationId — bấm Regenerate Android Manifest."
                            : manifestId != report.Current
                                ? $"AndroidManifest.xml đang mang fb{manifestId} — Regenerate Android Manifest."
                                : "AndroidManifest.xml khớp.";
                    }

                    break;
                }
                case (SdkKind.AppsFlyer, "devKey"):
                case (SdkKind.AppsFlyer, "iosAppId"):
                {
                    var constName = slot.Key == "devKey" ? CONST_APPSFLYER : CONST_IOS_APP_ID;
                    var path = SocialChecks.FindGameConstant();
                    var text = path == null ? null : File.ReadAllText(path);
                    report.Current = SocialChecks.ReadConst(text, constName);
                    report.GameConstantName = constName;
                    if (path != null)
                        report.Actions = new[]
                        {
                            ReadinessActions.OpenScript("Mở GameConstant.cs", path, constName),
                            ReadinessActions.KitTab("Mở tab Marketing", EzgKitWindow.Tab.Marketing),
                        };
                    break;
                }
                case (SdkKind.GameAnalytics, "gameKey"):
                case (SdkKind.GameAnalytics, "secretKey"):
                {
                    var settings = ReadProjectFile(GA_SETTINGS_PATH);
                    var pattern = slot.Key == "gameKey" ? "gameKey:\\s*\\n\\s*-\\s*(\\S+)" : "secretKey:\\s*\\n\\s*-\\s*(\\S+)";
                    var value = Match(settings, pattern);
                    report.Current = IsEmptyYaml(value) ? null : value;
                    if (settings == null) report.Note = "Chưa có Settings.asset — Assets > GameAnalytics > Select Settings tạo file.";
                    else report.Actions = new[] { ReadinessActions.SelectAsset("Chọn GA Settings", GA_SETTINGS_PATH) };
                    break;
                }
                default:
                    // ID ngoài Unity (Partner ID Meta…): không đọc được, chỉ bày giá trị + cách làm.
                    report.Status = EzgStatus.None;
                    return report;
            }

            report.Status = Grade(report);
            return report;
        }

        /// <summary>Publisher cấp: phải KHỚP (lệch là đỏ — số chảy sang tài khoản khác). Game tự tạo: có là xanh, trống là vàng.</summary>
        private static EzgStatus Grade(SlotReport slot)
        {
            if (slot.Wanted != null)
                return slot.Current == slot.Wanted ? EzgStatus.Ok : EzgStatus.Error;
            return string.IsNullOrEmpty(slot.Current) ? EzgStatus.Warn : EzgStatus.Ok;
        }

        #endregion

        #region Events

        private sealed class EventScan
        {
            internal string FirstFile, BestFile;
            internal bool BestParams, BestTrigger;
        }

        private static EventReport CheckEvent(SdkKind kind, RequiredEvent ev)
        {
            var report = new EventReport { Name = ev.Name };
            if (kind != SdkKind.AppsFlyer)
            {
                report.Status = EzgStatus.None;
                report.Note = "Tool chưa biết kiểm event của SDK này.";
                return report;
            }

            // Kết quả quét cache theo tên event + tham số tới khi source đổi (SourceIndex.Get).
            var scan = SourceIndex.Get("sdk.event:" + ev.Name + ":" + string.Join(",", ev.Parameters), files =>
            {
                var result = new EventScan();
                foreach (var source in files)
                {
                    if (source.IsThirdParty) continue;
                    var text = source.Text;
                    if (!text.Contains(ev.Name)) continue;
                    result.FirstFile ??= source.Absolute;

                    var allParams = true;
                    foreach (var parameter in ev.Parameters)
                        if (!text.Contains(parameter))
                        {
                            allParams = false;
                            break;
                        }

                    var trigger = text.Contains("OnApplicationPause") || text.Contains("OnApplicationQuit") || text.Contains("OnApplicationFocus");
                    if (result.BestFile == null || (allParams && trigger) || (allParams && !result.BestParams))
                    {
                        result.BestFile = source.Absolute;
                        result.BestParams = allParams;
                        result.BestTrigger = trigger;
                    }
                }

                return result;
            });

            var parameters = string.Join(", ", ev.Parameters);
            if (scan.FirstFile == null)
            {
                report.Status = EzgStatus.Error;
                report.Note = $"Không file .cs nào chứa \"{ev.Name}\".";
                report.Fix = $"Thêm AppsFlyer.sendEvent(\"{ev.Name}\", …) với tham số {parameters}; bắn lúc {ev.When}. Đặt cạnh AppsFlyerEvents.cs của dự án.";
                return report;
            }

            report.Value = RelativePath(scan.BestFile);
            report.Actions = new[] { ReadinessActions.OpenScript("Mở file event", scan.BestFile, Regex.Escape(ev.Name)) };
            if (!scan.BestParams)
            {
                report.Status = EzgStatus.Warn;
                report.Note = $"Có event nhưng thiếu tham số — cần đủ: {parameters}.";
                report.Fix = "Bổ sung tham số còn thiếu vào dictionary gửi cùng event.";
            }
            else if (!scan.BestTrigger)
            {
                report.Status = EzgStatus.Warn;
                report.Note = "File có event nhưng không có OnApplicationPause / OnApplicationQuit — không thấy chỗ bắn lúc xuống nền.";
                report.Fix = $"Bắn event lúc {ev.When}.";
            }
            else
            {
                report.Status = EzgStatus.Ok;
                report.Note = $"Đủ tham số ({parameters}), có chỗ bắn lúc xuống nền.";
            }

            return report;
        }

        #endregion

        #region Install spec (cho SdkSwitcher)

        /// <summary>
        ///     SDK này nằm ở đâu trong project và cài/gỡ bằng cách nào — dữ liệu cho <see cref="SdkSwitcher" />.
        ///     <para>
        ///         <see cref="CodeReferencePattern" /> là regex bắt CODE GAME (ngoài thư mục SDK) đang gọi thẳng
        ///         SDK: còn khớp thì gỡ SDK là vỡ compile, switcher CHẶN gỡ và chỉ tên file. Muốn switch sạch,
        ///         code game bọc lời gọi trong <c>#if {<see cref="Define" />}</c> — switcher gắn/gỡ define đó
        ///         theo bộ SDK của từng publisher.
        ///     </para>
        /// </summary>
        internal sealed class SdkInstallSpec
        {
            /// <summary>Tên package UPM (null = SDK chỉ nằm trong Assets/).</summary>
            internal string UpmName;

            /// <summary>Spec mặc định khi cài lại mà cache không nhớ (git URL#tag, version, file:…).</summary>
            internal string UpmDefaultSpec;

            /// <summary>Package UPM đi kèm phải gỡ/cài cùng (wrapper của Ezg phụ thuộc SDK gốc).</summary>
            internal string[] UpmAlso = Array.Empty<string>();

            /// <summary>Thư mục/asset trong Assets/ thuộc SDK — export vào cache rồi xoá khi gỡ.</summary>
            internal string[] AssetFolders = Array.Empty<string>();

            /// <summary>Trang tải .unitypackage khi cache không có.</summary>
            internal string ReleasePageUrl;

            internal string CodeReferencePattern;
            internal string Define;

            /// <summary>Cách tự tải .unitypackage khi cache không có. null = chỉ cache / kéo tay.</summary>
            internal DownloadSource Download;

            /// <summary>
            ///     SDK NỀN TẢNG của bản build (Game Center trên iOS, Play Asset Delivery trên Android): mọi
            ///     profile mặc nhiên giữ — không cần khai trong RequiredSdks, không bao giờ bị switcher gỡ.
            /// </summary>
            internal bool IsPlatform;

            /// <summary>Câu "vì sao giữ" hiện trên card khi profile không khai SDK này.</summary>
            internal string PlatformNote;

            internal bool HasAssets => AssetFolders.Length > 0;
        }

        /// <summary>
        ///     Nguồn tải .unitypackage của một SDK. Hai kiểu: <b>GitHub Releases</b> (API
        ///     <c>releases/latest</c>, chọn asset theo regex; asset là zip thì lấy entry .unitypackage bên
        ///     trong) và <b>Firebase</b> (zip theo version ở dl.google.com, chứa một .unitypackage mỗi
        ///     product — lấy đúng bộ product đang dùng, không import cả 1 GB).
        /// </summary>
        internal sealed class DownloadSource
        {
            internal string GitHubRepo;
            internal string AssetPattern;

            /// <summary>Asset là zip: regex chọn entry .unitypackage bên trong. null = asset chính là .unitypackage.</summary>
            internal string ZipEntryPattern;

            internal bool IsFirebase;
        }

        /// <summary>URL zip Firebase Unity SDK theo version; không biết version thì <see cref="FIREBASE_LATEST_URL" /> (redirect về bản mới nhất).</summary>
        internal const string FIREBASE_LATEST_URL = "https://firebase.google.com/download/unity";

        internal static string FirebaseZipUrl(string version) =>
            string.IsNullOrEmpty(version) ? FIREBASE_LATEST_URL : $"https://dl.google.com/firebase/sdk/unity/firebase_unity_sdk_{version}.zip";

        /// <summary>Bộ product Firebase của template Ezg — dùng khi project chưa có Firebase để biết import gì.</summary>
        internal static readonly string[] FirebaseDefaultProducts =
        {
            "FirebaseAnalytics", "FirebaseAuth", "FirebaseCrashlytics", "FirebaseFirestore",
            "FirebaseFunctions", "FirebaseMessaging", "FirebaseRemoteConfig", "FirebaseStorage",
        };

        /// <summary>
        ///     Product + version Firebase đang cài, đọc từ <c>Assets/Firebase/Editor/{Product}_version-{ver}_manifest.txt</c>.
        ///     Không có → version null, product = <see cref="FirebaseDefaultProducts" />.
        /// </summary>
        internal static string[] FirebaseInstalled(out string version)
        {
            version = null;
            var dir = Path.Combine(ProjectRoot(), FIREBASE_DIR, "Editor");
            if (!Directory.Exists(dir)) return FirebaseDefaultProducts;

            var products = new List<string>();
            foreach (var file in Directory.EnumerateFiles(dir, "*_manifest.txt"))
            {
                var match = Regex.Match(Path.GetFileName(file), "^(Firebase[A-Za-z]+)_version-(\\d+\\.\\d+\\.\\d+)_manifest\\.txt$");
                if (!match.Success) continue;
                products.Add(match.Groups[1].Value);
                version ??= match.Groups[2].Value;
            }

            products.Sort(StringComparer.Ordinal);
            return products.Count == 0 ? FirebaseDefaultProducts : products.ToArray();
        }

        internal static SdkInstallSpec SpecOf(SdkKind kind)
        {
            if (!_specs.TryGetValue(kind, out var spec)) _specs[kind] = spec = BuildSpec(kind);
            return spec;
        }

        private static SdkInstallSpec BuildSpec(SdkKind kind) =>
            kind switch
            {
                SdkKind.Meta => new SdkInstallSpec
                {
                    AssetFolders = new[] { FACEBOOK_DIR },
                    ReleasePageUrl = "https://github.com/facebook/facebook-sdk-for-unity/releases",
                    Download = new DownloadSource
                    {
                        GitHubRepo = "facebook/facebook-sdk-for-unity", AssetPattern = "^facebook-unity-sdk-.*\\.zip$",
                        ZipEntryPattern = "\\.unitypackage$",
                    },
                    CodeReferencePattern = "\\bFB\\.|Facebook\\.Unity",
                    Define = "EZG_SDK_META",
                },
                SdkKind.AppsFlyer => new SdkInstallSpec
                {
                    UpmName = "appsflyer-unity-plugin",
                    UpmDefaultSpec = "https://github.com/AppsFlyerSDK/appsflyer-unity-plugin.git#v6.18.1",
                    ReleasePageUrl = "https://github.com/AppsFlyerSDK/appsflyer-unity-plugin/releases",
                    CodeReferencePattern = "\\bAppsFlyer\\.",
                    Define = "EZG_SDK_APPSFLYER",
                },
                SdkKind.GameAnalytics => new SdkInstallSpec
                {
                    AssetFolders = new[] { GA_DIR, "Assets/Resources/GameAnalytics" },
                    ReleasePageUrl = "https://github.com/GameAnalytics/GA-SDK-UNITY/releases",
                    Download = new DownloadSource { GitHubRepo = "GameAnalytics/GA-SDK-UNITY", AssetPattern = "^GA_SDK_UNITY\\.unitypackage$" },
                    CodeReferencePattern = "GameAnalyticsSDK|\\bGameAnalytics\\.",
                    Define = "EZG_SDK_GA",
                },
                SdkKind.Firebase => new SdkInstallSpec
                {
                    UpmName = "com.ezg.firebase",
                    UpmDefaultSpec = "0.1.4",
                    AssetFolders = new[]
                    {
                        FIREBASE_DIR, "Assets/Plugins/Android/FirebaseApp.androidlib",
                        "Assets/Plugins/Android/FirebaseCrashlytics.androidlib",
                    },
                    ReleasePageUrl = "https://firebase.google.com/download/unity",
                    Download = new DownloadSource { IsFirebase = true },
                    CodeReferencePattern = "using Firebase|\\bFirebase\\.(Analytics|Crashlytics|RemoteConfig|Auth|Firestore|Storage|Messaging|FirebaseApp)|Ezg\\.Core\\.Firebase",
                    Define = "EZG_SDK_FIREBASE",
                },
                SdkKind.AppLovinMax => new SdkInstallSpec
                {
                    UpmName = "com.ezg.ads",
                    UpmDefaultSpec = "0.2.0",
                    AssetFolders = new[] { MAX_DIR },
                    ReleasePageUrl = "https://github.com/AppLovin/AppLovin-MAX-Unity-Plugin/releases",
                    Download = new DownloadSource { GitHubRepo = "AppLovin/AppLovin-MAX-Unity-Plugin", AssetPattern = "^AppLovin-MAX-Unity-Plugin-.*\\.unitypackage$" },
                    CodeReferencePattern = "\\bMaxSdk\\b|MaxSdkCallbacks|using Ezg\\.Ads|Ezg\\.Core\\.Ads",
                    Define = "EZG_SDK_MAX",
                },
                SdkKind.UnityIap => new SdkInstallSpec
                {
                    UpmName = "com.unity.purchasing",
                    UpmDefaultSpec = "5.4.0",
                    UpmAlso = new[] { "com.ezg.iap" },
                    CodeReferencePattern = "UnityEngine\\.Purchasing|\\bInAppManager\\b",
                    Define = "EZG_SDK_IAP",
                },
                SdkKind.GooglePlayPlugins => new SdkInstallSpec
                {
                    IsPlatform = true,
                    PlatformNote = "SDK nền tảng Android — Play Asset Delivery (AssetBundle theo AAB) + In-App Review. Đi với mọi bản build, giữ dù đi với publisher nào.",
                    AssetFolders = new[] { PLAY_PLUGINS_DIR },
                    ReleasePageUrl = "https://github.com/google/play-unity-plugins/releases",
                    CodeReferencePattern = "Google\\.Play\\.",
                    Define = "EZG_SDK_PAD",
                },
                SdkKind.AppleGameKit => new SdkInstallSpec
                {
                    IsPlatform = true,
                    PlatformNote = "SDK nền tảng iOS — Game Center / Sign in with Apple. Đi với mọi bản build, giữ dù đi với publisher nào.",
                    UpmName = "com.apple.unityplugin.gamekit",
                    UpmDefaultSpec = "file:com.apple.unityplugin.gamekit-4.0.1.tgz",
                    CodeReferencePattern = "Apple\\.GameKit",
                    Define = "EZG_SDK_GAMEKIT",
                },
                _ => new SdkInstallSpec(),
            };

        /// <summary>Spec hiện tại của một package trong manifest.json; null = không có.</summary>
        internal static string UpmSpec(string packageName) =>
            Match(ReadProjectFile(PACKAGES_MANIFEST), "\"" + Regex.Escape(packageName) + "\"\\s*:\\s*\"([^\"]+)\"");

        /// <summary>
        ///     File .cs của GAME (ngoài mọi thư mục SDK) còn gọi thẳng từng SDK — MỘT lượt đọc cho mọi
        ///     <paramref name="kinds" /> (1600 file × 5 SDK đọc riêng là 1,5 s mỗi lần đổi tab). Trả về
        ///     kind → (tổng số file, tối đa <paramref name="limit" /> đường dẫn mẫu).
        /// </summary>
        internal static Dictionary<SdkKind, (int Total, List<string> Samples)> CodeReferences(IEnumerable<SdkKind> kinds, int limit)
        {
            // Quét MỘT lượt cho cả 8 SDK trên SourceIndex và cache tới khi source đổi — mọi profile dùng chung.
            var all = SourceIndex.Get("sdk.refs", files =>
            {
                var result = new Dictionary<SdkKind, (int Total, List<string> Samples)>();
                var regexes = new List<(SdkKind Kind, Regex Regex)>();
                foreach (var kind in _kinds)
                {
                    result[kind] = (0, new List<string>());
                    var pattern = SpecOf(kind).CodeReferencePattern;
                    if (!string.IsNullOrEmpty(pattern)) regexes.Add((kind, new Regex(pattern, RegexOptions.Compiled)));
                }

                foreach (var source in files)
                {
                    if (source.IsThirdParty) continue;
                    foreach (var (kind, regex) in regexes)
                    {
                        if (!regex.IsMatch(source.Text)) continue;
                        var (total, samples) = result[kind];
                        if (samples.Count < limit) samples.Add(source.Relative);
                        result[kind] = (total + 1, samples);
                    }
                }

                return result;
            });

            var picked = new Dictionary<SdkKind, (int, List<string>)>();
            foreach (var kind in kinds) picked[kind] = all[kind];
            return picked;
        }

        #endregion

        #region Helpers


        private static string ProjectRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static bool DirExists(string projectRelative) => Directory.Exists(Path.Combine(ProjectRoot(), projectRelative));

        private static string ReadProjectFile(string projectRelativePath)
        {
            try
            {
                var path = Path.Combine(ProjectRoot(), projectRelativePath);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Match(string text, string pattern)
        {
            if (text == null) return null;
            var match = Regex.Match(text, pattern);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        private static string FindScript(string className)
        {
            foreach (var guid in AssetDatabase.FindAssets(className + " t:Script"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path) == className + ".cs") return path;
            }

            return null;
        }

        private static bool IsEmptyYaml(string value) =>
            string.IsNullOrEmpty(value) || value == "''" || value == "\"\"" || value == "-";

        private static string RelativePath(string absolute)
        {
            var root = ProjectRoot().Replace('\\', '/');
            var normalized = absolute.Replace('\\', '/');
            return normalized.StartsWith(root) ? normalized.Substring(root.Length).TrimStart('/') : normalized;
        }

        #endregion
    }
}
#endif
