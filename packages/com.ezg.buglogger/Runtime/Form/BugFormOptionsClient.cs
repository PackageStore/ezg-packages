using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Ezg.BugLogger
{
    /// <summary>
    /// Tải lựa chọn form (BugHub contract §3.3): <c>GET {endpoint}/v1/projects/{code}/form</c>, timeout 15 s,
    /// không theo redirect. Thành công → ghi cache <c>&lt;root&gt;/form/&lt;CODE&gt;.json</c>; lỗi → đọc cache;
    /// không có cache → bộ mặc định. Giới hạn server trả được kẹp không vượt giới hạn mà bộ dựng report áp,
    /// để host validate đúng thứ sẽ được gửi.
    /// </summary>
    internal static class BugFormOptionsClient
    {
        #region Fields

        public const int TIMEOUT_SECONDS = 15;
        public const string FORM_FOLDER = "form";
        public const int DEFAULT_SCREENSHOT_MAX_BYTES = 3 * 1024 * 1024;

        private const string ROUTE_PREFIX = "/v1/projects/";
        private const string ROUTE_SUFFIX = "/form";
        private const string CACHE_EXTENSION = ".json";
        private const string TEMP_SUFFIX = ".tmp";
        private const char INVALID_FILE_CHAR_REPLACEMENT = '_';
        private const long HTTP_OK = 200;

        /// <summary>Category mặc định khi body form không có <c>categories</c> (contract §3.3).</summary>
        public static readonly IReadOnlyList<string> DefaultCategories = new[] { "Visual", "Logic", "GD" };

        [Serializable]
        private sealed class FormDto
        {
            public int schema;
            public string code;
            public bool intakeEnabled;
            public string[] categories;
            public OptionDto[] tags;
            public OptionDto[] people;
            public LimitsDto limits;
        }

        [Serializable]
        private sealed class OptionDto
        {
            public string id;
            public string name;
        }

        [Serializable]
        private sealed class LimitsDto
        {
            public int titleMax;
            public int fieldMax;
            public int maxTags;
            public int maxMentions;
            public int screenshotMaxBytes;
        }

        #endregion

        #region Public

        /// <summary>
        /// Tải lựa chọn form cho <paramref name="target"/>; không bao giờ ném vì mạng/disk. Request chạy trên
        /// main thread, đọc/ghi cache trên thread pool.
        /// </summary>
        /// <param name="target">Endpoint + mã project.</param>
        /// <param name="rootDirectory">Folder gốc của Bug Logger (chứa <c>form/</c>).</param>
        /// <param name="cancellationToken">Huỷ request đang chạy (vẫn trả cache/mặc định).</param>
        public static async UniTask<BugFormOptions> FetchAsync(BugReportTarget target, string rootDirectory, CancellationToken cancellationToken)
        {
            string body = null;
            if (target.IsConfigured)
                body = await DownloadAsync(BuildUrl(target.Endpoint, target.ProjectCode), cancellationToken);

            var cachePath = GetCachePath(rootDirectory, target.ProjectCode);
            await UniTask.SwitchToThreadPool();
            BugFormOptions result;
            try
            {
                result = Resolve(body, cachePath, target.ProjectCode);
            }
            catch (Exception)
            {
                result = CreateDefault(target.ProjectCode);
            }

            await UniTask.SwitchToMainThread();
            return result;
        }

        /// <summary>
        /// Chọn bộ lựa chọn từ body server (null = tải lỗi), rồi cache, rồi mặc định. Ghi cache khi body hợp
        /// lệ. Thuần IO — không Unity API, dùng được trên thread pool và trong EditMode test.
        /// </summary>
        public static BugFormOptions Resolve(string serverBody, string cachePath, string projectCode)
        {
            var fresh = Parse(serverBody, BugFormOptionsSource.Server);
            if (fresh != null)
            {
                TryWriteCache(cachePath, serverBody);
                return fresh;
            }

            return Parse(TryReadCache(cachePath), BugFormOptionsSource.Cache) ?? CreateDefault(projectCode);
        }

        /// <summary>Đọc JSON form; null khi trống, sai dạng, hoặc không phải body form (thiếu <c>schema</c>).</summary>
        public static BugFormOptions Parse(string json, BugFormOptionsSource source)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            FormDto dto;
            try
            {
                dto = JsonUtility.FromJson<FormDto>(json);
            }
            catch (Exception)
            {
                return null;
            }

            if (dto == null || dto.schema < 1)
                return null;

            var categories = dto.categories == null ? DefaultCategories : NormalizeCategories(dto.categories);
            return new BugFormOptions(dto.schema, dto.code, dto.intakeEnabled, categories,
                NormalizeOptions(dto.tags), NormalizeOptions(dto.people), NormalizeLimits(dto.limits), source);
        }

        /// <summary>Bộ mặc định của contract: category mặc định, không tag, không người, giới hạn §3.4.</summary>
        public static BugFormOptions CreateDefault(string projectCode)
        {
            return new BugFormOptions(0, projectCode, true, DefaultCategories,
                Array.Empty<BugFormOption>(), Array.Empty<BugFormOption>(), NormalizeLimits(null), BugFormOptionsSource.Default);
        }

        /// <summary>Đường dẫn file cache của <paramref name="projectCode"/> (viết hoa); null khi thiếu mã hoặc folder gốc.</summary>
        public static string GetCachePath(string rootDirectory, string projectCode)
        {
            var code = projectCode?.Trim();
            if (string.IsNullOrEmpty(rootDirectory) || string.IsNullOrEmpty(code))
                return null;

            var name = new StringBuilder(code.Length);
            foreach (var c in code.ToUpperInvariant())
                name.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : INVALID_FILE_CHAR_REPLACEMENT);

            return Path.Combine(rootDirectory, FORM_FOLDER, name + CACHE_EXTENSION);
        }

        /// <summary>URL của route form; mã project được escape.</summary>
        public static string BuildUrl(string endpoint, string projectCode)
        {
            return (endpoint ?? string.Empty).TrimEnd('/') + ROUTE_PREFIX + Uri.EscapeDataString(projectCode ?? string.Empty) + ROUTE_SUFFIX;
        }

        #endregion

        #region Private

        private static async UniTask<string> DownloadAsync(string url, CancellationToken cancellationToken)
        {
            UnityWebRequest request = null;
            try
            {
                request = UnityWebRequest.Get(url);
                request.timeout = TIMEOUT_SECONDS;
                request.redirectLimit = 0;
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        request.Abort();
                        return null;
                    }

                    await UniTask.Yield();
                }

                return request.result == UnityWebRequest.Result.Success && request.responseCode == HTTP_OK
                    ? request.downloadHandler?.text
                    : null;
            }
            catch (Exception)
            {
                // URL sai dạng / http bị chặn: coi như offline, dùng cache.
                return null;
            }
            finally
            {
                request?.Dispose();
            }
        }

        private static IReadOnlyList<string> NormalizeCategories(string[] categories)
        {
            var result = new List<string>(categories.Length);
            foreach (var raw in categories)
            {
                var category = raw?.Trim();
                if (!string.IsNullOrEmpty(category) && !result.Contains(category))
                    result.Add(category);
            }

            return result;
        }

        private static IReadOnlyList<BugFormOption> NormalizeOptions(OptionDto[] options)
        {
            if (options == null || options.Length == 0)
                return Array.Empty<BugFormOption>();

            var result = new List<BugFormOption>(options.Length);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var option in options)
            {
                var id = option?.id?.Trim();
                if (string.IsNullOrEmpty(id) || !ids.Add(id))
                    continue;

                var name = option.name?.Trim();
                result.Add(new BugFormOption(id, string.IsNullOrEmpty(name) ? id : name));
            }

            return result;
        }

        private static BugFormLimits NormalizeLimits(LimitsDto limits)
        {
            return new BugFormLimits(
                Clamp(limits?.titleMax ?? 0, BugReportBuilder.MAX_TITLE_LENGTH),
                Clamp(limits?.fieldMax ?? 0, BugReportBuilder.MAX_FIELD_LENGTH),
                Clamp(limits?.maxTags ?? 0, BugReportBuilder.MAX_TAG_IDS),
                Clamp(limits?.maxMentions ?? 0, BugReportBuilder.MAX_MENTION_IDS),
                Clamp(limits?.screenshotMaxBytes ?? 0, DEFAULT_SCREENSHOT_MAX_BYTES));
        }

        /// <summary>Thiếu/không dương → <paramref name="cap"/>; lớn hơn cap → cap.</summary>
        private static int Clamp(int value, int cap) => value > 0 ? Math.Min(value, cap) : cap;

        private static string TryReadCache(string cachePath)
        {
            if (string.IsNullOrEmpty(cachePath))
                return null;

            try
            {
                // App bị kill giữa lúc xoá bản cũ và rename bản mới: bản mới vẫn còn nguyên trong *.tmp.
                var path = File.Exists(cachePath) ? cachePath : cachePath + TEMP_SUFFIX;
                return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void TryWriteCache(string cachePath, string body)
        {
            if (string.IsNullOrEmpty(cachePath))
                return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                var temp = cachePath + TEMP_SUFFIX;
                File.WriteAllBytes(temp, Encoding.UTF8.GetBytes(body));
                if (File.Exists(cachePath))
                    File.Delete(cachePath);

                File.Move(temp, cachePath);
            }
            catch (Exception)
            {
                // Cache là best effort: lần sau tải lại.
            }
        }

        #endregion
    }
}
