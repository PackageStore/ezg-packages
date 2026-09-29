using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Bắt mọi log Unity (kể cả từ thread khác) vào một buffer có đánh số, để mỗi case lấy đúng lát log
    ///     phát sinh trong thời gian nó chạy.
    /// </summary>
    public static class AutoTestLogCapture
    {
        public struct Entry
        {
            public long Index;
            public double Time;
            public LogType Type;
            public string Message;
            public string Stack;
        }

        const int MAX_ENTRIES = 20000;
        const int MAX_MESSAGE_LENGTH = 4000;
        const int MAX_STACK_LENGTH = 6000;

        static readonly object _lock = new();
        static readonly List<Entry> _entries = new();
        static long _nextIndex;
        static bool _started;
        static List<Regex> _ignored = new();

        /// <summary>Chỉ số log kế tiếp — case ghi lại lúc bắt đầu để cắt lát.</summary>
        public static long Cursor
        {
            get
            {
                lock (_lock)
                {
                    return _nextIndex;
                }
            }
        }

        public static void Start(IEnumerable<string> ignoredPatterns = null)
        {
            SetIgnored(ignoredPatterns);
            if (_started) return;
            _started = true;
            Application.logMessageReceivedThreaded += OnLog;
        }

        public static void Stop()
        {
            if (!_started) return;
            _started = false;
            Application.logMessageReceivedThreaded -= OnLog;
        }

        public static void SetIgnored(IEnumerable<string> patterns)
        {
            var list = new List<Regex>();
            if (patterns != null)
                foreach (var p in patterns)
                {
                    if (string.IsNullOrWhiteSpace(p)) continue;
                    try
                    {
                        list.Add(new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
                    }
                    catch (ArgumentException)
                    {
                        // Regex sai cú pháp trong settings → bỏ qua, không làm hỏng lượt chạy.
                    }
                }

            lock (_lock)
            {
                _ignored = list;
            }
        }

        public static bool IsIgnored(string message)
        {
            List<Regex> ignored;
            lock (_lock)
            {
                ignored = _ignored;
            }

            foreach (var r in ignored)
                if (r.IsMatch(message))
                    return true;
            return false;
        }

        /// <summary>Lấy log từ chỉ số <paramref name="fromIndex" /> tới hiện tại.</summary>
        public static List<Entry> Since(long fromIndex)
        {
            var result = new List<Entry>();
            lock (_lock)
            {
                foreach (var e in _entries)
                    if (e.Index >= fromIndex)
                        result.Add(e);
            }

            return result;
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            // Log nội bộ của chính hệ thống test không tính là lỗi game.
            if (condition != null && condition.StartsWith(AutoTestLog.PREFIX, StringComparison.Ordinal) &&
                !IsErrorType(type))
                return;

            var entry = new Entry
            {
                Time = SafeNow(),
                Type = type,
                Message = Truncate(condition, MAX_MESSAGE_LENGTH),
                Stack = Truncate(stackTrace, MAX_STACK_LENGTH)
            };
            lock (_lock)
            {
                entry.Index = _nextIndex++;
                _entries.Add(entry);
                if (_entries.Count > MAX_ENTRIES) _entries.RemoveRange(0, _entries.Count - MAX_ENTRIES);
            }
        }

        static double SafeNow()
        {
            // realtimeSinceStartup chỉ gọi được trên main thread; log từ thread khác lấy mốc DateTime.
            try
            {
                return Time.realtimeSinceStartupAsDouble;
            }
            catch
            {
                return -1;
            }
        }

        static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        public static bool IsErrorType(LogType t)
        {
            return t == LogType.Error || t == LogType.Exception || t == LogType.Assert;
        }
    }

    /// <summary>Log nội bộ có tiền tố riêng để không bị bắt nhầm là lỗi game.</summary>
    public static class AutoTestLog
    {
        public const string PREFIX = "[EZG AutoTest] ";

        public static void Info(string message)
        {
            Debug.Log(PREFIX + message);
        }

        public static void Warn(string message)
        {
            Debug.LogWarning(PREFIX + message);
        }
    }
}
