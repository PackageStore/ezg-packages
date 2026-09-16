using System;
using System.Collections.Generic;
using System.Text;

namespace Ezg.UserSegment.Engine
{
    /// <summary>Tên event tracking — §11.1.</summary>
    public static class TrackEvent
    {
        public const string SegSnapshot = "seg_snapshot";
        public const string SegDecision = "seg_decision";
        public const string ActionSelected = "action_selected";
        public const string ActionPresented = "action_presented";
        public const string ActionExecuted = "action_executed";
        public const string ActionFailed = "action_failed";
        public const string ExpExposure = "exp_exposure";
        public const string ConfigRejected = "config_rejected";
        public const string RuleUnsupported = "rule_unsupported";
        public const string EngineError = "engine_error";
        public const string StateReset = "state_reset";
    }

    /// <summary>
    ///     Gói param theo §C.7: list nối ",", cặp nối ":", key=value nối "=", exp nối ";"; cắt 100 ký tự ở release build.
    ///     Mọi lời gọi sink bọc try/catch — lỗi tracking không lan ra engine.
    /// </summary>
    public sealed class TrackingEmitter
    {
        public const int MAX_STRING = 100;

        private readonly ITrackingSink _sink;
        private readonly ISegLogger _log;
        private readonly bool _debugBuild;

        /// <summary>Số lần LogEvent lỗi (debug overlay).</summary>
        public int SinkErrors { get; private set; }

        public TrackingEmitter(ITrackingSink sink, ISegLogger log, bool debugBuild)
        {
            _sink = sink;
            _log = log;
            _debugBuild = debugBuild;
        }

        public void Log(string name, Dictionary<string, object> p)
        {
            if (_sink == null) return;
            try
            {
                if (!_debugBuild)
                {
                    var keys = new List<string>(p.Keys);
                    foreach (var k in keys)
                        if (p[k] is string s && s.Length > MAX_STRING)
                            p[k] = Truncate(s);
                }

                _sink.LogEvent(name, p);
            }
            catch (Exception e)
            {
                SinkErrors++;
                _log?.Log(LogLevel.Warn, "[UserSegment] tracking sink failed: " + e.Message);
            }
        }

        public void SetUserProperty(string name, string value)
        {
            if (_sink == null) return;
            try
            {
                _sink.SetUserProperty(name, value);
            }
            catch (Exception e)
            {
                SinkErrors++;
                _log?.Log(LogLevel.Warn, "[UserSegment] user property failed: " + e.Message);
            }
        }

        /// <summary>Cắt tại ký tự nối cuối cùng trước vị trí 100 để không có phần tử cụt — §C.7.1.</summary>
        public static string Truncate(string s)
        {
            if (s == null || s.Length <= MAX_STRING) return s;
            var cut = -1;
            for (var i = MAX_STRING; i > 0; i--)
            {
                var ch = s[i];
                if (ch == ',' || ch == ';')
                {
                    cut = i;
                    break;
                }
            }

            return cut > 0 ? s.Substring(0, cut) : s.Substring(0, MAX_STRING);
        }

        public static string Join(IReadOnlyList<string> items)
        {
            if (items == null || items.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(items[i]);
            }

            return sb.ToString();
        }

        public static long B(bool v) => v ? 1 : 0;
    }
}
