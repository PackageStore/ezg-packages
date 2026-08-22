// EZG Feature Hub — tải file/text trong Editor không cần coroutine/UniTask.
// Dùng UnityWebRequest + poll qua EditorApplication.update để giữ async-friendly trong editor.
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Ezg.FeatureHub.Editor
{
    public static class EditorDownloader
    {
        #region Public Methods

        /// <summary>Tải nội dung text (JSON nhỏ) về bộ nhớ.</summary>
        public static void DownloadText(string url, Action<bool, string, string> onDone)
        {
            var request = CreateRequest(url);
            Poll(request, request.SendWebRequest(), null, () =>
            {
                bool ok = IsSuccess(request);
                string text = ok ? request.downloadHandler.text : null;
                string error = ok ? null : DescribeError(request);
                request.Dispose();
                onDone?.Invoke(ok, text, error);
            });
        }

        /// <summary>Tải file (stream thẳng ra đĩa, hợp với .unitypackage lớn).</summary>
        public static void DownloadToFile(
            string url,
            string destPath,
            Action<float> onProgress,
            Action<bool, string> onDone)
        {
            try
            {
                string dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch (Exception e)
            {
                onDone?.Invoke(false, $"Không tạo được thư mục đích: {e.Message}");
                return;
            }

            var request = CreateRequest(url);
            request.downloadHandler = new DownloadHandlerFile(destPath) { removeFileOnAbort = true };

            Poll(request, request.SendWebRequest(), onProgress, () =>
            {
                bool ok = IsSuccess(request);
                string error = ok ? null : DescribeError(request);
                request.Dispose();
                onDone?.Invoke(ok, error);
            });
        }

        #endregion

        #region Private Methods

        private static void Poll(
            UnityWebRequest request,
            UnityWebRequestAsyncOperation op,
            Action<float> onProgress,
            Action onComplete)
        {
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                onProgress?.Invoke(request.downloadProgress);
                if (!op.isDone)
                    return;

                EditorApplication.update -= tick;
                onComplete?.Invoke();
            };
            EditorApplication.update += tick;
        }

        // Chỉ request tới gateway của EZG mới được mang token; file bên thứ ba (Firebase trên
        // dl.google.com, package git...) phải đi tay không.
        private static UnityWebRequest CreateRequest(string url)
        {
            var request = UnityWebRequest.Get(url);

            if (EzgAuth.IsGatewayUrl(url))
            {
                string token = EzgAuth.Token;
                if (!string.IsNullOrEmpty(token))
                    request.SetRequestHeader("Authorization", "Bearer " + token);
            }

            return request;
        }

        // 401/403 từ gateway là "chưa đăng nhập", không phải lỗi mạng — nói thẳng cách xử lý thay vì
        // để lộ ra một dòng "HTTP/1.1 401 Unauthorized" khó hiểu.
        private static string DescribeError(UnityWebRequest request)
        {
            if (request.responseCode == 401 || request.responseCode == 403)
                return EzgAuth.SignInHint;

            return request.error;
        }

        private static bool IsSuccess(UnityWebRequest request)
        {
            return request.result == UnityWebRequest.Result.Success;
        }

        #endregion
    }
}
