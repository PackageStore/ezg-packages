#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ezg.Feature.IAP.Editor
{
    /// <summary>
    /// Sinh bộ class tích hợp (template) cho một project mới muốn dùng module IAP
    /// (namespace <c>Ezg.Feature.IAP</c>). Các file sinh ra là STUB biên dịch được + TODO:
    /// dev điền logic shop / player-data / analytics của project mình vào.
    ///
    /// Menu: <b>Assets > Create > Ezg > IAP > Project Setup</b> (cũng có ở chuột phải Project window).
    /// File được sinh vào folder đang chọn; nếu trùng tên sẽ hỏi ghi đè.
    /// </summary>
    public static class IapProjectSetupGenerator
    {
        private const string MENU_PATH = "Assets/Create/Ezg/IAP/Project Setup";
        private const string DIALOG_TITLE = "IAP Project Setup";

        [MenuItem(MENU_PATH, false, 80)]
        public static void Generate()
        {
            string folder = GetSelectedFolder();

            var files = new List<(string name, string content)>
            {
                ("InAppPurchase.cs", TEMPLATE_IN_APP_PURCHASE),
                ("GameIapHost.cs", TEMPLATE_GAME_IAP_HOST),
                ("IapBootstrap.cs", TEMPLATE_IAP_BOOTSTRAP),
                ("IAPEventName.cs", TEMPLATE_IAP_EVENT_NAME),
            };

            // Kiểm tra tồn tại
            var existing = new List<string>();
            foreach (var f in files)
            {
                if (File.Exists(folder + "/" + f.name))
                {
                    existing.Add(f.name);
                }
            }

            bool overwrite = true;
            if (existing.Count > 0)
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    DIALOG_TITLE,
                    "Các file sau đã tồn tại trong:\n" + folder + "\n\n- " + string.Join("\n- ", existing) +
                    "\n\nBạn muốn làm gì?",
                    "Ghi đè tất cả",   // 0
                    "Huỷ",             // 1
                    "Chỉ tạo file mới" // 2
                );

                if (choice == 1) return;       // Huỷ
                overwrite = (choice == 0);     // 0 = ghi đè, 2 = bỏ qua file đã tồn tại
            }

            int written = 0;
            int skipped = 0;
            foreach (var f in files)
            {
                string path = folder + "/" + f.name;
                if (File.Exists(path) && !overwrite)
                {
                    skipped++;
                    continue;
                }

                File.WriteAllText(path, f.content);
                written++;
            }

            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                DIALOG_TITLE,
                "Hoàn tất.\nĐã tạo/ghi đè: " + written + " file\nBỏ qua: " + skipped + " file\n\nFolder:\n" + folder +
                "\n\nMở từng file và hoàn thiện các // TODO.",
                "OK");

            var obj = AssetDatabase.LoadAssetAtPath<Object>(folder);
            if (obj != null) EditorGUIUtility.PingObject(obj);
        }

        /// <summary>Lấy folder đang chọn ở Project window (mặc định "Assets").</summary>
        private static string GetSelectedFolder()
        {
            foreach (var obj in Selection.GetFiltered<Object>(SelectionMode.Assets))
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;

                if (File.Exists(path))
                {
                    path = Path.GetDirectoryName(path);
                }

                return path.Replace('\\', '/');
            }

            return "Assets";
        }

        #region Templates

        private const string TEMPLATE_IN_APP_PURCHASE =
@"using System;
using System.Collections.Generic;

namespace Ezg.Feature.IAP
{
    /// <summary>
    /// TEMPLATE: implement IPurchasing — khai báo product id + xử lý khi mua hoàn tất.
    /// Đây là phần GAME-SPECIFIC, KHÔNG nằm trong package.
    /// </summary>
    public class InAppPurchase : IPurchasing
    {
        public Action<bool> OnTransactionRestored { get; set; }
        public Action<string> OnPurchaseFailed { get; set; }
        public Action<string> OnPurchaseCompleteBeforeCallback { get; set; }
        public Action<string> OnPurchaseComplete { get; set; }

        public InAppPurchase()
        {
            OnTransactionRestored += TransactionRestored;
            OnPurchaseFailed += PurchaseFailed;
            OnPurchaseComplete += PurchaseComplete;
            OnPurchaseCompleteBeforeCallback += PurchaseCompleteBeforeCallBack;
        }

