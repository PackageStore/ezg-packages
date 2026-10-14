namespace Ezg.BugLogger
{
    /// <summary>Nơi gửi một report: base URL của Worker và mã project (header <c>X-BugHub-Project</c>). Không có secret nào.</summary>
    public readonly struct BugReportTarget
    {
        #region Fields

        /// <summary>Base URL của Worker, không kèm route <c>/v1/...</c>.</summary>
        public readonly string Endpoint;

        /// <summary>Giá trị header <c>X-BugHub-Project</c>.</summary>
        public readonly string ProjectCode;

        #endregion

        #region Public

        /// <summary>Tạo target; null thành chuỗi rỗng.</summary>
        public BugReportTarget(string endpoint, string projectCode)
        {
            Endpoint = endpoint ?? string.Empty;
            ProjectCode = projectCode ?? string.Empty;
        }

        /// <summary>True khi có cả endpoint lẫn mã project; thiếu một trong hai thì report chỉ nằm chờ trong outbox.</summary>
        public bool IsConfigured => Endpoint.Length > 0 && ProjectCode.Length > 0;

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{Endpoint} [{ProjectCode}]";
        }

        #endregion
    }
}
