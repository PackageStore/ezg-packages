#if UNITY_EDITOR
using System;
using System.Text;
using UnityEditor;
using UnityEngine.Networking;

namespace Ezg.Editor.Shared.Firebase
{
    /// <summary>
    ///     Lớp HTTP mỏng cho Firebase Management API + Google OAuth. Cố ý ĐỒNG BỘ (block Editor, có
    ///     progress bar huỷ được) đúng như <c>MarketingSheetFetcher.Download</c>: đây là thao tác
    ///     bấm-rồi-chờ, và code chạy sau nó ghi file vào <c>Assets/</c> nên không được phép rơi vào
    ///     giữa một lần domain reload.
    ///     <para>
    ///         KHÔNG log body của request lấy token — body đó chứa JWT đã ký, dùng lại được trong 1 giờ.
    ///     </para>
    /// </summary>
    internal static class FirebaseRest
    {
        #region Constants

        private const int TIMEOUT_SECONDS = 30;

        /// <summary>Cắt body lỗi cho vừa hộp thoại; bản đầy đủ vẫn nằm ở Console.</summary>
        private const int ERROR_BODY_MAX = 600;

        #endregion

        #region Types

        // JsonUtility gán mấy field dưới đây qua reflection nên compiler không thấy chỗ nào assign.
#pragma warning disable CS0649

        [Serializable]
        private class GoogleErrorEnvelope
        {
            public GoogleError error;
        }

        /// <summary>
        ///     Field đặt tên khớp KEY JSON của Google (yêu cầu của <c>JsonUtility</c>) nên không theo
        ///     chuẩn `_camelCase` của runtime code.
        /// </summary>
        [Serializable]
        private class GoogleError
        {
            public int code;
            public string message;
            public string status;
        }

#pragma warning restore CS0649

        #endregion

        #region Public API

        internal static bool Get(string url, string bearer, string progress, out string body,
            out string error) =>
            Send("GET", url, bearer, null, null, progress, out body, out error);

        internal static bool PostJson(string url, string bearer, string json, string progress,
            out string body, out string error) =>
            Send("POST", url, bearer, Encoding.UTF8.GetBytes(string.IsNullOrEmpty(json) ? "{}" : json),
                "application/json", progress, out body, out error);

        /// <summary>POST form-urlencoded, không Bearer — chỉ dùng cho endpoint đổi JWT sang access token.</summary>
        internal static bool PostForm(string url, string form, string progress, out string body,
            out string error) =>
            Send("POST", url, null, Encoding.UTF8.GetBytes(form), "application/x-www-form-urlencoded",
                progress, out body, out error);

        /// <summary>
        ///     Mã lỗi HTTP đã bóc từ thông điệp của <see cref="Send" /> — dùng để phân biệt "project
        ///     chưa có" (404/403) với lỗi thật.
        /// </summary>
        internal static bool IsNotFoundOrForbidden(string error) =>
            error != null && (error.Contains("HTTP 404") || error.Contains("HTTP 403"));

        #endregion

        #region Internals

        private static bool Send(string method, string url, string bearer, byte[] payload,
            string contentType, string progress, out string body, out string error)
        {
            body = null;
            error = null;

            using var request = new UnityWebRequest(url, method);
            request.downloadHandler = new DownloadHandlerBuffer();
            if (payload != null)
            {
                request.uploadHandler = new UploadHandlerRaw(payload);
                request.SetRequestHeader("Content-Type", contentType);
            }

            if (!string.IsNullOrEmpty(bearer)) request.SetRequestHeader("Authorization", "Bearer " + bearer);
            request.timeout = TIMEOUT_SECONDS;

            var operation = request.SendWebRequest();
            try
            {
                while (!operation.isDone)
                {
                    if (!EditorUtility.DisplayCancelableProgressBar("Firebase", progress,
                            request.downloadProgress)) continue;

                    request.Abort();
                    error = "Nguoi dung huy.";
                    return false;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            body = request.downloadHandler?.text;
            if (request.result == UnityWebRequest.Result.Success) return true;

            error = $"HTTP {request.responseCode} {request.error}. {ExtractMessage(body)}";
            return false;
        }

        /// <summary>
        ///     Bóc <c>error.message</c> của Google ra — thông điệp của Google nói rõ THIẾU QUYỀN GÌ
        ///     (ví dụ "Permission firebase.clients.create denied"), giá trị hơn hẳn mã HTTP trơn.
        /// </summary>
        private static string ExtractMessage(string body)
        {
            if (string.IsNullOrEmpty(body)) return "(body rong)";

            try
            {
                var envelope = UnityEngine.JsonUtility.FromJson<GoogleErrorEnvelope>(body);
                if (!string.IsNullOrEmpty(envelope?.error?.message))
                    return $"{envelope.error.status}: {envelope.error.message}";
            }
            catch (Exception)
            {
                // Body không phải JSON (hay gặp: trang HTML của proxy/captive portal) -> trả thô bên dưới.
            }

            return body.Length <= ERROR_BODY_MAX ? body : body.Substring(0, ERROR_BODY_MAX) + "...";
        }

        #endregion
    }
}
#endif
