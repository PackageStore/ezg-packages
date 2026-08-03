using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ezg.Package.AdsManager.Editor
{
    /// <summary>
    /// Cảnh báo trước khi build nếu <see cref="AdsConfig" /> còn bật debugAds — bản build đó sẽ KHÔNG
    /// có quảng cáo thật và KHÔNG bắn tracking quảng cáo.
    /// <para>
    /// Build từ Unity Editor: hiện hộp thoại xác nhận, chọn "Huỷ build" để dừng hoặc "Build tiếp" để
    /// tiếp tục. Build headless (CI, -batchmode): không hiện được dialog nên chỉ log cảnh báo và cho
    /// build chạy tiếp — pipeline production nên tự đảm bảo cờ này tắt.
    /// </para>
    /// </summary>
    public class AdsBuildGuard : IPreprocessBuildWithReport
    {
        #region Fields

        private const string CONFIG_RESOURCE_NAME = "AdsConfig";

        /// <summary> Chạy sớm để cảnh báo trước các bước build khác. </summary>
        public int callbackOrder => -1000;

        #endregion

        #region Public Methods

        /// <summary>
        /// Kiểm tra cờ debugAds trước khi build.
        /// </summary>
        /// <param name="report">Thông tin build do Unity cung cấp.</param>
        public void OnPreprocessBuild(BuildReport report)
        {
            var config = Resources.Load<AdsConfig>(CONFIG_RESOURCE_NAME);
            if (config == null || !config.IsDebugAds) return;

            const string message =
                "AdsConfig đang bật DEBUG ADS.\n\n" +
                "Bản build này sẽ:\n" +
                "  • KHÔNG hiển thị quảng cáo thật (mọi ad tự động thành công)\n" +
                "  • KHÔNG dùng ad-unit-id thật\n" +
                "  • KHÔNG bắn tracking quảng cáo lên Firebase / AppsFlyer\n\n" +
                "Nếu đây là bản production, hãy huỷ và tắt debugAds trong AdsConfig.";

            // Batch mode (CI) không hiện được dialog — log rồi cho build tiếp để pipeline không treo.
            if (Application.isBatchMode)
            {
                Debug.LogWarning($"[Ads] {message}");
                return;
            }

            var buildAnyway = EditorUtility.DisplayDialog(
                "Debug Ads đang BẬT",
                message,
                "Build tiếp",
                "Huỷ build");

            if (!buildAnyway)
            {
                throw new BuildFailedException(
                    "[Ads] Build bị huỷ vì debugAds đang bật. Tắt debugAds trong AdsConfig rồi build lại.");
            }

            Debug.LogWarning("[Ads] Build tiếp với DEBUG ADS đang BẬT — bản build này không có ads thật " +
                             "và không bắn tracking quảng cáo.");
        }

        #endregion
    }
}
