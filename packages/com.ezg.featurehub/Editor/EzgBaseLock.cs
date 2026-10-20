// EZG Feature Hub — lock của base AI (.claude/ezg-base.lock.json) + phát hiện file project đã sửa tại chỗ.
//
// Mọi file AI mà base EZG ship (skill, command, agent, rule, docs, script, CLAUDE.md...) là CỦA BASE:
// project không sửa tại chỗ, phần riêng nằm ở CLAUDE_PROJECT.md / .claude/project/overrides/. Lock ghi
// hash của bản base đã cài cho từng file. Có lock (hoặc history.json của server = mọi hash từng publish
// cho một path) thì phân biệt được "base có bản mới" với "project đã sửa file này" — và Feature Hub
// KHÔNG ghi đè file project đã sửa: việc đó để /update-ai làm (chuyển phần sửa sang override rồi mới
// nhận bản mới).
//
// Hash ("nsha") = sha256 của nội dung, file text thì gộp CRLF → LF trước (không có byte NUL trong 8000
// byte đầu). Giống hệt ezg_base.py (skill update-ai) và ai-catalog-lib.mjs (script publish), nên checkout
// Windows có core.autocrlf không bị coi là đã sửa.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace Ezg.FeatureHub.Editor
{
    /// <summary>Một item trong lock (cùng shape với ezg_base.py).</summary>
    [Serializable]
    public class BaseLockItem
    {
        public string sha256;
        public string installPath;
        public bool isDirectory;
        public string ownership;
    }

    /// <summary>Một file base trong lock: item chứa nó + hash bản base đã cài.</summary>
    [Serializable]
    public class BaseLockFile
    {
        public string item;
        public string sha256;
    }

    [Serializable]
    public class BaseLock
    {
        [JsonProperty("$comment")] public string comment;
        public int schemaVersion = 1;
        public string updatedAtUtc;
        public SortedDictionary<string, BaseLockItem> items = new SortedDictionary<string, BaseLockItem>(StringComparer.Ordinal);
        public SortedDictionary<string, BaseLockFile> files = new SortedDictionary<string, BaseLockFile>(StringComparer.Ordinal);
    }

    /// <summary>ai/history.json: path → mọi hash từng publish cho path đó.</summary>
    [Serializable]
    public class AiHistory
    {
        public int schemaVersion;
        public Dictionary<string, List<string>> files = new Dictionary<string, List<string>>();
    }

    /// <summary>Kết quả soi một AI item so với đĩa.</summary>
    public class AiItemInspection
    {
        public readonly List<string> Modified = new List<string>();  // project sửa tại chỗ
        public readonly List<string> Outdated = new List<string>();  // project chưa sửa, base có bản mới / file thiếu
        public bool Exists;
    }

    public static class EzgBaseLock
    {
        #region Constants

        public const string LOCK_REL = ".claude/ezg-base.lock.json";
        public const string OWNERSHIP_MERGE = "merge";
        public const string UPDATE_COMMAND = "/update-ai";

        private const int TEXT_SNIFF_BYTES = 8000;
        private const string LOCK_COMMENT =
            "Baseline của các file thuộc base EZG — /update-ai và Feature Hub ghi file này, đừng sửa tay. " +
            "Commit cùng repo. Phần riêng của project: CLAUDE_PROJECT.md, .claude/project/ (xem rule base-ownership).";

        #endregion

        #region Fields

        private static BaseLock _cache;
        private static DateTime _cacheStamp;

        /// <summary>ai/history.json của server (null khi chưa tải được / server cũ) — window gán sau khi tải.</summary>
        public static AiHistory History;

        // path tuyệt đối → (mtime, size, hash): RebuildList gọi Inspect cho mọi item mỗi lần gõ tìm kiếm.
        private static readonly Dictionary<string, (DateTime time, long size, string hash)> _hashCache =
            new Dictionary<string, (DateTime, long, string)>();

        #endregion

        #region Public Methods — hash

        /// <summary>sha256 hex của nội dung, CRLF → LF nếu là file text.</summary>
        public static string Nsha(byte[] data)
        {
            if (data == null)
                return null;

            int sniff = Math.Min(data.Length, TEXT_SNIFF_BYTES);
            bool isText = Array.IndexOf(data, (byte)0, 0, sniff) < 0;
            byte[] normalized = isText ? FoldCrlf(data) : data;

            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(normalized);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>nsha của một file trong project (null nếu không có). Cache theo mtime + size.</summary>
        public static string LocalHash(string projectRelative)
        {
            string full = FeatureHubAiInstaller.FullPath(projectRelative);
            if (string.IsNullOrEmpty(full) || !File.Exists(full))
                return null;

            try
            {
                var info = new FileInfo(full);
                if (_hashCache.TryGetValue(full, out var cached) &&
                    cached.time == info.LastWriteTimeUtc && cached.size == info.Length)
                    return cached.hash;

                string hash = Nsha(File.ReadAllBytes(full));
                _hashCache[full] = (info.LastWriteTimeUtc, info.Length, hash);
                return hash;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FeatureHub] Không đọc được {projectRelative}: {e.Message}");
                return null;
            }
        }

        #endregion

        #region Public Methods — lock

        /// <summary>Lock hiện tại (null nếu project chưa có). Đọc lại khi file đổi trên đĩa — /update-ai ghi nó.</summary>
        public static BaseLock Load()
        {
            string path = FeatureHubAiInstaller.FullPath(LOCK_REL);
            if (!File.Exists(path))
            {
                _cache = null;
                return null;
            }

            DateTime stamp = File.GetLastWriteTimeUtc(path);
            if (_cache != null && stamp == _cacheStamp)
                return _cache;

            try
            {
                _cache = JsonConvert.DeserializeObject<BaseLock>(File.ReadAllText(path));
                _cacheStamp = stamp;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FeatureHub] Không đọc được {LOCK_REL}: {e.Message}");
                _cache = null;
            }

            return _cache;
        }

        /// <summary>Ghi nhận item vừa cài: baseline của từng file = hash bản vừa ghi.</summary>
        public static void MarkInstalled(AiItem item, List<string> writtenFiles)
        {
            if (item == null)
                return;

            var lockData = Load() ?? new BaseLock();
            foreach (var key in lockData.files.Where(p => p.Value?.item == item.id).Select(p => p.Key).ToList())
                lockData.files.Remove(key);

            var hashes = item.fileHashes;
            foreach (var rel in writtenFiles ?? new List<string>())
            {
                string hash = hashes != null && hashes.TryGetValue(rel, out var h) ? h : LocalHash(rel);
                if (!string.IsNullOrEmpty(hash))
                    lockData.files[rel] = new BaseLockFile { item = item.id, sha256 = hash };
            }

            lockData.items[item.id] = new BaseLockItem
            {
                sha256 = item.sha256 ?? string.Empty,
                installPath = item.installPath,
                isDirectory = item.isDirectory,
                ownership = OwnershipOf(item),
            };
            Save(lockData);
        }

        /// <summary>Gỡ item khỏi lock.</summary>
        public static void MarkUninstalled(AiItem item)
        {
            var lockData = Load();
            if (lockData == null || item == null)
                return;

            lockData.items.Remove(item.id);
            foreach (var key in lockData.files.Where(p => p.Value?.item == item.id).Select(p => p.Key).ToList())
                lockData.files.Remove(key);
            Save(lockData);
        }

        /// <summary>Baseline đã ghi của một file (null nếu chưa có).</summary>
        public static string Baseline(string projectRelative)
        {
            var lockData = Load();
            return lockData != null && lockData.files.TryGetValue(projectRelative, out var f) ? f?.sha256 : null;
        }

        #endregion

        #region Public Methods — inspection

        public static string OwnershipOf(AiItem item)
        {
            if (!string.IsNullOrEmpty(item?.ownership))
                return item.ownership;
            return item?.installPath == ".claude/settings.json" || item?.installPath == ".mcp.json"
                ? OWNERSHIP_MERGE
                : "base";
        }

        /// <summary>
        /// So từng file của item (catalog có fileHashes) với đĩa: khớp bản mới → ok; khớp baseline trong lock
        /// hoặc một bản từng publish (history) → project chưa sửa, cần cập nhật; còn lại → project đã sửa.
        /// File merge (settings.json, .mcp.json) mà baseline đã bằng bản mới → coi là ok (project thêm phần
        /// riêng là hợp lệ, base không có gì mới).
        /// </summary>
        public static AiItemInspection Inspect(AiItem item)
        {
            var history = History;
            var result = new AiItemInspection();
            if (item?.fileHashes == null)
                return result;

            bool merge = OwnershipOf(item) == OWNERSHIP_MERGE;
            foreach (var pair in item.fileHashes)
            {
                string rel = pair.Key;
                string upstream = pair.Value;
                string local = LocalHash(rel);
                if (local == null)
                {
                    result.Outdated.Add(rel);
                    continue;
                }

                result.Exists = true;
                if (local == upstream)
                    continue;

                string baseline = Baseline(rel);
                bool clean = local == baseline ||
                             (history?.files != null && history.files.TryGetValue(rel, out var known) && known.Contains(local));
                if (clean)
                    result.Outdated.Add(rel);
                else if (merge && baseline == upstream)
                    continue;
                else
                    result.Modified.Add(rel);
            }

            if (!result.Exists)
                result.Exists = FeatureHubAiInstaller.TargetExists(item);
            return result;
        }

        #endregion

        #region Private Methods

        private static void Save(BaseLock lockData)
        {
            lockData.comment = LOCK_COMMENT;
            lockData.schemaVersion = 1;
            lockData.updatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            string path = FeatureHubAiInstaller.FullPath(LOCK_REL);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(lockData, Formatting.Indented) + "\n");
                _cache = lockData;
                _cacheStamp = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[FeatureHub] Ghi {LOCK_REL} thất bại: {e.Message}");
            }
        }

        private static byte[] FoldCrlf(byte[] data)
        {
            int crlf = 0;
            for (int i = 0; i + 1 < data.Length; i++)
                if (data[i] == '\r' && data[i + 1] == '\n')
                    crlf++;
            if (crlf == 0)
                return data;

            var output = new byte[data.Length - crlf];
            int o = 0;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == '\r' && i + 1 < data.Length && data[i + 1] == '\n')
                    continue;
                output[o++] = data[i];
            }

            return output;
        }

        #endregion
    }
}
