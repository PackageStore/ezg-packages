#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Ezg.VoodooSdk.Editor
{
    /// <summary>
    /// Toàn bộ thông tin phải cung cấp cho mỗi project. Đọc từ
    /// <see cref="VoodooSdkPaths.ConfigFile"/>; không có gì khác cần chỉnh bằng tay.
    /// </summary>
    [Serializable]
    public class VoodooSdkConfig
    {
        #region Fields

        /// <summary>Chuỗi đánh dấu giá trị chưa điền trong file template.</summary>
        public const string Placeholder = "FILL_ME";

        public GameAnalyticsSection gameAnalytics = new();
        public FacebookSection facebook = new();
        public AdjustSection adjust = new();
        public GdprSection gdpr = new();
        public FirebaseSection firebase = new();

        #endregion

        #region Types

        [Serializable]
        public class PlatformKeys
        {
            public string gameKey = Placeholder;
            public string secretKey = Placeholder;
        }

        [Serializable]
        public class GameAnalyticsSection
        {
            public PlatformKeys android = new();
            public PlatformKeys ios = new();
        }

        [Serializable]
        public class FacebookSection
        {
            public string appId = Placeholder;

            /// <summary>Meta for Developers → App → Settings → <b>Advanced</b> → Client token.</summary>
            public string clientToken = Placeholder;

            public string appLabel = "";
        }

        [Serializable]
        public class AdjustSection
        {
            public string androidToken = Placeholder;
            public string iosToken = Placeholder;
        }

        [Serializable]
        public class GdprSection
        {
            public string companyName = Placeholder;
            public string privacyPolicyUrl = Placeholder;
            public string contactEmail = Placeholder;

            /// <summary>Bật nếu game hiển thị bất kỳ dạng quảng cáo nào (banner/interstitial/rewarded).</summary>
            public bool displaysAds = true;
        }

        [Serializable]
        public class FirebaseSection
        {
            /// <summary>
            /// Activity mở khi người dùng chạm notification. Để trống nếu project không dùng
            /// Firebase Messaging — khi đó bỏ qua bước vá manifest.
            /// </summary>
            public string notificationActivity = "com.google.firebase.MessagingUnityPlayerActivity";
        }

        #endregion

        #region Public

        /// <summary>Đọc config từ đĩa. Trả về null nếu chưa có file.</summary>
        public static VoodooSdkConfig Load()
        {
            string path = VoodooSdkPaths.Absolute(VoodooSdkPaths.ConfigFile);
            if (!File.Exists(path))
                return null;

            try
            {
                return JsonUtility.FromJson<VoodooSdkConfig>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                Debug.LogError($"[VoodooSdk] Không đọc được {VoodooSdkPaths.ConfigFile}: {exception.Message}");
                return null;
            }
        }

        /// <summary>
        /// Liệt kê field còn thiếu. Rỗng nghĩa là đủ điều kiện chạy install.
        /// Key iOS được bỏ qua khi build target không phải iOS, để project Android-only
        /// không bị chặn.
        /// </summary>
        public List<string> FindMissingFields(bool requireIos)
        {
            var missing = new List<string>();

            Check("gameAnalytics.android.gameKey", gameAnalytics?.android?.gameKey);
            Check("gameAnalytics.android.secretKey", gameAnalytics?.android?.secretKey);
            Check("facebook.appId", facebook?.appId);
            Check("facebook.clientToken", facebook?.clientToken);
            Check("adjust.androidToken", adjust?.androidToken);
            Check("gdpr.companyName", gdpr?.companyName);
            Check("gdpr.privacyPolicyUrl", gdpr?.privacyPolicyUrl);
            Check("gdpr.contactEmail", gdpr?.contactEmail);

            if (requireIos)
            {
                Check("gameAnalytics.ios.gameKey", gameAnalytics?.ios?.gameKey);
                Check("gameAnalytics.ios.secretKey", gameAnalytics?.ios?.secretKey);
                Check("adjust.iosToken", adjust?.iosToken);
            }

            return missing;

            void Check(string name, string value)
            {
                if (string.IsNullOrWhiteSpace(value) || value.Contains(Placeholder))
                    missing.Add(name);
            }
        }

        /// <summary>Ghi file template có chú thích để người dùng điền. Không ghi đè file đã có.</summary>
        public static bool WriteTemplateIfMissing()
        {
            string path = VoodooSdkPaths.Absolute(VoodooSdkPaths.ConfigFile);
            if (File.Exists(path))
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Template);
            Debug.Log($"[VoodooSdk] Đã tạo {VoodooSdkPaths.ConfigFile} — điền các giá trị {Placeholder} rồi chạy lại.");
            return true;
        }

        #endregion

        #region Private

        // JSON không có comment, nên dùng field "_note" để hướng dẫn ngay trong file.
        private const string Template = @"{
  ""_note"": ""Điền hết giá trị FILL_ME rồi chạy: Tools > Voodoo SDK > Install. Key iOS bỏ trống được nếu chỉ ship Android."",

  ""gameAnalytics"": {
    ""_note"": ""GameAnalytics dashboard > Game Settings. Mỗi platform là một game riêng, key khác nhau."",
    ""android"": { ""gameKey"": ""FILL_ME"", ""secretKey"": ""FILL_ME"" },
    ""ios"":     { ""gameKey"": ""FILL_ME"", ""secretKey"": ""FILL_ME"" }
  },

  ""facebook"": {
    ""_note"": ""appId: Meta for Developers > App > Settings > Basic. clientToken: mục Advanced (KHÁC app secret)."",
    ""appId"": ""FILL_ME"",
    ""clientToken"": ""FILL_ME"",
    ""appLabel"": """"
  },

  ""adjust"": {
    ""_note"": ""Adjust dashboard > App > App token. iOS và Android là hai app khác nhau."",
    ""androidToken"": ""FILL_ME"",
    ""iosToken"": ""FILL_ME""
  },

  ""gdpr"": {
    ""_note"": ""Hiện trên popup GDPR lúc mở game lần đầu."",
    ""companyName"": ""FILL_ME"",
    ""privacyPolicyUrl"": ""FILL_ME"",
    ""contactEmail"": ""FILL_ME"",
    ""displaysAds"": true
  },

  ""firebase"": {
    ""_note"": ""Activity mở khi chạm notification. Để rỗng nếu project không dùng Firebase Messaging."",
    ""notificationActivity"": ""com.google.firebase.MessagingUnityPlayerActivity""
  }
}
";

        #endregion
    }
}
#endif
