using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace UnityFigmaBridge.Editor.FigmaApi
{
    /// <summary>Streams one Figma API response to a file, waiting out a short rate limit.</summary>
    internal static class FigmaFileDownload
    {
        private const int RATE_LIMIT_WAITS = 3;

        /// <summary>A longer Retry-After means the seat's quota is spent; waiting would hold the import for minutes.</summary>
        private const int MAX_RETRY_AFTER_SECONDS = 60;

        /// <exception cref="FigmaApiRequestException">Status 0 when the connection or the stream dropped.</exception>
        public static async Task ToFile(string url, string accessToken, string path, string progressTitle, string progressLabel)
        {
            for (var wait = 1; ; wait++)
            {
                using var webRequest = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET,
                    new DownloadHandlerFile(path) { removeFileOnAbort = true }, null);
                webRequest.SetRequestHeader("X-Figma-Token", accessToken);
                var operation = webRequest.SendWebRequest();
                // Figma sends no length for a file response, so the bar can only report the bytes so far
                while (!operation.isDone)
                {
                    EditorUtility.DisplayProgressBar(progressTitle,
                        $"{progressLabel} ({webRequest.downloadedBytes / 1048576.0:0.0} MB)", webRequest.downloadProgress);
                    await Task.Delay(100);
                }

                if (webRequest.result == UnityWebRequest.Result.Success) return;
                var failure = FigmaApiUtils.DescribeFailure(webRequest);
                var retryAfter = RetryAfterSeconds(webRequest);
                if (retryAfter > 0 && wait <= RATE_LIMIT_WAITS)
                {
                    Debug.LogWarning($"[FigmaBridge] {progressLabel}: {failure}, waiting {retryAfter} s");
                    await Task.Delay(retryAfter * 1000);
                    continue;
                }
                // A stream reset after the headers (curl error 92) reports HTTP 200 with a non-protocol error
                var statusCode = webRequest.result == UnityWebRequest.Result.ProtocolError ? webRequest.responseCode : 0;
                throw new FigmaApiRequestException(statusCode, $"{failure}\nError downloading FIGMA document, url - {url}");
            }
        }

        /// <returns>Seconds a 429 asks to wait, or 0 when there is nothing short to wait for.</returns>
        private static int RetryAfterSeconds(UnityWebRequest request)
        {
            if (request.responseCode != 429) return 0;
            return int.TryParse(request.GetResponseHeader("retry-after"), out var seconds) && seconds <= MAX_RETRY_AFTER_SECONDS
                ? Math.Max(seconds, 1)
                : 0;
        }
    }
}
