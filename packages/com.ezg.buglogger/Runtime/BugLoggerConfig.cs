using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ezg.BugLogger
{
    /// <summary>
    /// Hook tuỳ chọn host cấp cho Bug Logger. Endpoint và mã project KHÔNG nằm ở đây — chúng đến từ
    /// <see cref="BugLoggerSettings"/> lúc <see cref="BugLogger.Initialize"/>. Mọi callback được gọi trên main thread.
    /// </summary>
    public static class BugLoggerConfig
    {
        #region Fields

        /// <summary>Gửi cho <see cref="Notify"/> khi report đã lưu nhưng chưa gửi được lúc này (offline / chưa cấu hình).</summary>
        public const string NOTIFY_SAVED_OFFLINE = "Bug report saved - it will be sent when online.";

        /// <summary>Gửi cho <see cref="Notify"/> khi report đã lưu và đang gửi.</summary>
        public const string NOTIFY_SAVED_SENDING = "Bug report saved - sending...";

        /// <summary>Gửi cho <see cref="Notify"/> khi report không ghi được xuống disk.</summary>
        public const string NOTIFY_SAVE_FAILED = "Bug report could not be saved.";

        private const string HTTPS_SCHEME = "https://";

        private static IBugReportTransport _transport;
        private static string _endpoint = string.Empty;
        private static string _projectCode = string.Empty;

        #endregion

        #region Public

        /// <summary>Tuỳ chọn: id người chơi gửi trong <c>player.id</c>. Null → rỗng.</summary>
        public static Func<string> PlayerIdProvider { get; set; }

        /// <summary>
        /// Tuỳ chọn: context game gửi trong <c>extra</c> (string → string, tối đa 32 cặp, mỗi giá trị ≤ 256).
        /// Gọi trên main thread lúc submit nên phải rẻ.
        /// </summary>
        public static Func<IReadOnlyDictionary<string, string>> ExtraContextProvider { get; set; }

        /// <summary>
        /// Tuỳ chọn: save snapshot (JSON) đính kèm thành <c>save.json</c> (≤ 5 MiB, có hạn 10 s realtime). Được
        /// serialize ngoài main thread. Trả null thì không đính kèm.
        /// </summary>
        public static Func<UniTask<string>> SaveSnapshotProvider { get; set; }

        /// <summary>Tuỳ chọn: hook toast; nhận một trong các hằng <c>NOTIFY_*</c> (host tự localize).</summary>
        public static Action<string> Notify { get; set; }

        /// <summary>
        /// Transport gửi report. Null = <see cref="BugHubTransport"/> có sẵn. Seam cho test (fake mạng) hoặc
        /// backend khác. Gán là đánh thức uploader.
        /// </summary>
        public static IBugReportTransport Transport
        {
            get => _transport;
            set
            {
                _transport = value;
                BugReportUploader.Wake();
            }
        }

        #endregion

        #region Internal

        /// <summary>Endpoint + mã project của lần gửi hiện tại.</summary>
        internal static BugReportTarget CurrentTarget => new BugReportTarget(_endpoint, _projectCode);

        /// <summary>
        /// Đặt nơi gửi (từ settings, hoặc từ test) rồi đánh thức uploader để gửi report của phiên trước. Trên thiết
        /// bị chỉ nhận <c>https://</c> — report chứa log, save, id thiết bị nên không bao giờ đi cleartext, kể cả
        /// khi project bật "Allow downloads over HTTP".
        /// </summary>
        internal static void Configure(string endpoint, string projectCode)
        {
            _endpoint = endpoint?.Trim().TrimEnd('/') ?? string.Empty;
#if !UNITY_EDITOR
            if (_endpoint.Length > 0 && !_endpoint.StartsWith(HTTPS_SCHEME, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("[BugLogger] Endpoint must be https:// on devices - reports stay in the outbox.");
                _endpoint = string.Empty;
            }
#endif
            _projectCode = projectCode?.Trim() ?? string.Empty;
            BugReportUploader.Wake();
        }

        /// <summary>Trả mọi hook về mặc định. Report đang chờ vẫn trên disk.</summary>
        internal static void Reset()
        {
            _transport = null;
            _endpoint = string.Empty;
            _projectCode = string.Empty;
            PlayerIdProvider = null;
            ExtraContextProvider = null;
            SaveSnapshotProvider = null;
            Notify = null;
        }

        #endregion

        #region Private

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Reset();
        }

        #endregion
    }
}
