using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;

namespace UnityFigmaBridge.Editor.Bridge
{
    public enum BridgeStatus { Idle, Connecting, NoHub, Connected }

    /// Live hub status for editor UI. The owner must call Dispose from OnDisable; closing a window does not dispose it.
    public sealed class BridgeStatusMonitor : IDisposable
    {
        const double RetrySeconds = 3;

        sealed class Outcome
        {
            public int Token;
            public HubClient Client;
        }

        readonly Action _repaint;
        readonly object _sync = new object();
        HubClient _client;
        Outcome _pending;
        int _token;
        bool _disposed;
        double _lastStart;
        int _seenVersion;
        IReadOnlyList<HubFile> _files = Array.Empty<HubFile>();

        public BridgeStatusMonitor(Action repaint) { _repaint = repaint; }

        public BridgeStatus Status { get; private set; }
        public int Port { get; private set; }

        public IReadOnlyList<HubFile> Files
        {
            get { return Status != BridgeStatus.Connected ? Array.Empty<HubFile>() : _files; }
        }

        public bool IsUsable(HubFile file)
        {
            var client = _client;
            return client != null && client.IsUsable(file);
        }

        public void Tick(int port)
        {
            if (_disposed) return;
            var status = Status;
            var version = _seenVersion;

            if (port != Port)
            {
                DisposeClient();
                _files = Array.Empty<HubFile>();
                Port = port;
                Status = BridgeStatus.Connecting;
                StartConnect(port);
            }
            else if (Status == BridgeStatus.Connecting)
            {
                Outcome taken;
                lock (_sync) { taken = _pending; _pending = null; }
                if (taken != null)
                {
                    if (taken.Client != null)
                    {
                        _client = taken.Client;
                        _seenVersion = _client.FilesVersion;
                        _files = _client.Files;
                        Status = BridgeStatus.Connected;
                    }
                    else Status = BridgeStatus.NoHub;
                }
            }

            if (Status == BridgeStatus.Connected)
            {
                if (!_client.IsConnected)
                {
                    DisposeClient();
                    _files = Array.Empty<HubFile>();
                    Status = BridgeStatus.NoHub;
                }
                else if (_client.FilesVersion != _seenVersion)
                {
                    _seenVersion = _client.FilesVersion;
                    _files = _client.Files;
                }
            }
            else if ((Status == BridgeStatus.Idle || Status == BridgeStatus.NoHub)
                     && EditorApplication.timeSinceStartup - _lastStart >= RetrySeconds)
            {
                Status = BridgeStatus.Connecting;
                StartConnect(port);
            }

            if (Status != status || _seenVersion != version) Notify();
        }

        public void Dispose()
        {
            Outcome taken;
            lock (_sync)
            {
                _disposed = true;
                _token++;
                taken = _pending;
                _pending = null;
            }
            if (taken != null && taken.Client != null) taken.Client.Dispose();
            DisposeClient();
            _files = Array.Empty<HubFile>();
            Status = BridgeStatus.Idle;
        }

        void DisposeClient()
        {
            var client = _client;
            _client = null;
            if (client != null) client.Dispose();
        }

        void Notify()
        {
            if (_repaint != null) _repaint();
        }

        void StartConnect(int port)
        {
            int token;
            Outcome taken;
            lock (_sync)
            {
                token = ++_token;
                taken = _pending;
                _pending = null;
                _lastStart = EditorApplication.timeSinceStartup;
            }
            if (taken != null && taken.Client != null) taken.Client.Dispose();
            _ = RunConnect(port, token);
        }

        async Task RunConnect(int port, int token)
        {
            HubClient client = null;
            try { client = await HubClient.Connect(port, TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
            catch (Exception) { }
            Deliver(token, client);
        }

        void Deliver(int token, HubClient client)
        {
            bool stale;
            lock (_sync)
            {
                stale = _disposed || token != _token;
                if (!stale) _pending = new Outcome { Token = token, Client = client };
            }
            if (stale && client != null) client.Dispose();
        }
    }
}
