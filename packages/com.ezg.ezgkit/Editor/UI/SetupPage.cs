#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Ezg.Editor.Shared.EzgKit
{
    /// <summary>Một việc còn phải làm của một trang (hiện ở khối "Còn việc" và trong API).</summary>
    internal sealed class TodoItem
    {
        internal EzgStatus Level;
        internal string Text;
        internal string Fix;

        internal JsonObject ToJson() =>
            new JsonObject()
                .Set("level", SetupStateText.Id(Level))
                .Set("text", Text ?? string.Empty)
                .Set("fix", Fix ?? string.Empty);
    }

    /// <summary>Kết quả detector của một trang — đọc thẳng từ project, KHÔNG ghi gì.</summary>
    internal sealed class PageReport
    {
        internal SetupState State = SetupState.Todo;
        internal string Summary = string.Empty;
        internal readonly List<TodoItem> Todos = new();

        /// <summary>Số mục đã đạt — để phân biệt "chưa làm gì" với "làm được một phần".</summary>
        internal int OkCount;

        internal PageReport Ok(string what = null)
        {
            OkCount++;
            return this;
        }

        internal PageReport Add(EzgStatus level, string text, string fix = null)
        {
            if (level == EzgStatus.Ok)
            {
                OkCount++;
                return this;
            }

            Todos.Add(new TodoItem { Level = level, Text = text, Fix = fix });
            return this;
        }

        internal EzgStatus Worst
        {
            get
            {
                var worst = EzgStatus.None;
                foreach (var todo in Todos)
                    if (todo.Level > worst)
                        worst = todo.Level;
                return worst;
            }
        }

        /// <summary>Suy trạng thái trang từ các mục: lỗi → Error; còn việc → Partial/Todo; sạch → Done.</summary>
        internal PageReport Resolve(string summary = null)
        {
            if (summary != null) Summary = summary;
            var worst = Worst;
            State = worst switch
            {
                EzgStatus.Error => SetupState.Error,
                EzgStatus.Warn => OkCount > 0 ? SetupState.Partial : SetupState.Todo,
                _ => SetupState.Done,
            };
            return this;
        }
    }

    /// <summary>Kết quả một lượt "Xem thay đổi" / "Áp dụng".</summary>
    internal sealed class ApplyResult
    {
        internal bool Ok = true;
        internal bool DryRun;
        internal string Error;
        internal readonly List<ChangeRow> Rows = new();
        internal readonly List<string> Notes = new();

        internal int ChangedCount
        {
            get
            {
                var count = 0;
                foreach (var row in Rows)
                    if (!row.Matched)
                        count++;
                return count;
            }
        }

        internal static ApplyResult Fail(string error) => new() { Ok = false, Error = error };

        internal JsonObject ToJson(string pageId)
        {
            var rows = new List<object>();
            foreach (var row in Rows) rows.Add(row.ToJson());
            var notes = new List<object>();
            foreach (var note in Notes) notes.Add(note);
            return new JsonObject()
                .Set("page", pageId)
                .Set("ok", Ok)
                .Set("dryRun", DryRun)
                .Set("error", Error ?? string.Empty)
                .Set("changed", ChangedCount)
                .Set("rows", rows)
                .Set("notes", notes);
        }
    }

    /// <summary>Thứ cửa sổ cung cấp cho trang lúc dựng UI.</summary>
    internal interface IPageHost
    {
        /// <summary>Chạy lại detector mọi trang + vẽ lại cột trái (sau khi trang ghi gì đó).</summary>
        void RefreshAll();

        /// <summary>Dựng lại thân trang đang mở (giữ nguyên cột trái).</summary>
        void Rebuild();

        void Toast(string message, EzgStatus level);

        void Open(string pageId);

        /// <summary>Hiện bảng diff (xem trước hoặc kết quả đã ghi) ngay trong thân trang.</summary>
        void ShowResult(ApplyResult result);
    }

    /// <summary>
    ///     Một trang của EzgKit 1.0. Tách hai nửa:
    ///     <list type="bullet">
    ///         <item><b>Core</b> — <see cref="Detect" />, <see cref="GetValues" />, <see cref="Apply" />: KHÔNG UI, KHÔNG
    ///         dialog. <c>EzgKitApi</c> (Claude qua Unity MCP) gọi đúng các hàm này.</item>
    ///         <item><b>UI</b> — <see cref="Build" /> dựng thân trang, <see cref="CollectUi" /> đọc lại ô nhập thành
    ///         cùng dạng giá trị mà <see cref="Apply" /> nhận. Nút footer của cửa sổ chỉ nối hai nửa với nhau.</item>
    ///     </list>
    /// </summary>
    internal abstract class SetupPage
    {
        internal const string GROUP_OVERVIEW = "overview";
        internal const string GROUP_SETUP = "setup";
        internal const string GROUP_ADVANCED = "advanced";

        internal abstract string Id { get; }

        internal abstract string Title { get; }

        /// <summary>Một câu trả lời "trang này để làm gì".</summary>
        internal abstract string Description { get; }

        internal virtual string Group => GROUP_SETUP;

        /// <summary>Trang có ghi vào project được không (hiện nút Xem thay đổi / Áp dụng).</summary>
        internal virtual bool CanApply => false;

        /// <summary>Tính vào tiến độ "x/y xong" ở đầu cột trái.</summary>
        internal virtual bool CountsInProgress => Group == GROUP_SETUP;

        internal abstract PageReport Detect();

        /// <summary>Giá trị hiện tại của trang (để API trả về / điền sẵn ô). Secret che khi <paramref name="maskSecrets" />.</summary>
        internal virtual JsonObject GetValues(bool maskSecrets) => new();

        /// <summary>
        ///     Ghi <paramref name="values" /> (chỉ key có mặt mới được đụng) vào project. <paramref name="dryRun" /> =
        ///     chỉ đối chiếu. KHÔNG dialog — xác nhận là việc của UI / của người gọi API.
        /// </summary>
        internal virtual ApplyResult Apply(JsonObject values, bool dryRun) =>
            ApplyResult.Fail($"Trang '{Id}' không có thao tác ghi.");

        internal abstract void Build(VisualElement body, IPageHost host);

        /// <summary>Đọc ô nhập trên UI thành giá trị cho <see cref="Apply" />. null = trang không ghi.</summary>
        internal virtual JsonObject CollectUi() => null;

        /// <summary>Câu nhắc thêm trong hộp xác nhận trước khi ghi (thứ không undo được). null = không cần.</summary>
        internal virtual string ApplyWarning => null;

        /// <summary>Trạng thái đã áp marker (để sau / không áp dụng) của người dùng.</summary>
        internal PageReport DetectResolved()
        {
            PageReport report;
            try
            {
                report = Detect();
            }
            catch (System.Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
                report = new PageReport();
                report.Add(EzgStatus.Error, "Detector lỗi: " + exception.Message, "Mở Console xem stack trace.").Resolve();
            }

            report.State = EzgKitState.Resolve(Id, report.State);
            return report;
        }
    }
}
#endif
