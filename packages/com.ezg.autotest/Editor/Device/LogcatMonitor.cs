using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Ezg.AutoTest.Editor
{
    /// <summary>Một dòng logcat định dạng <c>-v threadtime</c> đã tách trường.</summary>
    public sealed class LogcatLine
    {
        public int Pid;
        public int Tid;
        public char Level;
        public string Tag;
        public string Message;
        public string Raw;

        // "09-29 11:29:00.123  1234  1250 I Unity   : message"
        static readonly Regex THREADTIME = new(
            @"^\d\d-\d\d\s+\d\d:\d\d:\d\d\.\d+\s+(\d+)\s+(\d+)\s+([VDIWEFSA])\s+([^:]*?)\s*:\s?(.*)$");

        public static bool TryParse(string raw, out LogcatLine line)
        {
            line = null;
            if (string.IsNullOrEmpty(raw)) return false;
            var m = THREADTIME.Match(raw);
            if (!m.Success)
            {
                // Dòng không đúng định dạng (vd "--------- beginning of main") — giữ làm raw.
                line = new LogcatLine { Pid = -1, Tid = -1, Level = '?', Tag = "", Message = raw, Raw = raw };
                return true;
            }

            line = new LogcatLine
            {
                Pid = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                Tid = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                Level = m.Groups[3].Value[0],
                Tag = m.Groups[4].Value.Trim(),
                Message = m.Groups[5].Value,
                Raw = raw
            };
            return true;
        }

        /// <summary>Sự kiện giao thức <c>EZG_AUTOTEST {json}</c> trong dòng này, null nếu không có / JSON hỏng.</summary>
        public DeviceProtocolEvent TryParseEvent()
        {
            var msg = Message ?? "";
            var idx = msg.IndexOf(DeviceProtocol.LOG_PREFIX, StringComparison.Ordinal);
            if (idx < 0) return null;
            var json = msg.Substring(idx + DeviceProtocol.LOG_PREFIX.Length).Trim();
            if (json.Length < 2 || json[0] != '{') return null;
            try
            {
                var evt = JsonUtility.FromJson<DeviceProtocolEvent>(json);
                return string.IsNullOrEmpty(evt?.e) ? null : evt;
            }
            catch
            {
                // Dòng bị logcat cắt ngang (quá dài) — bỏ qua.
                return null;
            }
        }
    }

    /// <summary>
    ///     Dò crash / ANR / app chết của MỘT package trong luồng logcat: Java FATAL EXCEPTION, native "Fatal signal"
    ///     + tombstone, ANR, crash handler của Unity (tag CRASH), tiến trình bị kill (kể cả low memory killer).
    ///     Dòng trước khi biết PID được giữ lại và quét lại khi biết PID.
    /// </summary>
    public sealed class LogcatCrashWatcher
    {
        const int HISTORY_MAX = 4000;
        const int EXCERPT_BEFORE = 10;

        static readonly HashSet<string> CRASH_TAGS = new(StringComparer.Ordinal)
        {
            "AndroidRuntime", "DEBUG", "libc", "CRASH", "ActivityManager", "lowmemorykiller", "lmkd", "tombstoned",
            "ActivityTaskManager", "Unity"
        };

        readonly string _pkg;
        readonly List<LogcatLine> _history = new();
        readonly Regex _died;
        int _historyOffset;

        public LogcatCrashWatcher(string packageName)
        {
            _pkg = packageName ?? "";
            _died = new Regex(@"Process " + Regex.Escape(_pkg) + @"(:\S+)? \(pid (\d+)\) has died");
        }

        public int AppPid { get; private set; } = -1;
        public bool Crashed => CrashKind != null;
        public string CrashKind { get; private set; }
        public string CrashLine { get; private set; }
        public bool ProcessDied { get; private set; }
        public bool LowMemoryKill { get; private set; }

        /// <summary>Chỉ số tuyệt đối của dòng crash (dùng cắt đoạn trích).</summary>
        int _crashIndex = -1;

        public void SetPid(int pid)
        {
            if (pid <= 0 || pid == AppPid) return;
            AppPid = pid;
            // Quét lại các dòng đã nhận trước khi biết PID.
            for (var i = 0; i < _history.Count && !Crashed; i++) Inspect(_history[i], _historyOffset + i);
        }

        public void Feed(LogcatLine line)
        {
            _history.Add(line);
            if (_history.Count > HISTORY_MAX)
            {
                var drop = _history.Count - HISTORY_MAX;
                _history.RemoveRange(0, drop);
                _historyOffset += drop;
            }

            Inspect(line, _historyOffset + _history.Count - 1);
        }

        void Inspect(LogcatLine l, int absIndex)
        {
            var msg = l.Message ?? "";
            var isApp = AppPid > 0 && l.Pid == AppPid;

            // Chỉ tính "has died" của đúng PID đang theo dõi (bỏ qua instance cũ bị force-stop trước khi mở app).
            if (!ProcessDied && AppPid > 0)
            {
                var m = _died.Match(msg);
                if (m.Success && m.Groups[1].Value.Length == 0 &&
                    int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var diedPid) &&
                    diedPid == AppPid)
                    ProcessDied = true;
            }

            if (!LowMemoryKill && (l.Tag == "lowmemorykiller" || l.Tag == "lmkd") && msg.Contains(_pkg))
                LowMemoryKill = true;

            if (Crashed) return;
            string kind = null;
            if (isApp && msg.Contains("FATAL EXCEPTION")) kind = "Java/Kotlin crash (FATAL EXCEPTION)";
            else if (l.Tag == "AndroidRuntime" && msg.StartsWith("Process: " + _pkg + ",", StringComparison.Ordinal))
                kind = "Java/Kotlin crash (FATAL EXCEPTION)";
            else if (msg.Contains("Fatal signal") && (isApp || msg.Contains("(" + _pkg + ")")))
                kind = "Native crash (Fatal signal)";
            else if (msg.Contains(">>> " + _pkg + " <<<")) kind = "Native crash (tombstone)";
            else if (msg.Contains("ANR in " + _pkg)) kind = "ANR — app treo không phản hồi";
            else if (isApp && (l.Tag == "CRASH" || msg.Contains("Crash!!!"))) kind = "Unity crash (CRASH handler)";

            if (kind == null) return;
            CrashKind = kind;
            CrashLine = l.Raw;
            _crashIndex = absIndex;
        }

        /// <summary>
        ///     Đoạn trích tối đa <paramref name="maxLines" /> dòng quanh chỗ crash (chỉ dòng của app / tag liên quan
        ///     crash / có tên package) — đính kèm cho dev đọc stack trace.
        /// </summary>
        public string Excerpt(int maxLines)
        {
            var sb = new StringBuilder();
            var start = _crashIndex >= 0 ? Math.Max(0, _crashIndex - _historyOffset - EXCERPT_BEFORE) : 0;
            if (_crashIndex < 0) start = Math.Max(0, _history.Count - maxLines * 4);
            var count = 0;
            for (var i = start; i < _history.Count && count < maxLines; i++)
            {
                var l = _history[i];
                var relevant = (AppPid > 0 && l.Pid == AppPid) || CRASH_TAGS.Contains(l.Tag) ||
                               (l.Message ?? "").Contains(_pkg);
                if (!relevant) continue;
                sb.AppendLine(l.Raw);
                count++;
            }

            return sb.ToString();
        }
    }
}