        public List<string> GetConsumableProducts()
        {
            // TODO: trả về danh sách product id consumable của bạn
            return new List<string>();
        }

        public List<string> GetNonConsumableProducts()
        {
            // TODO: trả về danh sách product id non-consumable (nếu có)
            return new List<string>();
        }

        public void RestoreItem()
        {
            // TODO: khôi phục non-consumable nếu cần
        }

        private void TransactionRestored(bool success)
        {
            // TODO: hiển thị message restore (success/fail)
        }

        private void PurchaseFailed(string message)
        {
            // TODO: hiển thị message mua thất bại
        }

        private void PurchaseCompleteBeforeCallBack(string productId)
        {
            // TODO (tuỳ chọn): xử lý trước khi callback mua chạy
        }

        private void PurchaseComplete(string productId)
        {
            // TODO: cộng vật phẩm cho productId + emit event mua thành công
        }
    }
}
";

        private const string TEMPLATE_GAME_IAP_HOST =
@"namespace Ezg.Feature.IAP
{
    /// <summary>
    /// TEMPLATE: cung cấp dữ liệu người chơi (IIapProfile) + báo cáo analytics/đồng bộ (IIapReporter).
    /// Đây là phần GAME-SPECIFIC, KHÔNG nằm trong package.
    /// </summary>
    public class GameIapHost : IIapProfile, IIapReporter
    {
        // ===== IIapProfile =====
        public string AccountId => string.Empty; // TODO: id tài khoản dùng cho tracking

        public bool IsCheatEnabled => false; // TODO: trả cờ cheat IAP nếu có

        public void RecordPurchase(decimal localizedPrice)
        {
            // TODO: cộng dồn số lượt mua + doanh thu vào player data rồi Save()
        }

        // ===== IIapReporter =====
        public void OnPurchaseClick(IapPurchaseInfo info)
        {
            // TODO: bắn event 'purchase_click' (Firebase...)
        }

        public void OnPurchaseValidated(IapPurchaseInfo info)
        {
            // TODO: bắn event doanh thu (AppsFlyer/GameAnalytics...) + cập nhật user property
        }

        public void OnConversionData(string conversionJson)
        {
            // TODO: xử lý conversion/attribution data từ AppsFlyer
        }

        public void RequestSync()
        {
            // TODO: emit event yêu cầu đồng bộ dữ liệu (vd ForceSyncData)
        }
    }
}
";

        private const string TEMPLATE_IAP_BOOTSTRAP =
@"namespace Ezg.Feature.IAP
{
    /// <summary>
    /// TEMPLATE: wiring giữa game và module IAP.
    /// PHẢI gọi Configure() TRƯỚC InAppManager.Init()/Buy() (vd trong scene splash).
    /// </summary>
    public static class IapBootstrap
    {
        // TODO: public key AppsFlyer (nếu dùng AppsFlyer validateAndSendInAppPurchase trên Android)
        private static readonly string APPSFLYER_PUBLIC_KEY = string.Empty;

        private static GameIapHost _host;
        private static bool _configured;

        public static void Configure()
        {
            if (_configured) return;

            _host ??= new GameIapHost();

            var config = new IapSecurityConfig
            {
                // TODO: gán GooglePlayTangle.Data() / AppleTangle.Data() sau khi sinh tangle
                //       (Services > In-App Purchasing > Receipt Validation Obfuscator)
                GooglePlayTangle = null,
                AppleTangle = null,
                AppsFlyerPublicKey = APPSFLYER_PUBLIC_KEY,
                DefaultPriceTextProvider = () => string.Empty, // TODO: chuỗi giá mặc định khi store chưa sẵn sàng
            };

            InAppManager.Instance.Configure(new InAppPurchase(), _host, _host, config);
            _configured = true;
        }
    }
}
";

        private const string TEMPLATE_IAP_EVENT_NAME =
@"public partial class EventName
{
    // TODO: tên event mua IAP thành công (UI shop lắng nghe để refresh trạng thái)
    public const string PurchasedIapSuccess = ""PurchasedIapSuccess"";

    // TODO: tên event yêu cầu đồng bộ dữ liệu sau giao dịch
    public const string ForceSyncData = ""ForceSyncData"";
}
";

        #endregion
    }
}
#endif
