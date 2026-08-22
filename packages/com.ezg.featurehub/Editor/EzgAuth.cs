// EZG Feature Hub — phiên đăng nhập dùng chung với trình cài đặt template.
//
// Catalog và package của EZG nằm sau một gateway yêu cầu đăng nhập Google (@easygoing.vn). Token do
// build_unity_template tạo ra và lưu ở ~/.ezg/credentials.json; Feature Hub đọc lại đúng file đó thay
// vì tự làm một luồng đăng nhập thứ hai — một máy chỉ có một phiên duy nhất.
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ezg.FeatureHub.Editor
{
    /// <summary>Đọc token đã cache và quyết định request nào cần gắn header xác thực.</summary>
    public static class EzgAuth
    {
        #region Fields

        private const string CredentialDirName = ".ezg";
        private const string CredentialFileName = "credentials.json";

        // Token đổi rất hiếm (30 ngày) nhưng mỗi lần cài feature là vài chục request, nên cache lại
        // và chỉ đọc lại đĩa khi file thay đổi.
        private static string _cachedToken;
        private static DateTime _cachedStamp;

        #endregion

        #region Public Methods

        /// <summary>Đường dẫn file credentials (theo user, không nằm trong project).</summary>
        public static string CredentialPath
        {
            get
            {
                string overridden = Environment.GetEnvironmentVariable("EZG_CRED_DIR");
                string dir = !string.IsNullOrEmpty(overridden)
                    ? overridden
                    : Path.Combine(HomeDirectory(), CredentialDirName);
                return Path.Combine(dir, CredentialFileName);
            }
        }

        /// <summary>Token phiên hiện tại, hoặc null nếu máy chưa đăng nhập.</summary>
        public static string Token
        {
            get
            {
                // Biến môi trường thắng: máy build CI chạy bằng service token, không có file nào cả.
                string fromEnv = Environment.GetEnvironmentVariable("EZG_TOKEN");
                if (!string.IsNullOrEmpty(fromEnv)) return fromEnv;

                string path = CredentialPath;
                if (!File.Exists(path))
                {
                    _cachedToken = null;
                    return null;
                }

                DateTime stamp = File.GetLastWriteTimeUtc(path);
                if (_cachedToken != null && stamp == _cachedStamp) return _cachedToken;

                try
                {
                    var parsed = JsonUtility.FromJson<Credentials>(File.ReadAllText(path));
                    _cachedToken = string.IsNullOrEmpty(parsed?.access_token) ? null : parsed.access_token;
                    _cachedStamp = stamp;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EZG] Không đọc được {path}: {e.Message}");
                    _cachedToken = null;
                }

                return _cachedToken;
            }
        }

        /// <summary>URL này có thuộc gateway của EZG không (chỉ khi đó mới được gửi token đi).</summary>
        public static bool IsGatewayUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;

            string gateway = GatewayUrl;
            if (url.StartsWith(gateway + "/", StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(url, gateway, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Gateway đang dùng — đổi được qua EZG_GATEWAY_URL khi test staging.</summary>
        public static string GatewayUrl
        {
            get
            {
                string overridden = Environment.GetEnvironmentVariable("EZG_GATEWAY_URL");
                string value = string.IsNullOrEmpty(overridden)
                    ? FeatureHubConstants.EZG_REGISTRY_URL
                    : overridden;
                return value.TrimEnd('/');
            }
        }

        /// <summary>Thông báo hiển thị khi server trả 401/403 — nói rõ phải làm gì để vào lại.</summary>
        public static string SignInHint =>
            "Phiên đăng nhập EZG đã hết hạn hoặc chưa có trên máy này.\n" +
            "Chạy lệnh sau ở thư mục chứa build_unity_template.sh rồi thử lại:\n\n" +
            "    ./build_unity_template.sh --login";

        /// <summary>Mở trang đăng nhập của gateway trong trình duyệt.</summary>
        public static void OpenSignInPage()
        {
            Application.OpenURL($"{GatewayUrl}/auth/device");
        }

        /// <summary>Dialog dùng chung khi một request bị chặn vì chưa đăng nhập.</summary>
        public static void ShowSignInDialog()
        {
            if (EditorUtility.DisplayDialog("EZG — cần đăng nhập", SignInHint, "Mở trang đăng nhập", "Đóng"))
                OpenSignInPage();
        }

        #endregion

        #region Private Methods

        private static string HomeDirectory()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home)) return home;

            home = Environment.GetEnvironmentVariable("HOME")
                   ?? Environment.GetEnvironmentVariable("USERPROFILE");
            return home ?? string.Empty;
        }

        #endregion

        #region Nested Types

        // Khớp đúng tên field trong credentials.json do build_unity_template ghi ra.
        [Serializable]
        private class Credentials
        {
            public string access_token;
            public string email;
        }

        #endregion
    }
}
