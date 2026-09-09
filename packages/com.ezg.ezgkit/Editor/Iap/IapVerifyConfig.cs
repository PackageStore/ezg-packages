#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Ezg.Editor.Shared.Iap
{
    /// <summary>
    ///     Đường gọi + API key cho lượt xác minh SKU trên store — API chỉ-đọc của <c>project-ezg</c>
    ///     (<c>GET /api/v1/iap/products</c>), trả về danh mục gói ĐANG CÓ THẬT trên App Store Connect /
    ///     Google Play của dự án mà API key thuộc về.
    ///     <para>
    ///         <b>Link nằm trong code, KHÔNG có ô nhập.</b> Đường gọi giống nhau ở mọi dự án và không đổi
    ///         theo dự án nào — dự án nằm trong chính chuỗi key. Một ô nhập cho một giá trị hằng chỉ tạo
    ///         thêm chỗ để gõ sai, rồi tab báo "không nối được server" cho một hạ tầng hoàn toàn bình
    ///         thường. Cần trỏ sang server dev tại máy (<see cref="LOCAL_API_URL" />) thì đặt biến môi
    ///         trường <see cref="ENV_API_URL" /> trước khi mở Unity — không ai phải sửa code, mà đường
    ///         mặc định vẫn chỉ có một giá trị.
    ///     </para>
    ///     <para>
    ///         <b>Không có "mã dự án".</b> API key đã trỏ về đúng một dự án và response tự khai
    ///         <c>project</c>, nên bắt người dùng gõ lại mã đó chỉ thêm một chỗ gõ sai — mà gõ sai thì tab
    ///         báo "key của dự án khác" cho một key hoàn toàn đúng. Ca dán nhầm key vẫn bị bắt, bằng thứ
    ///         không phải gõ tay: xem lượt kiểm ĐỘ KHỚP danh mục trong <c>IapSetupPage</c>.
    ///     </para>
    ///     <para>
    ///         <b>API key là secret duy nhất ở đây</b> (xem <see cref="ApiKey" />): hạn 180 ngày, nên nó
    ///         KHÔNG được đi vào repo. Key sống ở <see cref="EditorPrefs" /> theo từng project trên máy
    ///         này, fallback biến môi trường <see cref="ENV_API_KEY" /> cho CI — đúng cách server khuyến
    ///         nghị (<c>project-ezg/docs/api-kiem-tra-goi-ban.md</c>, mục "Đặt key ở đâu trong Unity").
    ///     </para>
    /// </summary>
    internal static class IapVerifyConfig
    {
        #region Constants

        /// <summary>Server thật. Đường gọi GIỐNG NHAU ở mọi dự án: dự án nằm trong chính chuỗi key.</summary>
        internal const string DEFAULT_API_URL = "https://project.easygoing.vn/api/v1/iap/products";

        /// <summary>Server dev chạy tại máy (project-ezg: <c>npm run dev:api</c>).</summary>
        internal const string LOCAL_API_URL = "http://localhost:3001/api/v1/iap/products";

        /// <summary>Biến môi trường trỏ sang server khác (dev tại máy, staging). Rỗng = server thật.</summary>
        internal const string ENV_API_URL = "EZG_IAP_API_URL";

        /// <summary>Biến môi trường cho API key — máy CI không có EditorPrefs của ai.</summary>
        internal const string ENV_API_KEY = "EZG_IAP_API_KEY";

        /// <summary>Trang cấp API key chỉ-đọc cho tool này.</summary>
        internal const string PORTAL_URL = "https://project.easygoing.vn/";

        #endregion

        #region Url

        internal static string ApiUrl
        {
            get
            {
                var overridden = Environment.GetEnvironmentVariable(ENV_API_URL);
                return string.IsNullOrWhiteSpace(overridden) ? DEFAULT_API_URL : overridden.Trim();
            }
        }

        /// <summary>Đang trỏ về máy này — tab phải nói rõ để không ai tưởng đang soi store thật.</summary>
        internal static bool IsLocalApi
        {
            get
            {
                // IndexOf chứ không Contains(string, StringComparison): overload đó chỉ có từ
                // .NET Standard 2.1, mà dự án đặt API Compatibility Level 2.0 thì không compile.
                var url = ApiUrl;
                return url.IndexOf("localhost", StringComparison.OrdinalIgnoreCase) >= 0
                       || url.IndexOf("127.0.0.1", StringComparison.Ordinal) >= 0;
            }
        }

        /// <summary>Link đến từ biến môi trường chứ không phải hằng mặc định — tab nói rõ.</summary>
        internal static bool ApiUrlFromEnvironment =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ENV_API_URL));

        #endregion

        #region Api key (secret — theo máy, không vào repo)

        /// <summary>
        ///     Chuỗi API key. EditorPrefs theo từng project trên máy này; rỗng thì đọc biến môi trường
        ///     <see cref="ENV_API_KEY" /> (đường dùng cho CI). Set = ghi EditorPrefs.
        /// </summary>
        internal static string ApiKey
        {
            get
            {
                var stored = EditorPrefs.GetString(ApiKeyPref, string.Empty);
                if (!string.IsNullOrEmpty(stored)) return stored;

                return Environment.GetEnvironmentVariable(ENV_API_KEY) ?? string.Empty;
            }
            set => EditorPrefs.SetString(ApiKeyPref, (value ?? string.Empty).Trim());
        }

        /// <summary>Key đang đến từ biến môi trường chứ không phải ô nhập — nhãn phải nói rõ.</summary>
        internal static bool ApiKeyFromEnvironment =>
            string.IsNullOrEmpty(EditorPrefs.GetString(ApiKeyPref, string.Empty))
            && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ENV_API_KEY));

        private static string ApiKeyPref => "Ezg.Iap.ApiKey:" + Application.dataPath;

        /// <summary>
        ///     Vài ký tự đầu để nhìn là biết đang cầm key nào, mà không bày cả chuỗi ra màn hình (tab
        ///     kit hay bị share screen lúc họp). Đúng độ dài server hiển thị trong trang quản trị.
        /// </summary>
        internal static string ApiKeyHint
        {
            get
            {
                var key = ApiKey;
                if (string.IsNullOrEmpty(key)) return string.Empty;
                return key.Length <= 14 ? key : key.Substring(0, 14) + "…";
            }
        }

        #endregion
    }
}
#endif
