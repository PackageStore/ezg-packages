using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Một nhóm test hiển thị thành một mục trong cửa sổ Auto Test (Kiểm tra tĩnh, Smoke, Economy…).
    ///     Kế thừa class này (có constructor không tham số) là suite tự xuất hiện — không cần đăng ký.
    /// </summary>
    public abstract class AutoTestSuite
    {
        /// <summary>Id ổn định, chữ thường-gạch ngang (vd "static", "smoke") — dùng trong CLI và settings.</summary>
        public abstract string Id { get; }

        public abstract string DisplayName { get; }
        public abstract string Description { get; }
        public abstract ExecutionMode Mode { get; }

        /// <summary>Thứ tự hiển thị/chạy (nhỏ chạy trước).</summary>
        public virtual int Order => 100;

        /// <summary>Tên icon built-in của Editor (EditorGUIUtility.IconContent).</summary>
        public virtual string Icon => "d_UnityEditor.ConsoleWindow";

        /// <summary>Suite có sửa dữ liệu người chơi ⇒ runner sao lưu + khôi phục PlayerPrefs quanh nó.</summary>
        public virtual bool MutatesPlayerData => false;

        /// <summary>Chạy được trong player build trên device không (chỉ suite Play thuần runtime).</summary>
        public virtual bool SupportsDevice => Mode == ExecutionMode.Play;

        /// <summary>
        ///     Liệt kê case. Được gọi ở Edit mode (để hiện danh sách) và lúc chạy — phải rẻ, không có side
        ///     effect. Case động (mỗi màn hình một case) liệt kê ở đây.
        /// </summary>
        public abstract IEnumerable<AutoTestCase> BuildCases(AutoTestBuildContext ctx);

        /// <summary>Chạy một lần trước case đầu tiên của suite.</summary>
        public virtual Task SetUp(AutoTestContext ctx)
        {
            return Task.CompletedTask;
        }

        /// <summary>Chạy một lần sau case cuối (kể cả khi bị dừng giữa chừng).</summary>
        public virtual Task TearDown(AutoTestContext ctx)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>Thông tin cho BuildCases.</summary>
    public sealed class AutoTestBuildContext
    {
        public AutoTestConfig Config;
        public IGameAdapter Game;

        /// <summary>True khi đang liệt kê để hiển thị (chưa chạy) — suite có thể trả case tổng quát hơn.</summary>
        public bool ForListing;

        public bool IsDevice;
    }

    /// <summary>Một case chạy được.</summary>
    public sealed class AutoTestCase
    {
        public string Id;
        public string Name;
        public string Description;
        public string Category;

        /// <summary>0 = dùng timeout mặc định trong settings.</summary>
        public float TimeoutSeconds;

        public string[] Tags = Array.Empty<string>();
        public Func<AutoTestContext, Task> Run;

        public AutoTestCase()
        {
        }

        public AutoTestCase(string id, string name, string description, Func<AutoTestContext, Task> run,
            string category = null, float timeoutSeconds = 0)
        {
            Id = id;
            Name = name;
            Description = description;
            Run = run;
            Category = category;
            TimeoutSeconds = timeoutSeconds;
        }
    }

    /// <summary>
    ///     Đánh dấu một kịch bản test riêng của game. Đặt trên class kế thừa <see cref="AutoTestScenario" />.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class AutoTestScenarioAttribute : Attribute
    {
        /// <summary>Tên hiển thị.</summary>
        public string Name { get; }

        /// <summary>Nhóm (vd "Farm", "Shop") — gom kịch bản trong cửa sổ.</summary>
        public string Category { get; set; } = "Gameplay";

        public string Description { get; set; } = "";
        public int Order { get; set; } = 100;

        /// <summary>0 = timeout mặc định.</summary>
        public float TimeoutSeconds { get; set; }

        public string[] Tags { get; set; } = Array.Empty<string>();

        /// <summary>Kịch bản chạy được trên device thật.</summary>
        public bool RunOnDevice { get; set; } = true;

        /// <summary>Tạm tắt kịch bản (vd đang viết dở) — vẫn hiện nhưng bị bỏ qua.</summary>
        public bool Disabled { get; set; }

        public AutoTestScenarioAttribute(string name)
        {
            Name = name;
        }
    }

    /// <summary>
    ///     Kịch bản test riêng của từng game (gameplay đặc thù). Viết trong thư mục AutoTests của project,
    ///     gọi thẳng code game. Xem Documentation~/AI-SCENARIO-GUIDE.md.
    /// </summary>
    public abstract class AutoTestScenario
    {
        /// <summary>Chạy trước <see cref="Run" /> (chuẩn bị state: thêm tiền, mở khoá…).</summary>
        public virtual Task SetUp(AutoTestContext ctx)
        {
            return Task.CompletedTask;
        }

        /// <summary>Nội dung kịch bản. Ném exception / ctx.Fail ⇒ case lỗi; ctx.Check ghi lỗi mềm và chạy tiếp.</summary>
        public abstract Task Run(AutoTestContext ctx);

        /// <summary>Luôn chạy sau cùng (kể cả khi Run lỗi / bị dừng) — dọn state, đóng màn hình.</summary>
        public virtual Task TearDown(AutoTestContext ctx)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Điểm móc của từng project vào hệ thống test (không bắt buộc). Kế thừa <see cref="AutoTestProjectHooks" />
    ///     trong thư mục AutoTests của project — runner tự tìm (lấy class đầu tiên nếu có nhiều).
    /// </summary>
    public interface IAutoTestProjectHooks
    {
        /// <summary>Sau khi game boot xong, trước case đầu tiên của mỗi phiên Play (skip tutorial, tắt popup…).</summary>
        Task OnSessionStarted(AutoTestContext ctx);

        /// <summary>null = để adapter tự quyết game đã sẵn sàng chưa.</summary>
        bool? IsGameReady();

        /// <summary>Data mẫu truyền vào khi mở màn cần data (null = không truyền).</summary>
        object GetFeatureData(string featureName);

        /// <summary>Đóng popup chặn (rating, offer, daily…) giữa các case.</summary>
        Task DismissBlockingPopups(AutoTestContext ctx);

        /// <summary>Feature bỏ qua thêm (ngoài danh sách trong settings).</summary>
        IEnumerable<string> ExtraExcludedFeatures();
    }

    /// <summary>Base tiện lợi của <see cref="IAutoTestProjectHooks" /> — override phần cần.</summary>
    public abstract class AutoTestProjectHooks : IAutoTestProjectHooks
    {
        public virtual Task OnSessionStarted(AutoTestContext ctx)
        {
            return Task.CompletedTask;
        }

        public virtual bool? IsGameReady()
        {
            return null;
        }

        public virtual object GetFeatureData(string featureName)
        {
            return null;
        }

        public virtual Task DismissBlockingPopups(AutoTestContext ctx)
        {
            return Task.CompletedTask;
        }

        public virtual IEnumerable<string> ExtraExcludedFeatures()
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Case bị bỏ qua có lý do (thiếu điều kiện, adapter không hỗ trợ…).</summary>
    public sealed class AutoTestSkipException : Exception
    {
        public AutoTestSkipException(string reason) : base(reason)
        {
        }
    }

    /// <summary>Assertion thất bại — case chuyển Lỗi với severity tương ứng.</summary>
    public sealed class AutoTestAssertException : Exception
    {
        public Severity Severity { get; }
        public string Expected { get; }
        public string Actual { get; }

        public AutoTestAssertException(string message, Severity severity, string expected = null,
            string actual = null) : base(message)
        {
            Severity = severity;
            Expected = expected;
            Actual = actual;
        }
    }
}
