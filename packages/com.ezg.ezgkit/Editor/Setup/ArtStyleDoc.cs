#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Ezg.Editor.Shared.EzgKit;

namespace Ezg.Editor.Shared.Setup
{
    /// <summary>
    ///     Đọc <c>.claude/docs/ArtStyle.md</c> — nguồn chuẩn duy nhất về visual của game mà mọi skill visual
    ///     (mockup, create-ui, refactor-ui, gen-icon, ui-visual-reviewer) đọc trước khi quyết.
    ///     <para>
    ///         Kit không viết nội dung style (đó là việc của dev + Claude theo § Bootstrap của template). Kit chỉ:
    ///         cho biết file đang ở trạng thái nào, mục nào còn là khung trống, khai khối <c>art-style-boards</c>
    ///         (thư mục art để dựng board tham chiếu) và chạy <c>art-style-board.py</c>.
    ///     </para>
    /// </summary>
    internal sealed class ArtStyleDoc
    {
        #region Types

        internal sealed class Section
        {
            internal string Number;
            internal string Title;
            internal bool Filled;
        }

        internal sealed class Board
        {
            internal string Name;
            internal readonly List<string> Paths = new();

            internal bool ImageExists => File.Exists(Path.Combine(ProjectPaths.ArtStyleBoardDir, Name + ".png"));

            internal string ImagePath => Path.Combine(ProjectPaths.ArtStyleBoardDir, Name + ".png");
        }

        #endregion

        #region Fields

        private static readonly Regex _status = new(@"^\*\*Status:\*\*\s*([A-Za-z]+)", RegexOptions.Multiline);
        private static readonly Regex _heading = new(@"^##\s+(\d+[a-z]?)\.\s+(.+?)\s*$", RegexOptions.Multiline);
        private static readonly Regex _boardsBlock = new(@"```art-style-boards[ \t]*\n(.*?)```", RegexOptions.Singleline);

        internal bool Exists;
        internal bool TemplateExists;
        internal string Status = "template";
        internal string Title;
        internal readonly List<Section> Sections = new();
        internal readonly List<Board> Boards = new();
        internal bool HasBoardsBlock;

        /// <summary>Mục có trong template mới mà file chưa có (file soạn theo khung cũ).</summary>
        internal readonly List<string> MissingSections = new();

        #endregion

        #region Load

        internal static ArtStyleDoc Load()
        {
            var doc = new ArtStyleDoc
            {
                Exists = File.Exists(ProjectPaths.ArtStyleFile),
                TemplateExists = File.Exists(ProjectPaths.ArtStyleTemplate),
            };
            if (!doc.Exists) return doc;

            var text = File.ReadAllText(ProjectPaths.ArtStyleFile);
            var template = doc.TemplateExists ? File.ReadAllText(ProjectPaths.ArtStyleTemplate) : null;

            var status = _status.Match(text);
            doc.Status = status.Success ? status.Groups[1].Value.Trim().ToLowerInvariant() : "unknown";

            var title = Regex.Match(text, @"^#\s+(.+)$", RegexOptions.Multiline);
            doc.Title = title.Success ? title.Groups[1].Value.Trim() : null;

            var templateSections = template == null ? new Dictionary<string, string>() : SplitSections(template);
            foreach (var pair in SplitSections(text))
            {
                // Mục §0 (giải thích file) và các mục không đánh số không tính.
                if (pair.Key == "0") continue;
                var heading = _heading.Match(pair.Value);
                var body = Body(pair.Value);
                templateSections.TryGetValue(pair.Key, out var templateBody);
                doc.Sections.Add(new Section
                {
                    Number = pair.Key,
                    Title = heading.Success ? heading.Groups[2].Value : pair.Key,
                    // Status: template = chưa ai soạn (Bootstrap đổi sang draft khi điền xong) — khung khác template
                    // mới chỉ vì template đổi chữ, không phải vì đã điền.
                    Filled = doc.Status != "template" && HasNewContent(body, templateBody == null ? null : Body(templateBody)),
                });
            }

            foreach (var key in templateSections.Keys)
                if (key != "0" && !doc.Sections.Exists(x => x.Number == key))
                    doc.MissingSections.Add(key);

            var block = _boardsBlock.Match(text);
            doc.HasBoardsBlock = block.Success;
            if (block.Success)
                foreach (var raw in block.Groups[1].Value.Split('\n'))
                {
                    var line = raw.Split('#')[0].Trim();
                    var colon = line.IndexOf(':');
                    if (line.Length == 0 || colon <= 0) continue;
                    var board = new Board { Name = line.Substring(0, colon).Trim() };
                    foreach (var path in line.Substring(colon + 1).Split(','))
                        if (path.Trim().Length > 0)
                            board.Paths.Add(path.Trim());
                    doc.Boards.Add(board);
                }

            return doc;
        }

        /// <summary>Số mục §1–§11 đã điền (khác khung template).</summary>
        internal int FilledCount
        {
            get
            {
                var count = 0;
                foreach (var section in Sections)
                    if (section.Filled)
                        count++;
                return count;
            }
        }

        internal bool TitleFilled => !string.IsNullOrEmpty(Title) && Title.IndexOf('<') < 0;

