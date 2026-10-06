using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace UnityFigmaBridge.Editor.Bridge
{
    /// Agent on the hub's /agent route. Thread-agnostic: no Unity calls, nothing needs the main thread.
    public sealed class HubClient : IDisposable
    {
        readonly ClientWebSocket _socket = new ClientWebSocket();
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        readonly BlockingCollection<string> _inbox = new BlockingCollection<string>();
        readonly PartAssembler _parts = new PartAssembler(HubProtocol.AssembledMaxChars, TimeSpan.FromSeconds(HubProtocol.PartTtlSeconds));
        readonly HubRequests _requests;
        readonly TaskCompletionSource<bool> _welcome = NewSignal();
        readonly object _gate = new object();
        readonly List<HubFile> _files = new List<HubFile>();
        readonly HashSet<string> _unusable = new HashSet<string>();
        TaskCompletionSource<bool> _changed = NewSignal();
        bool _closed;

        HubClient() { _requests = new HubRequests(text => Send(text, _cts.Token)); }
        /// ws://127.0.0.1:{port}/agent, sends agent-hello, waits for agent-welcome.
        public static async Task<HubClient> Connect(int port, TimeSpan timeout, CancellationToken ct = default)
        {
            var client = new HubClient();
            try { await client.Open(port, timeout, ct).ConfigureAwait(false); return client; }
            catch (Exception) { client.Dispose(); throw; }
        }

        public IReadOnlyList<HubFile> Files { get { lock (_gate) return _files.ToArray(); } }
        /// False for a file whose plugin speaks another protocol than this client; requests to it are refused.
        public bool IsUsable(HubFile file) { lock (_gate) return file != null && !_unusable.Contains(file.ConnectionId); }

        /// Waits until a plugin hello with this fileKey arrives; null after the timeout.
        public async Task<HubFile> WaitForFile(string fileKey, TimeSpan timeout)
        {
            if (fileKey == null) return null;
            var expired = Task.Delay(timeout);
            while (true)
            {
                Task changed;
                lock (_gate)
                {
                    var known = _files.LastOrDefault(f => f.FileKey == fileKey);
                    if (known != null || _closed) return known;
                    changed = _changed.Task;
                }
                if (await Task.WhenAny(changed, expired).ConfigureAwait(false) == expired) return null;
            }
        }

        /// The reply's result, or a HubRequestException.
        public Task<JToken> Request(string connectionId, string op, JToken payload, TimeSpan timeout,
            CancellationToken ct = default)
        {
            lock (_gate)
                if (_unusable.Contains(connectionId))
                    return Task.FromException<JToken>(new HubRequestException(HubRequestKind.Remote,
                        "This Figma file runs an EZG Tools plugin with another bridge protocol; update the EZG Tools plugin"));
            return _requests.Start(connectionId, op, payload, timeout, ct);
        }

        public void Dispose() { Shutdown("hub client disposed"); _ = CloseSocket(); }

        async Task Open(int port, TimeSpan timeout, CancellationToken ct)
        {
            using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token))
            using (var hello = CancellationTokenSource.CreateLinkedTokenSource(attempt.Token))
            {
                attempt.CancelAfter(timeout);
                try
                {
                    await _socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}{HubProtocol.AgentPath}"), attempt.Token).ConfigureAwait(false);
                    hello.CancelAfter(HubProtocol.HelloTimeoutMs);
                    await Send(HubProtocol.AgentHello(), hello.Token).ConfigureAwait(false);
                    _ = Task.Run(ReceiveLoop);
                    _ = Task.Factory.StartNew(ProcessLoop, TaskCreationOptions.LongRunning);
                    using (attempt.Token.Register(() => _welcome.TrySetCanceled(attempt.Token)))
                        await _welcome.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                { throw new TimeoutException($"The hub on port {port} did not welcome this agent within {timeout.TotalSeconds:0.#} s"); }
            }
        }

        // Only reads and queues: a stalled loop stops pong replies and the hub cuts the agent off.
        async Task ReceiveLoop()
        {
            var buffer = new ArraySegment<byte>(new byte[64 * 1024]);
            var message = new MemoryStream();
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    message.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _socket.ReceiveAsync(buffer, _cts.Token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        message.Write(buffer.Array, 0, result.Count);
                    }
                    while (!result.EndOfMessage);
                    if (result.MessageType == WebSocketMessageType.Text)
                        _inbox.Add(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                }
            }
            catch (Exception) { }
            finally { _inbox.CompleteAdding(); }
        }
        void ProcessLoop()
        {
            try { foreach (var raw in _inbox.GetConsumingEnumerable(_cts.Token)) { try { Route(raw); } catch (Exception) { } } }
            catch (Exception) { }
            finally { Shutdown("hub connection closed"); }
        }
        void Route(string raw)
        {
            var text = _parts.Accept(raw);
            var message = text == null ? null : HubProtocol.Parse(text);
            if (message == null) return;
            switch (message.Kind)
            {
                case HubMessageKind.Welcome: _welcome.TrySetResult(true); break;
                case HubMessageKind.Hello: AddFile(message); break;
                case HubMessageKind.Closed: RemoveFile(message.ConnectionId); break;
                case HubMessageKind.Reply: _requests.OnReply(message); break;
                case HubMessageKind.Progress: _requests.OnProgress(message); break;
                case HubMessageKind.Bye: Shutdown($"The hub ended the connection: {message.Reason}"); break;
            }
        }

        void AddFile(HubMessage message)
        {
            var file = message.File;
            TaskCompletionSource<bool> previous;
            lock (_gate)
            {
                if (_closed) return;
                _files.RemoveAll(f => f.ConnectionId == file.ConnectionId);
                _files.Add(file);
                if (message.Protocol == HubProtocol.PluginProtocol) _unusable.Remove(file.ConnectionId);
                else _unusable.Add(file.ConnectionId);
                previous = _changed;
                _changed = NewSignal();
            }
            previous.TrySetResult(true);
        }
        void RemoveFile(string connectionId)
        {
            lock (_gate) { _files.RemoveAll(f => f.ConnectionId == connectionId); _unusable.Remove(connectionId); }
            _requests.FailConnection(connectionId);
        }
        void Shutdown(string reason)
        {
            lock (_gate)
            {
                if (_closed) return;
                _closed = true;
                _files.Clear();
                _unusable.Clear();
            }
            _welcome.TrySetException(new InvalidOperationException(reason));
            _requests.FailAll(reason);
            _changed.TrySetResult(true);
        }
        static TaskCompletionSource<bool> NewSignal() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Sends are serialized: the timeout cancel and a new request can write at the same time.
        async Task Send(string text, CancellationToken token)
        {
            var bytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(text));
            await _sendLock.WaitAsync(token).ConfigureAwait(false);
            try { await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, token).ConfigureAwait(false); }
            finally { _sendLock.Release(); }
        }

        async Task CloseSocket()
        {
            try
            {
                using (var limit = new CancellationTokenSource(1000))
                    await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", limit.Token).ConfigureAwait(false);
            }
            catch (Exception) { }
            finally { _cts.Cancel(); _socket.Dispose(); }
        }
    }
}
