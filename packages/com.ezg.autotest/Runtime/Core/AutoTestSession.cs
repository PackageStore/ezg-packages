using System;
using System.Collections.Generic;
using System.Threading;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Một phiên chạy: cấu hình, adapter game, hooks project, thư mục output và state dùng chung giữa các case.
    ///     Editor tạo lại mỗi phiên Play; device tạo một lần cho cả lượt chạy.
    /// </summary>
    public sealed class AutoTestSession
    {
        readonly Dictionary<string, object> _bag = new();

        public AutoTestSession(AutoTestConfig config, IGameAdapter game, IAutoTestProjectHooks hooks, string outputDir,
            bool isDevice, string deviceName = null)
        {
            Config = config ?? new AutoTestConfig();
            Game = game;
            Hooks = hooks;
            OutputDir = outputDir;
            IsDevice = isDevice;
            DeviceName = deviceName;
            Ui = new UiDriver(this);
        }

        public AutoTestConfig Config { get; }
        public IGameAdapter Game { get; }

        /// <summary>Có thể null nếu project không khai báo hooks.</summary>
        public IAutoTestProjectHooks Hooks { get; }

        public UiDriver Ui { get; }

        /// <summary>Thư mục tuyệt đối của lượt chạy (screenshots/, attachments/ nằm bên trong).</summary>
        public string OutputDir { get; }

        public bool IsDevice { get; }
        public string DeviceName { get; }

        /// <summary>Token dừng toàn lượt (user bấm Dừng).</summary>
        public CancellationToken RunToken { get; set; }

        /// <summary>Token của case đang chạy (huỷ khi Dừng hoặc case quá timeout) — UiDriver/GameFlow dùng.</summary>
        public CancellationToken CurrentToken { get; set; }

        /// <summary>Chỉ số log lúc phiên bắt đầu (trước khi game boot) — case boot lấy log từ đây.</summary>
        public long SessionLogCursor { get; set; }

        /// <summary>
        ///     Suite kết quả phát sinh thêm (vd kết quả chạy trên device) — runner gộp vào report sau suite hiện tại.
        /// </summary>
        public List<TestSuiteResult> ExtraSuites { get; } = new();

        /// <summary>Gọi mỗi khi case/bước đổi — cửa sổ Editor + device logcat dùng để hiện tiến độ.</summary>
        public Action<TestCaseResult, string> Progress;

        public void ReportProgress(TestCaseResult result, string step)
        {
            try
            {
                Progress?.Invoke(result, step);
            }
            catch
            {
                // UI tiến độ lỗi không được làm hỏng test.
            }
        }

        /// <summary>Lưu state dùng chung giữa các case (vd kết quả boot, danh sách màn đã mở).</summary>
        public void Set<T>(string key, T value)
        {
            _bag[key] = value;
        }

        public T Get<T>(string key, T fallback = default)
        {
            return _bag.TryGetValue(key, out var v) && v is T t ? t : fallback;
        }

        public bool Has(string key)
        {
            return _bag.ContainsKey(key);
        }
    }
}
