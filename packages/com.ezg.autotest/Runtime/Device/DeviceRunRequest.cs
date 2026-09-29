using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Yêu cầu chạy test trên device — Editor bake vào build test dưới dạng TextAsset
    ///     <c>Resources/EZGAutoTestDeviceConfig.json</c>, hoặc truyền tay vào
    ///     <see cref="AutoTestDeviceRunner.RunInProcess(DeviceRunRequest)" /> (menu debug trong game).
    /// </summary>
    [Serializable]
    public class DeviceRunRequest
    {
        /// <summary>Id lượt chạy (rỗng = tự sinh theo thời gian). Cũng là tên thư mục report trên device.</summary>
        public string runId;

        /// <summary>Id suite chạy (rỗng = mọi suite Play hỗ trợ device).</summary>
        public List<string> suites = new();

        /// <summary>
        ///     Tự chạy ngay khi mở app, không cần intent/argument. Build test thông thường để false — Editor kích hoạt
        ///     bằng intent extra; bật true khi muốn app tự test lúc QA mở tay.
        /// </summary>
        public bool autoRunOnLaunch;

        /// <summary>JSON của <see cref="AutoTestConfig" /> (JsonUtility) — rỗng = cấu hình mặc định.</summary>
        public string configJson;

        /// <summary>Phiên bản package lúc build (ghi vào report).</summary>
        public string packageVersion;

        /// <summary>Build number (versionCode / CFBundleVersion) lúc build (ghi vào report).</summary>
        public string buildNumber;
    }

    /// <summary>
    ///     Giao thức giữa player build và Editor: tên intent extra / argument, tên file, và các dòng log
    ///     <c>EZG_AUTOTEST {json}</c> (một dòng JSON mỗi sự kiện) mà Editor đọc qua logcat.
    /// </summary>
    public static class DeviceProtocol
    {
        /// <summary>Tiền tố dòng log giao thức (Editor tìm chuỗi này trong logcat).</summary>
        public const string LOG_PREFIX = "EZG_AUTOTEST ";

        /// <summary>Tên TextAsset trong Resources chứa <see cref="DeviceRunRequest" /> đã bake.</summary>
        public const string RESOURCE_NAME = "EZGAutoTestDeviceConfig";

        /// <summary>Intent extra (Android, <c>am start --es</c>) — id lượt chạy.</summary>
        public const string EXTRA_RUN = "ezg_autotest_run";

        /// <summary>Intent extra — danh sách suite, cách nhau dấu phẩy.</summary>
        public const string EXTRA_SUITES = "ezg_autotest_suites";

        /// <summary>Argument dòng lệnh (iOS devicectl / desktop) — id lượt chạy.</summary>
        public const string ARG_RUN = "-ezgAutotestRun";

        /// <summary>Argument dòng lệnh — danh sách suite.</summary>
        public const string ARG_SUITES = "-ezgAutotestSuites";

        /// <summary>Biến môi trường dự phòng (devicectl --environment-variables).</summary>
        public const string ENV_RUN = "EZG_AUTOTEST_RUN";

        public const string ENV_SUITES = "EZG_AUTOTEST_SUITES";

        /// <summary>Action của Firebase Test Lab Game Loop.</summary>
        public const string ACTION_GAME_LOOP = "com.google.intent.action.TEST_LOOP";

        /// <summary>Thư mục con trong Application.persistentDataPath chứa report các lượt chạy.</summary>
        public const string OUTPUT_FOLDER = "EZGAutoTest";

        public const string REPORT_FILE = "report.json";

        /// <summary>File đánh dấu lượt chạy đã xong (ghi sau cùng) — iOS dò file này để biết khi nào lấy report.</summary>
        public const string DONE_FLAG = "done.flag";

        public const string EVENT_START = "start";
        public const string EVENT_BEGIN = "begin";
        public const string EVENT_CASE = "case";
        public const string EVENT_DONE = "done";
        public const string EVENT_ERROR = "error";

        const int MAX_MESSAGE_LENGTH = 600;

        /// <summary>Tách danh sách suite "a, b,c" → ["a","b","c"] (bỏ rỗng, bỏ trùng).</summary>
        public static List<string> SplitSuites(string csv)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(csv)) return list;
            foreach (var raw in csv.Split(',', ';', ' '))
            {
                var s = raw.Trim();
                if (s.Length > 0 && !list.Contains(s)) list.Add(s);
            }

            return list;
        }

        /// <summary>Làm sạch id lượt chạy để dùng làm tên thư mục (chỉ chữ, số, '-', '_', '.').</summary>
        public static string SanitizeRunId(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId)) return null;
            var chars = runId.Trim().ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '_' ||
                      c == '.'))
                    chars[i] = '_';
            }

            return new string(chars);
        }

        #region Ghi sự kiện (JSON một dòng, gọn để không bị logcat cắt)

        /// <summary>Bộ ghi JSON phẳng tối giản — chỉ string / số / mảng string.</summary>
        public sealed class EventWriter
        {
            readonly StringBuilder _sb = new(128);
            bool _first = true;

            public EventWriter(string eventName)
            {
                _sb.Append('{');
                Str("e", eventName);
            }

            public EventWriter Str(string key, string value)
            {
                Key(key);
                AppendString(_sb, value);
                return this;
            }

            public EventWriter Num(string key, double value)
            {
                Key(key);
                _sb.Append(Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture));
                return this;
            }

            public EventWriter List(string key, IEnumerable<string> values)
            {
                Key(key);
                _sb.Append('[');
                var first = true;
                if (values != null)
                    foreach (var v in values)
                    {
                        if (!first) _sb.Append(',');
                        first = false;
                        AppendString(_sb, v);
                    }

                _sb.Append(']');
                return this;
            }

            public string Build()
            {
                return _sb.ToString() + "}";
            }

            void Key(string key)
            {
                if (!_first) _sb.Append(',');
                _first = false;
                AppendString(_sb, key);
                _sb.Append(':');
            }
        }

        static void AppendString(StringBuilder sb, string value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            if (value.Length > MAX_MESSAGE_LENGTH) value = value.Substring(0, MAX_MESSAGE_LENGTH) + "…";
            sb.Append('"');
            foreach (var c in value)
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }

            sb.Append('"');
        }

        #endregion
    }

    /// <summary>
    ///     Một sự kiện giao thức đã parse (Editor dùng JsonUtility.FromJson). Field thiếu trong JSON giữ mặc định.
    /// </summary>
    [Serializable]
    public class DeviceProtocolEvent
    {
        /// <summary>Loại sự kiện: start / begin / case / done / error.</summary>
        public string e;

        public string run;
        public string suite;

        // "case" là từ khoá C# — tên field khi serialize vẫn là "case".
        public string @case;
        public string name;
        public string status;
        public double ms;

        /// <summary>Thư mục report tuyệt đối trên device.</summary>
        public string dir;

        public int total;
        public int passed;
        public int failed;
        public string msg;
        public List<string> suites = new();

        /// <summary>Suite được yêu cầu nhưng không có trong build.</summary>
        public List<string> missing = new();

        public string device;

        /// <summary>Nguồn kích hoạt: intent / game-loop / command-line / environment / baked-config / in-process.</summary>
        public string src;

        /// <summary>Điểm sức khoẻ (sự kiện done).</summary>
        public int health;
    }

    /// <summary>Kết quả tóm tắt ghi vào file kết quả của Firebase Test Lab Game Loop.</summary>
    [Serializable]
    public class GameLoopResult
    {
        public string runId;
        public int scenario;
        public string device;
        public string startedAt;
        public string finishedAt;
        public RunSummary summary = new();
        public List<GameLoopFailure> failures = new();
    }

    [Serializable]
    public class GameLoopFailure
    {
        public string suite;
        public string testCase;
        public string status;
        public string message;
    }
}
