using System;
using System.Collections.Generic;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Toàn bộ cấu hình của hệ thống auto test — dạng dữ liệu thuần (JsonUtility) để dùng được cả trong
    ///     Editor (lưu ở ProjectSettings, commit chung cho team) lẫn trên device (bake vào build test).
    /// </summary>
    [Serializable]
    public class AutoTestConfig
    {
        public GeneralConfig general = new();
        public AdapterConfig adapter = new();
        public StaticCheckConfig staticCheck = new();
        public SmokeConfig smoke = new();
        public UiAuditConfig uiAudit = new();
        public ButtonSweepConfig buttonSweep = new();
        public MonkeyConfig monkey = new();
        public EconomyConfig economy = new();
        public PerformanceConfig performance = new();
        public VisualRegressionConfig visual = new();
        public DeviceConfig device = new();

        /// <summary>Id các suite bị tắt hẳn (không hiện "chạy tất cả").</summary>
        public List<string> disabledSuites = new();

        /// <summary>Id case bị tắt, dạng "suiteId/caseId".</summary>
        public List<string> disabledCases = new();

        public bool IsSuiteEnabled(string suiteId)
        {
            return !disabledSuites.Contains(suiteId);
        }

        public bool IsCaseEnabled(string suiteId, string caseId)
        {
            return !disabledCases.Contains(suiteId + "/" + caseId);
        }
    }

    public enum LogErrorPolicy
    {
        /// <summary>Error/Exception log trong lúc case chạy ⇒ case Lỗi.</summary>
        FailCase = 0,

        /// <summary>Chỉ ghi issue mức Minor, case Cảnh báo.</summary>
        WarnCase = 1,

        /// <summary>Chỉ đính kèm log, không tính là lỗi.</summary>
        Ignore = 2
    }

    [Serializable]
    public class GeneralConfig
    {
        /// <summary>Thư mục report, tương đối với thư mục project.</summary>
        public string reportFolder = "AutoTestReports";

        /// <summary>Số lượt report giữ lại (cũ hơn thì xoá).</summary>
        public int keepReports = 30;

        public bool openReportWhenFinished = true;

        /// <summary>Timeout mặc định mỗi case (giây).</summary>
        public float caseTimeoutSeconds = 120f;

        /// <summary>Severity từ mức này trở lên ⇒ case Lỗi (thấp hơn ⇒ Cảnh báo).</summary>
        public Severity failSeverity = Severity.Major;

        public LogErrorPolicy exceptionPolicy = LogErrorPolicy.FailCase;
        public LogErrorPolicy errorLogPolicy = LogErrorPolicy.FailCase;
        public bool captureWarnings = true;

        /// <summary>Regex log bỏ qua (nhiễu đã biết: SDK chưa cấu hình trong Editor…).</summary>
        public List<string> ignoredLogPatterns = new()
        {
            "Firebase.*(not|isn't).*(initialized|available|configured)",
            "AppsFlyer",
            "\\[Ads?\\].*(editor|not supported)",
            "MaxSdk",
            "The referenced script \\(Unknown\\) on this Behaviour is missing"
        };

        /// <summary>Mỗi suite PlayMode chạy trong một phiên Play riêng (cô lập state giữa các suite).</summary>
        public bool isolateSuitesInPlaySessions = true;

        /// <summary>Scene khởi động khi vào Play (rỗng = scene đầu tiên trong Build Settings).</summary>
        public string bootScenePath = "";

        /// <summary>Thời gian tối đa chờ game boot xong (giây).</summary>
        public float bootTimeoutSeconds = 90f;

        /// <summary>Chờ thêm sau khi game báo sẵn sàng để các popup đầu game hiện hết (giây).</summary>
        public float settleSecondsAfterBoot = 3f;

        /// <summary>Sao lưu + khôi phục PlayerPrefs quanh các suite có sửa dữ liệu người chơi.</summary>
        public bool sandboxPlayerData = true;

        /// <summary>Tự bỏ Pause khi Console bật "Error Pause" (nếu không test sẽ treo).</summary>
        public bool autoUnpause = true;

        public bool captureScreenshots = true;

        /// <summary>Key PlayerPrefs sao lưu thêm (ngoài key adapter tự biết).</summary>
        public List<string> extraSaveKeys = new();

        /// <summary>Tỉ lệ thu nhỏ ảnh chụp (1 = gốc) để report nhẹ.</summary>
        public float screenshotScale = 0.5f;
    }

    /// <summary>Tên type của template để adapter phản chiếu (đổi được nếu project đặt tên khác).</summary>
    [Serializable]
    public class AdapterConfig
    {
        /// <summary>Type đầy đủ của adapter tuỳ biến (rỗng = tự chọn: adapter của project → EZG template → Generic).</summary>
        public string adapterType = "";

        public string uiManagerType = "UIManager";
        public string featuresEnumType = "GameEnums+Features";
        public string playerDataManagerType = "PlayerDataManager";
        public string playerResourceType = "PlayerResource";
        public string moneyTypesEnumType = "EnumBase+MoneyTypes";
        public string dataManagerType = "DataManager";
        public string rewardsServiceType = "RewardsService";
        public string purchaseManagerType = "PurchaseManager";
        public string timeManagerType = "TimeManager";

        /// <summary>Giá trị tiền tệ giả (không phải tiền thật) bỏ qua khi test economy.</summary>
        public List<string> pseudoCurrencies = new() { "None", "Ads", "Cash", "Free", "Iap", "IAP" };
    }

    [Serializable]
    public class StaticCheckConfig
    {
        /// <summary>Thư mục quét (rỗng = cả Assets).</summary>
        public List<string> includeFolders = new() { "Assets/_Project" };

        /// <summary>Thư mục bỏ qua (third-party, sample…).</summary>
        public List<string> excludeFolders = new()
        {
            "Assets/Plugins", "Assets/ThirdParty", "Assets/3rdParty", "Assets/_Project/3rdParty",
            "Assets/Samples", "Assets/TextMesh Pro/Examples & Extras"
        };

        public int maxTextureSize = 2048;
        public float maxTextureMemoryMb = 16f;
        public float maxAudioFileMb = 5f;
        public float maxAssetFileMb = 50f;
        public int androidTargetApiMin = 35;
        public int androidMinApiMin = 24;
        public string iosMinVersion = "13.0";

        /// <summary>Scripting define không được bật khi build release.</summary>
        public List<string> forbiddenReleaseDefines = new()
            { "EZG_AUTOTEST", "ENABLE_CHEAT", "CHEAT", "DEBUG_MENU", "ENABLE_LOG_ALL" };

        /// <summary>Luật quét code: regex + thông điệp + severity.</summary>
        public List<CodeRule> codeRules = new()
        {
            new CodeRule
            {
                id = "no-datetime-now", pattern = "DateTime\\.(Now|UtcNow)\\b",
                message = "Dùng TimeManager thay vì DateTime.Now/UtcNow (tránh hack giờ máy, lệch múi giờ).",
                severity = Severity.Minor, excludePattern = "TimeManager|Editor/"
            },
            new CodeRule
            {
                id = "no-async-void", pattern = "\\basync\\s+void\\s+(?!On[A-Z])\\w+\\s*\\(",
                message = "Tránh async void (exception nuốt mất) — dùng UniTask/UniTaskVoid.",
                severity = Severity.Minor
            },
            new CodeRule
            {
                id = "no-find-in-update", pattern = "GameObject\\.Find\\s*\\(",
                message = "GameObject.Find chậm — cache reference trong Awake.",
                severity = Severity.Info
            },
            new CodeRule
            {
                id = "no-hardcoded-secret",
                pattern = "(?i)(api[_-]?key|secret|password|token)\\s*=\\s*\"[A-Za-z0-9_\\-]{16,}\"",
                message = "Nghi là secret hardcode trong code.", severity = Severity.Critical
            }
        };

        /// <summary>Folder CSV config (rỗng = tự tìm mọi thư mục tên CsvConfig).</summary>
        public List<string> csvFolders = new();

        /// <summary>Folder dữ liệu localize (chứa &lt;lang&gt;/&lt;tab&gt;.csv).</summary>
        public string localizationFolder = "";
    }

    [Serializable]
    public class CodeRule
    {
        public string id;
        public string pattern;
        public string excludePattern;
        public string message;
        public Severity severity = Severity.Minor;
        public bool enabled = true;
    }

    [Serializable]
    public class SmokeConfig
    {
        /// <summary>
        ///     Regex (không phân biệt hoa thường, khớp một phần — dùng ^…$ để khớp nguyên tên) của feature KHÔNG mở
        ///     trong smoke/UI audit/button sweep (không phải màn hình, cần data đặc biệt, bước tutorial…).
        /// </summary>
        public List<string> excludedFeatures = new()
        {
            "^none$", "^Tut\\d", "^Tutorial", "_Retired$", "^Toast", "^Loading", "^Splash", "^ForceUpdate$",
            "^OverviewCanvas$", "^CurrencyBar$"
        };

        /// <summary>Feature đang mở lúc game vừa boot xong mà là popup chặn (đóng trước khi test).</summary>
        public List<string> dismissAtStartFeatures = new()
        {
            "^OfflineEarning", "^Rating$", "^RewardPopup$", "^LevelUp$", "^DailyReward", "^DailyBonus",
            "Offer$", "Pack$"
        };

        public float screenOpenTimeoutSeconds = 10f;

        /// <summary>Thời gian giữ màn hình sau khi mở để animation/logic chạy (giây).</summary>
        public float screenSettleSeconds = 1.2f;

        /// <summary>Mở màn chậm hơn ngưỡng này ⇒ cảnh báo (ms).</summary>
        public float slowOpenMs = 1500f;

        public bool loadEveryBuildScene = true;
        public float sceneSettleSeconds = 3f;
        public bool closeScreenAfterOpen = true;
    }

    [Serializable]
    public class UiAuditConfig
    {
        /// <summary>Kích thước chạm tối thiểu (dp) — chuẩn Material 48dp, Apple 44pt.</summary>
        public float minTouchTargetDp = 44f;

        public bool checkTextOverflow = true;
        public bool checkMissingSprite = true;
        public bool checkOffScreen = true;
        public bool checkSafeArea = true;
        public bool checkRaycastBlocked = true;
        public bool checkLocalizationLeak = true;
        public bool checkDeadButtons = true;

        /// <summary>Regex nhận diện text là key localize bị lộ (vd "key_shop_title", "txt_buy").</summary>
        public string localizationKeyPattern = "^[a-z0-9]+(_[a-z0-9]+){1,}$";

        /// <summary>Tên object bỏ qua khi audit (regex).</summary>
        public List<string> ignoredObjectPatterns = new()
            { "psd", "cheat", "debug", "^ignore_", "stats", "StatusBar", "game_speed", "IngameDebugConsole" };
    }

    [Serializable]
    public class ButtonSweepConfig
    {
        /// <summary>Nút có tên/text khớp regex này sẽ KHÔNG bấm (mua thật, xoá data, mở link…).</summary>
        public List<string> blacklistPatterns = new()
        {
            "buy", "purchase", "iap", "restore", "pay", "price", "delete", "reset", "logout", "log_out",
            "quit", "exit", "link", "url", "facebook", "fanpage", "discord", "rate", "review", "privacy",
            "terms", "support", "mail", "cheat", "debug"
        };

        public int maxButtonsPerScreen = 40;
        public float waitAfterClickSeconds = 0.6f;

        /// <summary>Đóng màn mới mở ra sau mỗi lần bấm để trở về màn đang test.</summary>
        public bool closeSpawnedScreens = true;
    }

    [Serializable]
    public class MonkeyConfig
    {
        public float durationSeconds = 60f;
        public float actionsPerSecond = 6f;

        /// <summary>0 = random theo thời gian (log lại để tái hiện).</summary>
        public int seed;

        /// <summary>Tỉ lệ tap vào nút (còn lại tap/drag ngẫu nhiên lên màn hình).</summary>
        [UnityEngine.Range(0f, 1f)] public float buttonTapRatio = 0.7f;

        public bool allowBackKey = true;
        public bool respectBlacklist = true;
    }

    [Serializable]
    public class EconomyConfig
    {
        /// <summary>Lượng dùng khi test cộng/trừ tiền.</summary>
        public double testAmount = 1234;

        public bool testOverflow = true;
        public bool testSaveLoad = true;
        public bool testRewards = true;
        public bool testPurchaseOffline = true;
        public bool testConfigSanity = true;

        /// <summary>
        ///     Tiền tệ bỏ qua khi test phát thưởng (game định tuyến thưởng sang ví khác, vd tiền trong map).
        /// </summary>
        public List<string> rewardSkipCurrencies = new();

        /// <summary>Tiền tệ bỏ qua hoàn toàn khi test economy.</summary>
        public List<string> skipCurrencies = new();

        /// <summary>Regex tên field số không được âm trong config CSV (price, cost, reward…).</summary>
        public string nonNegativeFieldPattern = "(?i)(price|cost|amount|reward|value|quantity|qty|count|gold|gem|diamond|coin)";

        /// <summary>Regex tên field là id (phải duy nhất trong collection).</summary>
        public string idFieldPattern = "^(?i)(id|key|productid|product_id)$";
    }

    [Serializable]
    public class PerformanceConfig
    {
        public float warmupSeconds = 3f;
        public float sampleSeconds = 20f;
        public int targetFps = 60;
        public float maxP95FrameMs = 33.4f;
        public float maxP99FrameMs = 50f;
        public float hitchThresholdMs = 100f;
        public int maxHitches = 3;
        public float maxGcAllocPerFrameKb = 4f;
        public float maxTotalMemoryMb = 1200f;
        public int maxDrawCalls = 250;
        public int maxSetPassCalls = 100;
    }

    [Serializable]
    public class VisualRegressionConfig
    {
        /// <summary>Thư mục baseline, tương đối với project (commit vào git để cả team dùng chung).</summary>
        public string baselineFolder = "AutoTestBaselines";

        /// <summary>Sai khác mỗi kênh màu (0–255) dưới ngưỡng này coi như giống (khử răng cưa/nén).</summary>
        public int pixelTolerance = 16;

        /// <summary>% pixel khác vượt ngưỡng ⇒ lỗi.</summary>
        public float maxDiffPercent = 1.5f;

        /// <summary>Object khớp regex bị ẩn trước khi chụp (text đồng hồ, số tiền nhảy liên tục…).</summary>
        public List<string> maskObjectPatterns = new() { "time", "timer", "countdown", "clock", "fps" };
    }

    [Serializable]
    public class DeviceConfig
    {
        /// <summary>Đường dẫn adb (rỗng = tự tìm từ Android SDK của Unity / PATH).</summary>
        public string adbPath = "";

        /// <summary>Package name (rỗng = PlayerSettings.applicationIdentifier).</summary>
        public string packageName = "";

        /// <summary>Activity khởi động (rỗng = tự resolve bằng cmd package resolve-activity).</summary>
        public string launchActivity = "";

        /// <summary>APK có sẵn để cài (rỗng = build mới).</summary>
        public string apkPath = "";

        public string buildOutputFolder = "Builds/AutoTest";
        public bool buildBeforeRun = true;
        public bool uninstallBeforeInstall;
        public float runTimeoutSeconds = 900f;

        /// <summary>Suite chạy trên device.</summary>
        public List<string> suites = new() { "smoke", "ui-audit", "economy", "performance", "scenarios" };

        public bool collectLogcat = true;
        public bool measureColdStart = true;
    }
}
