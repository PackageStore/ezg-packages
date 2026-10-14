using System.Collections.Generic;

namespace Ezg.BugLogger
{
    /// <summary>Loại report gửi lên BugHub.</summary>
    public enum BugReportKind
    {
        /// <summary>Bug: tạo issue + thread. Bắt buộc <see cref="BugReportDraft.Title"/> và <see cref="BugReportDraft.Actual"/>.</summary>
        Bug,

        /// <summary>Góp ý: chỉ gửi tin nhắn vào kênh feedback, không tạo issue.</summary>
        Feedback,
    }

    /// <summary>
    /// Nội dung QA nhập trên form, host dựng rồi đưa vào <see cref="BugLogger.Submit"/>. Mọi chuỗi bị cắt
    /// theo giới hạn của contract (title 100, actual/expected/steps/description 4000, reproduceRate 16);
    /// <see cref="TagIds"/> tối đa 4, <see cref="MentionIds"/> tối đa 5 — server tự lọc id không hợp lệ.
    /// </summary>
    public sealed class BugReportDraft
    {
        /// <summary>Bug (mặc định) hay feedback.</summary>
        public BugReportKind Kind = BugReportKind.Bug;

        /// <summary>Tiêu đề QA gõ (thô — server tự ghép tiền tố tên thread).</summary>
        public string Title;

        /// <summary>Màn đang mở khi QA bấm icon.</summary>
        public string FeatureKey;

        /// <summary>Một giá trị trong <see cref="BugFormOptions.Categories"/>, hoặc rỗng.</summary>
        public string Category;

        /// <summary>Tỷ lệ tái hiện, tự do (ví dụ <c>3/10</c>).</summary>
        public string ReproduceRate;

        /// <summary>Kết quả thực tế.</summary>
        public string Actual;

        /// <summary>Kết quả mong đợi.</summary>
        public string Expected;

        /// <summary>Các bước tái hiện.</summary>
        public string Steps;

        /// <summary>Nội dung feedback / ghi chú thêm.</summary>
        public string Description;

        /// <summary>Id forum tag lấy từ <see cref="BugFormOptions.Tags"/>.</summary>
        public IReadOnlyList<string> TagIds;

        /// <summary>Id người được tag lấy từ <see cref="BugFormOptions.People"/>.</summary>
        public IReadOnlyList<string> MentionIds;
    }
}
