using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ezg.BugLogger
{
    /// <summary>Một folder report sẵn sàng gửi (đã có <c>report.json</c>).</summary>
    internal readonly struct BugPendingReport
    {
        public readonly string Id;
        public readonly DateTime QueuedAtUtc;
        public readonly long Bytes;

        public BugPendingReport(string id, DateTime queuedAtUtc, long bytes)
        {
            Id = id;
            QueuedAtUtc = queuedAtUtc;
            Bytes = bytes;
        }
    }

    /// <summary>
    /// Hàng đợi report trên disk: <c>&lt;root&gt;/outbox/&lt;id&gt;/</c> chứa <c>report.json</c> (+
    /// <c>screenshot.jpg</c>, <c>log.txt</c>, <c>save.json</c>, <c>attempt.json</c>). Mọi file ghi ra
    /// <c>*.tmp</c> rồi rename, <c>report.json</c> ghi cuối cùng, nên folder "sẵn sàng" đúng khi có
    /// <c>report.json</c> — crash giữa chừng để lại folder dở bị bỏ qua rồi dọn sau. Report bị từ chối hoặc
    /// bỏ cuộc chuyển sang <c>&lt;root&gt;/failed/&lt;id&gt;/</c>. Trần: <see cref="MAX_REPORTS"/> report /
    /// <see cref="MAX_TOTAL_BYTES"/>, bỏ cái cũ nhất trước. Không dùng PlayerPrefs hay save game, nên reset
    /// save không xoá được report.
    /// Method instance làm IO, dành cho thread pool; member static là facade main thread của uploader và
    /// <see cref="BugLogger"/>.
    /// </summary>
    internal sealed class BugReportOutbox
    {
        #region Fields

        public const string ROOT_FOLDER = "BugLogger";
        public const string OUTBOX_FOLDER = "outbox";
        public const string FAILED_FOLDER = "failed";
        public const string REPORT_FILE = "report.json";
        public const string SCREENSHOT_FILE = "screenshot.jpg";
        public const string LOG_FILE = "log.txt";
        public const string SAVE_FILE = "save.json";
        public const string ATTEMPT_FILE = "attempt.json";
        public const string FAILURE_FILE = "failure.json";
        public const int MAX_REPORTS = 50;
        public const long MAX_TOTAL_BYTES = 50L * 1024 * 1024;

        /// <summary>Số đếm công bố khi chưa quét lần nào.</summary>
        public const int UNKNOWN_COUNT = -1;

        private const string TEMP_SUFFIX = ".tmp";
        private const string ROUND_TRIP_FORMAT = "o";
        private const double STALE_INCOMPLETE_MINUTES = 10;
        private const int MAX_REASON_LENGTH = 500;
        private const int SAVE_SNAPSHOT_TIMEOUT_SECONDS = 10;

        private static readonly Comparison<BugPendingReport> OldestFirst = CompareOldestFirst;

        [Serializable]
        private sealed class AttemptState
        {
            public int attempts;
            public string lastError;
            public string lastAttemptUtc;
            public long firstAttemptTicksUtc;
        }

        [Serializable]
        private sealed class FailureState
        {
            public string reason;
            public long statusCode;
            public int attempts;
            public string failedAtUtc;
        }

        private static BugReportOutbox _default;

        private readonly string _outboxDirectory;
        private readonly string _failedDirectory;
        private readonly int _maxReports;
        private readonly long _maxBytes;
        private readonly object _ioLock = new object();

        /// <summary>Số report sẵn sàng trong outbox mặc định, hoặc <see cref="UNKNOWN_COUNT"/> trước lần quét đầu.</summary>
        public static int PendingCount { get; private set; } = UNKNOWN_COUNT;

        #endregion

        #region Initialize

        /// <summary>Tạo outbox gốc tại <paramref name="rootDirectory"/>.</summary>
        public BugReportOutbox(string rootDirectory, int maxReports = MAX_REPORTS, long maxBytes = MAX_TOTAL_BYTES)
        {
            RootDirectory = rootDirectory;
            _outboxDirectory = Path.Combine(rootDirectory, OUTBOX_FOLDER);
            _failedDirectory = Path.Combine(rootDirectory, FAILED_FOLDER);
            _maxReports = Math.Max(1, maxReports);
            _maxBytes = Math.Max(1, maxBytes);
        }

        /// <summary>
        /// Outbox dưới <c>Application.persistentDataPath/BugLogger</c>. Lần truy cập đầu phải ở main thread
        /// (đọc <c>persistentDataPath</c>); test được thay bằng folder tạm.
        /// </summary>
        public static BugReportOutbox Default
        {
            get => _default ??= new BugReportOutbox(Path.Combine(Application.persistentDataPath, ROOT_FOLDER));
            set => _default = value;
        }

        #endregion

        #region Public — IO (thread pool)

        /// <summary>Folder chứa <c>outbox/</c> và <c>failed/</c>.</summary>
        public string RootDirectory { get; }

        /// <summary>Folder của một report đang chờ.</summary>
        public string GetReportDirectory(string id) => Path.Combine(_outboxDirectory, id);

        /// <summary>Folder của một report đã thất bại.</summary>
        public string GetFailedDirectory(string id) => Path.Combine(_failedDirectory, id);

        /// <summary>
        /// Ghi <paramref name="files"/> nguyên tử rồi áp trần (không bao giờ bỏ report vừa ghi). Lỗi IO ném
        /// ra — caller báo lỗi.
        /// </summary>
        /// <returns>Số report cũ bị trần bỏ đi.</returns>
        public int Enqueue(BugReportFiles files)
        {
            lock (_ioLock)
            {
                var directory = GetReportDirectory(files.Id);
                Directory.CreateDirectory(directory);

                if (files.Screenshot != null)
                    WriteAtomic(Path.Combine(directory, SCREENSHOT_FILE), files.Screenshot);
                if (files.Log != null)
                    WriteAtomic(Path.Combine(directory, LOG_FILE), files.Log);
                if (files.Save != null)
                    WriteAtomic(Path.Combine(directory, SAVE_FILE), files.Save);

                // Cuối cùng: folder chỉ tính là đã xếp hàng khi có report.json.
                WriteAtomic(Path.Combine(directory, REPORT_FILE), files.Report);

                return EnforceCaps(files.Id);
            }
        }

        /// <summary>Số report sẵn sàng.</summary>
        public int CountReady()
        {
            lock (_ioLock)
                return ScanReady().Count;
        }

        /// <summary>
        /// Report sẵn sàng cũ nhất không nằm trong <paramref name="skipIds"/>, hoặc null khi outbox rỗng / mọi report
        /// đều bị bỏ qua.
        /// </summary>
        /// <param name="readyCount">Số report sẵn sàng (tính cả report bị bỏ qua).</param>
        /// <param name="skipIds">Id đang chờ backoff riêng; null = không bỏ qua cái nào.</param>
        public BugPendingReport? PeekOldest(out int readyCount, ICollection<string> skipIds = null)
        {
            lock (_ioLock)
            {
                var ready = ScanReady();
                readyCount = ready.Count;
                foreach (var report in ready)
                {
                    if (skipIds == null || !skipIds.Contains(report.Id))
                        return report;
                }

                return null;
            }
        }

        /// <summary>Đọc các file của một report đang chờ, hoặc null khi nó đã không còn (đã gửi, bị bỏ).</summary>
        public BugReportPayload Load(string id)
        {
            lock (_ioLock)
            {
                var directory = GetReportDirectory(id);
                var reportPath = Path.Combine(directory, REPORT_FILE);
                if (!File.Exists(reportPath))
                    return null;

                try
                {
                    return new BugReportPayload(id,
                        File.ReadAllBytes(reportPath),
                        ReadOptional(Path.Combine(directory, SCREENSHOT_FILE)),
                        ReadOptional(Path.Combine(directory, LOG_FILE)),
                        ReadOptional(Path.Combine(directory, SAVE_FILE)));
                }
                catch (IOException)
                {
                    return null;
                }
                catch (UnauthorizedAccessException)
                {
                    return null;
                }
            }
        }

        /// <summary>True khi report vẫn đang chờ (còn <c>report.json</c>).</summary>
        public bool Exists(string id)
        {
            lock (_ioLock)
                return File.Exists(Path.Combine(GetReportDirectory(id), REPORT_FILE));
        }

        /// <summary>Xoá report đã gửi xong.</summary>
        /// <returns>False khi không xoá được folder (sẽ bị gửi lại).</returns>
        public bool Complete(string id)
        {
            lock (_ioLock)
                return TryDeleteDirectory(GetReportDirectory(id));
        }

        /// <summary>Chuyển report sang <c>failed/&lt;id&gt;</c> kèm <c>failure.json</c>.</summary>
        /// <param name="id">Report.</param>
        /// <param name="reason">Lý do ngắn ghi vào <c>failure.json</c>.</param>
        /// <param name="statusCode">HTTP status của lần thử cuối; 0 khi không có response.</param>
        /// <param name="isAttemptRecorded">True khi lần thử cuối đã được <see cref="IncrementAttempt"/> ghi (luật bỏ cuộc).</param>
        /// <returns>False khi report vẫn còn trong outbox sau đó (chuyển thất bại).</returns>
        public bool MoveToFailed(string id, string reason, long statusCode, bool isAttemptRecorded = false)
        {
            lock (_ioLock)
            {
                var source = GetReportDirectory(id);
                if (!Directory.Exists(source))
                    return true;

                var target = GetFailedDirectory(id);
                try
                {
                    var attempts = ReadAttemptState(source)?.attempts ?? 0;
                    Directory.CreateDirectory(_failedDirectory);
                    TryDeleteDirectory(target);
                    Directory.Move(source, target);

                    var failure = new FailureState
                    {
                        reason = BugReportBuilder.Truncate(reason, MAX_REASON_LENGTH),
                        statusCode = statusCode,
                        attempts = isAttemptRecorded ? attempts : attempts + 1,
                        failedAtUtc = UtcStamp(),
                    };
                    WriteAtomic(Path.Combine(target, FAILURE_FILE), Encoding.UTF8.GetBytes(JsonUtility.ToJson(failure)));
                }
                catch (IOException)
                {
                    // Tệ nhất là nằm trong failed/ mà thiếu failure.json; chỉ chuyển thất bại mới giữ trong hàng đợi.
                }
                catch (UnauthorizedAccessException)
                {
                }

                TrimFailed();
                return !Directory.Exists(source);
            }
        }

        /// <summary>Ghi thêm một lần thử thất bại vào <c>attempt.json</c>.</summary>
        /// <param name="id">Report.</param>
        /// <param name="reason">Lý do ngắn (không chứa dữ liệu nhạy cảm).</param>
        /// <param name="firstAttemptUtc">Thời điểm lần thử thất bại đầu tiên của report.</param>
        /// <returns>Số lần thử sau lần này (1 cho lần đầu), 0 khi report đã không còn.</returns>
        public int IncrementAttempt(string id, string reason, out DateTime firstAttemptUtc)
        {
            lock (_ioLock)
            {
                var now = BugLoggerClock.UtcNow;
                firstAttemptUtc = now;

                var directory = GetReportDirectory(id);
                if (!File.Exists(Path.Combine(directory, REPORT_FILE)))
                    return 0;

                var state = ReadAttemptState(directory) ?? new AttemptState();
                if (state.firstAttemptTicksUtc <= 0 || state.firstAttemptTicksUtc > now.Ticks)
                    state.firstAttemptTicksUtc = now.Ticks;

                state.attempts = Math.Max(0, state.attempts) + 1;
                state.lastError = BugReportBuilder.Truncate(reason, MAX_REASON_LENGTH);
                state.lastAttemptUtc = UtcStamp();
                WriteAtomic(Path.Combine(directory, ATTEMPT_FILE), Encoding.UTF8.GetBytes(JsonUtility.ToJson(state)));

                firstAttemptUtc = new DateTime(state.firstAttemptTicksUtc, DateTimeKind.Utc);
                return state.attempts;
            }
        }

        /// <summary>Số lần thử thất bại đã ghi của một report đang chờ (0 khi chưa có).</summary>
        public int GetAttemptCount(string id)
        {
            lock (_ioLock)
                return ReadAttemptState(GetReportDirectory(id))?.attempts ?? 0;
        }

        #endregion

        #region Public — facade main thread

        /// <summary>
        /// Pipeline submit, khởi chạy trên main thread: build info → save snapshot của host → (thread pool)
        /// dựng + ghi file → (main thread) số đếm, <see cref="BugLogger.ReportQueued"/>, Notify, đánh thức uploader.
        /// Lỗi IO → Notify <see cref="BugLoggerConfig.NOTIFY_SAVE_FAILED"/> + <see cref="BugLogger.ReportFailed"/>.
        /// </summary>
        public static async UniTaskVoid SubmitAsync(BugReportCapture capture)
        {
            var outbox = Default;

            await BugLoggerBuildInfo.EnsureLoadedAsync();
            capture.CommitSha = BugLoggerBuildInfo.CommitSha;
            capture.Branch = BugLoggerBuildInfo.Branch;
            capture.BuildNumber = BugLoggerBuildInfo.BuildNumber;

            var save = await CaptureSaveAsync(capture);

            await UniTask.SwitchToThreadPool();
            var pending = UNKNOWN_COUNT;
            var dropped = 0;
            Exception failure = null;
            try
            {
                dropped = outbox.Enqueue(BugReportBuilder.Build(capture, save));
                pending = outbox.CountReady();
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            await UniTask.SwitchToMainThread();

            if (failure != null)
            {
                var reason = $"save failed: {failure.GetType().Name}";
                Debug.LogError($"[BugLogger] Could not save bug report {capture.Id}: {failure.GetType().Name}: {failure.Message}");
                BugLogger.RaiseNotify(BugLoggerConfig.NOTIFY_SAVE_FAILED);
                BugLogger.RaiseReportFailed(capture.Id, reason);
                return;
            }

            if (dropped > 0)
                Debug.LogWarning($"[BugLogger] Outbox over {MAX_REPORTS} reports / {MAX_TOTAL_BYTES} bytes - dropped the {dropped} oldest.");

            PublishPendingCount(pending);
            BugLogger.RaiseReportQueued();

            var isOffline = !BugLoggerConfig.CurrentTarget.IsConfigured ||
                            Application.internetReachability == NetworkReachability.NotReachable;
            BugLogger.RaiseNotify(isOffline ? BugLoggerConfig.NOTIFY_SAVED_OFFLINE : BugLoggerConfig.NOTIFY_SAVED_SENDING);

            BugReportUploader.Wake();
        }

        /// <summary>Cập nhật <see cref="PendingCount"/>. Main thread.</summary>
        public static void PublishPendingCount(int count)
        {
            PendingCount = count;
        }

        #endregion

        #region Private

        private static async UniTask<string> CaptureSaveAsync(BugReportCapture capture)
        {
            var provider = BugLoggerConfig.SaveSnapshotProvider;
            if (provider == null)
                return null;

            try
            {
                // Có trần: serializer của host treo/chậm không được giữ report ngoài disk.
                return await provider().Timeout(TimeSpan.FromSeconds(SAVE_SNAPSHOT_TIMEOUT_SECONDS), DelayType.Realtime);
            }
            catch (TimeoutException)
            {
                capture.Notes.Add(new KeyValuePair<string, string>(BugReportBuilder.NOTE_SAVE, $"timed out after {SAVE_SNAPSHOT_TIMEOUT_SECONDS} s"));
                await UniTask.SwitchToMainThread();
                Debug.LogWarning($"[BugLogger] SaveSnapshotProvider timed out after {SAVE_SNAPSHOT_TIMEOUT_SECONDS} s - report saved without save.json.");
                return null;
            }
            catch (Exception ex)
            {
                capture.Notes.Add(new KeyValuePair<string, string>(BugReportBuilder.NOTE_SAVE, $"provider threw {ex.GetType().Name}"));
                await UniTask.SwitchToMainThread();
                Debug.LogWarning($"[BugLogger] SaveSnapshotProvider threw {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Report sẵn sàng, cũ nhất trước. Dọn folder dở quá cửa sổ stale.</summary>
        private List<BugPendingReport> ScanReady()
        {
            var result = new List<BugPendingReport>();
            if (!Directory.Exists(_outboxDirectory))
                return result;

            var staleBefore = BugLoggerClock.UtcNow.AddMinutes(-STALE_INCOMPLETE_MINUTES);
            foreach (var directory in Directory.GetDirectories(_outboxDirectory))
            {
                var reportPath = Path.Combine(directory, REPORT_FILE);
                if (!File.Exists(reportPath))
                {
                    if (Directory.GetLastWriteTimeUtc(directory) < staleBefore)
                        TryDeleteDirectory(directory);

                    continue;
                }

                result.Add(new BugPendingReport(Path.GetFileName(directory), File.GetLastWriteTimeUtc(reportPath), DirectorySize(directory)));
            }

            result.Sort(OldestFirst);
            return result;
        }

        private int EnforceCaps(string protectedId)
        {
            var ready = ScanReady();
            var count = ready.Count;
            long bytes = 0;
            foreach (var report in ready)
                bytes += report.Bytes;

            var dropped = 0;
            foreach (var report in ready)
            {
                if (count <= _maxReports && bytes <= _maxBytes)
                    break;

                if (report.Id == protectedId)
                    continue;

                TryDeleteDirectory(GetReportDirectory(report.Id));
                count--;
                bytes -= report.Bytes;
                dropped++;
            }

            return dropped;
        }

        private void TrimFailed()
        {
            if (!Directory.Exists(_failedDirectory))
                return;

            var directories = Directory.GetDirectories(_failedDirectory);
            if (directories.Length <= _maxReports)
                return;

            Array.Sort(directories, (a, b) => Directory.GetLastWriteTimeUtc(a).CompareTo(Directory.GetLastWriteTimeUtc(b)));
            for (var i = 0; i < directories.Length - _maxReports; i++)
                TryDeleteDirectory(directories[i]);
        }

        private static int CompareOldestFirst(BugPendingReport a, BugPendingReport b)
        {
            var byTime = a.QueuedAtUtc.CompareTo(b.QueuedAtUtc);
            return byTime != 0 ? byTime : string.CompareOrdinal(a.Id, b.Id);
        }

        private static AttemptState ReadAttemptState(string directory)
        {
            var path = Path.Combine(directory, ATTEMPT_FILE);
            // App bị kill giữa lúc xoá bản cũ và rename bản mới (WriteAtomic): bản mới vẫn còn trong *.tmp.
            if (!File.Exists(path))
                path += TEMP_SUFFIX;

            if (!File.Exists(path))
                return null;

            try
            {
                return JsonUtility.FromJson<AttemptState>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void WriteAtomic(string path, byte[] data)
        {
            var temp = path + TEMP_SUFFIX;
            File.WriteAllBytes(temp, data);
            if (File.Exists(path))
                File.Delete(path);

            File.Move(temp, path);
        }

        private static byte[] ReadOptional(string path)
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        private static long DirectorySize(string directory)
        {
            long total = 0;
            foreach (var file in Directory.GetFiles(directory))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                    // File biến mất giữa lúc liệt kê và lúc stat.
                }
            }

            return total;
        }

        /// <returns>True khi folder không còn tồn tại.</returns>
        private static bool TryDeleteDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
            catch (IOException)
            {
                // Bị khoá: báo lại cho caller, caller sẽ backoff.
            }
            catch (UnauthorizedAccessException)
            {
            }

            return !Directory.Exists(directory);
        }

        private static string UtcStamp()
        {
            return BugLoggerClock.UtcNow.ToString(ROUND_TRIP_FORMAT, CultureInfo.InvariantCulture);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Domain reload có thể tắt: số đếm của phiên trước không được sống sang phiên sau.
            _default = null;
            PendingCount = UNKNOWN_COUNT;
        }

        #endregion
    }
}
