// EZG Feature Hub — tự đảm bảo runtime dependency của chính Hub (rlottie) có trong project.
//
// Vì sao cần: Feature Hub render icon Lottie qua assembly LottiePlugin.Runtime (gói
// com.gindemit.rlottie, cài bằng git-url). UPM KHÔNG resolve git-dependency khai báo gián tiếp
// trong package.json của một UPM package -> không thể đưa rlottie vào "dependencies" của Hub.
// Hậu quả: cài Feature Hub trên máy mới mà manifest chưa có rlottie => thiếu LottiePlugin =>
// lỗi compile CS0246 (đã chặn ở mức code bằng define EZG_HAS_RLOTTIE, xem LottieElement.cs).
//
// Class [InitializeOnLoad] này chạy mỗi lần load editor / sau domain reload, kiểm tra manifest.json
// và TỰ THÊM git-url rlottie nếu thiếu, rồi resolve. Sau khi Unity resolve xong, versionDefines bật
// EZG_HAS_RLOTTIE -> recompile -> icon Lottie hoạt động. Idempotent: nếu đã có thì không làm gì
// (no-op, không loop) vì lần thêm chỉ xảy ra đúng một lần khi thật sự thiếu.
using UnityEditor;
using UnityEngine;

namespace Ezg.FeatureHub.Editor
{
    [InitializeOnLoad]
    public static class FeatureHubRuntimeDependency
    {
        #region Constants

        // Cờ session để chỉ kiểm tra một lần mỗi phiên editor (tránh đọc file thừa sau mỗi reload).
        private const string CHECKED_KEY = "Ezg.FeatureHub.RlottieChecked";

        #endregion

        #region Initialize

        static FeatureHubRuntimeDependency()
        {
            // Defer tới khi editor ổn định (giống FeatureHubImportFinalizer) để tránh ghi file /
            // gọi Client.Resolve ngay giữa lúc đang load domain.
            EditorApplication.delayCall += EnsureRlottie;
        }

        #endregion

        #region Private Methods

        private static void EnsureRlottie()
        {
            if (SessionState.GetBool(CHECKED_KEY, false))
                return;
            SessionState.SetBool(CHECKED_KEY, true);

            bool added = FeatureHubService.EnsureDependency(
                FeatureHubConstants.RLOTTIE_PACKAGE_NAME,
                FeatureHubConstants.RLOTTIE_PACKAGE_URL);

            if (!added)
                return;

            Debug.Log(
                "[FeatureHub] Đã thêm com.gindemit.rlottie (LottiePlugin) vào Packages/manifest.json " +
                "để bật icon Lottie. Unity đang resolve — editor sẽ recompile khi xong.");
            FeatureHubService.ResolveNow();
        }

        #endregion
    }
}
