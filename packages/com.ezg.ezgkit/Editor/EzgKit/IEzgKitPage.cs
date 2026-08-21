#if UNITY_EDITOR
namespace Ezg.Editor.Shared.EzgKit
{
    /// <summary>
    ///     Một trang trong cửa sổ setup tổng (<see cref="EzgKitWindow" />). Mỗi tool setup dự án
    ///     (marketing sheet, Firebase app, …) hiện diện dưới dạng một page — thêm tool mới chỉ cần
    ///     implement interface này rồi nhét vào danh sách page của cửa sổ, không phải sửa GUI cửa sổ.
    ///     <para>
    ///         <b>Cửa sổ vẽ phần đầu trang, page KHÔNG vẽ lại:</b> tiêu đề (<see cref="Title" />), một
    ///         dòng mô tả (<see cref="Subtitle" />) và chip trạng thái (<see cref="Status" /> +
    ///         <see cref="Headline" />) đều do <see cref="EzgKitWindow" /> vẽ. Nhờ vậy mọi tab có đúng
    ///         một kiểu phần đầu, không tab nào tự chế. <see cref="Draw" /> chỉ vẽ phần THÂN.
    ///     </para>
    ///     <para>
    ///         Page tự quản scroll view của phần thân: cửa sổ không biết nội dung page dài bao nhiêu.
    ///     </para>
    /// </summary>
    internal interface IEzgKitPage
    {
        /// <summary>Nhãn tab. ASCII-only để cột nav không phụ thuộc font của Editor.</summary>
        string Title { get; }

        /// <summary>Một câu trả lời cho "tab này để làm gì", hiện dưới tiêu đề trang.</summary>
        string Subtitle { get; }

        /// <summary>Một dòng trạng thái hiện tại — dùng cho chip đầu trang và thẻ bước ở Tổng quan.</summary>
        string Headline { get; }

        /// <summary>
        ///     Mức độ của <see cref="Headline" />. Quyết định màu chip, icon ở cột nav và màu số bước
        ///     trong tab Tổng quan — nên phải phản ánh ĐÚNG việc người dùng còn phải làm hay không.
        /// </summary>
        EzgStatus Status { get; }

        /// <summary>
        ///     Nhãn bước của page trong luồng "chạy hết". <c>null</c> = page không tham gia luồng đó.
        /// </summary>
        string RunAllLabel { get; }

        /// <summary>
        ///     Đọc lại trạng thái từ project. CHỈ ĐỌC — mở cửa sổ hay bấm Làm mới không được ghi gì.
        /// </summary>
        void Reload();

        /// <summary>Vẽ phần THÂN của trang (cửa sổ đã vẽ tiêu đề + chip trạng thái ở trên).</summary>
        void Draw();

        /// <summary>
        ///     Bước của page trong luồng 1 click. Trả về false nghĩa là dừng chuỗi tại đây (lỗi, hoặc
        ///     user huỷ) — page tự để lại thông điệp trong GUI của nó.
        /// </summary>
        bool RunAll();
    }
}
#endif
