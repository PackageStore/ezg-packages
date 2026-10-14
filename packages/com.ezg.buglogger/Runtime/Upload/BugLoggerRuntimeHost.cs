using UnityEngine;

namespace Ezg.BugLogger
{
    /// <summary>
    /// Object ẩn, sống qua scene, chỉ để chuyển <see cref="OnApplicationPause"/> (app quay lại foreground)
    /// cho uploader. Không có <c>Update</c>. Uploader tự tạo ở lần dùng đầu — không gắn tay.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class BugLoggerRuntimeHost : MonoBehaviour
    {
        #region Fields

        private const string OBJECT_NAME = "[BugLogger]";

        private static BugLoggerRuntimeHost _instance;

        #endregion

        #region Initialize

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!pauseStatus)
                BugReportUploader.Wake();
        }

        #endregion

        #region Internal

        /// <summary>Tạo host một lần mỗi phiên chơi. Main thread, chỉ trong play mode.</summary>
        internal static void EnsureCreated()
        {
            if (_instance != null)
                return;

            var host = new GameObject(OBJECT_NAME) { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<BugLoggerRuntimeHost>();
        }

        #endregion
    }
}
