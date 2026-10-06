#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ezg.Editor.Shared.EzgKit
{
    /// <summary>
    ///     <c>.claude/project-profile.json</c> — file khai giá trị riêng của dự án cho agent system (skill,
    ///     script trong <c>.claude/</c> đọc nó thay vì viết cứng). Kit ghi vài key người dùng điền ở cửa sổ
    ///     (tên dự án, nguồn localize) để Claude và các script thấy cùng một giá trị với Editor.
    ///     <para>
    ///         Ghi bằng <see cref="JsonText.SetString" />: chỉ thay đúng giá trị, giữ nguyên format + các key
    ///         <c>$comment</c> của file. Dự án không có agent system (không có file) thì mọi lệnh ghi là no-op.
    ///     </para>
    /// </summary>
    internal static class ProjectProfileFile
    {
        internal static bool Exists => File.Exists(ProjectPaths.ProfileFile);

        internal static JsonObject Load() =>
            Exists ? MiniJson.ParseObject(File.ReadAllText(ProjectPaths.ProfileFile)) : null;

        /// <summary>Giá trị string theo đường dẫn key; null = file / key không có.</summary>
        internal static string Get(params string[] path)
        {
            var node = Load();
            for (var i = 0; node != null && i < path.Length - 1; i++) node = node.Obj(path[i]);
            if (node == null || !node.TryGet(path[path.Length - 1], out var value)) return null;
            return value as string;
        }

        /// <summary>
        ///     Ghi nhiều key một lượt (một bản backup). Mỗi entry = (đường dẫn key, giá trị). Giá trị null bỏ
        ///     qua. Trả về false nếu file hỏng.
        /// </summary>
        internal static bool Set(IEnumerable<(string[] Path, string Value)> entries, bool dryRun,
            List<ChangeRow> changes, out string error)
        {
            error = null;
            if (!Exists)
            {
                changes?.Add(new ChangeRow("project-profile.json", "(file)", "không có", "bỏ qua (dự án không dùng agent system)", true));
                return true;
            }

            var text = File.ReadAllText(ProjectPaths.ProfileFile);
            var original = text;
            foreach (var (path, value) in entries)
            {
                if (value == null) continue;
                var current = Get(path) ?? string.Empty;
                var label = string.Join(".", path);
                var matched = current == value;
                changes?.Add(new ChangeRow("project-profile.json", label, current, value, matched));
                if (matched) continue;
                if (!JsonText.SetString(ref text, path, value, out error)) return false;
            }

            if (dryRun || text == original) return true;

            EzgBackup.Save(ProjectPaths.ProfileFile);
            File.WriteAllText(ProjectPaths.ProfileFile, text, new UTF8Encoding(false));
            return true;
        }
    }
}
#endif
