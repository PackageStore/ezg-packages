using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ezg.UserSegment.Engine;

namespace Ezg.UserSegment.Tests
{
    public sealed class FakeStorage : IStateStorage
    {
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
        public string Read(string key) => Files.TryGetValue(key, out var v) ? v : null;

        public void WriteAtomic(string key, string content)
        {
            lock (Files) Files[key] = content;
        }

        public void Delete(string key)
        {
            lock (Files) Files.Remove(key);
        }
    }

    public sealed class FakeClock : ITimeSource
    {
        public long Now;
        public double Mono;
        public long DeviceUtcNowSeconds() => Now;
        public double MonotonicSeconds() => Mono;

        /// <summary>Tiến cả đồng hồ lẫn monotonic (không tạo clock_suspect).</summary>
        public void Advance(long seconds)
        {
            Now += seconds;
            Mono += seconds;
        }

        public void Set(long now)
        {
            var d = now - Now;
            Now = now;
            if (d > 0) Mono += d;
        }
    }

    public sealed class TrackedEvent
    {
        public string Name;
        public Dictionary<string, object> Params;
        public override string ToString() => Name + " " + string.Join(", ", ParamsList());

        private IEnumerable<string> ParamsList()
        {
            foreach (var kv in Params) yield return kv.Key + "=" + kv.Value;
        }
    }

    public sealed class FakeTracking : ITrackingSink
    {
        public readonly List<TrackedEvent> Events = new List<TrackedEvent>();
        public readonly Dictionary<string, string> Properties = new Dictionary<string, string>();

        public void LogEvent(string name, IReadOnlyDictionary<string, object> parameters)
        {
            Events.Add(new TrackedEvent { Name = name, Params = new Dictionary<string, object>(parameters) });
        }

        public void SetUserProperty(string name, string value)
        {
            if (value == null) Properties.Remove(name);
            else Properties[name] = value;
        }

        public List<TrackedEvent> Of(string name) => Events.FindAll(e => e.Name == name);
    }

    public sealed class FakeHistoryStore : IActionHistoryStore
    {
        public string Blob;
        public int Saves;
        public string Load() => Blob;

        public void Save(string blob)
        {
            Blob = blob;
            Saves++;
        }
    }

    public sealed class FakeLogger : ISegLogger
    {
        public readonly List<string> Lines = new List<string>();
        public void Log(LogLevel level, string message) => Lines.Add(level + ": " + message);
    }

    public sealed class FakeFetcher : IConfigFetcher
    {
        public FetchResult Result;
        public Task<FetchResult> FetchAsync(string url, IReadOnlyDictionary<string, string> headers, int timeoutMs, CancellationToken ct) =>
            Task.FromResult(Result);
    }

    /// <summary>Ghi lại mọi Execute; báo kết quả theo hàng đợi `Reports` ("executed:granted" / "failed:offline"), mặc định result đầu tiên hợp lệ.</summary>
    public sealed class FakeExecutor : IActionExecutor
    {
        public readonly List<ActionRequest> Requests = new List<ActionRequest>();
        public readonly Queue<string> Reports = new Queue<string>();
        public bool ManualReport;

        public FakeExecutor(ActionType type)
        {
            ActionType = type;
        }

        public ActionType ActionType { get; }

        public void Execute(ActionRequest request)
        {
            Requests.Add(request);
            if (ManualReport) return;
            var report = Reports.Count > 0 ? Reports.Dequeue() : "executed:" + DefaultResult(request.Type);
            request.ReportPresented();
            var idx = report.IndexOf(':');
            var kind = idx < 0 ? report : report.Substring(0, idx);
            var value = idx < 0 ? string.Empty : report.Substring(idx + 1);
            if (kind == "failed") request.ReportFailed(value);
            else request.ReportExecuted(string.IsNullOrEmpty(value) ? DefaultResult(request.Type) : value);
        }

        public static string DefaultResult(ActionType t)
        {
            switch (t)
            {
                case ActionType.GIVE_REWARD: return "granted";
                case ActionType.SHOW_POPUP: return "clicked";
                case ActionType.SHOW_OFFER: return "closed";
                case ActionType.CHANGE_DIFFICULTY: return "applied";
                default: return "scheduled";
            }
        }
    }
}
