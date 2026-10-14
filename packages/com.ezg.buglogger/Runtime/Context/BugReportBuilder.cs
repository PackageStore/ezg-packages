using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ezg.BugLogger
{
    /// <summary>
    /// Dựng report theo hai nửa: <see cref="Capture"/> đọc mọi giá trị Unity/host trên main thread lúc
    /// submit; <see cref="Build"/> (thread pool, không Unity API) ghi <c>report.json</c> schema 1 (BugHub
    /// contract §3.4) và cắt mọi phần dưới giới hạn của Worker, để Worker không bao giờ trả 400/413 vì kích
    /// thước — report không thể kẹt vĩnh viễn trong <c>failed/</c> vì lý do đó.
    /// </summary>
    internal static class BugReportBuilder
    {
        #region Fields

        public const int SCHEMA_VERSION = 1;
        public const int MAX_TITLE_LENGTH = 100;
        public const int MAX_FIELD_LENGTH = 4000;
        public const int MAX_REPRODUCE_RATE_LENGTH = 16;
        public const int MAX_TAG_IDS = 4;
        public const int MAX_MENTION_IDS = 5;
        public const int MAX_ID_LENGTH = 64;
        public const int MAX_SHORT_FIELD_LENGTH = 200;
        public const int MAX_SCREEN_LENGTH = 32;
        public const int MAX_EXTRA_ENTRIES = 32;
        public const int MAX_EXTRA_KEY_LENGTH = 64;
        public const int MAX_EXTRA_VALUE_LENGTH = 256;
        public const int MAX_SCREENSHOT_BYTES = 3 * BYTES_PER_MB;
        public const int MAX_LOG_BYTES = 1 * BYTES_PER_MB;
        public const int MAX_SAVE_BYTES = 5 * BYTES_PER_MB;

        /// <summary>
        /// Trần <c>report.json</c> của Worker (256 KiB). Các giới hạn từng trường ở trên đã giữ report dưới
        /// trần này kể cả khi mọi ký tự đều phải escape <c>\uXXXX</c> — test kiểm trường hợp xấu nhất.
        /// </summary>
        public const int MAX_REPORT_BYTES = 256 * 1024;

        public const string KIND_BUG = "bug";
        public const string KIND_FEEDBACK = "feedback";
        public const string SCENE_ENTRY = "scene";
        public const string NOTE_SCREENSHOT = "bugLogger.screenshot";
        public const string NOTE_LOG = "bugLogger.log";
        public const string NOTE_SAVE = "bugLogger.save";
        public const string NOTE_EXTRA = "bugLogger.extra";
        public const string NOTE_PLAYER_ID = "bugLogger.playerId";

        private const int BYTES_PER_MB = 1024 * 1024;

        /// <summary>U+FEFF: <c>trim()</c> của JS (Worker) bỏ ký tự này, <c>Trim()</c> của .NET thì giữ.</summary>
        private const char BYTE_ORDER_MARK = (char)0xFEFF;

        private const int LOG_LINE_ALIGN_WINDOW = 64 * 1024;
        private const int REPORT_JSON_BASE_CAPACITY = 2048;
        private const string ISO_8601_UTC = "yyyy-MM-ddTHH:mm:ss.fffZ";
        private const string SCREEN_SIZE_FORMAT = "{0}x{1}";

        #endregion

        #region Public

        /// <summary>
        /// Lỗi của caller (không phải của mạng): bug thiếu tiêu đề hoặc kết quả thực tế. Bắt ở đây để report
        /// không đi tới Worker rồi kẹt trong <c>failed/</c> vì 400.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="draft"/> null.</exception>
        /// <exception cref="ArgumentException">Bug có <c>Title</c> hoặc <c>Actual</c> trống.</exception>
        public static void Validate(BugReportDraft draft)
        {
            if (draft == null)
                throw new ArgumentNullException(nameof(draft));

            if (draft.Kind != BugReportKind.Bug)
                return;

            if (TrimForWorker(draft.Title).Length == 0)
                throw new ArgumentException("A bug report needs a non-empty Title.", nameof(draft));

            if (TrimForWorker(draft.Actual).Length == 0)
                throw new ArgumentException("A bug report needs a non-empty Actual.", nameof(draft));
        }

        /// <summary>
        /// Chụp context report trên main thread: app/device/screen/scene, player id, extra của host và
        /// snapshot log hiện tại. Exception của provider không bao giờ thoát ra — nó thành ghi chú.
        /// </summary>
        /// <param name="draft">Nội dung QA nhập (đã <see cref="Validate"/>).</param>
        /// <param name="screenshot">Byte JPG, hoặc null.</param>
        public static BugReportCapture Capture(BugReportDraft draft, byte[] screenshot)
        {
            var capture = new BugReportCapture
            {
                Id = NewReportId(),
                Kind = draft.Kind,
                Title = draft.Title,
                FeatureKey = draft.FeatureKey ?? string.Empty,
                Category = draft.Category,
                ReproduceRate = draft.ReproduceRate,
                Actual = draft.Actual,
                Expected = draft.Expected,
                Steps = draft.Steps,
                Description = draft.Description,
                TagIds = NormalizeIds(draft.TagIds, MAX_TAG_IDS),
                MentionIds = NormalizeIds(draft.MentionIds, MAX_MENTION_IDS),
                CreatedAtUtc = BugLoggerClock.UtcNow.ToString(ISO_8601_UTC, CultureInfo.InvariantCulture),
                AppVersion = Application.version,
                BuildNumber = string.Empty,
                CommitSha = BugLoggerBuildInfo.UNKNOWN,
                Branch = BugLoggerBuildInfo.UNKNOWN,
                Platform = Application.platform.ToString(),
                UnityVersion = Application.unityVersion,
                DeviceId = SystemInfo.deviceUniqueIdentifier,
                DeviceModel = SystemInfo.deviceModel,
                OperatingSystem = SystemInfo.operatingSystem,
                RamMb = Math.Max(0, SystemInfo.systemMemorySize),
                Screen = string.Format(CultureInfo.InvariantCulture, SCREEN_SIZE_FORMAT, UnityEngine.Screen.width, UnityEngine.Screen.height),
                Scene = SceneManager.GetActiveScene().name,
                LogLines = BugLogRingBuffer.Shared?.TakeSnapshot(),
                Screenshot = screenshot,
                Extra = new List<KeyValuePair<string, string>>(),
                Notes = new List<KeyValuePair<string, string>>(),
            };

            try
            {
                capture.PlayerId = BugLoggerConfig.PlayerIdProvider?.Invoke() ?? string.Empty;
            }
            catch (Exception ex)
            {
                capture.PlayerId = string.Empty;
                capture.Notes.Add(new KeyValuePair<string, string>(NOTE_PLAYER_ID, $"provider threw {ex.GetType().Name}"));
                Debug.LogWarning($"[BugLogger] PlayerIdProvider threw {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                var extra = BugLoggerConfig.ExtraContextProvider?.Invoke();
                if (extra != null)
                {
                    foreach (var pair in extra)
                        capture.Extra.Add(pair);
                }
            }
            catch (Exception ex)
            {
                capture.Notes.Add(new KeyValuePair<string, string>(NOTE_EXTRA, $"provider threw {ex.GetType().Name}"));
                Debug.LogWarning($"[BugLogger] ExtraContextProvider threw {ex.GetType().Name}: {ex.Message}");
            }

            return capture;
        }

        /// <summary>
        /// Dựng các file outbox của <paramref name="capture"/>. An toàn trên thread pool (không Unity API).
        /// Screenshot trên 3 MiB và save trên 5 MiB bị bỏ, log cắt còn 1 MiB cuối — mỗi việc ghi chú vào
        /// <c>extra</c>; chuỗi bị cắt theo giới hạn từng trường của contract.
        /// </summary>
        /// <param name="capture">Context chụp lúc submit.</param>
        /// <param name="saveJson">Save snapshot của host, hoặc null.</param>
        public static BugReportFiles Build(BugReportCapture capture, string saveJson)
        {
            var notes = capture.Notes != null
                ? new List<KeyValuePair<string, string>>(capture.Notes)
                : new List<KeyValuePair<string, string>>();

            var screenshot = capture.Screenshot;
            if (screenshot != null && screenshot.Length > MAX_SCREENSHOT_BYTES)
            {
                notes.Add(new KeyValuePair<string, string>(NOTE_SCREENSHOT, $"dropped: {screenshot.Length} bytes > {MAX_SCREENSHOT_BYTES}"));
                screenshot = null;
            }

            if (screenshot != null && screenshot.Length == 0)
                screenshot = null;

            byte[] log = null;
            var logText = BugLogRingBuffer.Format(capture.LogLines);
            if (logText.Length > 0)
            {
                log = Encoding.UTF8.GetBytes(logText);
                if (log.Length > MAX_LOG_BYTES)
                {
                    log = ClipUtf8Tail(log, MAX_LOG_BYTES);
                    notes.Add(new KeyValuePair<string, string>(NOTE_LOG, $"clipped to the last {MAX_LOG_BYTES} bytes"));
                }
            }

            byte[] save = null;
            if (!string.IsNullOrEmpty(saveJson))
            {
                var saveBytes = Encoding.UTF8.GetByteCount(saveJson);
                if (saveBytes > MAX_SAVE_BYTES)
                    notes.Add(new KeyValuePair<string, string>(NOTE_SAVE, $"dropped: {saveBytes} bytes > {MAX_SAVE_BYTES}"));
                else
                    save = Encoding.UTF8.GetBytes(saveJson);
            }

            var extra = BuildExtra(capture, notes);
            return new BugReportFiles
            {
                Id = capture.Id,
                Report = Encoding.UTF8.GetBytes(WriteReport(capture, extra)),
                Screenshot = screenshot,
                Log = log,
                Save = save,
            };
        }

        /// <summary>UUID RFC 4122 version 4 viết thường (Worker từ chối mọi dạng khác).</summary>
        public static string NewReportId()
        {
            var bytes = Guid.NewGuid().ToByteArray();
            bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
            return new Guid(bytes).ToString("D");
        }

        /// <summary>Cắt <paramref name="value"/> còn <paramref name="maxLength"/> đơn vị UTF-16, không tách đôi surrogate.</summary>
        public static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (value.Length <= maxLength)
                return value;

            var cut = maxLength;
            if (cut > 0 && char.IsHighSurrogate(value[cut - 1]))
                cut--;

            return value.Substring(0, cut);
        }

        /// <summary>
        /// Bỏ khoảng trắng hai đầu đúng như <c>trim()</c> của Worker (kể cả U+FEFF mà <c>Trim()</c> của .NET
        /// giữ lại). Null thành rỗng.
        /// </summary>
        public static string TrimForWorker(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var start = 0;
            var end = value.Length - 1;
            while (start <= end && IsWorkerWhiteSpace(value[start]))
                start++;

            while (end >= start && IsWorkerWhiteSpace(value[end]))
                end--;

            return value.Substring(start, end - start + 1);
        }

        /// <summary>
        /// Giữ <paramref name="maxBytes"/> byte cuối của text UTF-8, bắt đầu ở ranh giới ký tự (và ranh giới
        /// dòng nếu có dòng ở gần).
        /// </summary>
        public static byte[] ClipUtf8Tail(byte[] utf8, int maxBytes)
        {
            if (utf8.Length <= maxBytes)
                return utf8;

            var start = utf8.Length - maxBytes;
            while (start < utf8.Length && (utf8[start] & 0xC0) == 0x80)
                start++;

            var alignLimit = Math.Min(utf8.Length, start + LOG_LINE_ALIGN_WINDOW);
            for (var i = start; i < alignLimit; i++)
            {
                if (utf8[i] != (byte)'\n')
                    continue;

                start = i + 1;
                break;
            }

            var result = new byte[utf8.Length - start];
            Buffer.BlockCopy(utf8, start, result, 0, result.Length);
            return result;
        }

        #endregion

        #region Private

        private static bool IsWorkerWhiteSpace(char c) => char.IsWhiteSpace(c) || c == BYTE_ORDER_MARK;

        /// <summary>Bỏ id trống/trùng, cắt độ dài từng id, giữ tối đa <paramref name="max"/> id.</summary>
        private static string[] NormalizeIds(IReadOnlyList<string> ids, int max)
        {
            if (ids == null || ids.Count == 0)
                return Array.Empty<string>();

            var result = new List<string>(Math.Min(ids.Count, max));
            foreach (var raw in ids)
            {
                if (result.Count >= max)
                    break;

                var id = Truncate(TrimForWorker(raw), MAX_ID_LENGTH);
                if (id.Length > 0 && !result.Contains(id))
                    result.Add(id);
            }

            return result.ToArray();
        }

        private static string ClipTitle(string title) => TrimForWorker(Truncate(TrimForWorker(title), MAX_TITLE_LENGTH));

        /// <summary>Ghi chú của package trước, rồi entry của host, cuối cùng là scene — tất cả trong giới hạn map của Worker.</summary>
        private static List<KeyValuePair<string, string>> BuildExtra(BugReportCapture capture, List<KeyValuePair<string, string>> notes)
        {
            var result = new List<KeyValuePair<string, string>>(MAX_EXTRA_ENTRIES);
            var keys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var note in notes)
                TryAddEntry(result, keys, note.Key, note.Value);

            var hostEntries = new List<KeyValuePair<string, string>>(capture.Extra?.Count ?? 0);
            if (capture.Extra != null)
            {
                var hostKeys = new HashSet<string>(keys, StringComparer.Ordinal);
                foreach (var pair in capture.Extra)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key))
                        continue;

                    if (hostKeys.Add(Truncate(pair.Key, MAX_EXTRA_KEY_LENGTH)))
                        hostEntries.Add(pair);
                }
            }

            var room = MAX_EXTRA_ENTRIES - result.Count;
            var kept = hostEntries.Count <= room ? hostEntries.Count : Math.Max(0, room - 1);
            for (var i = 0; i < kept; i++)
                TryAddEntry(result, keys, hostEntries[i].Key, hostEntries[i].Value);

            if (kept < hostEntries.Count)
                TryAddEntry(result, keys, NOTE_EXTRA, $"{hostEntries.Count - kept} entries dropped (limit {MAX_EXTRA_ENTRIES})");

            if (result.Count < MAX_EXTRA_ENTRIES && !string.IsNullOrEmpty(capture.Scene))
                TryAddEntry(result, keys, SCENE_ENTRY, capture.Scene);

            return result;
        }

        private static void TryAddEntry(List<KeyValuePair<string, string>> entries, HashSet<string> keys, string key, string value)
        {
            if (entries.Count >= MAX_EXTRA_ENTRIES || string.IsNullOrWhiteSpace(key))
                return;

            key = Truncate(key, MAX_EXTRA_KEY_LENGTH);
            if (!keys.Add(key))
                return;

            entries.Add(new KeyValuePair<string, string>(key, Truncate(value, MAX_EXTRA_VALUE_LENGTH)));
        }

        private static string WriteReport(BugReportCapture capture, List<KeyValuePair<string, string>> extra)
        {
            var writer = new BugJsonWriter(REPORT_JSON_BASE_CAPACITY);

            writer.BeginObject()
                .Property("schema", SCHEMA_VERSION)
                .Property("id", capture.Id)
                .Property("kind", capture.Kind == BugReportKind.Feedback ? KIND_FEEDBACK : KIND_BUG)
                .Property("title", ClipTitle(capture.Title))
                .Property("featureKey", Truncate(capture.FeatureKey, MAX_SHORT_FIELD_LENGTH))
                .Property("category", Truncate(capture.Category, MAX_SHORT_FIELD_LENGTH))
                .Property("reproduceRate", Truncate(capture.ReproduceRate, MAX_REPRODUCE_RATE_LENGTH))
                .Property("actual", Truncate(capture.Actual, MAX_FIELD_LENGTH))
                .Property("expected", Truncate(capture.Expected, MAX_FIELD_LENGTH))
                .Property("steps", Truncate(capture.Steps, MAX_FIELD_LENGTH))
                .Property("description", Truncate(capture.Description, MAX_FIELD_LENGTH))
                .Property("tagIds", capture.TagIds)
                .Property("mentionIds", capture.MentionIds)
                .Property("createdAtUtc", capture.CreatedAtUtc);

            writer.BeginObject("app")
                .Property("version", Truncate(capture.AppVersion, MAX_SHORT_FIELD_LENGTH))
                .Property("build", Truncate(capture.BuildNumber, MAX_SHORT_FIELD_LENGTH))
                .Property("commitSha", Truncate(capture.CommitSha, MAX_SHORT_FIELD_LENGTH))
                .Property("branch", Truncate(capture.Branch, MAX_SHORT_FIELD_LENGTH))
                .Property("platform", Truncate(capture.Platform, MAX_SHORT_FIELD_LENGTH))
                .Property("unityVersion", Truncate(capture.UnityVersion, MAX_SHORT_FIELD_LENGTH))
                .EndObject();

            writer.BeginObject("device")
                .Property("id", Truncate(capture.DeviceId, MAX_SHORT_FIELD_LENGTH))
                .Property("model", Truncate(capture.DeviceModel, MAX_SHORT_FIELD_LENGTH))
                .Property("os", Truncate(capture.OperatingSystem, MAX_SHORT_FIELD_LENGTH))
                .Property("ramMb", Math.Max(0, capture.RamMb))
                .Property("screen", Truncate(capture.Screen, MAX_SCREEN_LENGTH))
                .EndObject();

            writer.BeginObject("player")
                .Property("id", Truncate(capture.PlayerId, MAX_SHORT_FIELD_LENGTH))
                .EndObject();

            writer.BeginObject("extra");
            foreach (var pair in extra)
                writer.Property(pair.Key, pair.Value);

            writer.EndObject();
            return writer.EndObject().ToString();
        }

        #endregion
    }
}
