#if UNITY_EDITOR
using System;
using System.Text.RegularExpressions;

namespace Ezg.Editor.Shared.Setup
{
    /// <summary>
    ///     Kiểm giá trị người dùng nhập — dùng chung cho validate tại chỗ trên cửa sổ, detector trạng thái và
    ///     <c>EzgKitApi.Apply</c>. Trả về null = hợp lệ, chuỗi = vì sao không.
    /// </summary>
    internal static class Validate
    {
        /// <summary>Android package name: chữ, số, gạch dưới; mỗi đoạn bắt đầu bằng chữ.</summary>
        private static readonly Regex _bundle = new(@"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+$");

        /// <summary>iOS bundle id cho phép thêm gạch nối.</summary>
        private static readonly Regex _bundleIos = new(@"^[a-zA-Z0-9][a-zA-Z0-9\-]*(\.[a-zA-Z0-9][a-zA-Z0-9\-]*)+$");
        private static readonly Regex _email = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");

        /// <summary>Bundle id / package name Unity sinh sẵn hoặc của template — dùng là app trùng / bị store từ chối.</summary>
        private static readonly string[] PlaceholderBundles =
        {
            "com.company.", "com.defaultcompany.", "com.unity3d.", "com.example.", "com.yourcompany.",
        };

        private static readonly string[] PlaceholderProducts = { "unity game template", "new unity project", "my project", "productname" };

        private static readonly string[] PlaceholderCompanies = { "defaultcompany", "companyname", "default company" };

        internal static string BundleId(string value) => BundleId(value, false);

        internal static string BundleIdIos(string value) => BundleId(value, true);

        internal static string BundleId(string value, bool ios)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Chưa có bundle id.";
            var id = value.Trim();
            if (ios ? !_bundleIos.IsMatch(id) : !_bundle.IsMatch(id))
                return ios
                    ? "Sai định dạng — iOS cần dạng com.studio.game (chữ, số, gạch nối)."
                    : "Sai định dạng — Android cần dạng com.studio.game (chữ, số, gạch dưới; mỗi đoạn bắt đầu bằng chữ, không gạch nối).";
            var lower = id.ToLowerInvariant() + ".";
            foreach (var placeholder in PlaceholderBundles)
                if (lower.StartsWith(placeholder, StringComparison.Ordinal))
                    return $"Đang là id mẫu ({placeholder.TrimEnd('.')}…) — đổi sang id thật của game.";
            return null;
        }

        internal static string ProductName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Chưa có tên hiển thị.";
            foreach (var placeholder in PlaceholderProducts)
                if (string.Equals(value.Trim(), placeholder, StringComparison.OrdinalIgnoreCase))
                    return "Đang là tên mẫu của Unity / template.";
            return null;
        }

        internal static string CompanyName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Chưa có tên công ty.";
            foreach (var placeholder in PlaceholderCompanies)
                if (string.Equals(value.Trim(), placeholder, StringComparison.OrdinalIgnoreCase))
                    return "Đang là tên mẫu của Unity (DefaultCompany).";
            return null;
        }

        internal static string ProjectName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Chưa có tên dự án.";
            if (value.Contains("__PROJECT_")) return "Còn placeholder của template.";
            return null;
        }

        /// <summary>URL http(s) — <paramref name="optional" /> = rỗng vẫn hợp lệ.</summary>
        internal static string Url(string value, bool optional = true)
        {
            if (string.IsNullOrWhiteSpace(value)) return optional ? null : "Chưa có link.";
            var v = value.Trim();
            if (!v.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !v.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                return "Không phải URL http(s).";
            return v.IndexOf(' ') >= 0 ? "URL có khoảng trắng." : null;
        }

        internal static string GoogleSheet(string value, bool optional = true)
        {
            if (string.IsNullOrWhiteSpace(value)) return optional ? null : "Chưa có link Google Sheet.";
            return LocalizeBridge.SheetId(value) == null ? "Không phải link Google Sheets (cần …/spreadsheets/d/<id>/…)." : null;
        }

        internal static string AppStoreId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return Regex.IsMatch(value.Trim(), @"^\d{6,12}$") ? null : "App Store ID là dãy số (thường 10 chữ số), không có chữ \"id\".";
        }

        internal static string AdmobAppId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return Regex.IsMatch(value.Trim(), @"^ca-app-pub-\d+~\d+$") ? null : "AdMob app id dạng ca-app-pub-XXXXXXXX~YYYYYYYY.";
        }

        internal static string Email(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return _email.IsMatch(value.Trim()) ? null : "Không phải địa chỉ email.";
        }

        internal static string DiscordWebhook(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return Regex.IsMatch(value.Trim(), @"^https://(discord|discordapp)\.com/api/webhooks/\d+/[A-Za-z0-9_-]+$")
                ? null
                : "Webhook Discord dạng https://discord.com/api/webhooks/<id>/<token>.";
        }
    }
}
#endif
