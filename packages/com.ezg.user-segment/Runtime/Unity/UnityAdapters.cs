using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace Ezg.UserSegment
{
    /// <summary>Đồng hồ thiết bị thô + Stopwatch monotonic — §4.4. KHÔNG cộng offset của game (TimeManager) vào đây.</summary>
    public sealed class UnityTimeSource : ITimeSource
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        public long DeviceUtcNowSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        public double MonotonicSeconds() => _sw.Elapsed.TotalSeconds;
    }

    public sealed class UnityLogger : ISegLogger
    {
        public void Log(LogLevel level, string message)
        {
            switch (level)
            {
                case LogLevel.Error: Debug.LogError(message); break;
                case LogLevel.Warn: Debug.LogWarning(message); break;
                default: Debug.Log(message); break;
            }
        }
    }

    /// <summary>File JSON trong persistentDataPath/seg/, ghi atomic tmp + rename — §C.10.1.</summary>
    public sealed class FileStateStorage : IStateStorage
    {
        private readonly string _dir;

        public FileStateStorage(string dir = null)
        {
            _dir = dir ?? Path.Combine(Application.persistentDataPath, "seg");
        }

        private string PathOf(string key) => Path.Combine(_dir, key + ".json");

        public string Read(string key)
        {
            var p = PathOf(key);
            return File.Exists(p) ? File.ReadAllText(p) : null;
        }

        public void WriteAtomic(string key, string content)
        {
            Directory.CreateDirectory(_dir);
            var p = PathOf(key);
            var tmp = p + ".tmp";
            File.WriteAllText(tmp, content);
            if (File.Exists(p)) File.Replace(tmp, p, null);
            else File.Move(tmp, p);
        }

        public void Delete(string key)
        {
            var p = PathOf(key);
            if (File.Exists(p)) File.Delete(p);
        }
    }

    /// <summary>UnityWebRequest GET, không retry — §C.11. Chạy trên main thread qua UniTask.</summary>
    public sealed class UnityConfigFetcher : IConfigFetcher
    {
        public async Task<FetchResult> FetchAsync(string url, IReadOnlyDictionary<string, string> headers, int timeoutMs,
            CancellationToken ct)
        {
            return await FetchUniTask(url, headers, timeoutMs, ct);
        }

        public async UniTask<FetchResult> FetchUniTask(string url, IReadOnlyDictionary<string, string> headers, int timeoutMs,
            CancellationToken ct)
        {
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = Mathf.Max(1, Mathf.CeilToInt(timeoutMs / 1000f));
                req.SetRequestHeader("Accept-Encoding", "gzip");
                if (headers != null)
                    foreach (var kv in headers)
                        req.SetRequestHeader(kv.Key, kv.Value);
                try
                {
                    await req.SendWebRequest().WithCancellation(ct);
                }
                catch (OperationCanceledException)
                {
                    return FetchResult.Failed("timeout");
                }
                catch (Exception e)
                {
                    return FetchResult.Failed(e.Message);
                }

                if (req.result != UnityWebRequest.Result.Success && req.responseCode == 0)
                    return FetchResult.Failed(req.error ?? "network");
                return FetchResult.Ok((int)req.responseCode, req.downloadHandler?.text);
            }
        }
    }
}
