using UnityEngine;

namespace Ezg.Core.Firebase
{
    /// <summary>
    ///     Runtime configuration cho các Firebase manager. Mỗi project tạo 1 asset
    ///     (đặt trong thư mục <c>Resources</c> với tên <c>FirebaseConfig</c>) để custom bucket storage,
    ///     timeout đăng nhập, các hằng tuning Game Center... mà KHÔNG phải sửa code trong package.
    ///     Nếu không tìm thấy asset, package fallback về một instance default (giữ nguyên giá trị cũ).
    ///     Menu tạo asset: <b>Create &gt; Ezg &gt; Firebase &gt; Firebase Config</b> (do
    ///     <c>FirebaseConfigCreator</c> trong assembly Editor cung cấp — menu này còn scaffold luôn
    ///     <c>GameRemoteConfig.cs</c> phía game).
    /// </summary>
    public class FirebaseConfig : ScriptableObject
    {
        #region Fields

        private const string RESOURCE_NAME = "FirebaseConfig";

        [Header("Storage")]
        [Tooltip("Firebase Storage bucket URL, ví dụ gs://your-app.firebasestorage.app")]
        [SerializeField] private string storageBucketUrl = "gs://m1-food-merge.firebasestorage.app";

        [Tooltip("Thư mục con chứa save data của người chơi trong bucket.")]
        [SerializeField] private string playerDataFolder = "PlayersData";

        [Tooltip("Giới hạn dung lượng tối đa (byte) khi tải save data về.")]
        [SerializeField] private long maxDownloadSizeBytes = 1 * 1024 * 1024;

        [Header("Auth")]
        [Tooltip("Thời gian chờ tối đa (giây) cho một lần đăng nhập.")]
        [SerializeField] private float signInTimeoutSeconds = 30f;

        [Tooltip("Key PlayerPrefs lưu Apple user id.")]
        [SerializeField] private string appleUserIdPrefsKey = "AppleUserId";

        [Header("Game Center (iOS)")]
        [SerializeField] private int maxCredentialAttempts = 6;
        [SerializeField] private int credentialRetryDelayMs = 1000;
        [SerializeField] private float gameCenterAuthTimeoutSeconds = 15f;
        [SerializeField] private float playerAuthWaitTimeoutSeconds = 5f;
        [SerializeField] private int playerAuthPollDelayMs = 150;
        [SerializeField] private int postNativeAuthSettleDelayMs = 750;
        [SerializeField] private int postProviderReadySettleDelayMs = 500;

        [Header("Remote Config")]
        [Tooltip("Khoảng fetch tối thiểu (ms). 0 = luôn fetch (dùng cho dev).")]
        [SerializeField] private long minimumFetchIntervalMs = 0;

        private static FirebaseConfig _instance;

        #endregion

        #region Public Methods

        /// <summary>
        ///     Instance dùng chung, load từ <c>Resources/FirebaseConfig</c>. Nếu không có asset thì
        ///     tạo instance default trong bộ nhớ để package vẫn chạy với giá trị mặc định.
        /// </summary>
        public static FirebaseConfig Instance
        {
            get
            {
                if (_instance != null) return _instance;

                _instance = Resources.Load<FirebaseConfig>(RESOURCE_NAME);
                if (_instance == null)
                {
                    Debug.LogWarning(
                        $"[FirebaseConfig] Không tìm thấy Resources/{RESOURCE_NAME}. Dùng giá trị default. " +
                        "Tạo asset qua menu 'Ezg/Firebase/Firebase Config' và đặt trong thư mục Resources để custom.");
                    _instance = CreateInstance<FirebaseConfig>();
                }

                return _instance;
            }
        }

        // Storage
        public string StorageBucketUrl => storageBucketUrl;
        public string PlayerDataFolder => playerDataFolder;
        public long MaxDownloadSizeBytes => maxDownloadSizeBytes;

        // Auth
        public float SignInTimeoutSeconds => signInTimeoutSeconds;
        public string AppleUserIdPrefsKey => appleUserIdPrefsKey;

        // Game Center (iOS)
        public int MaxCredentialAttempts => maxCredentialAttempts;
        public int CredentialRetryDelayMs => credentialRetryDelayMs;
        public float GameCenterAuthTimeoutSeconds => gameCenterAuthTimeoutSeconds;
        public float PlayerAuthWaitTimeoutSeconds => playerAuthWaitTimeoutSeconds;
        public int PlayerAuthPollDelayMs => playerAuthPollDelayMs;
        public int PostNativeAuthSettleDelayMs => postNativeAuthSettleDelayMs;
        public int PostProviderReadySettleDelayMs => postProviderReadySettleDelayMs;

        // Remote Config
        public long MinimumFetchIntervalMs => minimumFetchIntervalMs;

        #endregion
    }
}
