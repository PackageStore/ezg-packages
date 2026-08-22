// EZG Feature Hub — phiên đăng nhập dùng chung với trình cài đặt template.
//
// Catalog và package của EZG nằm sau một gateway yêu cầu đăng nhập Google (@easygoing.vn). Token do
// build_unity_template tạo ra và lưu ở ~/.ezg/credentials.json; Feature Hub đọc lại đúng file đó thay
// vì tự làm một luồng đăng nhập thứ hai — một máy chỉ có một phiên duy nhất.
using System;
using System.IO;
using System.Text.RegularExpressions;
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

        // Token đổi mỗi vài tiếng nhưng mỗi lần cài feature là vài chục request, nên cache lại và chỉ
        // đọc lại đĩa khi file thay đổi.
        private static string _cachedToken;
        private static long _cachedExpiresAt;
        private static DateTime _cachedStamp;

        // Gia hạn khi phiên còn dưới ngưỡng này. Rộng tay để một buổi làm việc bình thường không bao
        // giờ chạm hạn giữa chừng.
        private const int RefreshWhenUnderSeconds = 2 * 3600;

        // Chống gọi refresh dồn dập: mỗi lần compile lại script là một lần domain reload.
        private const string ThrottleKey = "Ezg.Auth.LastRefreshTicks";
        private const int ThrottleSeconds = 10 * 60;

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
                    _cachedExpiresAt = parsed?.expires_at ?? 0;
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

        /// <summary>Thời điểm phiên hết hạn (Unix seconds), 0 nếu không rõ.</summary>
        public static long ExpiresAt
        {
            get
            {
                _ = Token; // đọc file nếu cần, cập nhật _cachedExpiresAt
                return _cachedExpiresAt;
            }
        }

        /// <summary>Còn bao nhiêu giây nữa hết hạn (âm = đã hết).</summary>
        public static long SecondsLeft =>
            ExpiresAt <= 0 ? 0 : ExpiresAt - DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public static bool IsSignedIn => !string.IsNullOrEmpty(Token) && SecondsLeft > 0;

        /// <summary>Ghi credentials mới (JSON nguyên văn từ server) và đồng bộ luôn ~/.upmconfig.toml.</summary>
        public static void SaveSession(string credentialsJson)
        {
            string path = CredentialPath;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, credentialsJson);
                _cachedToken = null; // buộc đọc lại ở lần truy cập kế tiếp

                var parsed = JsonUtility.FromJson<Credentials>(credentialsJson);
                if (!string.IsNullOrEmpty(parsed?.access_token)) WriteUpmConfig(parsed.access_token);
            }
            catch (Exception e)
            {
                Debug.LogError($"[EZG] Không ghi được {path}: {e.Message}");
            }
        }

        /// <summary>
        /// Ghi token vào ~/.upmconfig.toml để Package Manager tự xác thực được.
        /// Chỉ thay block của registry mình, entry của registry khác giữ nguyên.
        /// </summary>
        public static void WriteUpmConfig(string token)
        {
            string path = UpmConfigPath;
            string registry = GatewayUrl;
            try
            {
                string existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
                string pattern = "^\\[npmAuth\\.\"" + Regex.Escape(registry) + "/?\"\\].*?(?=^\\[|\\z)";
                string cleaned = Regex
                    .Replace(existing, pattern, string.Empty, RegexOptions.Multiline | RegexOptions.Singleline)
                    .TrimEnd();
                string block = $"[npmAuth.\"{registry}\"]\ntoken = \"{token}\"\nalwaysAuth = true\n";
                File.WriteAllText(path, (cleaned.Length > 0 ? cleaned + "\n\n" : string.Empty) + block);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EZG] Không cập nhật được {path}: {e.Message}. Unity có thể không tải được package com.ezg.*.");
            }
        }

        public static string UpmConfigPath
        {
            get
            {
                string overridden = Environment.GetEnvironmentVariable("UPM_USER_CONFIG_PATH");
                return !string.IsNullOrEmpty(overridden)
                    ? overridden
                    : Path.Combine(HomeDirectory(), ".upmconfig.toml");
            }
        }

        /// <summary>
        /// Đẩy hạn phiên về sau. Phiên sống theo cửa sổ "không hoạt động", nên editor đang mở chỉ cần
        /// gọi định kỳ là người dùng không bao giờ gặp hạn giữa buổi làm.
        /// </summary>
        public static void Refresh(Action<bool> onDone = null)
        {
            string token = Token;
            if (string.IsNullOrEmpty(token))
            {
                onDone?.Invoke(false);
                return;
            }

            EditorDownloader.PostJson($"{GatewayUrl}/auth/refresh", "{}", token, (ok, body, _) =>
            {
                if (ok && !string.IsNullOrEmpty(body)) SaveSession(body);
                onDone?.Invoke(ok);
            });
        }

        /// <summary>
        /// Chạy mỗi lần domain reload: nếu phiên sắp hết hạn thì âm thầm gia hạn. Đây là thứ khiến
        /// cửa sổ 6 tiếng gần như vô hình với người đang làm việc trong Editor.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void AutoRefreshOnLoad()
        {
            if (!File.Exists(CredentialPath)) return;
            if (SecondsLeft <= 0 || SecondsLeft > RefreshWhenUnderSeconds) return;

            long last = 0;
            long.TryParse(SessionState.GetString(ThrottleKey, "0"), out last);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now - last < ThrottleSeconds) return;
            SessionState.SetString(ThrottleKey, now.ToString());

            Refresh(ok =>
            {
                if (ok) Debug.Log($"[EZG] Đã gia hạn phiên đăng nhập ({Email}).");
            });
        }

        /// <summary>Email của phiên hiện tại, chỉ để hiển thị.</summary>
        public static string Email
        {
            get
            {
                try
                {
                    if (!File.Exists(CredentialPath)) return null;
                    return JsonUtility.FromJson<Credentials>(File.ReadAllText(CredentialPath))?.email;
                }
                catch { return null; }
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
            "Đăng nhập lại bằng tài khoản Google công ty ngay trong Unity — không cần chạy lại builder.";

        /// <summary>Mở trang đăng nhập của gateway trong trình duyệt.</summary>
        public static void OpenSignInPage()
        {
            Application.OpenURL($"{GatewayUrl}/auth/device");
        }

        /// <summary>Dialog dùng chung khi một request bị chặn vì chưa đăng nhập.</summary>
        public static void ShowSignInDialog()
        {
            if (EditorUtility.DisplayDialog("EZG — cần đăng nhập", SignInHint, "Đăng nhập ngay", "Để sau"))
                EzgSignInWindow.Open();
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
            public long expires_at;
        }

        #endregion
    }
}
