#if UNITY_EDITOR
namespace Ezg.Editor.Shared.EzgKit
{
    /// <summary>
    ///     Id ổn định của các trang — dùng trong state file, API cho Claude (<c>EzgKitApi</c>) và nút "Mở trang"
    ///     của Readiness. KHÔNG đổi giá trị: skill <c>/setup-project</c> gọi theo chuỗi này.
    /// </summary>
    internal static class PageIds
    {
        internal const string OVERVIEW = "overview";
        internal const string PROJECT = "project";
        internal const string MARKETING = "marketing";
        internal const string ADS = "ads";
        internal const string IAP = "iap";
        internal const string ARTSTYLE = "artstyle";
        internal const string LOCALIZE = "localize";
        internal const string FIREBASE = "firebase";
        internal const string PUBLISHER = "publisher";
        internal const string SOCIAL = "social";
    }
}
#endif
