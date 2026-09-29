using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Bộ đếm frame/thời gian cho mọi thao tác chờ của test — chạy được ở cả Edit mode (Editor bơm qua
    ///     EditorApplication.update) lẫn Play mode / device (bơm qua <see cref="AutoTestPump" />). Không phụ
    ///     thuộc UniTask để package dùng được ở mọi project.
    /// </summary>
    public static class AutoTestClock
    {
        struct Waiter
        {
            public long TargetTick;
            public double TargetTime;
            public TaskCompletionSource<bool> Tcs;
            public CancellationToken Token;
        }

        static readonly List<Waiter> _waiters = new();
        static readonly List<Waiter> _ready = new();
        static long _tick;
        static int _lastFrame = -1;
        static bool _pumpInstalled;

        /// <summary>Số tick đã bơm (mỗi frame Play / mỗi vòng update Editor = 1 tick).</summary>
        public static long Tick => _tick;

        /// <summary>Thời gian thực (giây) — không bị Time.timeScale ảnh hưởng.</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>Editor gắn hàm này vào EditorApplication.update; device dùng AutoTestPump.</summary>
        public static void Pump()
        {
            // Play mode: có thể bị bơm 2 lần/frame (Editor update + MonoBehaviour) → chỉ tính 1 lần/frame.
            if (Application.isPlaying)
            {
                var frame = Time.frameCount;
                if (frame == _lastFrame) return;
                _lastFrame = frame;
            }

            _tick++;
            if (_waiters.Count == 0) return;

            var now = Now;
            _ready.Clear();
            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                var w = _waiters[i];
                if (w.Token.IsCancellationRequested || (_tick >= w.TargetTick && now >= w.TargetTime))
                {
                    _ready.Add(w);
                    _waiters.RemoveAt(i);
                }
            }

            foreach (var w in _ready)
                if (w.Token.IsCancellationRequested) w.Tcs.TrySetCanceled();
                else w.Tcs.TrySetResult(true);
            _ready.Clear();
        }

        /// <summary>Huỷ mọi lệnh chờ đang treo (khi runner dừng hẳn / đổi domain).</summary>
        public static void CancelAll()
        {
            foreach (var w in _waiters) w.Tcs.TrySetCanceled();
            _waiters.Clear();
        }

        /// <summary>Đảm bảo có nguồn bơm ở runtime (device / Play mode không có Editor).</summary>
        public static void EnsureRuntimePump()
        {
            if (_pumpInstalled && AutoTestPump.Instance != null) return;
            if (!Application.isPlaying) return;
            AutoTestPump.Create();
            _pumpInstalled = true;
        }

        public static Task NextFrame(CancellationToken token = default)
        {
            return Frames(1, token);
        }

        public static Task Frames(int count, CancellationToken token = default)
        {
            return Enqueue(Math.Max(1, count), 0, token);
        }

        public static Task Seconds(double seconds, CancellationToken token = default)
        {
            return Enqueue(1, Math.Max(0, seconds), token);
        }

        /// <summary>Chờ tới khi điều kiện đúng; trả false nếu hết thời gian.</summary>
        public static async Task<bool> Until(Func<bool> condition, double timeoutSeconds,
            CancellationToken token = default, double pollSeconds = 0)
        {
            var deadline = Now + timeoutSeconds;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                bool ok;
                try
                {
                    ok = condition();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AutoTest] Điều kiện chờ ném exception: {e.Message}");
                    ok = false;
                }

                if (ok) return true;
                if (Now >= deadline) return false;
                if (pollSeconds > 0) await Seconds(pollSeconds, token);
                else await NextFrame(token);
            }
        }

        static Task Enqueue(int frames, double seconds, CancellationToken token)
        {
            if (token.IsCancellationRequested) return Task.FromCanceled(token);
            EnsureRuntimePump();
            var tcs = new TaskCompletionSource<bool>();
            _waiters.Add(new Waiter
            {
                TargetTick = _tick + frames,
                TargetTime = Now + seconds,
                Tcs = tcs,
                Token = token
            });
            return tcs.Task;
        }
    }

    /// <summary>Nguồn bơm runtime — tự tạo, ẩn, DontDestroyOnLoad.</summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(-32000)]
    public sealed class AutoTestPump : MonoBehaviour
    {
        public static AutoTestPump Instance { get; private set; }

        public static void Create()
        {
            if (Instance != null) return;
            var go = new GameObject("[EZG AutoTest Pump]") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<AutoTestPump>();
        }

        void Update()
        {
            AutoTestClock.Pump();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
