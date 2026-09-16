using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ezg.UserSegment
{
    /// <summary>
    ///     Adapter tracking: nhận event + user property từ engine, chuyển sang SDK tracking hiện có (Firebase / AppsFlyer).
    ///     Giá trị param chỉ là long, double, string (bool đã đổi thành long 0/1 trước khi tới đây) — §C.6.4.
    /// </summary>
    public interface ITrackingSink
    {
        void LogEvent(string name, IReadOnlyDictionary<string, object> parameters);

        /// <summary>value == null → xoá property.</summary>
        void SetUserProperty(string name, string value);
    }

    /// <summary>Kết quả fetch config. <see cref="Error" /> != null ⇒ lỗi mạng / timeout.</summary>
    public readonly struct FetchResult
    {
        public readonly int StatusCode;
        public readonly string Body;
        public readonly string Error;

        public FetchResult(int statusCode, string body, string error)
        {
            StatusCode = statusCode;
            Body = body;
            Error = error;
        }

        public static FetchResult Failed(string error) => new FetchResult(0, null, error ?? "unknown");
        public static FetchResult Ok(int status, string body) => new FetchResult(status, body, null);
    }

    /// <summary>Fetch envelope từ Worker. Trả về khi có response, lỗi mạng hoặc hết timeout. Không retry bên trong — §C.6.4.</summary>
    public interface IConfigFetcher
    {
        Task<FetchResult> FetchAsync(string url, IReadOnlyDictionary<string, string> headers, int timeoutMs,
            CancellationToken ct);
    }

    /// <summary>Storage cho tầng vận hành SDK. Key dùng: "state", "config_cache" — §C.10.</summary>
    public interface IStateStorage
    {
        /// <summary>null nếu không có.</summary>
        string Read(string key);

        /// <summary>Ghi atomic (tmp + rename). Có thể chạy ở thread nền — caller đảm bảo latest-wins.</summary>
        void WriteAtomic(string key, string content);

        void Delete(string key);
    }

    /// <summary>Nguồn thời gian — §4.4. Monotonic chỉ để đối chiếu trong foreground.</summary>
    public interface ITimeSource
    {
        /// <summary>Đồng hồ thiết bị UTC, chưa hiệu chỉnh (epoch giây).</summary>
        long DeviceUtcNowSeconds();

        /// <summary>Giây monotonic — dừng khi app suspend, chỉ dùng để đối chiếu trong cùng khoảng foreground.</summary>
        double MonotonicSeconds();
    }

    public enum LogLevel
    {
        Debug,
        Info,
        Warn,
        Error
    }

    /// <summary>Logger của SDK. Đặt tên ISegLogger để không đụng UnityEngine.ILogger.</summary>
    public interface ISegLogger
    {
        void Log(LogLevel level, string message);
    }

    /// <summary>
    ///     Tầng lịch sử action (theo user) — §4.5. Game implement để blob đi theo save + backup sẵn có.
    /// </summary>
    public interface IActionHistoryStore
    {
        /// <summary>Blob JSON đã lưu trong save của game, hoặc null / "" nếu chưa có.</summary>
        string Load();

        /// <summary>Ghi vào save; game quyết định khi nào flush xuống disk / cloud. Gọi đồng bộ trên main thread.</summary>
        void Save(string blob);
    }
}
