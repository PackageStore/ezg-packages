using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Debug = UnityEngine.Debug;

namespace UnityFigmaBridge.Editor
{
    /// <summary>
    ///     Wall-clock time of each import phase, logged as one report when the import ends.
    ///     A phase entered again adds to its first entry, so phases that alternate inside one
    ///     loop (download a file, then import it) are split exactly. Calls made while no import is
    ///     timed do nothing.
    /// </summary>
    public static class FigmaImportTimer
    {
        private sealed class PhaseRecord
        {
            public string Name;
            public double Seconds;
            public string Detail;
            public int MeasuredSpans;
        }

        private static readonly List<PhaseRecord> s_Phases = new();
        private static readonly Dictionary<string, PhaseRecord> s_PhaseLookup = new();
        private static readonly Stack<PhaseRecord> s_Suspended = new();
        private static readonly Stopwatch s_Total = new();
        private static readonly Stopwatch s_PhaseClock = new();
        private static PhaseRecord s_Current;

        /// <summary>Report of the last timed import; null before the first one.</summary>
        public static string LastReport { get; private set; }

        public static bool Running => s_Total.IsRunning;

        public static void Start()
        {
            s_Phases.Clear();
            s_PhaseLookup.Clear();
            s_Suspended.Clear();
            s_Current = null;
            s_PhaseClock.Reset();
            s_Total.Restart();
        }

        /// <summary>Ends the running phase and starts <paramref name="name"/>.</summary>
        public static void Begin(string name)
        {
            if (!Running) return;
            Close();
            Open(name);
        }

        /// <summary>
        ///     Times a span inside the running phase as its own phase. The running phase is paused
        ///     for the span and continues when the returned scope is disposed.
        /// </summary>
        public static IDisposable Measure(string name)
        {
            if (!Running) return null;
            var outer = s_Current;
            Close();
            s_Suspended.Push(outer);
            Open(name);
            s_Current.MeasuredSpans++;
            return new MeasureScope();
        }

        /// <summary>Counts shown after a phase, e.g. "214 nodes, 1 request".</summary>
        public static void SetDetail(string name, string detail)
        {
            if (!Running) return;
            GetOrAdd(name).Detail = detail;
        }

        /// <summary>Stops the clock and logs the report. <paramref name="error"/> null means the import completed.</summary>
        public static void Finish(string error)
        {
            if (!Running) return;
            Close();
            s_Suspended.Clear();
            s_Total.Stop();

            var total = s_Total.Elapsed.TotalSeconds;
            var report = new StringBuilder();
            report.AppendLine(error == null
                ? "[FigmaBridge] Import complete! Here's the report:"
                : $"[FigmaBridge] Import stopped ({error}). Time until it stopped:");

            var index = 1;
            var measured = 0.0;
            foreach (var phase in s_Phases)
            {
                measured += phase.Seconds;
                report.Append($"{index++}. {phase.Name}: {FormatSeconds(phase.Seconds)} - {FormatShare(phase.Seconds, total)}");
                if (!string.IsNullOrEmpty(phase.Detail)) report.Append($" ({phase.Detail})");
                else if (phase.MeasuredSpans > 0) report.Append($" ({phase.MeasuredSpans}x)");
                report.AppendLine();
            }
            var unmeasured = total - measured;
            if (unmeasured >= 0.05)
                report.AppendLine($"{index}. Outside any phase: {FormatSeconds(unmeasured)} - {FormatShare(unmeasured, total)}");

            report.AppendLine();
            report.Append($"Total import time: {FormatSeconds(total)}");
            LastReport = report.ToString();
            Debug.Log(LastReport);
        }

        private static void Open(string name)
        {
            s_Current = GetOrAdd(name);
            s_PhaseClock.Restart();
        }

        private static void Close()
        {
            if (s_Current != null) s_Current.Seconds += s_PhaseClock.Elapsed.TotalSeconds;
            s_Current = null;
            s_PhaseClock.Reset();
        }

        private static PhaseRecord GetOrAdd(string name)
        {
            if (s_PhaseLookup.TryGetValue(name, out var phase)) return phase;
            phase = new PhaseRecord { Name = name };
            s_PhaseLookup[name] = phase;
            s_Phases.Add(phase);
            return phase;
        }

        private static string FormatSeconds(double seconds) =>
            seconds.ToString(seconds < 10 ? "0.00" : "0.0", CultureInfo.InvariantCulture) + "s";

        private static string FormatShare(double seconds, double total)
        {
            if (total <= 0) return "0%";
            var percent = seconds / total * 100;
            return percent > 0 && percent < 1 ? "<1%" : $"{Math.Round(percent):0}%";
        }

        private sealed class MeasureScope : IDisposable
        {
            private bool m_Disposed;

            public void Dispose()
            {
                if (m_Disposed || !Running) return;
                m_Disposed = true;
                Close();
                if (s_Suspended.Count == 0) return;
                var outer = s_Suspended.Pop();
                if (outer == null) return;
                s_Current = outer;
                s_PhaseClock.Restart();
            }
        }
    }
}
