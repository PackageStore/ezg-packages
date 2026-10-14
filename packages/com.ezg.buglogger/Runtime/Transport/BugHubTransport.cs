using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Ezg.BugLogger
{
    /// <summary>
    /// Transport mặc định (BugHub contract §3.2): <c>POST {Endpoint}/v1/reports</c> dạng
    /// <c>multipart/form-data</c> (part <c>report</c>, <c>screenshot</c>, <c>log</c>, <c>save</c>), header
    /// duy nhất là <c>X-BugHub-Project</c>. Không có key/secret nào đi kèm.
    /// </summary>
    public sealed class BugHubTransport : IBugReportTransport
    {
        #region Fields

        /// <summary>Timeout của cả request, tính bằng giây.</summary>
        public const int TIMEOUT_SECONDS = 15;

        /// <summary>Route nối sau <see cref="BugReportTarget.Endpoint"/>.</summary>
        public const string REPORTS_ROUTE = "/v1/reports";

        /// <summary>Header mang mã project.</summary>
        public const string HEADER_PROJECT = "X-BugHub-Project";

        /// <summary>Trần thời gian chờ nhận từ <c>Retry-After</c>, để một header lạ không treo hàng đợi cả ngày.</summary>
        public const int MAX_RETRY_AFTER_SECONDS = 3600;

        private const string HEADER_RETRY_AFTER = "Retry-After";
        private const string REPORT_PART = "report";
        private const string REPORT_FILE_NAME = "report.json";
        private const string SCREENSHOT_PART = "screenshot";
        private const string SCREENSHOT_MIME_TYPE = "image/jpeg";
        private const string LOG_PART = "log";
        private const string SAVE_PART = "save";
        private const string JSON_MIME_TYPE = "application/json";
        private const string TEXT_MIME_TYPE = "text/plain";
        private const int HTTP_ACCEPTED = 202;
        private const int HTTP_FORBIDDEN = 403;
        private const int HTTP_TOO_MANY_REQUESTS = 429;
        private const int MAX_ERROR_CODE_LENGTH = 64;

        [Serializable]
        private sealed class WorkerResponse
        {
            public int number;
            public string url;
            public bool duplicate;
            public string error;
            public string field;
            public string part;
        }

        #endregion

        #region Public

        /// <inheritdoc />
        public async UniTask<BugReportSendResult> SendAsync(BugReportPayload payload, BugReportTarget target, CancellationToken cancellationToken)
        {
            UnityWebRequest request = null;
            UnityWebRequestAsyncOperation operation;
            try
            {
                request = UnityWebRequest.Post(BuildUrl(target.Endpoint), BuildSections(payload));
                request.SetRequestHeader(HEADER_PROJECT, target.ProjectCode);
                request.timeout = TIMEOUT_SECONDS;
                // Worker không bao giờ redirect; redirect nghĩa là endpoint trỏ nhầm chỗ.
                request.redirectLimit = 0;
                operation = request.SendWebRequest();
            }
            catch (Exception ex)
            {
                // Lỗi cấu hình local (endpoint sai dạng, header lạ, http bị player settings chặn), không
                // phải report hỏng: giữ hàng đợi, backoff thử lại.
                request?.Dispose();
                return new BugReportSendResult(BugReportSendStatus.Transient, 0, 0, null,
                    $"request not sent: invalid configuration ({ex.GetType().Name})");
            }

            using (request)
            {
                while (!operation.isDone)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        request.Abort();
                        return BugReportSendResult.NetworkError("cancelled");
                    }

                    await UniTask.Yield();
                }

                var isNetworkError = request.result == UnityWebRequest.Result.ConnectionError ||
                                     request.result == UnityWebRequest.Result.DataProcessingError;
                return Classify(request.responseCode, isNetworkError, request.downloadHandler?.text, request.error,
                    request.GetResponseHeader(HEADER_RETRY_AFTER));
            }
        }

        /// <summary>
        /// Ánh xạ một kết quả HTTP sang việc uploader làm tiếp (BugHub contract §3.2): 2xx → đã nhận (trùng
        /// khi body nói vậy); 403 (server chỉ trả 403 khi <c>intake_disabled</c> — admin có thể bật lại), 429,
        /// 5xx, không có response → tạm; 4xx còn lại (400/404/413…) → vĩnh viễn. Hàm thuần — không Unity API.
        /// </summary>
        /// <param name="statusCode">HTTP status; 0 khi không có response.</param>
        /// <param name="isNetworkError">True với lỗi kết nối và timeout.</param>
        /// <param name="body">Body response (Worker luôn trả JSON), có thể null.</param>
        /// <param name="networkError">Mô tả lỗi mạng, có thể null.</param>
        /// <param name="retryAfter">Giá trị header <c>Retry-After</c>, có thể null.</param>
        /// <returns>Kết quả đã phân loại.</returns>
        public static BugReportSendResult Classify(long statusCode, bool isNetworkError, string body, string networkError = null, string retryAfter = null)
        {
            if (isNetworkError || statusCode <= 0)
            {
                var reason = string.IsNullOrEmpty(networkError) ? "no response" : networkError;
                return new BugReportSendResult(BugReportSendStatus.Transient, statusCode, 0, null, $"network: {reason}");
            }

            var response = ParseResponse(body);

            if (statusCode >= 200 && statusCode < 300)
            {
                if (statusCode == HTTP_ACCEPTED)
                    return new BugReportSendResult(BugReportSendStatus.Success, statusCode, 0, null, "accepted");

                var isDuplicate = response != null && response.duplicate;
                return new BugReportSendResult(
                    isDuplicate ? BugReportSendStatus.Duplicate : BugReportSendStatus.Success,
                    statusCode, response?.number ?? 0, response?.url, isDuplicate ? "duplicate" : "created");
            }

            if (statusCode == HTTP_FORBIDDEN || statusCode == HTTP_TOO_MANY_REQUESTS || statusCode >= 500)
            {
                var wait = ParseRetryAfterSeconds(retryAfter, BugLoggerClock.UtcNow);
                return new BugReportSendResult(BugReportSendStatus.Transient, statusCode, 0, null, DescribeError(statusCode, response), wait);
            }

            if (statusCode >= 400)
                return new BugReportSendResult(BugReportSendStatus.Permanent, statusCode, 0, null, DescribeError(statusCode, response));

            // 1xx / 3xx (không theo redirect): endpoint trỏ nhầm, không phải report hỏng — giữ lại.
            return new BugReportSendResult(BugReportSendStatus.Transient, statusCode, 0, null, DescribeError(statusCode, response));
        }

        /// <summary>
        /// Đọc <c>Retry-After</c>: số giây, hoặc HTTP-date (tính từ <paramref name="nowUtc"/>). Kết quả kẹp trong
        /// [0, <see cref="MAX_RETRY_AFTER_SECONDS"/>]; header trống/sai dạng → 0.
        /// </summary>
        public static int ParseRetryAfterSeconds(string header, DateTime nowUtc)
        {
            if (string.IsNullOrWhiteSpace(header))
                return 0;

            var value = header.Trim();
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                return (int)Math.Min(Math.Max(seconds, 0), MAX_RETRY_AFTER_SECONDS);

            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date))
                return 0;

            var delta = Math.Ceiling((date - nowUtc).TotalSeconds);
            return (int)Math.Min(Math.Max(delta, 0), MAX_RETRY_AFTER_SECONDS);
        }

        /// <summary>Nối endpoint với <see cref="REPORTS_ROUTE"/>, không lặp dấu <c>/</c>.</summary>
        public static string BuildUrl(string endpoint)
        {
            return (endpoint ?? string.Empty).TrimEnd('/') + REPORTS_ROUTE;
        }

        #endregion

        #region Private

        private static List<IMultipartFormSection> BuildSections(BugReportPayload payload)
        {
            var sections = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection(REPORT_PART, payload.Report, REPORT_FILE_NAME, JSON_MIME_TYPE)
            };

            // Unity từ chối section file rỗng, Worker cũng bỏ qua part rỗng.
            if (payload.Screenshot != null && payload.Screenshot.Length > 0)
                sections.Add(new MultipartFormFileSection(SCREENSHOT_PART, payload.Screenshot, BugReportOutbox.SCREENSHOT_FILE, SCREENSHOT_MIME_TYPE));

            if (payload.Log != null && payload.Log.Length > 0)
                sections.Add(new MultipartFormFileSection(LOG_PART, payload.Log, BugReportOutbox.LOG_FILE, TEXT_MIME_TYPE));

            if (payload.Save != null && payload.Save.Length > 0)
                sections.Add(new MultipartFormFileSection(SAVE_PART, payload.Save, BugReportOutbox.SAVE_FILE, JSON_MIME_TYPE));

            return sections;
        }

        private static WorkerResponse ParseResponse(string body)
        {
            if (string.IsNullOrEmpty(body))
                return null;

            try
            {
                return JsonUtility.FromJson<WorkerResponse>(body);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string DescribeError(long statusCode, WorkerResponse response)
        {
            if (response == null || string.IsNullOrEmpty(response.error))
                return $"HTTP {statusCode}";

            var error = Clip(response.error);
            var detail = !string.IsNullOrEmpty(response.field) ? response.field : response.part;
            return string.IsNullOrEmpty(detail)
                ? $"HTTP {statusCode} {error}"
                : $"HTTP {statusCode} {error}/{Clip(detail)}";
        }

        private static string Clip(string value)
        {
            return value.Length <= MAX_ERROR_CODE_LENGTH ? value : value.Substring(0, MAX_ERROR_CODE_LENGTH);
        }

        #endregion
    }
}
