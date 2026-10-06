#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Ezg.Editor.Shared.Setup;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ezg.Editor.Shared.EzgKit.Pages
{
    /// <summary>
    ///     ArtStyle — <c>.claude/docs/ArtStyle.md</c> là nguồn chuẩn visual mà mọi skill/agent vẽ UI, icon, mockup
    ///     đọc. Trang này cho thấy file đang ở mức nào, mục nào còn khung trống, khai thư mục art cho board tham
    ///     chiếu và dựng board. Phần viết nội dung style (palette, chữ, bố cục…) là việc của dev + Claude:
    ///     "Nhờ Claude soạn" ghi yêu cầu để <c>/setup-project</c> làm theo § Bootstrap của template.
    /// </summary>
    internal sealed class ArtStylePage : SetupPage
    {
        internal const string REQUEST = "artstyle";

        private readonly List<(TextField Name, TextField Paths)> _boardRows = new();
        private VisualElement _boardList;
        private Label _log;
        private bool _building;

        internal override string Id => PageIds.ARTSTYLE;

        internal override string Title => "ArtStyle";

        internal override string Description =>
            "Nguồn chuẩn visual của game (.claude/docs/ArtStyle.md): palette, kit UI, chữ, icon, motion. Mọi skill vẽ UI / icon đọc file này trước.";

        internal override bool CanApply => true;

        #region Core

        internal override PageReport Detect()
        {
            var doc = ArtStyleDoc.Load();
            var report = new PageReport();
            if (!doc.Exists)
            {
                report.Add(EzgStatus.Warn, doc.TemplateExists ? "Chưa có ArtStyle.md." : "Dự án chưa có agent system (.claude/docs).",
                    doc.TemplateExists ? "Bấm \"Tạo từ template\" rồi \"Nhờ Claude soạn\"." : "Chạy bootstrap của template.");
                return report.Resolve("Chưa có file.");
            }

            switch (doc.Status)
            {
                case "approved":
                    report.Ok();
                    break;
                case "draft":
                    report.Ok();
                    report.Add(EzgStatus.Warn, "ArtStyle.md đang là bản nháp (Status: draft).", "Dev / hoạ sĩ duyệt rồi đổi Status thành approved.");
                    break;
                default:
                    report.Add(EzgStatus.Warn, "ArtStyle.md còn là khung template (Status: template).", "Bấm \"Nhờ Claude soạn\" — Claude soạn theo § Bootstrap từ art thật của game.");
                    break;
            }

            if (!doc.TitleFilled) report.Add(EzgStatus.Warn, "Tiêu đề còn placeholder <Tên game>.", "Điền tên game ở dòng đầu file.");
            if (doc.MissingSections.Count > 0)
                report.Add(EzgStatus.Warn, $"File theo khung cũ — thiếu mục {string.Join(", ", doc.MissingSections.ConvertAll(x => "§" + x))} của template mới.",
                    "Nhờ Claude cập nhật khung (giữ nội dung đã điền, thêm mục mới từ ArtStyle.template.md).");

            var empty = new List<string>();
            foreach (var section in doc.Sections)
                if (!section.Filled)
                    empty.Add("§" + section.Number);
            if (empty.Count > 0) report.Add(EzgStatus.Warn, $"{empty.Count} mục còn khung trống: {string.Join(", ", empty)}.", "Soạn theo § Bootstrap của file.");
            else if (doc.Sections.Count > 0) report.Ok();

            if (doc.Boards.Count == 0) report.Add(EzgStatus.Warn, "Chưa khai board art (khối art-style-boards).", "Thêm thư mục kit UI / art chính ở card Board rồi Áp dụng.");
            else
            {
                var missing = 0;
                foreach (var board in doc.Boards)
                    if (!board.ImageExists)
                        missing++;
                if (missing > 0) report.Add(EzgStatus.Warn, $"{missing} board chưa dựng ảnh.", "Bấm \"Dựng board\".");
                else report.Ok();
            }

            return report.Resolve($"Status: {doc.Status} · {doc.FilledCount}/{doc.Sections.Count} mục đã điền · {doc.Boards.Count} board.");
        }

        internal override JsonObject GetValues(bool maskSecrets)
        {
            var doc = ArtStyleDoc.Load();
            var boards = new List<object>();
            foreach (var board in doc.Boards)
                boards.Add(new JsonObject()
                    .Set("name", board.Name)
                    .Set("paths", new List<string>(board.Paths))
                    .Set("image", board.ImageExists ? ProjectPaths.Rel(board.ImagePath) : string.Empty));
            var sections = new List<object>();
            foreach (var section in doc.Sections)
                sections.Add(new JsonObject().Set("section", section.Number).Set("title", section.Title).Set("filled", section.Filled));
            return new JsonObject()
                .Set("exists", doc.Exists)
                .Set("path", ".claude/docs/ArtStyle.md")
                .Set("status", doc.Status)
                .Set("title", doc.Title ?? string.Empty)
                .Set("sections", sections)
                .Set("missingSections", new List<string>(doc.MissingSections))
                .Set("boards", boards)
                .Set("requested", EzgKitState.Requests().Contains(REQUEST));
        }

        /// <summary>Chỉ ghi khối <c>art-style-boards</c> (<c>boards: [{name, paths:[…]}]</c>); <c>createFromTemplate</c> = true tạo file trước.</summary>
        internal override ApplyResult Apply(JsonObject values, bool dryRun)
        {
            var result = new ApplyResult { DryRun = dryRun };
            if (values.Bool("createFromTemplate") && !File.Exists(ProjectPaths.ArtStyleFile))
            {
                result.Rows.Add(new ChangeRow("ArtStyle.md", "(file)", "chưa có", "copy từ ArtStyle.template.md", false));
                if (!dryRun && !ArtStyleDoc.CreateFromTemplate(out var error)) return ApplyResult.Fail(error);
                if (dryRun) return result;
            }

            var list = values.List("boards");
            if (list == null) return result;

            var boards = new List<ArtStyleDoc.Board>();
            foreach (var item in list)
            {
                if (item is not JsonObject obj) continue;
                var board = new ArtStyleDoc.Board { Name = obj.Str("name") };
                var paths = obj.List("paths");
                if (paths != null)
                    foreach (var p in paths)
                        if (p is string s && s.Trim().Length > 0)
                            board.Paths.Add(s.Trim());
                if (paths == null && obj.Has("paths"))
                    foreach (var s in obj.Str("paths").Split(','))
                        if (s.Trim().Length > 0)
                            board.Paths.Add(s.Trim());
                boards.Add(board);
            }

            foreach (var board in boards)
            foreach (var path in board.Paths)
                if (!Directory.Exists(ProjectPaths.Abs(path)) && !File.Exists(ProjectPaths.Abs(path)))
                    result.Notes.Add($"Board \"{board.Name}\": không thấy \"{path}\" (đường dẫn tương đối repo root).");

            if (!ArtStyleDoc.WriteBoards(boards, dryRun, result.Rows, out var writeError)) return ApplyResult.Fail(writeError);
            return result;
        }

        #endregion

        #region UI

        internal override void Build(VisualElement body, IPageHost host)
        {
            var doc = ArtStyleDoc.Load();

            var file = Ui.Card(body, "ArtStyle.md", doc.Exists
                ? $"Status: {doc.Status} · {doc.FilledCount}/{doc.Sections.Count} mục đã điền"
                : "Chưa có file — tạo từ template rồi để Claude soạn từ art thật của game.");
            var actions = Ui.Row(file, "ezg-actions");
            if (!doc.Exists)
                Ui.Button(actions, "Tạo từ template", () =>
                {
                    if (!ArtStyleDoc.CreateFromTemplate(out var error)) host.Toast(error, EzgStatus.Error);
                    else host.Toast("Đã tạo .claude/docs/ArtStyle.md từ template.", EzgStatus.Ok);
                    host.RefreshAll();
                    host.Rebuild();
                }, "primary");
            else
                Ui.Button(actions, "Mở file", () => InternalEditorUtility.OpenFileAtLineExternal(ProjectPaths.ArtStyleFile, 1), "secondary");

            var requested = EzgKitState.Requests().Contains(REQUEST);
            Ui.Button(actions, requested ? "Đã nhờ Claude (huỷ)" : "Nhờ Claude soạn", () =>
            {
                if (requested) EzgKitState.ClearRequest(REQUEST);
                else
                {
                    EzgKitState.AddRequest(REQUEST);
                    EditorGUIUtility.systemCopyBuffer = "/setup-project artstyle";
                }

                host.Rebuild();
                host.Toast(requested
                    ? "Đã huỷ yêu cầu."
                    : "Đã ghi yêu cầu + copy \"/setup-project artstyle\" — dán vào Claude Code (cùng project) để Claude soạn ArtStyle.md theo § Bootstrap.",
                    requested ? EzgStatus.None : EzgStatus.Ok);
            }, requested ? "secondary" : "primary");

            if (doc.Exists && doc.Sections.Count > 0)
            {
                var sections = Ui.Fold(file, "Các mục", doc.FilledCount < doc.Sections.Count);
                foreach (var section in doc.Sections)
                {
                    var row = Ui.Row(sections, "ezg-todo");
                    row.Add(Ui.Dot(section.Filled ? EzgStatus.Ok : EzgStatus.Warn));
                    row.Add(Ui.Text($"§{section.Number}. {section.Title}" + (section.Filled ? string.Empty : "  — còn khung trống")));
                }
            }

            var boards = Ui.Card(body, "Board tham chiếu",
                "Mỗi board = contact sheet các sprite / PSD của một thư mục kit (quét đệ quy). Agent NHÌN board để biết house style thay vì đoán theo tên file.");
            _boardList = new VisualElement();
            boards.Add(_boardList);
            _boardRows.Clear();
            foreach (var board in doc.Boards) AddBoardRow(board.Name, string.Join(", ", board.Paths));
            if (doc.Boards.Count == 0) AddBoardRow("kit-ui", string.Empty);

            var boardActions = Ui.Row(boards, "ezg-actions");
            Ui.Button(boardActions, "+ Thêm board", () => AddBoardRow(string.Empty, string.Empty), "secondary");
            Ui.Button(boardActions, _building ? "Đang dựng…" : "Dựng board", () => BuildBoards(host), "secondary",
                "Chạy python3 .claude/scripts/art-style-board.py (cần Pillow; PSD cần psd-tools). Áp dụng trước nếu vừa sửa danh sách.");
            _log = Ui.Text(string.Empty, "ezg-log");
            _log.style.display = DisplayStyle.None;
            boards.Add(_log);

            foreach (var board in doc.Boards)
            {
                if (!board.ImageExists) continue;
                var texture = LoadTexture(board.ImagePath);
                if (texture == null) continue;
                boards.Add(Ui.Text(board.Name, "ezg-strong"));
                var image = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit };
                image.AddToClassList("ezg-board");
                image.style.height = Mathf.Min(360, texture.height);
                boards.Add(image);
            }

            if (!File.Exists(ProjectPaths.ArtStyleBoardScript))
                Ui.Message(boards, "Không có .claude/scripts/art-style-board.py — dự án chưa có agent system của template.", EzgStatus.Warn);
        }

        private void AddBoardRow(string name, string paths)
        {
            var row = Ui.Row(_boardList, "ezg-field");
            var nameField = new TextField { value = name };
            nameField.AddToClassList("ezg-input");
            nameField.style.width = 140;
            nameField.style.marginRight = 6;
            nameField.tooltip = "Tên board (thành tên file PNG).";
            var pathsField = new TextField { value = paths };
            pathsField.AddToClassList("ezg-input");
            pathsField.AddToClassList("ezg-grow");
            pathsField.tooltip = "Thư mục / file, ngăn bởi dấu phẩy, tương đối repo root.";
            row.Add(nameField);
            row.Add(pathsField);
            var pick = new Button(() =>
            {
                var picked = EditorUtility.OpenFolderPanel("Thư mục art", Application.dataPath, string.Empty);
                if (string.IsNullOrEmpty(picked)) return;
                var rel = ProjectPaths.IsInsideProject(picked) ? ProjectPaths.Rel(picked) : picked;
                pathsField.value = string.IsNullOrWhiteSpace(pathsField.value) ? rel : pathsField.value + ", " + rel;
            }) { text = "Chọn…" };
            pick.AddToClassList("ezg-btn-mini");
            row.Add(pick);
            var remove = new Button(() =>
            {
                _boardList.Remove(row);
                _boardRows.RemoveAll(r => r.Name == nameField);
            }) { text = "✕" };
            remove.AddToClassList("ezg-btn-mini");
            row.Add(remove);
            _boardRows.Add((nameField, pathsField));
        }

        private void BuildBoards(IPageHost host)
        {
            if (_building) return;
            var python = ProcessRunner.FindPython();
            if (python == null)
            {
                host.Toast("Không tìm thấy python3 trên máy.", EzgStatus.Error);
                return;
            }

            _building = true;
            _log.style.display = DisplayStyle.Flex;
            _log.text = "Đang dựng board…";
            ProcessRunner.RunAsync(python, $"\"{ProjectPaths.ArtStyleBoardScript}\"", ProjectPaths.Root, result =>
            {
                _building = false;
                host.RefreshAll();
                host.Rebuild();
                host.Toast(result.Ok ? "Đã dựng board." : "Dựng board lỗi:\n" + result.Combined, result.Ok ? EzgStatus.Ok : EzgStatus.Error);
            });
        }

        private static Texture2D LoadTexture(string path)
        {
            try
            {
                var texture = new Texture2D(2, 2);
                return texture.LoadImage(File.ReadAllBytes(path)) ? texture : null;
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        internal override JsonObject CollectUi()
        {
            var boards = new List<object>();
            foreach (var (name, paths) in _boardRows)
            {
                if (string.IsNullOrWhiteSpace(name.value) && string.IsNullOrWhiteSpace(paths.value)) continue;
                var list = new List<object>();
                foreach (var p in paths.value.Split(','))
                    if (p.Trim().Length > 0)
                        list.Add(p.Trim());
                boards.Add(new JsonObject().Set("name", name.value).Set("paths", list));
            }

            return new JsonObject().Set("boards", boards).Set("createFromTemplate", true);
        }

        #endregion
    }
}
#endif
