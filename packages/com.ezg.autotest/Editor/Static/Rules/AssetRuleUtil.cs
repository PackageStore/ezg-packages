using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Bộ ghi issue có giới hạn cho một luật asset — vượt ngưỡng thì đếm bớt và cuối luật gom thành đúng 1
    ///     dòng Info "... còn N lỗi nữa bị lược" (report không bị ngập khi project có hàng nghìn lỗi giống nhau).
    /// </summary>
    internal sealed class AssetIssueSink
    {
        public const int DEFAULT_MAX_ISSUES = 500;

        readonly AutoTestContext _ctx;
        readonly string _category;
        readonly int _max;
        int _reported;
        int _dropped;

        public AssetIssueSink(AutoTestContext ctx, string category, int max = DEFAULT_MAX_ISSUES)
        {
            _ctx = ctx;
            _category = category;
            _max = max;
        }

        /// <summary>Số issue đã ghi thật.</summary>
        public int Reported => _reported;

        /// <summary>Ghi issue; trả null nếu đã chạm giới hạn (issue bị lược).</summary>
        public TestIssue Report(Severity severity, string title, string message = null, string location = null,
            string objectPath = null, string expected = null, string actual = null)
        {
            if (_reported >= _max)
            {
                _dropped++;
                return null;
            }

            _reported++;
            return _ctx.Report(severity, _category, title, message, location, objectPath, expected, actual);
        }

        /// <summary>Asset không đọc được (hỏng, lỗi import…) — ghi Info rồi chạy tiếp asset khác.</summary>
        public void ReportUnreadable(string assetPath, Exception e)
        {
            Report(Severity.Info, "Không đọc được asset", $"{e.GetType().Name}: {e.Message}", assetPath);
        }

        /// <summary>Gọi một lần ở cuối luật.</summary>
        public void Flush()
        {
            if (_dropped <= 0) return;
            _ctx.Report(Severity.Info, _category, $"... còn {_dropped} lỗi nữa bị lược",
                $"Luật đã chạm giới hạn {_max} lỗi. Sửa bớt các lỗi phía trên rồi chạy lại để thấy phần còn lại.");
            _dropped = 0;
        }
    }

    /// <summary>
    ///     Nhường frame theo thời gian (không theo số phần tử) — dùng cho vòng lặp rẻ (đọc file text, stat file)
    ///     có hàng chục nghìn phần tử, nơi yield mỗi 40 phần tử sẽ làm luật chạy chậm vô ích.
    /// </summary>
    internal sealed class FrameBudget
    {
        const double BUDGET_MS = 30d;

        readonly Stopwatch _watch = Stopwatch.StartNew();

        public async Task Tick(AutoTestContext ctx, int index, int total, string label)
        {
            ctx.ThrowIfCancelled();
            if (_watch.Elapsed.TotalMilliseconds < BUDGET_MS) return;
            if (total > 0) ctx.Session.ReportProgress(ctx.Result, $"{label} {index}/{total}");
            await ctx.NextFrame();
            _watch.Restart();
        }
    }

    /// <summary>Tra GUID → asset có tồn tại không, có cache (một file scene có thể lặp lại cùng GUID hàng nghìn lần).</summary>
    internal sealed class GuidResolver
    {
        readonly Dictionary<string, bool> _missing = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>GUID không trỏ tới asset nào trong project (bỏ qua GUID built-in của Unity).</summary>
        public bool IsMissing(string guid)
        {
            if (string.IsNullOrEmpty(guid) || AssetRuleUtil.IsBuiltinGuid(guid)) return false;
            if (_missing.TryGetValue(guid, out var missing)) return missing;
            missing = string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid));
            _missing[guid] = missing;
            return missing;
        }
    }

    /// <summary>Một file trên đĩa trong phạm vi quét.</summary>
    internal struct ScopeFile
    {
        public string Path;
        public long Size;
    }

    /// <summary>Tiện ích dùng chung cho các luật asset (tách khỏi StaticCheckUtil để không đụng file của suite).</summary>
    internal static class AssetRuleUtil
    {
        public const double BYTES_PER_MB = 1024d * 1024d;
        public const string PREFAB_EXTENSION = ".prefab";
        public const string META_EXTENSION = ".meta";

        /// <summary>GUID của resource built-in (unity default resources, unity_builtin_extra…) đều có 16 số 0 đầu.</summary>
        const string BUILTIN_GUID_PREFIX = "0000000000000000";

        const string YAML_MAGIC = "%YAML";
        const int MAX_LIST_LINES_DEFAULT = 10;

        public static bool IsBuiltinGuid(string guid)
        {
            return guid.StartsWith(BUILTIN_GUID_PREFIX, StringComparison.Ordinal);
        }

        public static double ToMb(long bytes)
        {
            return bytes / BYTES_PER_MB;
        }

        /// <summary>File asset serialize dạng text (bắt đầu bằng "%YAML") — file binary bị bỏ qua khi quét text.</summary>
        public static bool IsYamlFile(string absolutePath)
        {
            var buffer = new byte[YAML_MAGIC.Length];
            using (var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read < buffer.Length) return false;
            }

            return Encoding.ASCII.GetString(buffer) == YAML_MAGIC;
        }

        /// <summary>Prefab (.prefab) trong phạm vi quét — loại model (.fbx) mà filter "t:Prefab" có thể trả về.</summary>
        public static List<string> FindPrefabs(StaticCheckConfig cfg)
        {
            var result = new List<string>();
            foreach (var path in StaticCheckUtil.FindAssets("t:Prefab", cfg))
                if (path.EndsWith(PREFAB_EXTENSION, StringComparison.OrdinalIgnoreCase))
                    result.Add(path);
            return result;
        }

        /// <summary>Asset theo filter AssetDatabase, chỉ giữ đuôi mong muốn.</summary>
        public static List<string> FindAssetsWithExtension(string filter, StaticCheckConfig cfg, string extension)
        {
            var result = new List<string>();
            foreach (var path in StaticCheckUtil.FindAssets(filter, cfg))
                if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    result.Add(path);
            return result;
        }

        /// <summary>Unity bỏ qua file/thư mục ẩn (".x"), đuôi "~", tên "cvs" và đuôi ".tmp" — không import, không cần meta.</summary>
        public static bool IsIgnoredByUnity(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            return name[0] == '.' || name.EndsWith("~", StringComparison.Ordinal) ||
                   name.Equals("cvs", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     Duyệt đĩa MỘT lượt qua các thư mục include (không lặp theo từng đuôi như FindFiles), bỏ .meta và
        ///     file Unity bỏ qua. <paramref name="extensions" /> rỗng = mọi đuôi.
        /// </summary>
        public static List<ScopeFile> EnumerateScopeFiles(StaticCheckConfig cfg, ICollection<string> extensions = null)
        {
            var root = StaticCheckUtil.ProjectRoot();
            var folders = cfg.includeFolders != null && cfg.includeFolders.Count > 0
                ? cfg.includeFolders
                : new List<string> { "Assets" };
            var extSet = extensions != null && extensions.Count > 0
                ? new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase)
                : null;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<ScopeFile>();
            foreach (var folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                var abs = Path.Combine(root, folder);
                if (!Directory.Exists(abs)) continue;
                var stack = new Stack<DirectoryInfo>();
                stack.Push(new DirectoryInfo(abs));
                while (stack.Count > 0)
                {
                    var dir = stack.Pop();
                    IEnumerable<FileSystemInfo> entries;
                    try
                    {
                        entries = dir.EnumerateFileSystemInfos();
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    foreach (var entry in entries)
                    {
                        if (IsIgnoredByUnity(entry.Name)) continue;
                        if (entry is DirectoryInfo sub)
                        {
                            stack.Push(sub);
                            continue;
                        }

                        if (entry.Name.EndsWith(META_EXTENSION, StringComparison.OrdinalIgnoreCase)) continue;
                        if (extSet != null && !extSet.Contains(Path.GetExtension(entry.Name))) continue;
                        var rel = StaticCheckUtil.ToAssetPath(entry.FullName);
                        if (!StaticCheckUtil.InScope(rel, cfg) || !seen.Add(rel)) continue;
                        result.Add(new ScopeFile { Path = rel, Size = ((FileInfo)entry).Length });
                    }
                }
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            return result;
        }

        /// <summary>Danh sách gạch đầu dòng, tối đa <paramref name="max" /> dòng + "… và N mục khác".</summary>
        public static string BulletList(IList<string> items, int max = MAX_LIST_LINES_DEFAULT)
        {
            var sb = new StringBuilder();
            var count = Math.Min(items.Count, max);
            for (var i = 0; i < count; i++) sb.Append("\n- ").Append(items[i]);
            if (items.Count > max) sb.Append("\n… và ").Append(items.Count - max).Append(" mục khác");
            return sb.ToString();
        }

        /// <summary>Compile danh sách regex (bỏ qua regex sai cú pháp thay vì làm hỏng cả luật).</summary>
        public static List<Regex> CompilePatterns(IEnumerable<string> patterns)
        {
            var result = new List<Regex>();
            if (patterns == null) return result;
            foreach (var p in patterns)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                try
                {
                    result.Add(new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
                }
                catch (ArgumentException)
                {
                    AutoTestLog.Warn($"Regex bỏ qua không hợp lệ: {p}");
                }
            }

            return result;
        }

        /// <summary>Object (hoặc một cha của nó trong prefab) có tên khớp regex bỏ qua không.</summary>
        public static bool IsIgnoredObject(Transform t, List<Regex> patterns)
        {
            if (patterns == null || patterns.Count == 0) return false;
            while (t != null)
            {
                foreach (var re in patterns)
                    if (re.IsMatch(t.name))
                        return true;
                t = t.parent;
            }

            return false;
        }

        /// <summary>Scene trong phạm vi quét + mọi scene trong Build Settings (kể cả nằm ngoài phạm vi).</summary>
        public static List<string> CollectScenes(StaticCheckConfig cfg)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var f in EnumerateScopeFiles(cfg, new[] { ".unity" }))
                if (set.Add(f.Path))
                    result.Add(f.Path);
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s == null || string.IsNullOrEmpty(s.path)) continue;
                if (!File.Exists(StaticCheckUtil.AbsolutePath(s.path))) continue;
                if (set.Add(s.path)) result.Add(s.path);
            }

            return result;
        }
    }
}
