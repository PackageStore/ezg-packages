using UnityEngine;

namespace Ezg.BugLogger
{
    /// <summary>
    /// Toàn bộ cấu hình client của Bug Logger: mã project và (tuỳ chọn) endpoint thay thế. Asset đặt ở
    /// <c>Resources/BugLoggerSettings.asset</c>. Assembly này cố ý KHÔNG bị gate bằng define, để asset vẫn
    /// resolve được script ở bản store (không thành missing script) dù code gửi report đã bị loại.
    /// </summary>
    [CreateAssetMenu(fileName = RESOURCE_NAME, menuName = "Ezg/Bug Logger Settings")]
    public sealed class BugLoggerSettings : ScriptableObject
    {
        #region Fields

        /// <summary>Tên asset trong <c>Resources/</c> mà <c>BugLogger.Initialize()</c> nạp.</summary>
        public const string RESOURCE_NAME = "BugLoggerSettings";

        /// <summary>Worker BugHub dùng khi <see cref="EndpointOverride"/> trống.</summary>
        public const string DEFAULT_ENDPOINT = "https://bug-reporter.developer-a1f.workers.dev";

        // Tên field serialize là một phần contract (skill setup ghi thẳng key YAML này) — không đổi tên.
        [SerializeField]
        [Tooltip("Mã project đã đăng ký trên BugHub, ví dụ SM007. Bắt buộc — trống thì report chỉ nằm trong outbox.")]
        private string projectCode = string.Empty;

        [SerializeField]
        [Tooltip("Trống = Worker mặc định. Dùng cho server local khi mock test; http:// chỉ chạy trong Editor.")]
        private string endpointOverride = string.Empty;

        #endregion

        #region Public

        /// <summary>Mã project (đã trim), rỗng khi chưa cấu hình.</summary>
        public string ProjectCode => projectCode?.Trim() ?? string.Empty;

        /// <summary>Endpoint thay thế (đã trim), rỗng khi dùng Worker mặc định.</summary>
        public string EndpointOverride => endpointOverride?.Trim() ?? string.Empty;

        /// <summary>Base URL thật sự dùng để gửi: <see cref="EndpointOverride"/> nếu có, không thì <see cref="DEFAULT_ENDPOINT"/>. Không có dấu <c>/</c> cuối.</summary>
        public string ResolveEndpoint()
        {
            var endpoint = EndpointOverride;
            return (endpoint.Length == 0 ? DEFAULT_ENDPOINT : endpoint).TrimEnd('/');
        }

        #endregion
    }
}
