// EZG Feature Hub — cài/gỡ AI item (skill, command, agent, rule... của Claude).
//
// Khác hẳn 3 tab kia: nội dung KHÔNG nằm trong Assets/ mà là file tooling ở PROJECT ROOT
// (.claude/skills/..., .mcp.json, CLAUDE.md...). Vì vậy ở đây không đụng tới AssetDatabase,
// không ImportPackage, không gây domain reload — chỉ tải .zip, verify SHA-256 rồi ghi file.
//
// Payload là .zip mà mỗi entry đã là path TƯƠNG ĐỐI PROJECT ROOT (vd ".claude/skills/ui-kit/SKILL.md"),
// nên cài = giải nén thẳng xuống project root. Mọi entry đều bị kiểm tra phải nằm trong
// item.installPath trước khi ghi (chặn zip-slip: entry kiểu "../../../etc/passwd").
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Ezg.FeatureHub.Editor
{
    public static class FeatureHubAiInstaller
    {
        #region Constants

        // Chỉ 2 đuôi này được chạy trực tiếp (./script.sh, double-click .command). ZipArchive của .NET
        // bỏ qua unix mode trong zip nên phải tự set lại cờ +x sau khi giải nén.
        private static readonly string[] EXECUTABLE_EXT = { ".sh", ".command" };

        #endregion

        #region Public Methods

        /// <summary>Path tuyệt đối của một path tương đối project root.</summary>
        public static string FullPath(string projectRelative)
        {
            if (string.IsNullOrEmpty(projectRelative))
                return null;
            return Path.Combine(ProjectRoot(), projectRelative.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>True nếu đích cài của item đã tồn tại trên đĩa (file hoặc thư mục).</summary>
        public static bool TargetExists(AiItem item)
        {
            string full = FullPath(item?.installPath);
            return !string.IsNullOrEmpty(full) && (File.Exists(full) || Directory.Exists(full));
        }

        /// <summary>
        /// Tải + cài một AI item. onDone(success, errorOrNull).
        /// Item dạng thư mục được thay MỚI HOÀN TOÀN để bản cập nhật không để sót file đã bị gỡ khỏi
        /// skill — nhưng chỉ sau khi gói đã giải nén xong xuôi vào staging (xem Extract).
        /// </summary>
        public static void Install(AiItem item, Action<float> onProgress, Action<bool, string> onDone)
        {
            if (item == null || string.IsNullOrEmpty(item.url) || string.IsNullOrEmpty(item.installPath))
            {
                onDone?.Invoke(false, "Item AI thiếu url hoặc installPath.");
                return;
            }

            string tempPath = Path.Combine(TempDir(), item.category ?? "AI", item.fileName ?? (item.name + ".zip"));

            EditorDownloader.DownloadToFile(item.url, tempPath, onProgress, (ok, error) =>
            {
                if (!ok)
                {
                    TryDelete(tempPath);
                    onDone?.Invoke(false, $"Tải thất bại: {error}");
                    return;
                }

                if (!string.IsNullOrEmpty(item.sha256))
                {
                    string actual = Sha256File(tempPath);
                    if (!string.Equals(actual, item.sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        TryDelete(tempPath);
                        onDone?.Invoke(false, "SHA-256 không khớp — bỏ qua để an toàn.");
                        return;
                    }
                }

                try
                {
                    var written = Extract(item, tempPath);
                    if (written.Count == 0)
                    {
                        onDone?.Invoke(false, "Gói rỗng hoặc mọi entry đều nằm ngoài đích cài.");
                        return;
                    }

                    MakeExecutable(written);
                    FeatureHubInstallRecord.MarkAiInstalled(item, written);
                    onDone?.Invoke(true, null);
                }
                catch (Exception e)
                {
                    onDone?.Invoke(false, $"Giải nén lỗi: {e.Message}");
                }
                finally
                {
                    TryDelete(tempPath);
                }
            });
        }

        /// <summary>
        /// Gỡ một AI item: xóa đích cài rồi xóa record. Ưu tiên danh sách file trong record (đúng thứ
        /// Feature Hub đã ghi ra); item dạng thư mục thì xóa cả thư mục installPath.
        /// onDone(success, errorOrNull).
        /// </summary>
        public static void Uninstall(AiItem item, Action<bool, string> onDone)
        {
            if (item == null || string.IsNullOrEmpty(item.installPath))
            {
                onDone?.Invoke(false, "Item AI thiếu installPath.");
                return;
            }

            var record = FeatureHubInstallRecord.GetAi(item.id);
            // Xóa record TRƯỚC để trạng thái không kẹt ở "Đã cài" nếu bước xóa file lỗi giữa chừng.
            FeatureHubInstallRecord.RemoveAi(item.id);

            var failed = new List<string>();
            int deleted = 0;

            if (item.isDirectory)
            {
                string dir = FullPath(item.installPath);
                if (Directory.Exists(dir))
                {
                    try
                    {
                        Directory.Delete(dir, recursive: true);
                        deleted++;
                    }
                    catch (Exception e)
                    {
                        failed.Add($"{item.installPath} ({e.Message})");
                    }
                }
            }
            else
            {
                var targets = record?.files != null && record.files.Count > 0 ? record.files : item.files;
                foreach (var rel in targets ?? new List<string>())
                {
                    string full = FullPath(rel);
                    if (string.IsNullOrEmpty(full) || !File.Exists(full))
                        continue;
                    try
                    {
                        File.Delete(full);
                        deleted++;
                    }
                    catch (Exception e)
                    {
                        failed.Add($"{rel} ({e.Message})");
                    }
                }
            }

            if (failed.Count > 0)
            {
                onDone?.Invoke(deleted > 0, $"Đã xóa {deleted} mục, lỗi {failed.Count}: {string.Join("; ", failed)}");
                return;
            }

            if (deleted == 0)
            {
                // Không còn gì trên đĩa (user đã xóa tay) — record vẫn được dọn ở trên.
                onDone?.Invoke(record != null, record != null ? null : "Không tìm thấy file nào để gỡ.");
                return;
            }

            onDone?.Invoke(true, null);
        }

        #endregion

        #region Private Methods — Extract

        /// <summary>
        /// Giải nén .zip vào project root. Trả về danh sách file (path tương đối project) đã ghi.
        /// Giải nén TRỌN VẸN vào staging trong Temp/ trước, xong xuôi mới thay thứ đang có trong
        /// project — để một gói hỏng/đứt giữa chừng không đổi lấy thư mục skill cũ của user bằng
        /// một bản cài dở dang.
        /// </summary>
        private static List<string> Extract(AiItem item, string zipPath)
        {
            string installPath = item.installPath.Replace('\\', '/').TrimEnd('/');
            string staging = Path.Combine(TempDir(), "staging");
            var written = new List<string>();

            TryDeleteDirectory(staging);
            try
            {
                using (var stream = File.OpenRead(zipPath))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    foreach (var entry in archive.Entries)
                    {
                        string name = entry.FullName.Replace('\\', '/');
                        if (name.EndsWith("/", StringComparison.Ordinal))
                            continue; // entry thư mục — thư mục được tạo theo file bên dưới

                        if (!IsInsideTarget(name, installPath))
                        {
                            Debug.LogWarning($"[FeatureHub] Bỏ qua entry nằm ngoài đích cài: {entry.FullName}");
                            continue;
                        }

                        string dest = Path.Combine(staging, name.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));

                        using var source = entry.Open();
                        using var target = File.Create(dest);
                        source.CopyTo(target);

                        written.Add(name);
                    }
                }

                if (written.Count == 0)
                    return written;

                string staged = Path.Combine(staging, installPath.Replace('/', Path.DirectorySeparatorChar));
                string final = FullPath(installPath);
                string finalDir = Path.GetDirectoryName(final);
                if (!string.IsNullOrEmpty(finalDir) && !Directory.Exists(finalDir))
                    Directory.CreateDirectory(finalDir);

                if (item.isDirectory)
                {
                    // Thay mới hoàn toàn: bản cập nhật không được để sót file đã bị gỡ khỏi skill.
                    TryDeleteDirectory(final);
                    Directory.Move(staged, final);
                }
                else
                {
                    File.Copy(staged, final, overwrite: true);
                }

                return written;
            }
            finally
            {
                TryDeleteDirectory(staging);
            }
        }

        /// <summary>
        /// Entry chỉ hợp lệ khi nằm trong đích cài của item: bằng installPath (item 1 file) hoặc nằm
        /// dưới "installPath/". Chặn path tuyệt đối, ổ đĩa Windows và mọi dạng ".." (zip-slip).
        /// </summary>
        private static bool IsInsideTarget(string entryName, string installPath)
        {
            if (string.IsNullOrEmpty(entryName) || string.IsNullOrEmpty(installPath))
                return false;
            if (entryName.StartsWith("/", StringComparison.Ordinal) || entryName.Contains(":"))
                return false;
            if (entryName == ".." || entryName.StartsWith("../", StringComparison.Ordinal) ||
                entryName.Contains("/../") || entryName.EndsWith("/..", StringComparison.Ordinal))
                return false;

            return string.Equals(entryName, installPath, StringComparison.Ordinal) ||
                   entryName.StartsWith(installPath + "/", StringComparison.Ordinal);
        }

        /// <summary>Set cờ +x cho script vừa giải nén (macOS/Linux). Windows không có khái niệm này.</summary>
        private static void MakeExecutable(List<string> files)
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
                return;

            var targets = new List<string>();
            foreach (var rel in files)
            {
                string ext = Path.GetExtension(rel)?.ToLowerInvariant();
                if (Array.IndexOf(EXECUTABLE_EXT, ext) < 0)
                    continue;
                targets.Add("\"" + FullPath(rel) + "\"");
            }

            if (targets.Count == 0)
                return;

            try
            {
                var info = new System.Diagnostics.ProcessStartInfo("/bin/chmod")
                {
                    Arguments = "+x " + string.Join(" ", targets),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = System.Diagnostics.Process.Start(info);
                process?.WaitForExit(5000);
            }
            catch (Exception e)
            {
                // Không chặn cài đặt: file vẫn đúng nội dung, user chỉ cần tự chmod nếu muốn chạy.
                Debug.LogWarning($"[FeatureHub] Không set được cờ thực thi: {e.Message}");
            }
        }

        #endregion

        #region Private Methods — Paths & Hash

        private static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        private static string TempDir()
        {
            return Path.Combine(ProjectRoot(), "Temp", FeatureHubConstants.TEMP_DIR_NAME, "ai");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FeatureHub] Không xóa được file tạm '{path}': {e.Message}");
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FeatureHub] Không xóa được thư mục cũ '{path}': {e.Message}");
            }
        }

        private static string Sha256File(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(stream);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        #endregion
    }
}
