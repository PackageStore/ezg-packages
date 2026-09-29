using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Đọc report.json kéo về từ device và gộp các suite của nó vào report của Editor (qua
    ///     <see cref="AutoTestSession.ExtraSuites" />): đổi id/tên suite theo device, sửa đường dẫn ảnh/đính kèm cho
    ///     khớp thư mục <c>device_&lt;serial&gt;/</c>, tính lại fingerprint issue theo device.
    /// </summary>
    public static class DeviceReportMerger
    {
        /// <summary>Tìm report.json trong <paramref name="folder" /> (kể cả thư mục con), null nếu không có.</summary>
        public static string FindReport(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
            var direct = Path.Combine(folder, DeviceProtocol.REPORT_FILE);
            if (File.Exists(direct)) return direct;
            try
            {
                return Directory.GetFiles(folder, DeviceProtocol.REPORT_FILE, SearchOption.AllDirectories)
                    .OrderBy(p => p.Length)
                    .FirstOrDefault();
            }
            catch (Exception e)
            {
                AutoTestLog.Warn("Không quét được thư mục report device: " + e.Message);
                return null;
            }
        }

        /// <summary>Đọc report (null nếu file hỏng / rỗng).</summary>
        public static TestRunReport Load(string reportPath, out string error)
        {
            error = null;
            try
            {
                var json = File.ReadAllText(reportPath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    error = "report.json rỗng";
                    return null;
                }

                var report = JsonUtility.FromJson<TestRunReport>(json);
                if (report == null) error = "Không parse được report.json";
                return report;
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
        }

        /// <summary>
        ///     Chuyển thư mục vừa kéo về (<paramref name="pulledFolder" />, chứa report.json ở đâu đó bên trong) thành
        ///     <c>&lt;outputDir&gt;/&lt;relFolder&gt;</c> — gốc thư mục là nơi có report.json. Trả đường dẫn report.json mới.
        /// </summary>
        public static string Normalize(string pulledFolder, string outputDir, string relFolder)
        {
            var report = FindReport(pulledFolder);
            if (report == null) return null;
            var root = Path.GetDirectoryName(report)!;
            var target = Path.Combine(outputDir, relFolder);
            if (PathsEqual(root, target)) return report;
            try
            {
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(root, target);
                if (Directory.Exists(pulledFolder) && !PathsEqual(pulledFolder, target))
                    Directory.Delete(pulledFolder, true);
                return Path.Combine(target, DeviceProtocol.REPORT_FILE);
            }
            catch (Exception e)
            {
                AutoTestLog.Warn($"Không chuyển được thư mục report device về {target}: {e.Message}");
                return report;
            }
        }

        /// <summary>
        ///     Gộp suite của report device vào <paramref name="session" />.ExtraSuites. Trả (số case, đạt, lỗi).
        /// </summary>
        /// <param name="label">Tên device hiển thị (model, duy nhất trong lượt chạy).</param>
        /// <param name="relFolder">Thư mục tương đối (trong report Editor) chứa file của device, vd "device_R58M…".</param>
        public static (int total, int passed, int failed) Merge(AutoTestSession session, TestRunReport deviceReport,
            string label, string relFolder)
        {
            int total = 0, passed = 0, failed = 0;
            if (deviceReport?.suites == null) return (0, 0, 0);
            var prefix = relFolder.Replace('\\', '/').TrimEnd('/') + "/";
            foreach (var suite in deviceReport.suites)
            {
                var originalId = suite.suiteId;
                suite.suiteId = $"device:{label}:{originalId}";
                suite.name = $"[{label}] {suite.name}";
                suite.mode = ExecutionMode.Device;
                foreach (var c in suite.cases)
                {
                    c.suiteId = suite.suiteId;
                    c.device = label;
                    foreach (var a in c.attachments) a.path = Rebase(prefix, a.path);
                    foreach (var issue in c.issues)
                    {
                        issue.screenshot = Rebase(prefix, issue.screenshot);
                        // Fingerprint theo device: cùng lỗi trên 2 máy là 2 dòng riêng khi so sánh lượt trước/sau.
                        issue.id = AutoTestExecutor.Fingerprint(suite.suiteId, c.caseId, issue.category, issue.title,
                            issue.location, issue.objectPath);
                    }

                    total++;
                    if (c.status == TestStatus.Passed || c.status == TestStatus.Warning) passed++;
                    else if (AutoTestStatusUtil.IsBad(c.status)) failed++;
                }

                suite.status = AutoTestStatusUtil.Aggregate(suite.cases);
                session.ExtraSuites.Add(suite);
            }

            return (total, passed, failed);
        }

        static string Rebase(string prefix, string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var p = path.Replace('\\', '/');
            if (Path.IsPathRooted(p) || p.StartsWith(prefix, StringComparison.Ordinal) || p.Contains("://")) return p;
            return prefix + p.TrimStart('/');
        }

        static bool PathsEqual(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
