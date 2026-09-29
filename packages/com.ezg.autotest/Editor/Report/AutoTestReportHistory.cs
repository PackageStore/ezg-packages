using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using F = Ezg.AutoTest.Editor.AutoTestReportFormat;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Lịch sử các lượt chạy trong thư mục report (mỗi lượt một thư mục con chứa report.json + report.html…):
    ///     liệt kê, đọc lại, tìm lượt trước để so sánh, dọn bớt lượt cũ. Header được cache theo thời gian ghi file để
    ///     cửa sổ Editor gọi <see cref="List" /> mỗi lần repaint vẫn nhẹ.
    /// </summary>
    public static class AutoTestReportHistory
    {
        #region Kiểu dữ liệu

        /// <summary>Tóm tắt một lượt chạy (đọc từ header report.json, không nạp case/issue).</summary>
        public sealed class Entry
        {
            public string runId;
            public string title;
            public string startedAt;
            public double durationMs;

            /// <summary>Thư mục tuyệt đối của lượt chạy.</summary>
            public string folder;

            /// <summary>Đường dẫn report.html (rỗng nếu file không tồn tại).</summary>
            public string htmlPath;

            public RunSummary summary;
            public bool cancelled;
            public List<string> suiteIds;
            public string trigger;
        }

        // Field chỉ được JsonUtility gán qua serializer ⇒ tắt cảnh báo "never assigned" (CS0649).
#pragma warning disable CS0649
        /// <summary>Chỉ các field cần cho danh sách — JsonUtility bỏ qua phần còn lại (case, issue, log…).</summary>
        [Serializable]
        sealed class ReportHeader
        {
            public string runId;
            public string title;
            public string startedAt;
            public double durationMs;
            public string trigger;
            public bool cancelled;
            public RunSummary summary;
            public List<SuiteHeader> suites;
        }

        [Serializable]
        sealed class SuiteHeader
        {
            public string suiteId;
        }
#pragma warning restore CS0649

        sealed class CachedEntry
        {
            /// <summary>Thư mục tuyệt đối (đã chuẩn hoá) — có cả khi report.json hỏng để Prune xoá được.</summary>
            public string folderPath;

            public DateTime writeTimeUtc;
            public long length;

            /// <summary>Null nếu report.json hỏng.</summary>
            public Entry entry;

            /// <summary>Thời điểm dùng để sắp xếp: startedAt, không parse được thì lấy giờ ghi report.json.</summary>
            public DateTimeOffset sortTime;
        }

        #endregion

        #region Fields

        static readonly Dictionary<string, CachedEntry> Cache = new(StringComparer.Ordinal);
        static readonly GeneralConfig DefaultGeneral = new();

        #endregion

        #region Public

        /// <summary>&lt;thư mục project&gt;/&lt;config.general.reportFolder&gt; (tuyệt đối), tự tạo nếu chưa có.</summary>
        public static string ReportsRoot(AutoTestConfig config)
        {
            var folder = config?.general?.reportFolder;
            if (string.IsNullOrWhiteSpace(folder)) folder = DefaultGeneral.reportFolder;
            var root = Path.GetFullPath(Path.IsPathRooted(folder) ? folder : Path.Combine(ProjectRoot(), folder));
            Directory.CreateDirectory(root);
            return root;
        }

        /// <summary>Các lượt chạy, mới nhất trước. Thư mục không có report.json hoặc report.json hỏng bị bỏ qua.</summary>
        public static List<Entry> List(AutoTestConfig config)
        {
            var result = new List<Entry>();
            foreach (var cached in Scan(config))
                if (cached.entry != null)
                    result.Add(cached.entry);
            return result;
        }

        /// <summary>Đọc đầy đủ report của một thư mục lượt chạy. Null nếu thiếu/hỏng.</summary>
        public static TestRunReport Load(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return null;
            var path = Path.Combine(folder, AutoTestReportWriter.JSON_FILE);
            if (!File.Exists(path)) return null;
            try
            {
                var report = JsonUtility.FromJson<TestRunReport>(File.ReadAllText(path));
                if (report == null) return null;
                F.EnsureNotNull(report);
                report.outputDir = Path.GetFullPath(folder);
                return report;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AutoTest] Không đọc được {path}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        ///     Lượt chạy gần nhất KHÁC <paramref name="current" /> (bắt đầu trước nó) có bộ suite giao với lượt hiện tại.
        ///     Ưu tiên lượt chứa đủ mọi suite của lượt hiện tại (cùng bộ hoặc bộ lớn hơn — so sánh trên phần giao là như
        ///     nhau); không có thì lấy lượt mới nhất có giao. Null nếu không có.
        /// </summary>
        public static TestRunReport FindPrevious(AutoTestConfig config, TestRunReport current)
        {
            if (current == null) return null;
            var currentIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var suite in F.Suites(current))
                if (!string.IsNullOrEmpty(suite.suiteId))
                    currentIds.Add(suite.suiteId);
            if (currentIds.Count == 0) return null;

            var hasTime = F.TryParseDate(current.startedAt, out var currentTime);
            var currentFolder = NormalizeFolder(current.outputDir);
            var covering = new List<Entry>();
            var partial = new List<Entry>();
            foreach (var e in List(config))
            {
                if (!string.IsNullOrEmpty(current.runId) && e.runId == current.runId) continue;
                if (currentFolder != null && NormalizeFolder(e.folder) == currentFolder) continue;
                if (hasTime && (!F.TryParseDate(e.startedAt, out var t) || t >= currentTime)) continue;

                var overlap = 0;
                if (e.suiteIds != null)
                    foreach (var id in new HashSet<string>(e.suiteIds, StringComparer.Ordinal))
                        if (currentIds.Contains(id))
                            overlap++;
                if (overlap == 0) continue;
                (overlap == currentIds.Count ? covering : partial).Add(e);
            }

            // List đã sắp mới nhất trước ⇒ lấy cái đầu tiên đọc được (report hỏng thì thử cái kế tiếp).
            foreach (var candidates in new[] { covering, partial })
            foreach (var e in candidates)
            {
                var report = Load(e.folder);
                if (report != null) return report;
            }

            return null;
        }

        /// <summary>
        ///     Giữ lại <c>config.general.keepReports</c> lượt mới nhất, xoá phần còn lại (≤ 0 = giữ tất cả). Chỉ xoá
        ///     thư mục có report.json — thư mục khác người dùng tự tạo trong đó không bao giờ bị đụng tới.
        /// </summary>
        public static void Prune(AutoTestConfig config)
        {
            var keep = config?.general?.keepReports ?? DefaultGeneral.keepReports;
            if (keep <= 0) return;
            var all = Scan(config);
            for (var i = keep; i < all.Count; i++)
            {
                var folder = FolderOf(all[i]);
                if (folder != null) Delete(folder);
            }
        }

        /// <summary>Xoá thư mục một lượt chạy (chỉ khi nó đúng là thư mục report: có report.json hoặc report.html).</summary>
        public static void Delete(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            var full = Path.GetFullPath(folder);
            if (!File.Exists(Path.Combine(full, AutoTestReportWriter.JSON_FILE)) &&
                !File.Exists(Path.Combine(full, AutoTestReportWriter.HTML_FILE)))
            {
                Debug.LogWarning($"[AutoTest] Bỏ qua xoá '{full}': không phải thư mục report (thiếu report.json/report.html).");
                return;
            }

            try
            {
                ClearReadOnly(full);
                Directory.Delete(full, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AutoTest] Không xoá được '{full}': {e.Message}");
            }

            Cache.Remove(NormalizeFolder(full) ?? full);
        }

        #endregion

        #region Private

        static string ProjectRoot()
        {
            return Path.GetDirectoryName(Application.dataPath) ?? Directory.GetCurrentDirectory();
        }

        /// <summary>Mọi thư mục con có report.json (kể cả hỏng), mới nhất trước.</summary>
        static List<CachedEntry> Scan(AutoTestConfig config)
        {
            var result = new List<CachedEntry>();
            string[] dirs;
            try
            {
                dirs = Directory.GetDirectories(ReportsRoot(config));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AutoTest] Không đọc được thư mục report: {e.Message}");
                return result;
            }

            var alive = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dir in dirs)
            {
                var cached = Read(dir);
                if (cached == null) continue;
                alive.Add(NormalizeFolder(dir));
                result.Add(cached);
            }

            // Dọn cache của thư mục đã bị xoá bên ngoài.
            var stale = new List<string>();
            foreach (var key in Cache.Keys)
                if (!alive.Contains(key) && !Directory.Exists(key))
                    stale.Add(key);
            foreach (var key in stale) Cache.Remove(key);

            // Mới nhất trước; bằng nhau thì theo tên thư mục (giảm dần) cho ổn định.
            result.Sort((a, b) =>
            {
                var c = b.sortTime.CompareTo(a.sortTime);
                return c != 0 ? c : string.CompareOrdinal(FolderOf(b), FolderOf(a));
            });
            return result;
        }

        /// <summary>Đọc header report.json của một thư mục (có cache). Null nếu thư mục không có report.json.</summary>
        static CachedEntry Read(string dir)
        {
            var key = NormalizeFolder(dir);
            var json = Path.Combine(key, AutoTestReportWriter.JSON_FILE);
            FileInfo info;
            try
            {
                info = new FileInfo(json);
                if (!info.Exists) return null;
            }
            catch (Exception)
            {
                return null;
            }

            if (Cache.TryGetValue(key, out var cached) && cached.writeTimeUtc == info.LastWriteTimeUtc &&
                cached.length == info.Length)
                return cached;

            cached = new CachedEntry
            {
                writeTimeUtc = info.LastWriteTimeUtc,
                length = info.Length,
                sortTime = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)
            };
            try
            {
                var header = JsonUtility.FromJson<ReportHeader>(File.ReadAllText(json));
                if (header != null) cached.entry = ToEntry(header, key);
            }
            catch (Exception)
            {
                // report.json hỏng (ghi dở, sửa tay…) ⇒ entry = null, List bỏ qua nhưng Prune vẫn tính.
                cached.entry = null;
            }

            if (cached.entry != null && F.TryParseDate(cached.entry.startedAt, out var started)) cached.sortTime = started;
            cached.folderPath = key;
            Cache[key] = cached;
            return cached;
        }

        static Entry ToEntry(ReportHeader h, string folder)
        {
            var ids = new List<string>();
            if (h.suites != null)
                foreach (var s in h.suites)
                    if (s != null && !string.IsNullOrEmpty(s.suiteId) && !ids.Contains(s.suiteId))
                        ids.Add(s.suiteId);
            var html = Path.Combine(folder, AutoTestReportWriter.HTML_FILE);
            return new Entry
            {
                runId = string.IsNullOrEmpty(h.runId) ? Path.GetFileName(folder) : h.runId,
                title = F.Safe(h.title),
                startedAt = F.Safe(h.startedAt),
                durationMs = h.durationMs,
                folder = folder,
                htmlPath = File.Exists(html) ? html : string.Empty,
                summary = h.summary ?? new RunSummary(),
                cancelled = h.cancelled,
                suiteIds = ids,
                trigger = F.Safe(h.trigger)
            };
        }

        static string FolderOf(CachedEntry c)
        {
            return c?.folderPath;
        }

        static string NormalizeFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return null;
            try
            {
                return Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception)
            {
                return folder;
            }
        }

        static void ClearReadOnly(string folder)
        {
            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                var attr = File.GetAttributes(file);
                if ((attr & FileAttributes.ReadOnly) != 0) File.SetAttributes(file, attr & ~FileAttributes.ReadOnly);
            }
        }

        #endregion
    }
}
