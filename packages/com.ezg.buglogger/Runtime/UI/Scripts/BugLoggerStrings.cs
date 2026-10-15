namespace Ezg.BugLogger.UI
{
    /// <summary>
    ///     Chữ trên UI Bug Logger. Công cụ nội bộ cho QA nên chỉ có tiếng Việt, không đi qua hệ localize của game
    ///     (mỗi project một sheet — UI phải giống nhau ở mọi project). Font đóng kèm package đủ dấu tiếng Việt.
    /// </summary>
    internal static class BugLoggerStrings
    {
        public const string HEADER_TITLE = "Báo lỗi";
        public const string SUBTITLE_SEPARATOR = "  ·  ";

        public const string SCREENSHOT_TITLE = "Ảnh chụp màn hình";
        public const string SCREENSHOT_HINT_FORMAT = "Chạm để khoanh vùng, vẽ, cắt  ·  {0}×{1}";
        public const string SCREENSHOT_EDITED_FORMAT = "Đã chỉnh sửa · chạm để sửa tiếp  ·  {0}×{1}";
        public const string SCREENSHOT_NONE = "Không chụp được màn hình";
        public const string SCREENSHOT_ATTACH = "Đính kèm vào báo cáo";

        public const string LABEL_TITLE = "Tiêu đề";
        public const string PLACEHOLDER_TITLE = "Tóm tắt lỗi trong một câu";
        public const string LABEL_CONTENT = "Mô tả lỗi";
        public const string PLACEHOLDER_CONTENT = "Các bước tái hiện, kết quả thực tế, kết quả mong đợi…";
        public const string LABEL_FEATURE = "Màn hình / tính năng";
        public const string PLACEHOLDER_FEATURE = "Ví dụ: Shop";
        public const string LABEL_CATEGORY = "Phân loại";
        public const string LABEL_REPRODUCE = "Tỉ lệ tái hiện";
        public const string REPRODUCE_HITS = "lần gặp";
        public const string REPRODUCE_TRIES = "lần thử";
        public const string REPRODUCE_SEPARATOR = "/";
        public const string LABEL_TAGS = "Tag";
        public const string LABEL_PEOPLE = "Gắn người";
        public const string LIMIT_HINT_FORMAT = "Tối đa {0}";
        public const string REQUIRED_MARK = "*";
        public const string COUNTER_FORMAT = "{0}/{1}";
        public const string EDITOR_DONE = "Xong";

        public const string SUBMIT = "Gửi báo cáo";
        public const string SUBMITTED = "Đã lưu";

        public const string STATUS_ONLINE = "Có mạng — gửi ngay sau khi lưu";
        public const string STATUS_OFFLINE = "Đang offline — lưu trong máy, có mạng tự gửi";
        public const string STATUS_PENDING_FORMAT = "{0} báo cáo đang chờ gửi";

        public const string OPTIONS_FALLBACK = "Không tải được lựa chọn từ server — đang dùng bản lưu sẵn.";
        public const string OPTIONS_INTAKE_DISABLED = "Server đang tạm ngừng nhận báo cáo — báo cáo sẽ chờ trong máy.";

        public const string ERROR_TITLE_REQUIRED = "Nhập tiêu đề cho lỗi";
        public const string ERROR_CONTENT_REQUIRED = "Mô tả lỗi để dev tái hiện được";
        public const string ERROR_MAX_TAGS_FORMAT = "Chỉ chọn tối đa {0} tag";
        public const string ERROR_MAX_PEOPLE_FORMAT = "Chỉ gắn tối đa {0} người";
        public const string ERROR_FORM_LOADING = "Đang tải form, đợi một chút…";

        public const string TOAST_SAVED_SENDING = "Đã lưu báo cáo · đang gửi…";
        public const string TOAST_SAVED_OFFLINE = "Đã lưu báo cáo · có mạng sẽ tự gửi";
        public const string TOAST_SAVE_FAILED = "Không lưu được báo cáo";
        public const string TOAST_SENT_FORMAT = "Đã gửi báo cáo #{0}";
        public const string TOAST_SENT = "Đã gửi báo cáo";
        public const string TOAST_FAILED_FORMAT = "Gửi báo cáo thất bại: {0}";

        public const string EDITOR_TITLE = "Chỉnh sửa ảnh";
        public const string EDITOR_CLEAR = "Xoá hết";
        public const string EDITOR_DISCARD_CONFIRM = "Chạm lần nữa để bỏ các thay đổi";
        public const string EDITOR_LIMIT = "Đã đủ số nét tối đa — hoàn tác bớt hoặc Xoá hết";
        public const string EDITOR_SPOTLIGHT_LIMIT_FORMAT = "Tối đa {0} vùng làm nổi";
        public const string EDITOR_MARKER_LIMIT_FORMAT = "Tối đa {0} số";
        public const string EDITOR_RENDER_FAILED = "Không xuất được ảnh đã sửa — thử lại";

        public const string TOOL_CROP = "Cắt";
        public const string TOOL_RECT = "Khung";
        public const string TOOL_ELLIPSE = "Tròn";
        public const string TOOL_ARROW = "Mũi tên";
        public const string TOOL_PEN = "Bút";
        public const string TOOL_MARKER = "Đánh số";
        public const string TOOL_SPOTLIGHT = "Làm nổi";

        public const string HINT_CROP = "Kéo góc / cạnh để cắt · kéo giữa khung để dời";
        public const string HINT_RECT = "Kéo để khoanh vùng lỗi · chạm để đặt khung nhanh";
        public const string HINT_ELLIPSE = "Kéo để khoanh tròn vùng lỗi · chạm để đặt nhanh";
        public const string HINT_ARROW = "Kéo từ đuôi tới chỗ lỗi · chạm để chỉ nhanh";
        public const string HINT_PEN = "Vẽ tự do lên ảnh";
        public const string HINT_MARKER = "Chạm để đặt số · nhắc lại số này trong mô tả";
        public const string HINT_SPOTLIGHT = "Kéo chọn vùng cần chú ý · phần còn lại tối đi";
    }
}
