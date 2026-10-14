using System;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Ezg.BugLogger
{
    /// <summary>
    /// Danh tính build gửi trong <c>app.commitSha</c> / <c>app.branch</c> / <c>app.build</c>. Baker của
    /// Editor (<c>BugLoggerBuildInfoBaker</c>) ghi <c>StreamingAssets/BugLogger/buglogger_build.json</c>
    /// vào build có <c>EZG_QA</c>; file được đọc một lần qua <see cref="UnityWebRequest"/> vì trên Android
    /// StreamingAssets nằm trong APK (URL <c>jar:</c>). Trong Editor không có file bake, assembly Editor cấp
    /// HEAD + branch hiện tại qua <see cref="EditorResolver"/>.
    /// </summary>
    internal static class BugLoggerBuildInfo
    {
        #region Fields

        public const string FILE_NAME = "buglogger_build.json";
        public const string STREAMING_ASSETS_FOLDER = "BugLogger";
        public const string UNKNOWN = "unknown";
        public const string EDITOR_BUILD = "editor";

        private const string URL_SCHEME_SEPARATOR = "://";

        [Serializable]
        internal sealed class Data
        {
            public string commitSha;
            public string branch;
            public string build;
            public string builtAtUtc;
        }

        private static Data _data;
        private static bool _isLoading;

        /// <summary>
        /// Assembly Editor gán; trả commit + branch hiện tại. Chạy trên thread pool nên không được đụng
        /// Unity API. Cố ý sống qua các lần reset của play mode.
        /// </summary>
        internal static Func<Data> EditorResolver;

        #endregion

        #region Public

        /// <summary>Commit sha của build, hoặc <see cref="UNKNOWN"/> khi chưa/không biết.</summary>
        public static string CommitSha => OrUnknown(_data?.commitSha);

        /// <summary>Branch build ra bản này, hoặc <see cref="UNKNOWN"/>.</summary>
        public static string Branch => OrUnknown(_data?.branch);

        /// <summary>Số build của nền tảng (Android bundle version code, iOS build number), hoặc rỗng.</summary>
        public static string BuildNumber => _data?.build ?? string.Empty;

        /// <summary>Nạp build info một lần; các lần gọi sau trả ngay. Main thread.</summary>
        public static async UniTask EnsureLoadedAsync()
        {
            if (_data != null)
                return;

            if (_isLoading)
            {
                while (_isLoading)
                    await UniTask.Yield();

                return;
            }

            _isLoading = true;
            try
            {
                _data = await LoadAsync() ?? NewUnknown();
            }
            catch (Exception)
            {
                // Danh tính build là best effort: không bao giờ được chặn việc ghi report.
                await UniTask.SwitchToMainThread();
                _data = NewUnknown();
            }
            finally
            {
                _isLoading = false;
            }
        }

        #endregion

        #region Private

        private static string OrUnknown(string value) => string.IsNullOrEmpty(value) ? UNKNOWN : value;

        private static Data NewUnknown() => new Data { commitSha = UNKNOWN, branch = UNKNOWN, build = string.Empty };

        private static async UniTask<Data> LoadAsync()
        {
#if UNITY_EDITOR
            var resolver = EditorResolver;
            if (resolver == null)
                return null;

            await UniTask.SwitchToThreadPool();
            Data data;
            try
            {
                data = resolver();
            }
            catch (Exception)
            {
                data = null;
            }

            await UniTask.SwitchToMainThread();
            return data;
#else
            var path = Path.Combine(Application.streamingAssetsPath, STREAMING_ASSETS_FOLDER, FILE_NAME);
            var uri = path.Contains(URL_SCHEME_SEPARATOR) ? path : new Uri(path).AbsoluteUri;

            using (var request = UnityWebRequest.Get(uri))
            {
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                    await UniTask.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                    return null;

                try
                {
                    return JsonUtility.FromJson<Data>(request.downloadHandler.text);
                }
                catch (Exception)
                {
                    return null;
                }
            }
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _data = null;
            _isLoading = false;
        }

        #endregion
    }
}
