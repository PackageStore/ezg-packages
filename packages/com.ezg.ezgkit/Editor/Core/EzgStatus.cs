#if UNITY_EDITOR
namespace Ezg.Editor.Shared.EzgKit
{
    /// <summary>
    ///     Mức độ của MỘT mục kiểm (một dòng readiness, một ô config). Bốn mức, vì đó là bốn cách xử lý
    ///     khác nhau của người dùng: kệ nó (<see cref="None" />), xong rồi (<see cref="Ok" />), phải làm gì
    ///     đó (<see cref="Warn" />), hỏng phải sửa ngay (<see cref="Error" />).
    ///     <para>
    ///         Khác <see cref="SetupState" />: cái này là của từng mục nhỏ, cái kia là trạng thái của cả
    ///         một trang setup ở cột trái.
    ///     </para>
    /// </summary>
    internal enum EzgStatus
    {
        /// <summary>Chưa biết / không áp dụng. KHÔNG phải lỗi — không tô màu cảnh báo.</summary>
        None = 0,

        Ok = 1,

        /// <summary>Có giá trị nhưng lệch, hoặc còn thiếu thứ bắt buộc — người dùng phải làm tiếp.</summary>
        Warn = 2,

        /// <summary>Sai tới mức chạy tiếp là hỏng (config trỏ sang dự án khác, fetch thất bại…).</summary>
        Error = 3,
    }

    /// <summary>
    ///     Trạng thái của cả một trang setup — thứ hiện ở cột trái và trong <c>EzgKitApi.GetStatusJson</c>.
    ///     <para>
    ///         <see cref="Deferred" /> và <see cref="NotApplicable" /> là quyết định của NGƯỜI (bấm "Để sau" /
    ///         "Không áp dụng"), lưu trong <c>ProjectSettings/EzgKitSetup.json</c> và thắng mọi detector.
    ///         Các mức còn lại do detector đọc thẳng từ project mỗi lần làm mới.
    ///     </para>
    /// </summary>
    internal enum SetupState
    {
        /// <summary>Chưa làm gì.</summary>
        Todo = 0,

        /// <summary>Làm được một phần, còn việc.</summary>
        Partial = 1,

        Done = 2,

        /// <summary>Người dùng bấm "Để sau".</summary>
        Deferred = 3,

        /// <summary>Người dùng bấm "Không áp dụng" — dự án không dùng tính năng này.</summary>
        NotApplicable = 4,

        /// <summary>Có thứ hỏng (config trỏ sang dự án khác, debug ads trong bản phát hành…).</summary>
        Error = 5,

        /// <summary>Trang chỉ để xem (Tổng quan) — không tính vào tiến độ.</summary>
        Info = 6,
    }

    internal static class SetupStateText
    {
        /// <summary>Id ổn định cho JSON (API, state file) — KHÔNG đổi, Claude đọc chuỗi này.</summary>
        internal static string Id(SetupState state) =>
            state switch
            {
                SetupState.Todo => "todo",
                SetupState.Partial => "partial",
                SetupState.Done => "done",
                SetupState.Deferred => "deferred",
                SetupState.NotApplicable => "na",
                SetupState.Error => "error",
                _ => "info",
            };

        /// <summary>Nhãn hiện trên pill.</summary>
        internal static string Label(SetupState state) =>
            state switch
            {
                SetupState.Todo => "Chưa làm",
                SetupState.Partial => "Còn việc",
                SetupState.Done => "Xong",
                SetupState.Deferred => "Để sau",
                SetupState.NotApplicable => "Không áp dụng",
                SetupState.Error => "Có lỗi",
                _ => "Tổng quan",
            };

        /// <summary>Class USS cho màu của pill / chấm trạng thái.</summary>
        internal static string Css(SetupState state) =>
            state switch
            {
                SetupState.Todo => "st-todo",
                SetupState.Partial => "st-partial",
                SetupState.Done => "st-done",
                SetupState.Deferred => "st-deferred",
                SetupState.NotApplicable => "st-na",
                SetupState.Error => "st-error",
                _ => "st-info",
            };

        internal static string Css(EzgStatus status) =>
            status switch
            {
                EzgStatus.Ok => "st-done",
                EzgStatus.Warn => "st-partial",
                EzgStatus.Error => "st-error",
                _ => "st-na",
            };

        internal static string Id(EzgStatus status) =>
            status switch
            {
                EzgStatus.Ok => "ok",
                EzgStatus.Warn => "warn",
                EzgStatus.Error => "error",
                _ => "info",
            };

        /// <summary>Gộp mức nặng nhất của các mục thành trạng thái trang.</summary>
        internal static SetupState FromWorst(EzgStatus worst, bool anyOk) =>
            worst switch
            {
                EzgStatus.Error => SetupState.Error,
                EzgStatus.Warn => anyOk ? SetupState.Partial : SetupState.Todo,
                EzgStatus.Ok => SetupState.Done,
                _ => SetupState.Todo,
            };
    }
}
#endif
