using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Ezg.UserSegment.Editor
{
    /// <summary>
    ///     Menu <c>Ezg &gt; User Segment &gt; Init</c>: copy tầng Integration (nằm ẩn trong <c>Samples~/Integration</c> của
    ///     package, Unity không compile) vào đúng vị trí chuẩn của project template
    ///     <see cref="TARGET_PATH" />, giữ nguyên <c>.meta</c> nên GUID không đổi (catalog asset không mất script).
    ///     Package KHÔNG khai field <c>samples</c> trong package.json để Package Manager không hiện nút Import —
    ///     menu này là cửa vào duy nhất, tránh dev import ra <c>Assets/Samples/</c> gây trùng class.
    /// </summary>
    public static class UserSegmentInitMenu
    {
        public const string MENU_PATH = "Ezg/User Segment/Init";
        public const string TARGET_PATH = "Assets/_Project/Features/System/UserSegment/Integration";

        private const string SAMPLE_REL_PATH = "Samples~/Integration";
        private const string EDITOR_ASSEMBLY = "Ezg.Package.UserSegment.Editor";
        private const string LOG_PREFIX = "[UserSegment] Init:";
        private static readonly Regex AsmdefReferenceRegex = new Regex("\"([A-Za-z0-9_.]+)\"", RegexOptions.Compiled);

        [MenuItem(MENU_PATH, priority = 100)]
        public static void Init()
        {
            var packageRoot = ResolvePackageRoot();
            if (packageRoot == null)
            {
                Debug.LogError($"{LOG_PREFIX} không xác định được thư mục gốc của package (asmdef {EDITOR_ASSEMBLY} không tìm thấy).");
                return;
            }

            var source = Path.Combine(packageRoot, SAMPLE_REL_PATH);
            if (!Directory.Exists(source))
            {
                Debug.LogError($"{LOG_PREFIX} không thấy {source}. Package có thể bị cắt Samples~ khi đóng gói.");
                return;
            }

            var missing = FindMissingAssemblies(source);
            if (missing.Count > 0)
            {
                var msg = $"Integration cần các assembly sau nhưng project chưa có:\n  - {string.Join("\n  - ", missing)}\n\n" +
                          "Project phải sinh từ Unity Game Template (Ezg.Features, Ezg.Tracking…) và đã cài com.ezg.local-notification. Không copy gì.";
                Debug.LogError($"{LOG_PREFIX} {msg}");
                if (!Application.isBatchMode) EditorUtility.DisplayDialog("User Segment Init", msg, "OK");
                return;
            }

            var target = Path.GetFullPath(TARGET_PATH);
            if (Directory.Exists(target))
            {
                var overwrite = Application.isBatchMode || EditorUtility.DisplayDialog("User Segment Init",
                    $"{TARGET_PATH} đã tồn tại.\n\nGhi đè sẽ THAY mọi file cùng tên bằng bản trong package (mất chỉnh sửa riêng của project). File project thêm vào sau vẫn giữ.",
                    "Ghi đè", "Huỷ");
                if (!overwrite)
                {
                    Debug.Log($"{LOG_PREFIX} huỷ, không thay đổi gì.");
                    return;
                }
            }

            var copied = CopyDirectory(source, target);

            // .meta của chính thư mục Integration nằm cạnh nó trong Samples~ — copy để GUID folder ổn định.
            var folderMeta = source + ".meta";
            var targetMeta = target + ".meta";
            if (File.Exists(folderMeta) && !File.Exists(targetMeta))
            {
                File.Copy(folderMeta, targetMeta);
                copied++;
            }

            AssetDatabase.Refresh();
            Debug.Log($"{LOG_PREFIX} đã copy {copied} file vào {TARGET_PATH}.\n" +
                      "Việc còn phải làm tay:\n" +
                      "  1. Mở Resources/UserSegmentCatalog.asset: điền gameId / env / configBaseUrl (+ configToken cho env ≠ prod), map rewards / popups / offers / notifications theo game.\n" +
                      "  2. UserSegmentSeedProvider.cs: sửa 2 TODO (TotalSpendUsd theo giá USD, ProgressMax theo tiến độ gameplay).\n" +
                      "  3. Nếu game có knob độ khó: gán UserSegmentBootstrap.DifficultyKnob TRƯỚC khi core phát PlayerDataLoaded.\n" +
                      "  4. Dev build: UserSegmentDevConfig.json trong Resources là envelope fallback khi chưa có URL.");
        }

        /// <summary>
        ///     Gốc package: qua PackageInfo khi cài từ registry (Library/PackageCache), fallback đi lên từ asmdef Editor
        ///     khi source còn nằm trong Assets/ (repo phát triển package).
        /// </summary>
        private static string ResolvePackageRoot()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UserSegmentInitMenu).Assembly);
            if (info != null && !string.IsNullOrEmpty(info.resolvedPath)) return info.resolvedPath;

            var asmdefPath = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(EDITOR_ASSEMBLY);
            if (string.IsNullOrEmpty(asmdefPath)) return null;
            var editorDir = Path.GetDirectoryName(Path.GetFullPath(asmdefPath));
            return editorDir == null ? null : Path.GetDirectoryName(editorDir);
        }

        /// <summary>Đọc "references" trong mọi asmdef của sample, so với các assembly project đang compile.</summary>
        private static List<string> FindMissingAssemblies(string sampleDir)
        {
            var available = new HashSet<string>(StringComparer.Ordinal);
            foreach (var asm in CompilationPipeline.GetAssemblies()) available.Add(asm.name);

            var missing = new List<string>();
            foreach (var asmdef in Directory.GetFiles(sampleDir, "*.asmdef", SearchOption.AllDirectories))
            {
                var json = File.ReadAllText(asmdef);
                var refStart = json.IndexOf("\"references\"", StringComparison.Ordinal);
                if (refStart < 0) continue;
                var refEnd = json.IndexOf(']', refStart);
                if (refEnd < 0) continue;
                var block = json.Substring(refStart + "\"references\"".Length, refEnd - refStart - "\"references\"".Length);
                foreach (Match m in AsmdefReferenceRegex.Matches(block))
                {
                    var name = m.Groups[1].Value;
                    if (name.StartsWith("GUID:", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!available.Contains(name) && !missing.Contains(name)) missing.Add(name);
                }
            }

            return missing;
        }

        private static int CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            var count = 0;
            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
                count++;
            }

            foreach (var dir in Directory.GetDirectories(source))
                count += CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
            return count;
        }
    }
}
