using System;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ezg.BugLogger
{
    /// <summary>
    /// API công khai của Bug Logger (BugHub contract §8). Gọi <see cref="Initialize"/> một lần lúc boot, tải
    /// lựa chọn form bằng <see cref="GetFormOptionsAsync"/>, gửi bằng <see cref="Submit"/>. Mọi member gọi
    /// trên main thread; mọi event cũng được raise trên main thread.
    /// Code host nằm trong namespace <c>Ezg.*</c> nên đặt alias <c>using BugLoggerApi = Ezg.BugLogger.BugLogger;</c>
    /// (ở đó tên <c>BugLogger</c> resolve ra namespace).
    /// </summary>
    public static class BugLogger
    {
        #region Fields

        /// <summary>Cạnh dài tối đa (px) của ảnh <see cref="EncodeScreenshot"/> trả về.</summary>
        public const int SCREENSHOT_MAX_LONG_EDGE = 1280;

        /// <summary>Chất lượng JPEG của <see cref="EncodeScreenshot"/>.</summary>
        public const int SCREENSHOT_JPG_QUALITY = 75;

        private static bool _isInitialized;

        /// <summary>Một report đã nằm an toàn trên disk.</summary>
        public static event Action ReportQueued;

        /// <summary>Worker đã nhận report (mới hoặc trùng); mang số issue — 0 với feedback.</summary>
        public static event Action<int> ReportSent;

        /// <summary>
        /// Report thất bại vĩnh viễn: bị từ chối (400/404/413), bỏ cuộc sau 24 h / 50 lần, hoặc không ghi được
        /// xuống disk. Mang id report và lý do. Report (nếu đã ghi) nằm trong <c>BugLogger/failed/&lt;id&gt;</c>.
        /// </summary>
        public static event Action<string, string> ReportFailed;

        #endregion

        #region Public

        /// <summary>Số report đang chờ gửi (0 trước lần quét outbox đầu tiên).</summary>
        public static int PendingCount => Math.Max(0, BugReportOutbox.PendingCount);

        /// <summary>
        /// Gọi một lần lúc boot: đọc <see cref="BugLoggerSettings"/> trong <c>Resources</c>, gắn log ring buffer,
        /// quét outbox và gửi tiếp report còn lại từ phiên trước. Gọi lại là vô hại.
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized)
                return;

            var settings = Resources.Load<BugLoggerSettings>(BugLoggerSettings.RESOURCE_NAME);
            if (settings == null)
            {
                Debug.LogWarning($"[BugLogger] Resources/{BugLoggerSettings.RESOURCE_NAME} not found - reports stay in the outbox until it exists.");
                Initialize(BugLoggerSettings.DEFAULT_ENDPOINT, string.Empty);
                return;
            }

            if (settings.ProjectCode.Length == 0)
                Debug.LogWarning($"[BugLogger] {BugLoggerSettings.RESOURCE_NAME}.projectCode is empty - reports stay in the outbox until it is set.");

            Initialize(settings.ResolveEndpoint(), settings.ProjectCode);
        }

        /// <summary>
        /// Lựa chọn của form (category, forum tag, người được tag, giới hạn). Lỗi mạng → bản cache disk → mặc
        /// định; không bao giờ ném vì mạng.
        /// </summary>
        /// <param name="cancellationToken">Huỷ request (vẫn trả cache/mặc định).</param>
        public static UniTask<BugFormOptions> GetFormOptionsAsync(CancellationToken cancellationToken = default)
        {
            Initialize();
            return BugFormOptionsClient.FetchAsync(BugLoggerConfig.CurrentTarget, BugReportOutbox.Default.RootDirectory, cancellationToken);
        }

        /// <summary>
        /// Chụp context, ghi report vào outbox (nguyên tử, bất đồng bộ) rồi đánh thức uploader. Id sinh đồng bộ
        /// nên trả ngay. Không bao giờ ném vì mạng; lỗi ghi disk báo qua <see cref="BugLoggerConfig.Notify"/>
        /// (<see cref="BugLoggerConfig.NOTIFY_SAVE_FAILED"/>) và <see cref="ReportFailed"/>.
        /// </summary>
        /// <param name="draft">Nội dung QA nhập.</param>
        /// <param name="screenshotJpg">JPG từ <see cref="EncodeScreenshot"/>, hoặc null. Package giữ tham chiếu — đừng sửa mảng sau khi gửi.</param>
        /// <returns>Id report (uuid v4).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="draft"/> null.</exception>
        /// <exception cref="ArgumentException">Bug thiếu <c>Title</c> hoặc <c>Actual</c> — lỗi của caller.</exception>
        public static string Submit(BugReportDraft draft, byte[] screenshotJpg)
        {
            BugReportBuilder.Validate(draft);
            Initialize();

            var capture = BugReportBuilder.Capture(draft, screenshotJpg);
            BugReportOutbox.SubmitAsync(capture).Forget();
            return capture.Id;
        }

        /// <summary>
        /// Mã hoá <paramref name="source"/> thành JPEG cạnh dài ≤ <see cref="SCREENSHOT_MAX_LONG_EDGE"/> px, chất
        /// lượng <see cref="SCREENSHOT_JPG_QUALITY"/>: blit qua RenderTexture tạm (không cần texture readable),
        /// đọc lại rồi huỷ mọi texture tạm. Không huỷ <paramref name="source"/> — caller sở hữu nó. Main thread.
        /// </summary>
        /// <returns>Byte JPG, hoặc null khi <paramref name="source"/> null hay mã hoá lỗi.</returns>
        public static byte[] EncodeScreenshot(Texture2D source)
        {
            if (source == null)
                return null;

            GetScaledSize(source.width, source.height, out var width, out var height);
            var renderTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Texture2D scaled = null;
            try
            {
                Graphics.Blit(source, renderTexture);
                RenderTexture.active = renderTexture;
                scaled = new Texture2D(width, height, TextureFormat.RGB24, false);
                scaled.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                return scaled.EncodeToJPG(SCREENSHOT_JPG_QUALITY);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugLogger] Screenshot encode failed ({ex.GetType().Name}) - report is sent without image.");
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTexture);
                DestroyTemporary(scaled);
            }
        }

        #endregion

        #region Internal

        /// <summary>Khởi tạo với nơi gửi cho sẵn (settings đã đọc, hoặc test). Gọi lại là vô hại.</summary>
        internal static void Initialize(string endpoint, string projectCode)
        {
            if (_isInitialized)
                return;

            _isInitialized = true;
            BugLogRingBuffer.Attach();
            BugLoggerConfig.Configure(endpoint, projectCode);
        }

        /// <summary>Trả toàn bộ package về trạng thái chưa khởi tạo (test). Report đang chờ vẫn trên disk.</summary>
        internal static void Reset()
        {
            BugReportUploader.Reset();
            BugLoggerConfig.Reset();
            _isInitialized = false;
            ReportQueued = null;
            ReportSent = null;
            ReportFailed = null;
        }

        internal static void RaiseNotify(string message)
        {
            try
            {
                BugLoggerConfig.Notify?.Invoke(message);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        internal static void RaiseReportQueued()
        {
            try
            {
                ReportQueued?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        internal static void RaiseReportSent(int number)
        {
            try
            {
                ReportSent?.Invoke(number);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        internal static void RaiseReportFailed(string id, string reason)
        {
            try
            {
                ReportFailed?.Invoke(id, reason);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        /// <summary>Thu <paramref name="sourceWidth"/> x <paramref name="sourceHeight"/> vào trong <see cref="SCREENSHOT_MAX_LONG_EDGE"/>, giữ tỷ lệ.</summary>
        internal static void GetScaledSize(int sourceWidth, int sourceHeight, out int width, out int height)
        {
            var longEdge = Math.Max(sourceWidth, sourceHeight);
            if (longEdge <= SCREENSHOT_MAX_LONG_EDGE)
            {
                width = Math.Max(1, sourceWidth);
                height = Math.Max(1, sourceHeight);
                return;
            }

            var scale = (double)SCREENSHOT_MAX_LONG_EDGE / longEdge;
            width = Math.Min(Math.Max((int)Math.Round(sourceWidth * scale), 1), SCREENSHOT_MAX_LONG_EDGE);
            height = Math.Min(Math.Max((int)Math.Round(sourceHeight * scale), 1), SCREENSHOT_MAX_LONG_EDGE);
        }

        #endregion

        #region Private

        private static void DestroyTemporary(Object temporary)
        {
            if (temporary == null)
                return;

            if (Application.isPlaying)
                Object.Destroy(temporary);
            else
                Object.DestroyImmediate(temporary);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Domain reload có thể tắt: subscriber + cờ khởi tạo của phiên trước không sống sang phiên sau.
            _isInitialized = false;
            ReportQueued = null;
            ReportSent = null;
            ReportFailed = null;
        }

        #endregion
    }
}