        /// <summary>Tách theo heading cấp 2 có số (<c>## 4. …</c>); heading con <c>### 4b.</c> nằm trong mục cha.</summary>
        private static Dictionary<string, string> SplitSections(string text)
        {
            var result = new Dictionary<string, string>();
            var matches = _heading.Matches(text);
            for (var i = 0; i < matches.Count; i++)
            {
                var start = matches[i].Index;
                var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
                // Mục cuối kéo tới heading cấp 2 không đánh số (## Bootstrap) thì cắt ở đó.
                var tail = Regex.Match(text.Substring(start + matches[i].Length, end - start - matches[i].Length), @"^##\s+[^\d]", RegexOptions.Multiline);
                if (tail.Success) end = start + matches[i].Length + tail.Index;
                result[matches[i].Groups[1].Value] = text.Substring(start, end - start);
            }

            return result;
        }

        private static string Body(string section)
        {
            var newline = section.IndexOf('\n');
            return newline < 0 ? string.Empty : section.Substring(newline + 1);
        }

        private static string Normalize(string text) => Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();

        private static readonly Regex _placeholder = new(@"<[^<>\n]{2,}>");

        /// <summary>
        ///     Mục đã được điền: có ít nhất một dòng nội dung KHÔNG có trong khung template, không chứa placeholder
        ///     <c>&lt;…&gt;</c>, không phải dòng trống / comment / kẻ bảng. So từng dòng (không so cả khối) để file soạn
        ///     theo khung template cũ vẫn được nhận đúng là "chưa điền".
        /// </summary>
        private static bool HasNewContent(string body, string templateBody)
        {
            var known = new HashSet<string>(StringComparer.Ordinal);
            if (templateBody != null)
                foreach (var line in templateBody.Split('\n'))
                    known.Add(Normalize(line));

            var withoutComments = Regex.Replace(body ?? string.Empty, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);
            foreach (var raw in withoutComments.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("```")) continue;
                if (Regex.IsMatch(line, @"^\|?[\s:\-|]+\|?$")) continue;
                if (known.Contains(Normalize(line))) continue;
                if (_placeholder.IsMatch(line)) continue;
                var stripped = Regex.Replace(line, @"\*\*[^*]+\*\*", string.Empty);
                stripped = Regex.Replace(stripped, @"[\|\-\*`>_:\s]", string.Empty);
                if (stripped.Length > 2) return true;
            }

            return false;
        }

        #endregion

        #region Write boards

        /// <summary>
        ///     Ghi lại khối <c>art-style-boards</c> (giữ dòng comment hướng dẫn đầu khối). File chưa có khối thì
        ///     chèn ngay sau heading §3. Trả về false nếu file chưa có.
        /// </summary>
        internal static bool WriteBoards(IList<Board> boards, bool dryRun, List<ChangeRow> changes, out string error)
        {
            error = null;
            if (!File.Exists(ProjectPaths.ArtStyleFile))
            {
                error = "Chưa có .claude/docs/ArtStyle.md — bấm \"Tạo từ template\" trước.";
                return false;
            }

            var text = File.ReadAllText(ProjectPaths.ArtStyleFile);
            var current = Load();

            var sb = new StringBuilder();
            sb.Append("```art-style-boards\n");
            sb.Append("# <tên-board>: <path thư mục hoặc file>, <path>…   (tương đối repo root, quét đệ quy *.png/*.psd)\n");
            foreach (var board in boards)
            {
                if (string.IsNullOrWhiteSpace(board.Name) || board.Paths.Count == 0) continue;
                sb.Append(Slug(board.Name)).Append(": ").Append(string.Join(", ", board.Paths)).Append('\n');
            }

            sb.Append("```");

            var oldBlock = _boardsBlock.Match(text);
            string updated;
            if (oldBlock.Success) updated = text.Substring(0, oldBlock.Index) + sb + text.Substring(oldBlock.Index + oldBlock.Length);
            else
            {
                var section3 = Regex.Match(text, @"^##\s+3\..*$", RegexOptions.Multiline);
                var insertAt = section3.Success ? section3.Index + section3.Length : text.Length;
                updated = text.Substring(0, insertAt) + "\n\n" + sb + "\n" + text.Substring(insertAt);
            }

            var before = string.Join("; ", current.Boards.ConvertAll(b => b.Name + ": " + string.Join(", ", b.Paths)));
            var after = new List<string>();
            foreach (var board in boards)
                if (!string.IsNullOrWhiteSpace(board.Name) && board.Paths.Count > 0)
                    after.Add(Slug(board.Name) + ": " + string.Join(", ", board.Paths));
            var afterText = string.Join("; ", after);
            changes?.Add(new ChangeRow("ArtStyle.md", "art-style-boards", before, afterText, before == afterText));

            if (dryRun || updated == text) return true;
            EzgBackup.Save(ProjectPaths.ArtStyleFile);
            File.WriteAllText(ProjectPaths.ArtStyleFile, updated, new UTF8Encoding(false));
            return true;
        }

        /// <summary>Tên board thành slug an toàn cho tên file PNG.</summary>
        internal static string Slug(string name)
        {
            var slug = Regex.Replace((name ?? string.Empty).Trim().ToLowerInvariant(), @"[^a-z0-9\-_]+", "-").Trim('-');
            return slug.Length == 0 ? "board" : slug;
        }

        /// <summary>Copy template thành ArtStyle.md (khi dự án chưa có).</summary>
        internal static bool CreateFromTemplate(out string error)
        {
            error = null;
            if (File.Exists(ProjectPaths.ArtStyleFile)) return true;
            if (!File.Exists(ProjectPaths.ArtStyleTemplate))
            {
                error = "Không có .claude/docs/ArtStyle.template.md — dự án chưa có agent system (chạy bootstrap của template).";
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ProjectPaths.ArtStyleFile) ?? ProjectPaths.Root);
            File.Copy(ProjectPaths.ArtStyleTemplate, ProjectPaths.ArtStyleFile);
            return true;
        }

        #endregion
    }
}
#endif
