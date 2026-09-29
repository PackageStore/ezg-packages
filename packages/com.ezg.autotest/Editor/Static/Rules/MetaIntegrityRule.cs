using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Tính toàn vẹn .meta trên TOÀN BỘ thư mục Assets (không theo phạm vi quét): GUID trùng giữa các file .meta,
    ///     asset thiếu .meta, .meta mồ côi. Chỉ đọc dòng "guid:" của mỗi meta — quét hệ thống file, không qua AssetDatabase.
    /// </summary>
    public sealed class MetaIntegrityRule : IStaticRule
    {
        const string ISSUE_CATEGORY = "Asset.Meta";
        const string GUID_PREFIX = "guid: ";
        const int MAX_META_HEADER_LINES = 8;
        const int MAX_PATHS_IN_MESSAGE = 10;

        /// <summary>Thư mục Unity coi là MỘT asset (plugin native) — file bên trong không có .meta riêng.</summary>
        static readonly HashSet<string> OPAQUE_FOLDER_EXTENSIONS = new(StringComparer.OrdinalIgnoreCase)
            { ".bundle", ".framework", ".xcframework", ".androidlib", ".plugin" };

        public string Id => "meta-guid";
        public string Name => "File .meta & GUID trùng";

        public string Description =>
            "Quét toàn bộ Assets: GUID trùng giữa các file .meta (tham chiếu nhảy lung tung), asset thiếu .meta " +
            "(máy khác sinh GUID mới → mất tham chiếu), file .meta mồ côi.";

        public string Category => "Asset";
        public int Order => 5;

        public async Task Run(AutoTestContext ctx)
        {
            var sink = new AssetIssueSink(ctx, ISSUE_CATEGORY);
            var projectRoot = StaticCheckUtil.ProjectRoot();
            var prefixLength = projectRoot.Length + 1;
            var assets = new List<string>();
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var metas = new List<string>();
            var budget = new FrameBudget();

            // 1. Duyệt đĩa (bỏ file/thư mục Unity bỏ qua: ẩn, đuôi "~", "cvs", ".tmp").
            var stack = new Stack<DirectoryInfo>();
            stack.Push(new DirectoryInfo(Path.Combine(projectRoot, "Assets")));
            var visited = 0;
            while (stack.Count > 0)
            {
                await budget.Tick(ctx, visited++, 0, "Duyệt thư mục");
                var dir = stack.Pop();
                IEnumerable<FileSystemInfo> entries;
                try
                {
                    entries = dir.EnumerateFileSystemInfos();
                }
                catch (Exception e)
                {
                    sink.ReportUnreadable(ToRelative(dir.FullName, prefixLength), e);
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (AssetRuleUtil.IsIgnoredByUnity(entry.Name)) continue;
                    var rel = ToRelative(entry.FullName, prefixLength);
                    if (entry is DirectoryInfo sub)
                    {
                        assets.Add(rel);
                        folders.Add(rel);
                        if (!OPAQUE_FOLDER_EXTENSIONS.Contains(Path.GetExtension(sub.Name))) stack.Push(sub);
                    }
                    else if (entry.Name.EndsWith(AssetRuleUtil.META_EXTENSION, StringComparison.OrdinalIgnoreCase))
                    {
                        metas.Add(rel);
                    }
                    else
                    {
                        assets.Add(rel);
                    }
                }
            }

            var assetSet = new HashSet<string>(assets, StringComparer.OrdinalIgnoreCase);
            var metaSet = new HashSet<string>(metas, StringComparer.OrdinalIgnoreCase);
            assets.Sort(ComparePaths);
            metas.Sort(ComparePaths);

            // 2. GUID: chỉ đọc vài dòng đầu mỗi .meta.
            var firstByGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var duplicates = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var duplicateOrder = new List<string>();
            for (var i = 0; i < metas.Count; i++)
            {
                await budget.Tick(ctx, i, metas.Count, "Đọc GUID");
                var meta = metas[i];
                var asset = meta.Substring(0, meta.Length - AssetRuleUtil.META_EXTENSION.Length);
                if (!assetSet.Contains(asset))
                {
                    sink.Report(Severity.Minor, "File .meta mồ côi (không có asset)",
                        "Asset đã bị xoá/đổi tên nhưng file .meta vẫn còn (thường do commit thiếu). Xoá file .meta này; " +
                        "nếu asset lẽ ra phải có thì khôi phục asset từ git.", meta, null, "Có asset đi kèm",
                        "Không có asset");
                    continue;
                }

                string guid;
                try
                {
                    guid = ReadGuid(Path.Combine(projectRoot, meta));
                }
                catch (Exception e)
                {
                    sink.ReportUnreadable(meta, e);
                    continue;
                }

                if (string.IsNullOrEmpty(guid))
                {
                    sink.Report(Severity.Major, "File .meta không có GUID",
                        "File .meta hỏng (không có dòng 'guid:') → Unity sẽ sinh GUID mới, mọi tham chiếu tới asset bị mất. " +
                        "Khôi phục file .meta từ git.", meta, null, "Có dòng guid", "Không có");
                    continue;
                }

                if (firstByGuid.TryGetValue(guid, out var first))
                {
                    if (!duplicates.TryGetValue(guid, out var list))
                    {
                        list = new List<string> { first };
                        duplicates[guid] = list;
                        duplicateOrder.Add(guid);
                    }

                    list.Add(asset);
                }
                else
                {
                    firstByGuid[guid] = asset;
                }
            }

            foreach (var guid in duplicateOrder)
            {
                var list = duplicates[guid];
                sink.Report(Severity.Critical, "GUID trùng giữa nhiều asset",
                    $"{list.Count} asset dùng chung GUID {guid} (thường do copy file kèm .meta). Unity chỉ giữ một asset, " +
                    "tham chiếu có thể trỏ nhầm sang asset kia. Xoá .meta của bản copy để Unity sinh GUID mới (kiểm tra lại " +
                    "tham chiếu sau đó):" + AssetRuleUtil.BulletList(list, MAX_PATHS_IN_MESSAGE),
                    list[0], null, "Mỗi GUID thuộc đúng 1 asset", $"{list.Count} asset");
            }

            // 3. Asset thiếu .meta — thư mục thiếu meta thì gộp luôn các file con.
            var missingCount = 0;
            string collapsedFolder = null;
            var collapsedChildren = 0;
            TestIssue folderIssue = null;
            foreach (var asset in assets)
            {
                if (metaSet.Contains(asset + AssetRuleUtil.META_EXTENSION)) continue;
                missingCount++;
                if (collapsedFolder != null && asset.StartsWith(collapsedFolder + "/", StringComparison.OrdinalIgnoreCase))
                {
                    collapsedChildren++;
                    continue;
                }

                FinishFolder(folderIssue, collapsedChildren);
                collapsedChildren = 0;
                var isFolder = folders.Contains(asset);
                var issue = sink.Report(Severity.Major, "Asset thiếu file .meta",
                    (isFolder ? "Thư mục" : "File") + " không có .meta → Unity sinh GUID mới khi import, máy khác mất mọi " +
                    "tham chiếu tới asset này. Mở Unity để sinh .meta rồi commit kèm asset (hoặc khôi phục .meta từ git).",
                    asset, null, "Có file .meta", "Thiếu");
                collapsedFolder = isFolder ? asset : null;
                folderIssue = isFolder ? issue : null;
            }

            FinishFolder(folderIssue, collapsedChildren);

            ctx.Metric("Asset trong Assets", assets.Count);
            ctx.Metric("File .meta", metas.Count);
            ctx.Metric("GUID trùng", duplicateOrder.Count);
            ctx.Metric("Asset thiếu .meta", missingCount);
            sink.Flush();
        }

        static void FinishFolder(TestIssue folderIssue, int children)
        {
            if (folderIssue != null && children > 0)
                folderIssue.message += $"\n{children} file/thư mục con bên trong cũng thiếu .meta.";
        }

        /// <summary>So sánh ordinal nhưng coi '/' nhỏ nhất → file con luôn đứng ngay sau thư mục cha.</summary>
        static int ComparePaths(string a, string b)
        {
            var n = Math.Min(a.Length, b.Length);
            for (var i = 0; i < n; i++)
            {
                var ca = a[i] == '/' ? '\u0001' : a[i];
                var cb = b[i] == '/' ? '\u0001' : b[i];
                if (ca != cb) return ca < cb ? -1 : 1;
            }

            return a.Length.CompareTo(b.Length);
        }

        static string ToRelative(string fullName, int prefixLength)
        {
            var p = fullName.Replace('\\', '/');
            return p.Length > prefixLength ? p.Substring(prefixLength) : p;
        }

        static string ReadGuid(string absolutePath)
        {
            using (var reader = new StreamReader(absolutePath))
            {
                for (var i = 0; i < MAX_META_HEADER_LINES; i++)
                {
                    var line = reader.ReadLine();
                    if (line == null) break;
                    if (line.StartsWith(GUID_PREFIX, StringComparison.Ordinal))
                        return line.Substring(GUID_PREFIX.Length).Trim();
                }
            }

            return null;
        }
    }
}
