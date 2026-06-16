using System;

namespace Ezg.Feature.IAP
{
    /// <summary>
    /// Cấu hình bảo mật + fallback inject vào module lúc khởi tạo.
    /// Giữ secrets (tangle/public key) bên ngoài module để module độc lập và tái dùng
    /// được trên project khác mà không gắn cứng khóa.
    /// </summary>
    public class IapSecurityConfig
    {
        /// <summary>Tangle bytes của Google Play (CrossPlatformValidator).</summary>
        public byte[] GooglePlayTangle;

        /// <summary>Tangle bytes của Apple (CrossPlatformValidator).</summary>
        public byte[] AppleTangle;

        /// <summary>Public key dùng cho AppsFlyer validateAndSendInAppPurchase (Android).</summary>
        public string AppsFlyerPublicKey;

        /// <summary>
        /// Provider trả về chuỗi giá mặc định (vd localize "coming_soon") khi store chưa sẵn sàng.
        /// Dùng Func để luôn lấy giá trị mới nhất mà module không phụ thuộc hệ localize cụ thể.
        /// </summary>
        public Func<string> DefaultPriceTextProvider;
    }
}
