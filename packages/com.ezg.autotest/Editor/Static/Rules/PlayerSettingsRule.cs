using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Player Settings sẵn sàng release cho CẢ Android lẫn iOS (không phụ thuộc target đang chọn): bundle id,
    ///     version, IL2CPP + ARM64, target/min API, keystore, cờ development/debug, tối ưu build. Chỉ đọc — không
    ///     bao giờ ghi PlayerSettings, không in mật khẩu keystore.
    /// </summary>
    public sealed class PlayerSettingsRule : IStaticRule
    {
        const string CATEGORY_IDENTITY = "Build.Identity";
        const string CATEGORY_VERSION = "Build.Version";
        const string CATEGORY_ANDROID = "Build.Android";
        const string CATEGORY_IOS = "Build.iOS";
        const string CATEGORY_SIGNING = "Build.Signing";
        const string CATEGORY_FLAGS = "Build.Flags";
        const string CATEGORY_OPTIMIZE = "Build.Optimize";
        const string SETTINGS_PATH = "ProjectSettings/ProjectSettings.asset";
        const string BUILD_UI = "File/Build Profiles";
        const string UI_PLAYER = "Project Settings/Player";

        static readonly Regex ANDROID_ID = new(@"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$");
        static readonly Regex IOS_ID = new(@"^[A-Za-z0-9\-]+(\.[A-Za-z0-9\-]+)+$");
        static readonly Regex NUMERIC_VERSION = new(@"^\d+(\.\d+){0,3}$");

        static readonly HashSet<string> PLACEHOLDER_SEGMENTS = new(StringComparer.OrdinalIgnoreCase)
        {
            "unity", "unity3d", "unitytechnologies", "template", "company", "yourcompany", "mycompany",
            "defaultcompany", "productname", "example"
        };

        // Segment trùng từ khoá Java ⇒ Gradle/aapt từ chối package name.
        static readonly HashSet<string> JAVA_KEYWORDS = new(StringComparer.Ordinal)
        {
            "abstract", "assert", "boolean", "break", "byte", "case", "catch", "char", "class", "const", "continue",
            "default", "do", "double", "else", "enum", "extends", "final", "finally", "float", "for", "goto", "if",
            "implements", "import", "instanceof", "int", "interface", "long", "native", "new", "package", "private",
            "protected", "public", "return", "short", "static", "strictfp", "super", "switch", "synchronized", "this",
            "throw", "throws", "transient", "try", "void", "volatile", "while", "true", "false", "null"
        };

        public string Id => "player-settings";
        public string Name => "Player Settings: sẵn sàng release";

        public string Description =>
            "Bundle id, version, IL2CPP + ARM64, Target/Min API, keystore, iOS min version, cờ Development/Debug, " +
            "stripping/GC — kiểm tra cả Android và iOS bất kể target đang chọn.";

        public string Category => "Build";
        public int Order => 110;

        public async Task Run(AutoTestContext ctx)
        {
            await StaticCheckUtil.Yield(ctx, 0);
            var cfg = ctx.Config.staticCheck;
            var table = new List<KeyValuePair<string, string>>();

            void Row(string key, object value)
            {
                table.Add(new KeyValuePair<string, string>(key, value == null ? "(null)" : value.ToString()));
            }

            CheckIdentity(ctx, Row);
            await StaticCheckUtil.Yield(ctx, 0);
            CheckVersions(ctx, Row);
            var androidBackend = CheckAndroid(ctx, cfg, Row);
            CheckIos(ctx, cfg, Row);
            CheckBuildFlags(ctx, Row);
            CheckOptimization(ctx, androidBackend, Row);

            ctx.Attach("player-settings.txt", FormatTable(table), "Tóm tắt Player Settings");
        }

        #region Identity

        static void CheckIdentity(AutoTestContext ctx, Action<string, object> row)
        {
            var company = PlayerSettings.companyName;
            var product = PlayerSettings.productName;
            row("Company Name", company);
            row("Product Name", product);

            if (string.IsNullOrWhiteSpace(company) ||
                company.Trim().Equals("DefaultCompany", StringComparison.OrdinalIgnoreCase))
                ctx.Report(Severity.Major, CATEGORY_IDENTITY, "Company Name còn mặc định",
                    $"Company Name = \"{company}\" — tên này hiện trong thông tin app và đường dẫn lưu dữ liệu (persistentDataPath).",
                    SETTINGS_PATH, UI_PLAYER + "/Company Name", "tên công ty thật", company);

            if (string.IsNullOrWhiteSpace(product) ||
                product.StartsWith("New Unity Project", StringComparison.OrdinalIgnoreCase) ||
                product.StartsWith("My project", StringComparison.OrdinalIgnoreCase))
                ctx.Report(Severity.Major, CATEGORY_IDENTITY, "Product Name còn mặc định",
                    $"Product Name = \"{product}\" — đây là tên app hiển thị dưới icon trên máy người chơi.",
                    SETTINGS_PATH, UI_PLAYER + "/Product Name", "tên app thật", product);

            var androidId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            var iosId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS);
            row("Bundle ID Android", androidId);
            row("Bundle ID iOS", iosId);
            CheckAppId(ctx, "Android", androidId, true);
            CheckAppId(ctx, "iOS", iosId, false);

            if (!string.IsNullOrWhiteSpace(androidId) && !string.IsNullOrWhiteSpace(iosId) &&
                !string.Equals(androidId, iosId, StringComparison.Ordinal))
                ctx.Report(Severity.Info, CATEGORY_IDENTITY, "Bundle ID Android và iOS khác nhau",
                    $"Android: {androidId} — iOS: {iosId}. Bình thường nếu cố ý; kiểm tra khớp với store/Firebase của từng nền tảng.",
                    SETTINGS_PATH, UI_PLAYER + "/Other Settings/Identification");
        }

        static void CheckAppId(AutoTestContext ctx, string platform, string id, bool android)
        {
            var uiPath = $"{UI_PLAYER}/{platform}/Other Settings/Identification/Package Name";
            if (string.IsNullOrWhiteSpace(id))
            {
                ctx.Report(Severity.Critical, CATEGORY_IDENTITY, $"Bundle ID {platform} trống",
                    "Không có bundle id/package name — không build/upload được lên store.", SETTINGS_PATH, uiPath);
                return;
            }

            var segments = id.Split('.');
            var placeholder = id.Equals("com.Company.ProductName", StringComparison.OrdinalIgnoreCase) ||
                              id.IndexOf("DefaultCompany", StringComparison.OrdinalIgnoreCase) >= 0;
            foreach (var seg in segments)
                if (PLACEHOLDER_SEGMENTS.Contains(seg))
                    placeholder = true;

            if (placeholder)
            {
                ctx.Report(Severity.Critical, CATEGORY_IDENTITY, $"Bundle ID {platform} còn là giá trị mẫu",
                    $"\"{id}\" là bundle id mặc định/placeholder — đổi thành id thật của app (khớp store + Firebase) trước khi build.",
                    SETTINGS_PATH, uiPath, "bundle id thật của app", id);
                return;
            }

            if (!(android ? ANDROID_ID : IOS_ID).IsMatch(id))
            {
                ctx.Report(Severity.Critical, CATEGORY_IDENTITY, $"Bundle ID {platform} sai định dạng",
                    android
                        ? $"\"{id}\": package name Android phải có ≥ 2 phần ngăn bởi dấu chấm, mỗi phần bắt đầu bằng chữ cái, chỉ gồm chữ/số/gạch dưới."
                        : $"\"{id}\": bundle id iOS chỉ gồm chữ, số, gạch ngang và dấu chấm (không dùng gạch dưới), ≥ 2 phần.",
                    SETTINGS_PATH, uiPath, null, id);
                return;
            }

            if (!android) return;
            foreach (var seg in segments)
                if (JAVA_KEYWORDS.Contains(seg))
                {
                    ctx.Report(Severity.Critical, CATEGORY_IDENTITY, "Bundle ID Android chứa từ khoá Java",
                        $"Phần \"{seg}\" trong \"{id}\" là từ khoá Java — build Gradle sẽ lỗi. Đổi tên phần này.",
                        SETTINGS_PATH, uiPath, null, id);
                    return;
                }
        }

        #endregion

        #region Version

        static void CheckVersions(AutoTestContext ctx, Action<string, object> row)
        {
            var version = PlayerSettings.bundleVersion;
            row("Version (bundleVersion)", version);
            if (string.IsNullOrWhiteSpace(version))
                ctx.Report(Severity.Major, CATEGORY_VERSION, "Version (bundleVersion) trống",
                    "Store cần version hiển thị (vd 1.0.0).", SETTINGS_PATH, UI_PLAYER + "/Other Settings/Version");
            else if (!NUMERIC_VERSION.IsMatch(version.Trim()))
                ctx.Report(Severity.Minor, CATEGORY_VERSION, "Version không đúng dạng số x.y.z",
                    $"\"{version}\" — App Store chỉ nhận version dạng số ngăn bởi dấu chấm (vd 1.2.3).",
                    SETTINGS_PATH, UI_PLAYER + "/Other Settings/Version", "x.y.z", version);

            var versionCode = PlayerSettings.Android.bundleVersionCode;
            row("Android Bundle Version Code", versionCode);
            if (versionCode <= 0)
                ctx.Report(Severity.Major, CATEGORY_VERSION, "Android Bundle Version Code không hợp lệ",
                    "Version code phải > 0 và tăng dần mỗi lần upload Google Play.",
                    SETTINGS_PATH, UI_PLAYER + "/Android/Other Settings/Bundle Version Code", "> 0",
                    versionCode.ToString());

            var buildNumber = PlayerSettings.iOS.buildNumber;
            row("iOS Build Number", buildNumber);
            if (string.IsNullOrWhiteSpace(buildNumber))
                ctx.Report(Severity.Major, CATEGORY_VERSION, "iOS Build Number trống",
                    "App Store Connect cần build number (tăng dần mỗi lần upload).",
                    SETTINGS_PATH, UI_PLAYER + "/iOS/Other Settings/Build");
            else if (!NUMERIC_VERSION.IsMatch(buildNumber.Trim()))
                ctx.Report(Severity.Minor, CATEGORY_VERSION, "iOS Build Number không đúng dạng số",
                    $"\"{buildNumber}\" — App Store chỉ nhận số ngăn bởi dấu chấm.",
                    SETTINGS_PATH, UI_PLAYER + "/iOS/Other Settings/Build", "số", buildNumber);
        }

        #endregion

        #region Android

        static ScriptingImplementation CheckAndroid(AutoTestContext ctx, StaticCheckConfig cfg,
            Action<string, object> row)
        {
            const string OTHER = UI_PLAYER + "/Android/Other Settings";

            var backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android);
            row("Android Scripting Backend", backend);
            if (backend != ScriptingImplementation.IL2CPP)
                ctx.Report(Severity.Major, CATEGORY_ANDROID, "Android chưa dùng IL2CPP",
                    $"Scripting Backend = {backend}. Mono không build được ARM64 (Google Play bắt buộc 64-bit); IL2CPP còn nhanh hơn và khó dịch ngược hơn.",
                    SETTINGS_PATH, OTHER + "/Configuration/Scripting Backend", "IL2CPP", backend.ToString());

            var arch = PlayerSettings.Android.targetArchitectures;
            row("Android Target Architectures", arch);
            if ((arch & AndroidArchitecture.ARM64) == 0)
                ctx.Report(Severity.Blocker, CATEGORY_ANDROID, "Android chưa bật ARM64",
                    $"Target Architectures = {arch}. Google Play từ chối app không có bản 64-bit (ARM64).",
                    SETTINGS_PATH, OTHER + "/Configuration/Target Architectures", "có ARM64", arch.ToString());

            var target = PlayerSettings.Android.targetSdkVersion;
            if (target == AndroidSdkVersions.AndroidApiLevelAuto)
            {
                row("Android Target API", "Automatic (theo SDK cài)");
                ctx.Report(Severity.Info, CATEGORY_ANDROID, "Android Target API = Automatic (tự động theo SDK cài)",
                    $"Bản build dùng API cao nhất có trong Android SDK của máy build — đảm bảo máy build/CI có SDK ≥ {cfg.androidTargetApiMin}.",
                    SETTINGS_PATH, OTHER + "/Identification/Target API Level");
            }
            else
            {
                var targetApi = (int)target;
                row("Android Target API", targetApi);
                if (targetApi < cfg.androidTargetApiMin)
                    ctx.Report(Severity.Critical, CATEGORY_ANDROID, "Android Target API thấp hơn yêu cầu của Google Play",
                        $"Target API = {targetApi}, yêu cầu ≥ {cfg.androidTargetApiMin}. Google Play không nhận bản cập nhật target API cũ.",
                        SETTINGS_PATH, OTHER + "/Identification/Target API Level", "≥ " + cfg.androidTargetApiMin,
                        targetApi.ToString());
            }

            var minApi = (int)PlayerSettings.Android.minSdkVersion;
            row("Android Minimum API", minApi);
            if (minApi < cfg.androidMinApiMin)
                ctx.Report(Severity.Minor, CATEGORY_ANDROID, "Android Minimum API thấp hơn cấu hình",
                    $"Minimum API = {minApi}, cấu hình tối thiểu {cfg.androidMinApiMin}. Máy quá cũ tốn công test/hỗ trợ và một số SDK (ads, Firebase) không còn hỗ trợ.",
                    SETTINGS_PATH, OTHER + "/Identification/Minimum API Level", "≥ " + cfg.androidMinApiMin,
                    minApi.ToString());

            CheckKeystore(ctx, row);

            var aab = EditorUserBuildSettings.buildAppBundle;
            row("Build App Bundle (.aab)", aab);
            if (!aab)
                ctx.Report(Severity.Info, CATEGORY_ANDROID, "Chưa bật Build App Bundle (.aab)",
                    "Google Play yêu cầu .aab khi upload. Bỏ qua nếu script CI tự bật lúc build release.",
                    BUILD_UI, BUILD_UI + "/Android/Build App Bundle (Google Play)");

            return backend;
        }

        static void CheckKeystore(AutoTestContext ctx, Action<string, object> row)
        {
            const string PUBLISHING = UI_PLAYER + "/Android/Publishing Settings";
            var custom = PlayerSettings.Android.useCustomKeystore;
            row("Android Custom Keystore", custom);
            if (!custom)
            {
                ctx.Report(Severity.Major, CATEGORY_SIGNING, "Chưa cấu hình keystore release",
                    "Build sẽ ký bằng debug keystore → không upload được Google Play và không cập nhật được bản đã phát hành.",
                    SETTINGS_PATH, PUBLISHING + "/Custom Keystore");
                return;
            }

            // Chỉ đọc đường dẫn + alias — TUYỆT ĐỐI không đọc/in mật khẩu.
            var keystore = PlayerSettings.Android.keystoreName;
            row("Android Keystore", string.IsNullOrWhiteSpace(keystore) ? "(trống)" : keystore);
            if (string.IsNullOrWhiteSpace(keystore))
                ctx.Report(Severity.Critical, CATEGORY_SIGNING, "Chưa chọn file keystore",
                    "Đã bật Custom Keystore nhưng chưa chọn file — build release sẽ lỗi.",
                    SETTINGS_PATH, PUBLISHING + "/Keystore");
            else if (!File.Exists(ResolveKeystorePath(keystore)))
                ctx.Report(Severity.Critical, CATEGORY_SIGNING, "Không tìm thấy file keystore",
                    $"\"{keystore}\" không tồn tại trên máy này — build release sẽ lỗi (máy build/CI cần có file này).",
                    SETTINGS_PATH, PUBLISHING + "/Keystore", "file tồn tại", "không có");

            var alias = PlayerSettings.Android.keyaliasName;
            row("Android Key Alias", string.IsNullOrWhiteSpace(alias) ? "(trống)" : alias);
            if (string.IsNullOrWhiteSpace(alias))
                ctx.Report(Severity.Major, CATEGORY_SIGNING, "Chưa chọn key alias",
                    "Keystore đã chọn nhưng chưa chọn alias để ký.", SETTINGS_PATH, PUBLISHING + "/Project Key/Alias");
        }

        static string ResolveKeystorePath(string keystore)
        {
            var p = keystore.Trim();
            if (p.StartsWith("~", StringComparison.Ordinal))
                p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p.Substring(1);
            return Path.IsPathRooted(p) ? p : Path.Combine(StaticCheckUtil.ProjectRoot(), p);
        }

        #endregion

        #region iOS

        static void CheckIos(AutoTestContext ctx, StaticCheckConfig cfg, Action<string, object> row)
        {
            var target = PlayerSettings.iOS.targetOSVersionString;
            row("iOS Target minimum version", target);
            if (!TryParseVersion(target, out var actual)) return;
            if (!TryParseVersion(cfg.iosMinVersion, out var required))
            {
                ctx.Report(Severity.Info, CATEGORY_IOS, "iosMinVersion trong settings Auto Test không hợp lệ",
                    $"\"{cfg.iosMinVersion}\" không phải số phiên bản — bỏ qua so sánh iOS min version.");
                return;
            }

            if (actual < required)
                ctx.Report(Severity.Minor, CATEGORY_IOS, "iOS Target minimum version thấp hơn cấu hình",
                    $"Target minimum iOS = {target}, cấu hình tối thiểu {cfg.iosMinVersion}. Nhiều SDK (Firebase, ads) yêu cầu iOS mới hơn → lỗi khi build Xcode/pod install.",
                    SETTINGS_PATH, UI_PLAYER + "/iOS/Other Settings/Target minimum iOS Version",
                    "≥ " + cfg.iosMinVersion, target);
        }

        static bool TryParseVersion(string s, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(s)) return false;
            var v = s.Trim();
            if (v.IndexOf('.') < 0) v += ".0";
            return Version.TryParse(v, out version);
        }

        #endregion

        #region Build flags + tối ưu

        static void CheckBuildFlags(AutoTestContext ctx, Action<string, object> row)
        {
            var dev = EditorUserBuildSettings.development;
            var debugging = EditorUserBuildSettings.allowDebugging;
            var profiler = EditorUserBuildSettings.connectProfiler;
            row("Development Build", dev);
            row("Script Debugging", debugging);
            row("Autoconnect Profiler", profiler);

            if (dev)
                ctx.Report(Severity.Major, CATEGORY_FLAGS, "Development Build đang bật",
                    "Bản build sẽ có watermark \"Development Build\", chậm hơn và in log debug — tắt trước khi build release.",
                    BUILD_UI, BUILD_UI + "/Development Build", "tắt", "bật");
            if (debugging)
                ctx.Report(Severity.Minor, CATEGORY_FLAGS, "Script Debugging đang bật",
                    "Cho phép gắn debugger vào app — chỉ dùng khi dev, tắt khi build release.",
                    BUILD_UI, BUILD_UI + "/Script Debugging", "tắt", "bật");
            if (profiler)
                ctx.Report(Severity.Minor, CATEGORY_FLAGS, "Autoconnect Profiler đang bật",
                    "App sẽ tự kết nối Profiler khi mở — tắt khi build release.",
                    BUILD_UI, BUILD_UI + "/Autoconnect Profiler", "tắt", "bật");
        }

        static void CheckOptimization(AutoTestContext ctx, ScriptingImplementation androidBackend,
            Action<string, object> row)
        {
            CheckStripping(ctx, "Android", NamedBuildTarget.Android, androidBackend, row);
            // iOS luôn build bằng IL2CPP.
            CheckStripping(ctx, "iOS", NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP, row);

            var stripEngine = PlayerSettings.stripEngineCode;
            row("Strip Engine Code", stripEngine);
            if (!stripEngine)
                ctx.Report(Severity.Info, CATEGORY_OPTIMIZE, "Strip Engine Code đang tắt",
                    "Bật để bỏ các module engine không dùng → build nhỏ hơn (chỉ có tác dụng với IL2CPP).",
                    SETTINGS_PATH, UI_PLAYER + "/Other Settings/Optimization/Strip Engine Code");

            var incrementalGc = PlayerSettings.gcIncremental;
            row("Incremental GC", incrementalGc);
            if (!incrementalGc)
                ctx.Report(Severity.Info, CATEGORY_OPTIMIZE, "Incremental GC đang tắt",
                    "Bật để GC chia nhỏ qua nhiều frame → giảm giật hình trên mobile.",
                    SETTINGS_PATH, UI_PLAYER + "/Other Settings/Configuration/Use incremental GC");
        }

        static void CheckStripping(AutoTestContext ctx, string platform, NamedBuildTarget target,
            ScriptingImplementation backend, Action<string, object> row)
        {
            var level = PlayerSettings.GetManagedStrippingLevel(target);
            row($"Managed Stripping {platform}", level);
            if (backend == ScriptingImplementation.IL2CPP && level == ManagedStrippingLevel.Disabled)
                ctx.Report(Severity.Info, CATEGORY_OPTIMIZE, $"Managed Stripping {platform} đang tắt",
                    "Bật mức Minimal/Low để giảm dung lượng build IL2CPP (kiểm tra link.xml nếu dùng reflection).",
                    SETTINGS_PATH, $"{UI_PLAYER}/{platform}/Other Settings/Optimization/Managed Stripping Level");
        }

        #endregion

        static string FormatTable(List<KeyValuePair<string, string>> rows)
        {
            var width = 0;
            foreach (var r in rows) width = Math.Max(width, r.Key.Length);
            var sb = new StringBuilder();
            sb.Append("# Player Settings (Android + iOS) — chỉ đọc, không chứa mật khẩu\n");
            foreach (var r in rows) sb.Append(r.Key.PadRight(width)).Append(" : ").Append(r.Value).Append('\n');
            return sb.ToString();
        }
    }
}
