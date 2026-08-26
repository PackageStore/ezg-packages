#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Ezg.Editor.Shared.EzgKit;
using Ezg.Editor.Shared.Marketing;
using Ezg.Editor.Shared.Social;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ezg.Editor.Shared.Readiness
{
    /// <summary>Nhóm của một mục kiểm — quyết định mục đó nằm trong card nào của tab Readiness.</summary>
    internal enum ReadinessGroup
    {
        Iap = 0,
        Firebase = 1,
        Sdk = 2,
        Store = 3,
        Social = 4,
    }

    /// <summary>
    ///     Một dòng trong bảng readiness: tên — giá trị đọc được — trạng thái — vì sao — làm gì tiếp —
    ///     nút đi tới chỗ sửa.
    ///     <para>
    ///         Quy ước: mục <b>Warn/Error PHẢI có <see cref="Fix" /></b> (câu hành động cụ thể: mở đâu,
    ///         sửa gì) và ít nhất một <see cref="Actions" /> hoặc <see cref="Links" /> — báo lỗi mà
    ///         không nói sửa ở đâu là đẩy PM đi hỏi dev, đúng thứ tab này sinh ra để tránh.
    ///         <see cref="Actions" /> chạy trong Editor (chọn asset, mở script, mở Project Settings, sửa
    ///         luôn); <see cref="Links" /> mở trang web (console của store/SDK).
    ///     </para>
    /// </summary>
    internal sealed class ReadinessItem
    {
        internal ReadinessGroup Group;
        internal string Label;
        internal string Value;
        internal EzgStatus Status;
        internal string Note;
        internal string Fix;
        internal (string Label, string Url)[] Links;
        internal (string Label, Action Run)[] Actions;

        internal ReadinessItem(ReadinessGroup group, string label, string value, EzgStatus status,
            string note = null, string fix = null, params (string Label, string Url)[] links)
        {
            Group = group;
            Label = label;
            Value = value;
            Status = status;
            Note = note;
            Fix = fix;
            Links = links;
        }

        /// <summary>Gắn nút hành động. Fluent để chuỗi ngay sau <c>new</c> mà không phải thêm overload ctor.</summary>
        internal ReadinessItem With(params (string Label, Action Run)[] actions)
        {
            Actions = actions;
            return this;
        }

        internal bool IsPending => Status is EzgStatus.Error or EzgStatus.Warn;
    }

    /// <summary>Kết quả tra App Store cho id trong <c>GameConstant.IOSAppId</c> (nút bấm, không tự chạy).</summary>
    internal sealed class AppStoreLookup
    {
        internal string QueriedId;
        internal bool Found;
        internal string TrackName;
        internal string BundleId;
        internal string Seller;
        internal string Error;
    }

    internal sealed class ReadinessReport
    {
        internal readonly List<ReadinessItem> Items = new();
        internal int Errors, Warns, Oks;

        /// <summary>SKU IAP phân biệt — đúng danh sách phải tạo tay trên Play Console / ASC.</summary>
        internal readonly List<string> Skus = new();

        internal void Add(ReadinessItem item)
        {
            Items.Add(item);
            switch (item.Status)
            {
                case EzgStatus.Error: Errors++; break;
                case EzgStatus.Warn: Warns++; break;
                case EzgStatus.Ok: Oks++; break;
            }
        }

        /// <summary>
        ///     Báo cáo dạng text để dán vào Discord/Slack cho PM. Emoji vì kênh chat không có icon của
        ///     Editor; mục "không áp dụng" bỏ qua — PM cần biết còn gì phải làm, không cần biết tool đã
        ///     soi những gì.
        /// </summary>
        internal string ToText(string productName, string androidId, string iosId, string version)
        {
            var sb = new StringBuilder();
            sb.Append("**Release Readiness — ").Append(productName).Append("** (")
              .Append(version).Append(")\n")
              .Append("Android `").Append(androidId).Append("` · iOS `").Append(iosId).Append("`\n")
              .Append(Errors).Append(" lỗi · ").Append(Warns).Append(" cảnh báo · ")
              .Append(Oks).Append(" sẵn sàng\n");

            foreach (ReadinessGroup group in Enum.GetValues(typeof(ReadinessGroup)))
            {
                var header = false;
                foreach (var item in Items)
                {
                    if (item.Group != group || item.Status == EzgStatus.None) continue;
                    if (!header)
                    {
                        sb.Append('\n').Append("__").Append(GroupTitle(group)).Append("__\n");
                        header = true;
                    }

                    sb.Append(Emoji(item.Status)).Append(' ').Append(item.Label);
                    if (!string.IsNullOrEmpty(item.Value)) sb.Append(": `").Append(item.Value).Append('`');
                    if (item.IsPending && !string.IsNullOrEmpty(item.Fix))
                        sb.Append(" → ").Append(item.Fix);
                    sb.Append('\n');
                }
            }

            if (Skus.Count > 0)
                sb.Append("\n__SKU phải có trên store (").Append(Skus.Count).Append(")__\n")
                  .Append(string.Join("\n", Skus)).Append('\n');

            return sb.ToString();
        }

        internal static string GroupTitle(ReadinessGroup group) =>
            group switch
            {
                ReadinessGroup.Iap => "IAP",
                ReadinessGroup.Firebase => "Firebase",
                ReadinessGroup.Sdk => "SDK (Ads / Attribution)",
                ReadinessGroup.Store => "Store / Build",
                ReadinessGroup.Social => "Social / Support",
                _ => group.ToString(),
            };

        private static string Emoji(EzgStatus status) =>
            status switch
            {
                EzgStatus.Ok => "✅",
                EzgStatus.Warn => "⚠️",
                EzgStatus.Error => "❌",
                _ => "▫️",
            };
    }

    /// <summary>
    ///     Đọc trạng thái "triển khai được chưa" của IAP / Firebase / SDK / Store từ chính project —
    ///     KHÔNG gọi API, KHÔNG ghi gì. Mọi thứ đọc được từ file config, asset trong <c>Resources</c>,
    ///     PlayerSettings và script build; thứ chỉ có trên console (SKU đã tạo chưa, Remote Config key,
    ///     Firestore rules) tool KHÔNG biết được và nói thẳng là việc tay.
    ///     <para>
    ///         Package dùng chung cho mọi dự án nên đây là bộ kiểm THEO ĐƯỜNG DẪN + tên field, không
    ///         tham chiếu assembly game: catalog shop đọc qua <see cref="SerializedObject" />, hằng số
    ///         đọc bằng regex trên <c>GameConstant.cs</c>. Dự án không có thứ đó thì mục tương ứng là
    ///         <see cref="EzgStatus.None" /> (không áp dụng), không phải lỗi.
    ///     </para>
    ///     <para>
    ///         Quy tắc mức: <b>Error</b> = build ra là hỏng/sai dự án (config trỏ project khác, SKU
    ///         không theo package name, debug ads bật, keystore thiếu); <b>Warn</b> = chạy được nhưng
    ///         còn việc trước khi lên store (consumable một lần, chưa link UGS, link store rỗng).
    ///     </para>
    ///     <para>
    ///         Nút hành động dựng ở đây (closure) chứ không ở page: mục nào biết mình sai ở file nào thì
    ///         mục đó biết mở file nào — page chỉ vẽ. Xem <see cref="ReadinessActions" />.
    ///     </para>
    /// </summary>
    internal static class ReadinessChecks
    {
        #region Constants

        private const int PURCHASE_TYPE_IAP = 3;

        private const string SHOP_CATALOG_TYPE = "ShopPackCatalog";
        private const string FIREBASE_CONFIG_NAME = "FirebaseConfig";
        private const string ADS_CONFIG_NAME = "AdsConfig";
        private const string APPLOVIN_SETTINGS_PATH = "Assets/MaxSdk/Resources/AppLovinSettings.asset";
        private const string FACEBOOK_SETTINGS_PATH = "Assets/FacebookSDK/SDK/Resources/FacebookSettings.asset";
        private const string BILLING_MODE_PATH = "Assets/Resources/BillingMode.json";
        private const string ANDROID_FIREBASE_JSON = "Assets/google-services.json";
        private const string IOS_FIREBASE_PLIST = "Assets/GoogleService-Info.plist";
        private const string FIREBASE_XML = "Assets/Plugins/Android/FirebaseApp.androidlib/res/values/google-services.xml";
        private const string IOS_POST_BUILD = "Tools/ios/post-build.sh";
        private const string PACKAGES_MANIFEST = "Packages/manifest.json";

        private const string SETTINGS_PLAYER = "Project/Player";
        private const string SETTINGS_SERVICES = "Project/Services";
        private const string SETTINGS_IAP = "Project/Services/In-App Purchasing";

        private const string URL_PLAY_CONSOLE = "https://play.google.com/console/";
        private const string URL_ASC = "https://appstoreconnect.apple.com/apps";
        private const string URL_UGS = "https://cloud.unity.com/";
        private const string URL_APPSFLYER = "https://hq1.appsflyer.com/";
        private const string URL_MAX = "https://dash.applovin.com/o/mediation/ad_units";
        private const string URL_FIREBASE = "https://console.firebase.google.com/";

        #endregion

        #region Entry

        internal static ReadinessReport Collect(AppStoreLookup lookup)
        {
            var report = new ReadinessReport();
            var androidId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) ?? "";
            var iosId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS) ?? "";
            var marketing = LoadMarketing();

            Safe(report, () => CheckIap(report, androidId));
            Safe(report, () => CheckFirebase(report, androidId, iosId));
            Safe(report, () => CheckSdk(report, marketing, iosId, lookup));
            Safe(report, () => CheckStore(report, marketing));
            // Link cộng đồng/hỗ trợ/rating + link hardcode + webhook Discord — tab Social sở hữu bộ kiểm.
            Safe(report, () => SocialChecks.Collect(report, SocialSource.Load(), null));
            return report;
        }

        /// <summary>
        ///     Một nhóm kiểm ném exception (asset hỏng, format lạ) không được làm mất các nhóm khác —
        ///     biến thành một dòng Error để người dùng thấy nhóm đó chưa kiểm được.
        /// </summary>
        private static void Safe(ReadinessReport report, Action check)
        {
            try
            {
                check();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                report.Add(new ReadinessItem(ReadinessGroup.Store, "Kiểm tra bị lỗi", exception.Message,
                    EzgStatus.Error, fix: "Mở Console xem stack trace — một nhóm kiểm không đọc được dữ liệu.")
                    .With(("Mở Console", () => EditorApplication.ExecuteMenuItem("Window/General/Console"))));
            }
        }

        #endregion

        #region IAP

        private static void CheckIap(ReadinessReport report, string androidId)
        {
            const ReadinessGroup g = ReadinessGroup.Iap;

            // Package
            var manifest = ReadProjectFile(PACKAGES_MANIFEST);
            var iapVersion = Match(manifest, "\"com\\.unity\\.purchasing\"\\s*:\\s*\"([^\"]+)\"");
            report.Add(iapVersion == null
                ? new ReadinessItem(g, "Unity Purchasing", null, EzgStatus.Error,
                        fix: "Cài com.unity.purchasing qua Package Manager.")
                    .With(ReadinessActions.PackageManager("Mở Package Manager", "com.unity.purchasing"))
                : new ReadinessItem(g, "Unity Purchasing", iapVersion, EzgStatus.Ok));

            // Billing mode
            var billing = Match(ReadProjectFile(BILLING_MODE_PATH), "\"androidStore\"\\s*:\\s*\"([^\"]+)\"");
            report.Add(new ReadinessItem(g, "Android store", billing,
                    billing == null ? EzgStatus.Warn : EzgStatus.Ok,
                    fix: billing == null ? "Project Settings > Services > In-App Purchasing > Target Android store = Google Play." : null)
                .With(ReadinessActions.ProjectSettings("Mở Services > IAP", SETTINGS_IAP)));

            // Receipt validation keys
            var googleTangle = FindScript("GooglePlayTangle");
            if (googleTangle == null)
                report.Add(new ReadinessItem(g, "Google Play licence key", null, EzgStatus.Warn,
                        "Chưa có GooglePlayTangle.cs — receipt validation Android bỏ qua.",
                        "Project Settings > Services > In-App Purchasing > Receipt Obfuscator: dán Base64 key lấy từ Play Console > Monetization setup > Licensing, rồi bấm Obfuscate.",
                        ("Play Console", URL_PLAY_CONSOLE))
                    .With(ReadinessActions.ProjectSettings("Mở Receipt Obfuscator", SETTINGS_IAP)));
            else
            {
                var populated = Regex.IsMatch(File.ReadAllText(googleTangle), "IsPopulated\\s*=\\s*true");
                report.Add(new ReadinessItem(g, "Google Play licence key", populated ? "đã dán" : "chưa dán",
                        populated ? EzgStatus.Ok : EzgStatus.Error,
                        populated ? "Có key nghĩa là app đã tồn tại trên Play Console." : "Receipt Android không validate được — mua fail hoặc nhận quà không kiểm chứng.",
                        populated ? null : "Dán Play licence key (Play Console > Monetization setup > Licensing) vào Receipt Obfuscator rồi bấm Obfuscate.",
                        ("Play Console", URL_PLAY_CONSOLE))
                    .With(ReadinessActions.ProjectSettings("Mở Receipt Obfuscator", SETTINGS_IAP),
                        ReadinessActions.OpenScript("Mở GooglePlayTangle.cs", googleTangle, "IsPopulated")));
            }

            var appleTangle = FindScript("AppleTangle");
            report.Add(new ReadinessItem(g, "Apple root cert (tangle)",
                    appleTangle == null ? null : "đã sinh",
                    appleTangle == null ? EzgStatus.Warn : EzgStatus.Ok,
                    fix: appleTangle == null ? "Project Settings > Services > In-App Purchasing > Receipt Obfuscator > Obfuscate (không cần key cho Apple)." : null)
                .With(ReadinessActions.ProjectSettings("Mở Receipt Obfuscator", SETTINGS_IAP)));

            // UGS link
            var ugsProject = CloudProjectSettings.projectId;
            var ugsMissing = string.IsNullOrEmpty(ugsProject);
            report.Add(new ReadinessItem(g, "Unity Gaming Services", ugsProject,
                    ugsMissing ? EzgStatus.Warn : EzgStatus.Ok,
                    ugsMissing ? "UnityServices.InitializeAsync sẽ fail (được nuốt) — IAP v5 vẫn chạy nhưng phải verify trên máy thật." : null,
                    ugsMissing ? "Project Settings > Services > đăng nhập Unity ID > Link project tới organization." : null,
                    ("Unity Cloud", URL_UGS))
                .With(ReadinessActions.ProjectSettings("Mở Services", SETTINGS_SERVICES)));

            CheckShopCatalog(report, androidId);
        }

        /// <summary>
        ///     Bảng SKU thật sự sẽ đăng ký với store: đọc <c>ShopPackCatalog</c> (asset trong Resources)
        ///     qua SerializedObject — từng bảng có <c>isEnabled</c> / <c>isNonConsumable</c>, từng gói có
        ///     <c>purchaseList[].purchaseType/purchaseCount</c> + product id.
        /// </summary>
        private static void CheckShopCatalog(ReadinessReport report, string androidId)
        {
            const ReadinessGroup g = ReadinessGroup.Iap;

            var guids = AssetDatabase.FindAssets("t:" + SHOP_CATALOG_TYPE);
            if (guids.Length == 0)
            {
                report.Add(new ReadinessItem(g, "Shop pack catalog", null, EzgStatus.Warn,
                    "Không có ShopPackCatalog — SKU đăng ký theo đường fallback trong ShopService.",
                    "Chuột phải trong Resources của Shop > Create > ScriptableObjects > Shop Pack Catalog, kéo các bảng pack vào."));
                return;
            }

            var catalogPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            var catalog = AssetDatabase.LoadAssetAtPath<ScriptableObject>(catalogPath);
            if (catalog == null) return;
            var selectCatalog = ReadinessActions.SelectObject("Chọn catalog", catalog);

            var so = new SerializedObject(catalog);
            var tables = so.FindProperty("tables");
            if (tables == null || !tables.isArray)
            {
                report.Add(new ReadinessItem(g, "Shop pack catalog", catalog.name, EzgStatus.Warn,
                        "Asset không có mảng `tables` — format catalog đã đổi, tool chưa đọc được.",
                        "Cập nhật ReadinessChecks theo format catalog mới.")
                    .With(selectCatalog));
                return;
            }

            var nonConsumableTables = 0;
            var skuSet = new HashSet<string>();
            var prefix = string.IsNullOrEmpty(androidId) ? null : androidId + ".";

            for (var t = 0; t < tables.arraySize; t++)
            {
                var entry = tables.GetArrayElementAtIndex(t);
                var label = entry.FindPropertyRelative("label")?.stringValue ?? $"bảng {t}";
                var enabled = entry.FindPropertyRelative("isEnabled")?.boolValue ?? true;
                var nonConsumable = entry.FindPropertyRelative("isNonConsumable")?.boolValue ?? false;
                var table = entry.FindPropertyRelative("table")?.objectReferenceValue as ScriptableObject;
                if (nonConsumable) nonConsumableTables++;

                if (table == null)
                {
                    report.Add(new ReadinessItem(g, label, null, enabled ? EzgStatus.Error : EzgStatus.None,
                            "Dòng catalog không trỏ tới bảng nào.", "Chọn catalog, kéo asset bảng pack vào ô `table` của dòng này.")
                        .With(selectCatalog));
                    continue;
                }

                var groups = new SerializedObject(table).FindProperty("dataGroups");
                if (groups == null || !groups.isArray) continue;

                // Nút chung cho mọi gói của bảng này: chọn bảng (asset đã import), mở CSV nguồn (nơi sửa thật).
                var csvPath = FindCsv(table.name);
                var tableActions = new List<(string, Action)>
                {
                    selectCatalog,
                    ReadinessActions.SelectObject($"Chọn bảng {table.name}", table),
                };
                if (csvPath != null) tableActions.Add(ReadinessActions.SelectAsset($"Mở CSV {table.name}", csvPath));
                var actions = tableActions.ToArray();

                for (var i = 0; i < groups.arraySize; i++)
                {
                    var pack = groups.GetArrayElementAtIndex(i);
                    var purchases = pack.FindPropertyRelative("purchaseList");
                    var iapCount = 0;
                    var isIap = false;
                    if (purchases != null && purchases.isArray)
                        for (var p = 0; p < purchases.arraySize; p++)
                        {
                            var purchase = purchases.GetArrayElementAtIndex(p);
                            var type = purchase.FindPropertyRelative("purchaseType");
                            if (type == null || type.intValue != PURCHASE_TYPE_IAP) continue;
                            isIap = true;
                            iapCount += purchase.FindPropertyRelative("purchaseCount")?.intValue ?? 0;
                        }

                    if (!isIap) continue;

                    var packName = pack.FindPropertyRelative("packName")?.stringValue ?? $"#{i}";
                    var google = pack.FindPropertyRelative("googleProductId")?.stringValue ?? "";
                    var apple = pack.FindPropertyRelative("appleProductId")?.stringValue ?? "";
                    var cost = pack.FindPropertyRelative("iapCost")?.floatValue ?? 0f;
                    var kind = nonConsumable ? "Non-Consumable" : "Consumable";
                    var rowLabel = $"{label} / {packName}";
                    var value = $"{google}  ·  ${cost:0.00}  ·  {kind}";

                    if (!enabled)
                    {
                        report.Add(new ReadinessItem(g, rowLabel, value, EzgStatus.None,
                            "Bảng đang tắt trong catalog — không đăng ký với store."));
                        continue;
                    }

                    if (string.IsNullOrEmpty(google) || string.IsNullOrEmpty(apple))
                    {
                        report.Add(new ReadinessItem(g, rowLabel, value, EzgStatus.Error,
                                "Thiếu product id.",
                                $"Mở CSV {table.name}, điền google_product_id / apple_product_id cho `{packName}` rồi import lại CSV.")
                            .With(actions));
                        continue;
                    }

                    skuSet.Add(google);
                    if (apple != google) skuSet.Add(apple);

                    if (prefix != null && !google.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        report.Add(new ReadinessItem(g, rowLabel, value, EzgStatus.Error,
                                $"Product id không theo package name `{androidId}`.",
                                $"Mở CSV {table.name}, đổi product id thành `{prefix}{packName}` (hoặc sửa package name ở Player Settings) rồi import lại.")
                            .With(actions));
                        continue;
                    }

                    if (apple != google)
                    {
                        report.Add(new ReadinessItem(g, rowLabel, value, EzgStatus.Warn,
                                $"Apple id khác Google id (`{apple}`).",
                                "Tạo đúng cả hai id trên hai store, hoặc đặt apple_product_id = google_product_id trong CSV.",
                                ("App Store Connect", URL_ASC), ("Play Console", URL_PLAY_CONSOLE))
                            .With(actions));
                        continue;
                    }

                    if (!nonConsumable && iapCount == 1)
                    {
                        report.Add(new ReadinessItem(g, rowLabel, value, EzgStatus.Warn,
                                "purchase_count = 1 mà khai Consumable: hết hàng vĩnh viễn sau lần mua đầu, "
                                + "StoreKit không restore, cài lại máy là mất quyền.",
                                $"Nếu là entitlement (remove ads, boost vĩnh viễn): đổi SKU sang Non-Consumable trên ASC/Play TRƯỚC, rồi chọn catalog > dòng `{label}` > tick isNonConsumable. "
                                + $"Nếu là hàng tiêu hao: mở CSV {table.name}, tăng purchase_count.",
                                ("App Store Connect", URL_ASC), ("Play Console", URL_PLAY_CONSOLE))
                            .With(actions));
                        continue;
                    }

                    report.Add(new ReadinessItem(g, rowLabel, value, EzgStatus.Ok).With(actions));
                }
            }

            report.Skus.AddRange(skuSet);
            report.Skus.Sort(StringComparer.Ordinal);
            report.Add(new ReadinessItem(g, "SKU phải có trên store", skuSet.Count.ToString(),
                    skuSet.Count == 0 ? EzgStatus.Warn : EzgStatus.Ok,
                    "Tool không thấy được console: tạo tay từng SKU trên Play Console + ASC, ĐÚNG loại "
                    + "Consumable/Non-Consumable như catalog. Danh sách nằm trong báo cáo (nút Copy).",
                    skuSet.Count == 0 ? "Chọn catalog, bật (isEnabled) ít nhất một bảng có gói IAP." : null,
                    ("App Store Connect", URL_ASC), ("Play Console", URL_PLAY_CONSOLE))
                .With(selectCatalog));

            CheckRestoreWired(report, nonConsumableTables);
        }

        /// <summary>
        ///     Apple guideline 3.1.1: có Non-Consumable là phải có nút Restore Purchases. Đếm lời gọi
        ///     <c>RestorePurchases(</c> trong script của dự án, bỏ dòng khai báo, dòng comment và lời
        ///     forward sang <c>InAppManager</c> của package (lớp bọc, không phải nút UI).
        /// </summary>
        private static void CheckRestoreWired(ReadinessReport report, int nonConsumableTables)
        {
            var callers = 0;
            string purchaseManager = null;
            var declaration = new Regex("\\b(void|Task|UniTask)\\s+RestorePurchases\\s*\\(");
            // Quét trên SourceIndex (text .cs cache trong RAM) — không đọc đĩa mỗi Reload.
            foreach (var source in SourceIndex.Files)
            {
                var file = source.Absolute;
                var text = source.Text;
                if (!text.Contains("RestorePurchases(")) continue;
                foreach (var line in text.Split('\n'))
                {
                    var trimmed = line.TrimStart();
                    if (!trimmed.Contains("RestorePurchases(")) continue;
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*")) continue;
                    if (declaration.IsMatch(trimmed))
                    {
                        purchaseManager ??= file;
                        continue;
                    }

                    if (trimmed.Contains("InAppManager")) continue;
                    callers++;
                }
            }

            EzgStatus status;
            string note, fix = null;
            if (callers > 0)
            {
                status = EzgStatus.Ok;
                note = $"{callers} chỗ gọi RestorePurchases.";
            }
            else if (nonConsumableTables > 0)
            {
                status = EzgStatus.Error;
                note = "Có SKU Non-Consumable mà không có nút Restore — Apple reject (3.1.1).";
                fix = "Thêm nút \"Restore Purchases\" vào màn Settings, onClick gọi PurchaseManager.RestorePurchases().";
            }
            else
            {
                status = EzgStatus.Warn;
                note = "Chưa có nút Restore. Hiện chưa bắt buộc (toàn Consumable) — sẽ bắt buộc ngay khi đổi remove-ads sang Non-Consumable.";
                fix = "Thêm nút \"Restore Purchases\" vào màn Settings gọi PurchaseManager.RestorePurchases() trước khi đổi loại SKU.";
            }

            var item = new ReadinessItem(ReadinessGroup.Iap, "Restore Purchases (UI)",
                callers > 0 ? $"{callers} caller" : "chưa nối", status, note, fix);
            if (purchaseManager != null)
                item.With(ReadinessActions.OpenScript("Mở RestorePurchases()", purchaseManager, "RestorePurchases\\s*\\("));
            report.Add(item);
        }

        #endregion

        #region Firebase

        private static void CheckFirebase(ReadinessReport report, string androidId, string iosId)
        {
            const ReadinessGroup g = ReadinessGroup.Firebase;

            var json = ReadProjectFile(ANDROID_FIREBASE_JSON);
            var plist = ReadProjectFile(IOS_FIREBASE_PLIST);
            var xml = ReadProjectFile(FIREBASE_XML);

            var jsonProject = Match(json, "\"project_id\"\\s*:\\s*\"([^\"]+)\"");
            var jsonPackage = Match(json, "\"package_name\"\\s*:\\s*\"([^\"]+)\"");
            var jsonBucket = Match(json, "\"storage_bucket\"\\s*:\\s*\"([^\"]+)\"");
            var plistProject = Match(plist, "<key>PROJECT_ID</key>\\s*<string>([^<]+)</string>");
            var plistBundle = Match(plist, "<key>BUNDLE_ID</key>\\s*<string>([^<]+)</string>");
            var xmlProject = Match(xml, "name=\"project_id\"[^>]*>([^<]+)<");

            var firebaseTab = ReadinessActions.KitTab("Mở tab Firebase", EzgKitWindow.Tab.Firebase);
            var selectJson = ReadinessActions.SelectAsset("Chọn google-services.json", ANDROID_FIREBASE_JSON);
            var selectPlist = ReadinessActions.SelectAsset("Chọn GoogleService-Info.plist", IOS_FIREBASE_PLIST);

            if (json == null)
                report.Add(new ReadinessItem(g, "google-services.json", null, EzgStatus.Error,
                        "Android build không có Firebase (Analytics/RemoteConfig/Crashlytics đều tắt).",
                        "Tab Firebase > chọn file service account > Tạo app + tải config (ghi google-services.json vào Assets/).")
                    .With(firebaseTab));
            else if (!string.IsNullOrEmpty(androidId) && jsonPackage != androidId)
                report.Add(new ReadinessItem(g, "google-services.json", $"{jsonProject} · {jsonPackage}",
                        EzgStatus.Error, $"package_name khác PlayerSettings `{androidId}` — số liệu chảy sang app khác.",
                        "Tab Firebase > Tạo app + tải config để lấy json đúng package name (hoặc sửa package name ở Player Settings nếu json mới đúng).")
                    .With(firebaseTab, selectJson, ReadinessActions.ProjectSettings("Mở Player Settings", SETTINGS_PLAYER)));
            else
                report.Add(new ReadinessItem(g, "google-services.json", $"{jsonProject} · {jsonPackage}", EzgStatus.Ok)
                    .With(selectJson));

            if (plist == null)
                report.Add(new ReadinessItem(g, "GoogleService-Info.plist", null, EzgStatus.Error,
                        "iOS build không có Firebase.", "Tab Firebase > Tạo app + tải config (ghi GoogleService-Info.plist vào Assets/).")
                    .With(firebaseTab));
            else if (!string.IsNullOrEmpty(iosId) && plistBundle != iosId)
                report.Add(new ReadinessItem(g, "GoogleService-Info.plist", $"{plistProject} · {plistBundle}",
                        EzgStatus.Error, $"BUNDLE_ID khác PlayerSettings `{iosId}`.",
                        "Tab Firebase > Tạo app + tải config để lấy plist đúng bundle id.")
                    .With(firebaseTab, selectPlist, ReadinessActions.ProjectSettings("Mở Player Settings", SETTINGS_PLAYER)));
            else
                report.Add(new ReadinessItem(g, "GoogleService-Info.plist", $"{plistProject} · {plistBundle}", EzgStatus.Ok)
                    .With(selectPlist));

            if (json != null)
            {
                var reimport = ReadinessActions.Reimport("Reimport google-services.json", ANDROID_FIREBASE_JSON);
                if (xml == null)
                    report.Add(new ReadinessItem(g, "google-services.xml (Android build)", null, EzgStatus.Warn,
                            "Generator của Firebase chưa sinh xml từ json — build Android chưa có config.",
                            "Bấm Reimport google-services.json (generator của Firebase.Editor ghi xml).")
                        .With(reimport, selectJson));
                else if (xmlProject != jsonProject)
                    report.Add(new ReadinessItem(g, "google-services.xml (Android build)", xmlProject, EzgStatus.Error,
                            $"xml trỏ project `{xmlProject}` khác json `{jsonProject}` — build Android bắn số liệu sang project đó.",
                            "Bấm Reimport google-services.json để generator ghi lại xml theo json hiện tại.")
                        .With(reimport, selectJson));
                else
                    report.Add(new ReadinessItem(g, "google-services.xml (Android build)", xmlProject, EzgStatus.Ok));

                if (plistProject != null && plistProject != jsonProject)
                    report.Add(new ReadinessItem(g, "Android/iOS cùng project", $"{jsonProject} ≠ {plistProject}",
                            EzgStatus.Error, "Hai nền tảng đang ở hai project Firebase khác nhau.",
                            "Tab Firebase > Tạo app + tải config cho cả hai nền tảng từ cùng một project.")
                        .With(firebaseTab, selectJson, selectPlist));
            }

            CheckFirebaseConfigAsset(report, jsonBucket);
            CheckIosCrashlyticsPhase(report);
        }

        /// <summary>
        ///     <c>com.ezg.firebase</c> đọc <c>Resources/FirebaseConfig</c>; thiếu asset là dùng default
        ///     trong package — bucket Storage của một game khác. Save-sync bật lên là đọc/ghi nhầm chỗ.
        /// </summary>
        private static void CheckFirebaseConfigAsset(ReadinessReport report, string jsonBucket)
        {
            const ReadinessGroup g = ReadinessGroup.Firebase;
            var expected = string.IsNullOrEmpty(jsonBucket) ? null : "gs://" + jsonBucket;
            var asset = Resources.Load<ScriptableObject>(FIREBASE_CONFIG_NAME);
            if (asset == null)
            {
                report.Add(new ReadinessItem(g, "Resources/FirebaseConfig.asset", null, EzgStatus.Error,
                        "Thiếu asset → package dùng bucket Storage mặc định của game khác cho save-sync.",
                        expected != null
                            ? $"Bấm \"Tạo FirebaseConfig.asset\" — tool tạo asset trong Resources với storageBucketUrl = {expected}."
                            : "Tạo asset FirebaseConfig trong một thư mục Resources (Create > Ezg > Firebase > Firebase Config), điền storageBucketUrl theo storage_bucket của google-services.json.")
                    .With(ReadinessActions.CreateFirebaseConfig("Tạo FirebaseConfig.asset", expected)));
                return;
            }

            var bucket = new SerializedObject(asset).FindProperty("storageBucketUrl")?.stringValue ?? "";
            var select = ReadinessActions.SelectObject("Chọn FirebaseConfig", asset);
            if (expected != null && !string.Equals(bucket.TrimEnd('/'), expected, StringComparison.Ordinal))
                report.Add(new ReadinessItem(g, "Storage bucket (save-sync)", bucket, EzgStatus.Error,
                        $"Khác bucket của project Firebase đang dùng (`{expected}`).",
                        $"Bấm \"Sửa bucket\" — ghi storageBucketUrl = {expected} vào {AssetDatabase.GetAssetPath(asset)}.")
                    .With(ReadinessActions.SetString("Sửa bucket", asset, "storageBucketUrl", expected), select));
            else if (string.IsNullOrEmpty(bucket))
                report.Add(new ReadinessItem(g, "Storage bucket (save-sync)", null, EzgStatus.Warn,
                        "storageBucketUrl trống — save-sync không có chỗ ghi.",
                        "Chọn FirebaseConfig, điền storageBucketUrl = gs://<storage_bucket trong google-services.json>.")
                    .With(select));
            else
                report.Add(new ReadinessItem(g, "Storage bucket (save-sync)", bucket, EzgStatus.Ok).With(select));
        }

        /// <summary>
        ///     Script build iOS của dự án từng vô hiệu hoá phase upload-symbols của Crashlytics vô điều
        ///     kiện (viết lúc chưa có plist). Bắt đúng ca: có chuỗi neutralize mà không có guard
        ///     kiểm plist → build store không có dSYM.
        /// </summary>
        private static void CheckIosCrashlyticsPhase(ReadinessReport report)
        {
            var script = ReadProjectFile(IOS_POST_BUILD);
            if (script == null) return; // dự án không có script này — không áp dụng

            var absolute = Path.Combine(ProjectRoot(), IOS_POST_BUILD);
            var neutralizes = script.Contains("Crashlytics skipped");
            var guarded = Regex.IsMatch(script, "if\\s+\\[\\s+-f\\s+\"?\\$X/GoogleService-Info\\.plist");
            if (neutralizes && !guarded)
                report.Add(new ReadinessItem(ReadinessGroup.Firebase, "Crashlytics iOS (dSYM)", "bị vô hiệu hoá",
                        EzgStatus.Error, $"{IOS_POST_BUILD} giết phase upload-symbols vô điều kiện — crash iOS ra stack trần.",
                        "Mở post-build.sh, bọc bước neutralize Crashlytics trong `if [ -f \"$X/GoogleService-Info.plist\" ]; then … else … fi` (hoặc bỏ hẳn bước đó).")
                    .With(ReadinessActions.Reveal("Mở post-build.sh", absolute)));
            else
                report.Add(new ReadinessItem(ReadinessGroup.Firebase, "Crashlytics iOS (dSYM)",
                        neutralizes ? "giữ phase khi có plist" : "giữ phase", EzgStatus.Ok)
                    .With(ReadinessActions.Reveal("Mở post-build.sh", absolute)));
        }

        #endregion

        #region SDK

        private static void CheckSdk(ReadinessReport report, MarketingConfig marketing, string iosId,
            AppStoreLookup lookup)
        {
            const ReadinessGroup g = ReadinessGroup.Sdk;
            var marketingTab = ReadinessActions.KitTab("Mở tab Marketing", EzgKitWindow.Tab.Marketing);

            // MAX / AdsConfig
            var ads = Resources.Load<ScriptableObject>(ADS_CONFIG_NAME);
            if (ads == null)
                report.Add(new ReadinessItem(g, "AdsConfig", null, EzgStatus.Warn,
                        "Không có Resources/AdsConfig — module ads không có key nào để init.",
                        "Tạo AdsConfig (Create > Ezg > Ads > AdsConfig) trong Resources rồi chạy tab Marketing để ghi key.")
                    .With(marketingTab));
            else
            {
                var selectAds = ReadinessActions.SelectObject("Chọn AdsConfig", ads);
                var so = new SerializedObject(ads);
                var debugAds = so.FindProperty("debugAds")?.boolValue ?? false;
                var debugItem = new ReadinessItem(g, "Debug ads", debugAds ? "BẬT" : "tắt",
                    debugAds ? EzgStatus.Error : EzgStatus.Ok,
                    debugAds ? "Ad auto-thành-công, không gọi mediation, không tracking — build store là mất sạch doanh thu ads." : null,
                    debugAds ? "Bấm \"Tắt debugAds\" (ghi debugAds = false vào AdsConfig)." : null);
                report.Add(debugAds
                    ? debugItem.With(ReadinessActions.SetBool("Tắt debugAds", ads, "debugAds", false), selectAds)
                    : debugItem.With(selectAds));

                report.Add(KeyItem(g, "MAX SDK key", so.FindProperty("maxAndroidSdkKey")?.stringValue,
                    so.FindProperty("maxIosSdkKey")?.stringValue,
                    "Lấy SDK key ở MAX dashboard > Account > Keys, điền sheet marketing rồi tab Marketing > ghi.", URL_MAX,
                    selectAds, marketingTab));
                report.Add(KeyItem(g, "MAX Interstitial id", so.FindProperty("maxAndroidInterstitialId")?.stringValue,
                    so.FindProperty("maxIosInterstitialId")?.stringValue,
                    "Tạo ad unit Interstitial trên MAX dashboard cho nền tảng còn trống, điền sheet rồi tab Marketing > ghi.", URL_MAX,
                    selectAds, marketingTab));
                report.Add(KeyItem(g, "MAX Rewarded id", so.FindProperty("maxAndroidRewardedId")?.stringValue,
                    so.FindProperty("maxIosRewardedId")?.stringValue,
                    "Tạo ad unit Rewarded trên MAX dashboard cho nền tảng còn trống, điền sheet rồi tab Marketing > ghi.", URL_MAX,
                    selectAds, marketingTab));
            }

            // AdMob app id (adapter Google trong MAX)
            var applovin = ReadProjectFile(APPLOVIN_SETTINGS_PATH);
            if (applovin != null)
            {
                // [ \t]* chứ không \s*: giá trị rỗng thì \s* nuốt xuống dòng và \S+ bắt nhầm key kế tiếp.
                var admobAndroid = Match(applovin, "adMobAndroidAppId:[ \\t]*(\\S*)");
                var admobIos = Match(applovin, "adMobIosAppId:[ \\t]*(\\S*)");
                var ok = IsAdmobAppId(admobAndroid) && IsAdmobAppId(admobIos);
                report.Add(new ReadinessItem(g, "AdMob app id (MAX adapter)",
                        $"{Dash(admobAndroid)} · {Dash(admobIos)}", ok ? EzgStatus.Ok : EzgStatus.Warn,
                        ok ? null : "Thiếu app id AdMob → adapter Google không fill, không log gì.",
                        ok ? null : "Điền admob.android/ios.appId (ca-app-pub-…~…) trong sheet marketing rồi tab Marketing > ghi AppLovinSettings; hoặc chọn AppLovinSettings sửa tay.")
                    .With(ReadinessActions.SelectAsset("Chọn AppLovinSettings", APPLOVIN_SETTINGS_PATH), marketingTab));
            }

            // AppsFlyer
            var gameConstant = FindScript("GameConstant");
            var constants = gameConstant == null ? null : File.ReadAllText(gameConstant);
            var devKey = Match(constants, "public const string AppsFlyerId = \"([^\"]*)\"");
            var iosAppId = Match(constants, "public const string IOSAppId = \"([^\"]*)\"");
            if (constants == null)
                report.Add(new ReadinessItem(g, "AppsFlyer", null, EzgStatus.None, "Không có GameConstant.cs."));
            else
            {
                report.Add(new ReadinessItem(g, "AppsFlyer dev key", devKey,
                        string.IsNullOrEmpty(devKey) ? EzgStatus.Error : EzgStatus.Ok,
                        string.IsNullOrEmpty(devKey) ? "SDK không init — không có attribution." : null,
                        string.IsNullOrEmpty(devKey) ? "Lấy dev key ở AppsFlyer > App Settings, điền appsflyerDevKey trong sheet rồi tab Marketing > ghi GameConstant." : null,
                        ("AppsFlyer", URL_APPSFLYER))
                    .With(ReadinessActions.OpenScript("Mở GameConstant.cs", gameConstant, "AppsFlyerId"), marketingTab));
                report.Add(AppsFlyerIosItem(iosAppId, marketing?.appleId, iosId, lookup)
                    .With(ReadinessActions.OpenScript("Mở GameConstant.cs", gameConstant, "IOSAppId"), marketingTab));
            }

            // Facebook
            var facebook = ReadProjectFile(FACEBOOK_SETTINGS_PATH);
            if (facebook != null)
            {
                var appId = Match(facebook, "appIds:\\s*\\n\\s*-\\s*(\\S+)");
                var token = Match(facebook, "clientTokens:\\s*\\n\\s*-\\s*(\\S+)");
                var wanted = marketing?.facebook?.appId;
                EzgStatus status;
                string note = null;
                if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(token))
                {
                    status = string.IsNullOrEmpty(wanted) ? EzgStatus.None : EzgStatus.Warn;
                    note = string.IsNullOrEmpty(wanted) ? "Không tích hợp Facebook." : "Sheet có appId mà FacebookSettings trống.";
                }
                else if (!string.IsNullOrEmpty(wanted) && wanted != appId)
                {
                    status = EzgStatus.Warn;
                    note = $"Khác appId trong sheet (`{wanted}`).";
                }
                else status = EzgStatus.Ok;

                report.Add(new ReadinessItem(g, "Facebook app id", appId, status, note,
                        status == EzgStatus.Warn ? "Tab Marketing > ghi lại FacebookSettings từ sheet (appId + client token)." : null)
                    .With(ReadinessActions.SelectAsset("Chọn FacebookSettings", FACEBOOK_SETTINGS_PATH), marketingTab));
            }
        }

        private static ReadinessItem KeyItem(ReadinessGroup g, string label, string android, string ios,
            string fix, string url, params (string Label, Action Run)[] actions)
        {
            var missingAndroid = string.IsNullOrEmpty(android);
            var missingIos = string.IsNullOrEmpty(ios);
            var value = $"{(missingAndroid ? "—" : Short(android))} · {(missingIos ? "—" : Short(ios))}";
            if (!missingAndroid && !missingIos) return new ReadinessItem(g, label, value, EzgStatus.Ok).With(actions);

            var which = missingAndroid && missingIos ? "cả hai nền tảng" : missingAndroid ? "Android" : "iOS";
            return new ReadinessItem(g, label, value, EzgStatus.Warn, $"Trống ở {which} — format đó không có ads.",
                fix, ("MAX dashboard", url)).With(actions);
        }

        /// <summary>
        ///     AppsFlyer iOS init bằng App Store ID. Sai id là SDK vẫn chạy bình thường, chỉ là số liệu
        ///     iOS không bao giờ hiện dưới app của mình — nên đây là chỗ DUY NHẤT tool cho tra thẳng App
        ///     Store (nút bấm, kết quả truyền vào qua <paramref name="lookup" />).
        /// </summary>
        private static ReadinessItem AppsFlyerIosItem(string iosAppId, string sheetAppleId, string iosBundle,
            AppStoreLookup lookup)
        {
            const ReadinessGroup g = ReadinessGroup.Sdk;
            const string label = "AppsFlyer iOS App Store ID";
            var links = new[] { ("App Store Connect", URL_ASC) };
            const string howToGet = "Lấy Apple ID (số 10 chữ số) ở App Store Connect > App > App Information > General Information";

            if (string.IsNullOrEmpty(iosAppId))
                return new ReadinessItem(g, label, null, EzgStatus.Warn,
                    "Trống → attribution iOS không hoạt động.",
                    howToGet + ", điền appleId trong sheet marketing rồi tab Marketing > ghi GameConstant.", links);

            if (lookup != null && lookup.QueriedId == iosAppId)
            {
                if (!string.IsNullOrEmpty(lookup.Error))
                    return new ReadinessItem(g, label, iosAppId, EzgStatus.Warn,
                        "Tra App Store thất bại: " + lookup.Error, "Bấm Tra App Store lại khi có mạng.", links);
                if (!lookup.Found)
                    return new ReadinessItem(g, label, iosAppId, EzgStatus.Warn,
                        "App Store không trả về app nào cho id này (app chưa public thì lookup cũng rỗng).",
                        howToGet + " và đối chiếu tay với IOSAppId.", links);
                if (!string.IsNullOrEmpty(iosBundle) && lookup.BundleId != iosBundle)
                    return new ReadinessItem(g, label, iosAppId, EzgStatus.Error,
                        $"Id này là app \"{lookup.TrackName}\" ({lookup.BundleId}, {lookup.Seller}) — KHÔNG phải app này. "
                        + "Attribution iOS đang chảy sang app lạ.",
                        howToGet + " của CHÍNH app này, điền appleId trong sheet marketing rồi tab Marketing > ghi GameConstant (hoặc mở GameConstant.cs sửa IOSAppId tay).",
                        links);
                return new ReadinessItem(g, label, iosAppId, EzgStatus.Ok,
                    $"App Store: \"{lookup.TrackName}\" · {lookup.BundleId}");
            }

            if (!string.IsNullOrEmpty(sheetAppleId) && sheetAppleId != iosAppId)
                return new ReadinessItem(g, label, iosAppId, EzgStatus.Error,
                    $"Khác appleId trong sheet marketing (`{sheetAppleId}`).",
                    "Tab Marketing > ghi lại GameConstant để IOSAppId theo sheet.", links);

            return new ReadinessItem(g, label, iosAppId,
                string.IsNullOrEmpty(sheetAppleId) ? EzgStatus.Warn : EzgStatus.Ok,
                string.IsNullOrEmpty(sheetAppleId)
                    ? "Sheet marketing chưa có appleId nên chưa đối chiếu được — id có thể là số copy từ template."
                    : null,
                string.IsNullOrEmpty(sheetAppleId)
                    ? "Bấm \"Tra App Store id\" ở đầu trang để xác minh id này là app nào; rồi " + howToGet + " và điền appleId vào sheet."
                    : null, links);
        }

        #endregion

        #region Store / build

        private static void CheckStore(ReadinessReport report, MarketingConfig marketing)
        {
            const ReadinessGroup g = ReadinessGroup.Store;
            var playerSettings = ReadinessActions.ProjectSettings("Mở Player Settings", SETTINGS_PLAYER);
            var marketingTab = ReadinessActions.KitTab("Mở tab Marketing", EzgKitWindow.Tab.Marketing);

            var version = PlayerSettings.bundleVersion;
            report.Add(new ReadinessItem(g, "Version",
                    $"{version} · Android code {PlayerSettings.Android.bundleVersionCode} · iOS build {PlayerSettings.iOS.buildNumber}",
                    string.IsNullOrEmpty(version) ? EzgStatus.Warn : EzgStatus.None,
                    fix: string.IsNullOrEmpty(version) ? "Player Settings > Version: điền dạng x.y.z." : null)
                .With(playerSettings));

            // Keystore
            var useCustom = PlayerSettings.Android.useCustomKeystore;
            var keystore = PlayerSettings.Android.keystoreName ?? "";
            var keystorePath = keystore.StartsWith("{inproject}:")
                ? Path.Combine(ProjectRoot(), keystore.Substring("{inproject}:".Length).Trim())
                : keystore;
            var keystoreExists = !string.IsNullOrEmpty(keystorePath) && File.Exists(keystorePath);
            if (!useCustom)
                report.Add(new ReadinessItem(g, "Android keystore", "debug key", EzgStatus.Error,
                        "Release build sẽ ký debug key — Play không nhận.",
                        "Player Settings > Publishing Settings > tick Custom Keystore, chọn file keystore của dự án + alias.")
                    .With(playerSettings));
            else if (!keystoreExists)
                report.Add(new ReadinessItem(g, "Android keystore", keystore, EzgStatus.Error,
                        "File keystore không tồn tại trên máy này.",
                        "Lấy đúng keystore của dự án về đường dẫn đã khai, hoặc Player Settings > Publishing Settings > chọn lại file.")
                    .With(playerSettings));
            else
                report.Add(new ReadinessItem(g, "Android keystore", keystore, EzgStatus.Ok,
                        $"alias `{PlayerSettings.Android.keyaliasName}`")
                    .With(playerSettings));

            // Store listing links
            if (marketing != null)
            {
                var play = marketing.links?.googlePlay;
                var appStore = marketing.links?.appStore;
                report.Add(new ReadinessItem(g, "Link Google Play", play,
                        string.IsNullOrEmpty(play) ? EzgStatus.Warn : EzgStatus.Ok,
                        string.IsNullOrEmpty(play) ? "Rỗng → nút rate/share trỏ link tự dựng từ package name (app chưa public thì 404)." : null,
                        string.IsNullOrEmpty(play) ? "Khi listing đã có: điền links.googlePlay trong sheet marketing rồi tab Marketing > ghi." : null,
                        ("Play Console", URL_PLAY_CONSOLE))
                    .With(marketingTab));
                report.Add(new ReadinessItem(g, "Link App Store", appStore,
                        string.IsNullOrEmpty(appStore) ? EzgStatus.Warn : EzgStatus.Ok,
                        string.IsNullOrEmpty(appStore) ? "Rỗng — cần Apple ID để dựng link." : null,
                        string.IsNullOrEmpty(appStore) ? "Điền appleId (hoặc links.appStore) trong sheet marketing rồi tab Marketing > ghi." : null,
                        ("App Store Connect", URL_ASC))
                    .With(marketingTab));
            }
            else
                report.Add(new ReadinessItem(g, "MarketingConfig.json", null, EzgStatus.Warn,
                        "Chưa tải sheet marketing — không đối chiếu được link store / Apple ID.",
                        "Tab Marketing > dán link sheet > Tải sheet.")
                    .With(marketingTab));
        }

        #endregion

        #region Helpers

        private static MarketingConfig LoadMarketing()
        {
            try
            {
                var path = MarketingConfig.JsonPath;
                if (!File.Exists(path)) return null;
                return JsonUtility.FromJson<MarketingConfig>(File.ReadAllText(path));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ProjectRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        /// <summary>Đọc file theo đường dẫn tính từ thư mục chứa <c>Assets/</c>; null = không có / không đọc được.</summary>
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

        /// <summary>Đường dẫn tuyệt đối của script <paramref name="className" />.cs trong Assets; null = không có.</summary>
        private static string FindScript(string className)
        {
            foreach (var guid in AssetDatabase.FindAssets(className + " t:Script"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path) == className + ".cs")
                    return Path.Combine(ProjectRoot(), path);
            }

            return null;
        }

        /// <summary>CSV nguồn của một bảng pack (<c>{tên bảng}.csv</c> trong CsvConfig) — nơi sửa thật; asset chỉ là bản import.</summary>
        private static string FindCsv(string tableName)
        {
            foreach (var guid in AssetDatabase.FindAssets(tableName + " t:TextAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path) == tableName + ".csv") return path;
            }

            return null;
        }

        private static string Dash(string value) => string.IsNullOrEmpty(value) ? "—" : value;

        private static bool IsAdmobAppId(string id) =>
            !string.IsNullOrEmpty(id) && id.StartsWith("ca-app-pub-", StringComparison.Ordinal);

        /// <summary>Key dài (MAX sdk key 86 ký tự) cắt cho vừa một dòng; id ngắn giữ nguyên.</summary>
        private static string Short(string value) =>
            value.Length <= 24 ? value : value.Substring(0, 10) + "…" + value.Substring(value.Length - 6);

        #endregion
    }
}
#endif
