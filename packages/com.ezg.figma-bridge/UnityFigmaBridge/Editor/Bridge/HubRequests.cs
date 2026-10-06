using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace UnityFigmaBridge.Editor.Bridge
{
    public enum HubRequestKind { Timeout, Closed, Remote }

    public sealed class HubRequestException : Exception
    {
        public HubRequestKind Kind { get; }
        public string RemoteCode { get; }

        public HubRequestException(HubRequestKind kind, string message, string remoteCode = null) : base(message)
        {
            Kind = kind;
            RemoteCode = remoteCode;
        }
    }

    /// Pending requests with deadlines. Mirrors bridge-server/src/rpc.ts; never touches Unity or the main thread.
    internal sealed class HubRequests
    {
        const long MaxDelayMs = 4294967294L;
        static readonly Stopwatch Clock = Stopwatch.StartNew();

        sealed class Pending
        {
            public string Id, ConnectionId, Op;
            public long StartedMs, DeadlineMs;
            public TaskCompletionSource<JToken> Completion;
            public Timer Timer;
            public CancellationTokenRegistration Cancel;
        }

        readonly Func<string, Task> _send;
        readonly object _gate = new object();
        readonly Dictionary<string, Pending> _pending = new Dictionary<string, Pending>();
        long _counter;
        bool _closed;

        public HubRequests(Func<string, Task> send) { _send = send; }

        public Task<JToken> Start(string connectionId, string op, JToken payload, TimeSpan timeout, CancellationToken ct)
        {
            var now = Clock.ElapsedMilliseconds;
            var delay = ToMs(timeout);
            var entry = new Pending
            {
                ConnectionId = connectionId,
                Op = op,
                StartedMs = now,
                DeadlineMs = now + delay,
                Completion = new TaskCompletionSource<JToken>(TaskCreationOptions.RunContinuationsAsynchronously),
            };
            lock (_gate)
            {
                if (_closed) return Task.FromException<JToken>(Closed("hub connection closed"));
                entry.Id = (++_counter).ToString(CultureInfo.InvariantCulture);
                _pending[entry.Id] = entry;
                entry.Timer = new Timer(OnTimer, entry, delay, Timeout.Infinite);
            }
            if (ct.CanBeCanceled) entry.Cancel = ct.Register(() => OnCancelled(entry, ct));
            _ = SendRequest(entry, HubProtocol.Request(connectionId, entry.Id, op, payload));
            return entry.Completion.Task;
        }

        public void OnReply(HubMessage message)
        {
            var entry = Take(message.Id, message.ConnectionId);
            if (entry == null) return;
            if (message.Ok) entry.Completion.TrySetResult(message.Result);
            else Fail(entry, HubRequestKind.Remote, message.ErrorMessage, message.ErrorCode);
        }

        public void OnProgress(HubMessage message)
        {
            lock (_gate)
            {
                if (message.Id == null || !_pending.TryGetValue(message.Id, out var entry)) return;
                if (entry.ConnectionId != message.ConnectionId) return;
                var now = Clock.ElapsedMilliseconds;
                var extend = Math.Min(Math.Max(message.ExtendMs, 0), HubProtocol.ProgressExtendMaxMs);
                if (now + extend <= entry.DeadlineMs) return;
                entry.DeadlineMs = now + extend;
                entry.Timer.Change(extend, Timeout.Infinite);
            }
        }

        public void FailConnection(string connectionId)
        {
            List<string> ids;
            lock (_gate) ids = _pending.Values.Where(e => e.ConnectionId == connectionId).Select(e => e.Id).ToList();
            foreach (var id in ids) FailClosed(id, "Figma file disconnected");
        }

        public void FailAll(string reason)
        {
            List<string> ids;
            lock (_gate)
            {
                _closed = true;
                ids = _pending.Keys.ToList();
            }
            foreach (var id in ids) FailClosed(id, reason);
        }

        void FailClosed(string id, string reason)
        {
            var entry = Take(id, null);
            if (entry != null) Fail(entry, HubRequestKind.Closed, reason);
        }

        void OnTimer(object state)
        {
            var entry = (Pending)state;
            lock (_gate)
            {
                if (!_pending.TryGetValue(entry.Id, out var current) || current != entry) return;
                var left = entry.DeadlineMs - Clock.ElapsedMilliseconds;
                if (left > 0) { entry.Timer.Change(left, Timeout.Infinite); return; }
            }
            if (Take(entry.Id, null) == null) return;
            var secs = (Clock.ElapsedMilliseconds - entry.StartedMs) / 1000.0;
            Fail(
                entry,
                HubRequestKind.Timeout,
                $"Figma {entry.Op} call timed out after {secs.ToString("0.0", CultureInfo.InvariantCulture)} s with no reply or progress; a cancel was sent"
            );
            SendCancel(entry);
        }

        void OnCancelled(Pending entry, CancellationToken ct)
        {
            if (Take(entry.Id, null) == null) return;
            entry.Completion.TrySetCanceled(ct);
            SendCancel(entry);
        }

        // The plugin keeps running a request nobody waits for; the cancel stops it at its next yield.
        void SendCancel(Pending entry)
        {
            var text = HubProtocol.Request(entry.ConnectionId, entry.Id + ".cancel", "cancel", new JObject { ["id"] = entry.Id });
            _ = SendQuietly(text);
        }

        async Task SendRequest(Pending entry, string text)
        {
            try { await _send(text).ConfigureAwait(false); }
            catch (Exception e) { if (Take(entry.Id, null) != null) Fail(entry, HubRequestKind.Closed, e.Message); }
        }

        async Task SendQuietly(string text)
        {
            try { await _send(text).ConfigureAwait(false); }
            catch (Exception) { }
        }

        Pending Take(string id, string connectionId)
        {
            Pending entry;
            lock (_gate)
            {
                if (id == null || !_pending.TryGetValue(id, out entry)) return null;
                if (connectionId != null && entry.ConnectionId != connectionId) return null;
                _pending.Remove(id);
            }
            entry.Timer.Dispose();
            entry.Cancel.Dispose();
            return entry;
        }

        static void Fail(Pending entry, HubRequestKind kind, string message, string code = null) =>
            entry.Completion.TrySetException(new HubRequestException(kind, message, code));

        static HubRequestException Closed(string message) => new HubRequestException(HubRequestKind.Closed, message);

        static long ToMs(TimeSpan timeout) =>
            timeout == Timeout.InfiniteTimeSpan ? MaxDelayMs : Math.Max(0, Math.Min(MaxDelayMs, (long)timeout.TotalMilliseconds));
    }
}
