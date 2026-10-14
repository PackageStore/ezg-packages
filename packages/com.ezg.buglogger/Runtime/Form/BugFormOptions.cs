using System.Collections.Generic;

namespace Ezg.BugLogger
{
    /// <summary>Nguồn của một bộ <see cref="BugFormOptions"/>.</summary>
    public enum BugFormOptionsSource
    {
        /// <summary>Vừa tải từ server.</summary>
        Server,

        /// <summary>Server lỗi/offline — bản cache lần tải thành công gần nhất.</summary>
        Cache,

        /// <summary>Không có server lẫn cache — giá trị mặc định của contract.</summary>
        Default,
    }

    /// <summary>Một lựa chọn trên form: forum tag hoặc người được tag.</summary>
    public sealed class BugFormOption
    {
        /// <summary>Id gửi lại trong <see cref="BugReportDraft.TagIds"/> / <see cref="BugReportDraft.MentionIds"/>.</summary>
        public string Id { get; }

        /// <summary>Tên hiển thị.</summary>
        public string Name { get; }

        /// <summary>Tạo lựa chọn.</summary>
        public BugFormOption(string id, string name)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
        }
    }

    /// <summary>Giới hạn từng trường host validate trước khi submit (BugHub contract §3.3).</summary>
    public sealed class BugFormLimits
    {
        /// <summary>Độ dài tối đa của tiêu đề.</summary>
        public int TitleMax { get; }

        /// <summary>Độ dài tối đa của mỗi trường actual / expected / steps / description.</summary>
        public int FieldMax { get; }

        /// <summary>Số forum tag tối đa.</summary>
        public int MaxTags { get; }

        /// <summary>Số người được tag tối đa.</summary>
        public int MaxMentions { get; }

        /// <summary>Kích thước screenshot tối đa (byte).</summary>
        public int ScreenshotMaxBytes { get; }

        /// <summary>Tạo bộ giới hạn.</summary>
        public BugFormLimits(int titleMax, int fieldMax, int maxTags, int maxMentions, int screenshotMaxBytes)
        {
            TitleMax = titleMax;
            FieldMax = fieldMax;
            MaxTags = maxTags;
            MaxMentions = maxMentions;
            ScreenshotMaxBytes = screenshotMaxBytes;
        }
    }

    /// <summary>
    /// Lựa chọn của form report tải từ <c>GET /v1/projects/:code/form</c> (BugHub contract §3.3). Không bao
    /// giờ null: lỗi mạng → bản cache disk → mặc định (category mặc định, tag/người rỗng).
    /// </summary>
    public sealed class BugFormOptions
    {
        /// <summary>Phiên bản schema server trả; 0 với bộ mặc định.</summary>
        public int Schema { get; }

        /// <summary>Mã project.</summary>
        public string Code { get; }

        /// <summary>False khi admin đang tắt nhận report (report vẫn được giữ trong outbox).</summary>
        public bool IntakeEnabled { get; }

        /// <summary>Các category chọn được.</summary>
        public IReadOnlyList<string> Categories { get; }

        /// <summary>Forum tag chọn được (đã trừ tag trạng thái).</summary>
        public IReadOnlyList<BugFormOption> Tags { get; }

        /// <summary>Người được tag chọn được.</summary>
        public IReadOnlyList<BugFormOption> People { get; }

        /// <summary>Giới hạn từng trường.</summary>
        public BugFormLimits Limits { get; }

        /// <summary>Bộ này đến từ server, cache hay mặc định.</summary>
        public BugFormOptionsSource Source { get; }

        /// <summary>Tạo bộ lựa chọn.</summary>
        public BugFormOptions(int schema, string code, bool intakeEnabled, IReadOnlyList<string> categories,
            IReadOnlyList<BugFormOption> tags, IReadOnlyList<BugFormOption> people, BugFormLimits limits, BugFormOptionsSource source)
        {
            Schema = schema;
            Code = code ?? string.Empty;
            IntakeEnabled = intakeEnabled;
            Categories = categories ?? System.Array.Empty<string>();
            Tags = tags ?? System.Array.Empty<BugFormOption>();
            People = people ?? System.Array.Empty<BugFormOption>();
            Limits = limits;
            Source = source;
        }
    }
}
