using System;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Ezg.BugLogger
{
    /// <summary>Một dòng log đã ghi lại.</summary>
    internal struct BugLogLine
    {
        public long TicksUtc;
        public LogType Type;
        public string Condition;
        public string StackTrace;
    }

    /// <summary>
    /// Giữ <see cref="DEFAULT_CAPACITY"/> dòng log cuối (kèm stack trace của exception) cho <c>log.txt</c>.
    /// Nhận từ <see cref="Application.logMessageReceivedThreaded"/> nên <see cref="Add"/> chạy trên mọi
    /// thread: chỉ lưu tham chiếu vào mảng cấp phát sẵn dưới lock — không cấp phát, không Unity API, không
    /// <c>Debug.Log</c> (sẽ đệ quy). Chỉ format một lần lúc dựng report.
    /// </summary>
    internal sealed class BugLogRingBuffer
    {
        #region Fields

        /// <summary>Số dòng buffer dùng chung giữ lại.</summary>
        public const int DEFAULT_CAPACITY = 200;

        private const string TIME_FORMAT = "HH:mm:ss.fff";
        private const string STACK_INDENT = "    ";
        private const int AVERAGE_LINE_LENGTH = 120;

        private readonly BugLogLine[] _lines;
        private readonly object _lock = new object();
        private int _next;
        private int _count;

        /// <summary>Buffer gắn vào log callback của Unity; tạo bởi <see cref="Attach"/>, null khi chưa gắn.</summary>
        public static BugLogRingBuffer Shared { get; private set; }

        #endregion

        #region Initialize

        /// <summary>Tạo buffer giữ <paramref name="capacity"/> dòng cuối.</summary>
        public BugLogRingBuffer(int capacity)
        {
            _lines = new BugLogLine[Math.Max(1, capacity)];
        }

        #endregion

        #region Public

        /// <summary>Số dòng đang giữ.</summary>
        public int Count
        {
            get
            {
                lock (_lock)
                    return _count;
            }
        }

        /// <summary>Gắn <see cref="Shared"/> vào log callback (một lần mỗi phiên chơi). Main thread.</summary>
        public static void Attach()
        {
            if (Shared != null)
                return;

            Shared = new BugLogRingBuffer(DEFAULT_CAPACITY);
            Application.logMessageReceivedThreaded += HandleLogMessage;
        }

        /// <summary>Ghi một dòng log. An toàn mọi thread; không cấp phát.</summary>
        public void Add(string condition, string stackTrace, LogType type)
        {
            var ticks = BugLoggerClock.UtcNow.Ticks;
            lock (_lock)
            {
                ref var line = ref _lines[_next];
                line.TicksUtc = ticks;
                line.Type = type;
                line.Condition = condition;
                line.StackTrace = type == LogType.Exception ? stackTrace : null;

                _next = (_next + 1) % _lines.Length;
                if (_count < _lines.Length)
                    _count++;
            }
        }

        /// <summary>Chép các dòng đang giữ, cũ nhất trước. Rẻ: không format.</summary>
        public BugLogLine[] TakeSnapshot()
        {
            lock (_lock)
            {
                var result = new BugLogLine[_count];
                var start = (_next - _count + _lines.Length) % _lines.Length;
                for (var i = 0; i < _count; i++)
                    result[i] = _lines[(start + i) % _lines.Length];

                return result;
            }
        }

        /// <summary>Xoá mọi dòng đang giữ.</summary>
        public void Clear()
        {
            lock (_lock)
            {
                Array.Clear(_lines, 0, _lines.Length);
                _next = 0;
                _count = 0;
            }
        }

        /// <summary>
        /// Format snapshot thành các dòng <c>HH:mm:ss.fff [T] message</c> (UTC), stack trace của exception
        /// thụt lề bên dưới. An toàn mọi thread.
        /// </summary>
        public static string Format(BugLogLine[] lines)
        {
            if (lines == null || lines.Length == 0)
                return string.Empty;

            var builder = new StringBuilder(lines.Length * AVERAGE_LINE_LENGTH);
            foreach (var line in lines)
            {
                builder.Append(new DateTime(line.TicksUtc, DateTimeKind.Utc).ToString(TIME_FORMAT, CultureInfo.InvariantCulture))
                    .Append(" [").Append(TypeTag(line.Type)).Append("] ")
                    .Append(line.Condition).Append('\n');

                if (string.IsNullOrEmpty(line.StackTrace))
                    continue;

                foreach (var frame in line.StackTrace.Split('\n'))
                {
                    if (frame.Length == 0)
                        continue;

                    builder.Append(STACK_INDENT).Append(frame.TrimEnd('\r')).Append('\n');
                }
            }

            return builder.ToString();
        }

        #endregion

        #region Private

        private static char TypeTag(LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                    return 'E';
                case LogType.Assert:
                    return 'A';
                case LogType.Warning:
                    return 'W';
                case LogType.Exception:
                    return 'X';
                default:
                    return 'I';
            }
        }

        private static void HandleLogMessage(string condition, string stackTrace, LogType type)
        {
            Shared?.Add(condition, stackTrace, type);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Domain reload có thể tắt: gỡ handler của phiên trước, phiên mới gắn lại qua Attach().
            Application.logMessageReceivedThreaded -= HandleLogMessage;
            Shared = null;
        }

        #endregion
    }
}
