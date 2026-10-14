using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ezg.BugLogger
{
    /// <summary>
    /// Bộ gửi single-flight cho outbox (BugHub contract §8). Tối đa một vòng lặp, khởi chạy bởi
    /// <see cref="Wake"/> (Initialize, submit, app quay lại foreground, mạng có lại): còn report thì gửi cái
    /// cũ nhất chưa phải chờ — đã nhận → xoá + <c>ReportSent</c>; bị từ chối (400/404/413…) → <c>failed/</c> + log
    /// lỗi + <c>ReportFailed</c>; lỗi tạm → giữ lại, thử lại sau 5 s × 2^n (≤ 300 s, không ngắn hơn
    /// <c>Retry-After</c> — phần đó trigger không cắt ngắn được), bỏ cuộc sau 50 lần hoặc 24 h → <c>failed/</c>.
    /// Server có trả lời mà vẫn lỗi tạm (5xx/403) thì chỉ report đó chờ backoff, vòng gửi đi tiếp sang report sau
    /// nó — một report server cứ từ chối tạm không chặn mọi bug mới tới khi bỏ cuộc; không có response (mất mạng,
    /// timeout) thì cả vòng chờ chung. Chưa cấu hình → dừng, giữ nguyên; mất mạng → 2 s xem lại một lần. Vòng lặp
    /// kết thúc khi outbox rỗng, nên outbox rỗng không tốn gì: không poll, không <c>Update</c>, không cấp phát.
    /// Mọi lần chờ dùng realtime vì form đặt <c>timeScale = 0</c>.
    /// </summary>
    internal static class BugReportUploader
    {
        #region Fields

        private const float OFFLINE_POLL_SECONDS = 2f;

        private enum Outcome
        {
            Retry,
            Delivered,
            Rejected,
            GaveUp,
        }

        private static bool _isRunning;
        private static bool _isWakeRequested;
        private static bool _isQuitting;
        private static int _generation;

        /// <summary>
        /// Số lỗi local (disk) liên tiếp, giữ trong RAM: backoff vẫn tăng khi chính <c>attempt.json</c> không
        /// ghi được. Về 0 khi sổ sách local thành công (report xong hoặc ghi được lần thử).
        /// </summary>
        private static int _localFailureStreak;

        /// <summary>
        /// Report vừa lỗi tạm có response → mốc realtime (<c>Time.realtimeSinceStartupAsDouble</c>) trước đó không
        /// gửi lại; vòng gửi bỏ qua nó để đi tiếp. Chỉ đọc/ghi trên main thread. <see cref="Wake"/> xoá hết: trigger
        /// = thử lại ngay như trước.
        /// </summary>
        private static readonly Dictionary<string, double> _retryNotBefore = new Dictionary<string, double>();

        private static CancellationTokenSource _waitCts;

        /// <summary>Lần chờ <c>Retry-After</c>: chỉ Stop/app thoát huỷ được, <see cref="Wake"/> thì không.</summary>
        private static CancellationTokenSource _holdCts;

        private static CancellationTokenSource _lifetimeCts;
        private static IBugReportTransport _defaultTransport;

        /// <summary>Hệ số nhân thời gian chờ thật — test thu nhỏ backoff 5/10/20 s; mặc định 1.</summary>
        internal static float WaitTimeScale = 1f;

        /// <summary>Báo (id, số giây) mỗi khi một report bị hoãn sau lỗi tạm từ transport. Main thread.</summary>
        internal static event Action<string, int> RetryScheduled;

        /// <summary>True khi vòng gửi đang chạy.</summary>
        public static bool IsRunning => _isRunning;

        #endregion

        #region Public

        /// <summary>
        /// Khởi chạy vòng lặp, hoặc cắt ngắn lần chờ backoff/offline của vòng đang chạy. Main thread; không làm
        /// gì ngoài play mode. Vòng lặp nhường một frame trước, nên mọi cấu hình gán cùng frame đều được thấy.
        /// </summary>
        public static void Wake()
        {
            if (_isQuitting || !Application.isPlaying)
                return;

            _isWakeRequested = true;
            _retryNotBefore.Clear();
            _waitCts?.Cancel();

            if (_isRunning)
                return;

            _isRunning = true;
            _lifetimeCts ??= new CancellationTokenSource();
            BugLoggerRuntimeHost.EnsureCreated();
            RunAsync(_generation).Forget();
        }

        /// <summary>
        /// Bỏ vòng đang chạy (nó thoát ở bước kế) và huỷ lần chờ của nó. Report đang chờ vẫn trên disk;
        /// <see cref="Wake"/> kế tiếp chạy lại từ đầu.
        /// </summary>
        public static void Stop()
        {
            _generation++;
            _isRunning = false;
            _isWakeRequested = false;
            _localFailureStreak = 0;
            _retryNotBefore.Clear();
            _waitCts?.Cancel();
            _waitCts = null;
            _holdCts?.Cancel();
            _holdCts = null;
        }

        /// <summary><see cref="Stop"/> kèm trả các seam của test về mặc định.</summary>
        internal static void Reset()
        {
            Stop();
            WaitTimeScale = 1f;
            RetryScheduled = null;
        }

        #endregion

        #region Private

        private static async UniTaskVoid RunAsync(int generation)
        {
            try
            {
                await UniTask.Yield();

                while (generation == _generation)
                {
                    _isWakeRequested = false;
                    var outbox = BugReportOutbox.Default;
                    var coolingIds = TakeCoolingIds();

                    await UniTask.SwitchToThreadPool();
                    var next = outbox.PeekOldest(out var pendingCount, coolingIds);
                    await UniTask.SwitchToMainThread();

                    if (generation != _generation)
                        return;

                    BugReportOutbox.PublishPendingCount(pendingCount);
                    if (next == null)
                    {
                        if (pendingCount == 0)
                            break;

                        // Mọi report còn lại đều đang chờ backoff riêng: ngủ tới hạn sớm nhất (trigger cắt ngắn được).
                        await WaitRealtimeAsync(GetSecondsUntilEarliestRetry(), generation);
                        continue;
                    }

                    var target = BugLoggerConfig.CurrentTarget;
                    if (!target.IsConfigured)
                        break;

                    if (Application.internetReachability == NetworkReachability.NotReachable)
                    {
                        // Offline: chỉ poll cờ reachability; quét lại khi mạng hoặc trigger quay lại.
                        while (generation == _generation && !_isWakeRequested &&
                               Application.internetReachability == NetworkReachability.NotReachable)
                            await WaitAsync(OFFLINE_POLL_SECONDS, generation);

                        continue;
                    }

                    var id = next.Value.Id;
                    await UniTask.SwitchToThreadPool();
                    var payload = outbox.Load(id);
                    var isUnreadable = payload == null && outbox.Exists(id);
                    await UniTask.SwitchToMainThread();

                    if (generation != _generation)
                        return;

                    if (payload == null)
                    {
                        // Đã mất (trần outbox bỏ đi) → quét lại; vẫn còn mà không đọc được → backoff.
                        if (isUnreadable)
                            await BackOffAsync(outbox, id, "local read failed", generation);

                        continue;
                    }

                    var result = await SendAsync(payload, target);
                    await UniTask.SwitchToMainThread();

                    if (generation != _generation)
                        return;

                    await HandleResultAsync(outbox, id, result, generation);
                }
            }
            catch (Exception ex)
            {
                await UniTask.SwitchToMainThread();
                Debug.LogWarning($"[BugLogger] Upload loop stopped ({ex.GetType().Name}: {ex.Message}) - pending reports are kept and retried on the next trigger.");
            }
            finally
            {
                if (generation == _generation)
                {
                    _isRunning = false;
                    // Wake tới sau lần quét cuối sẽ bị mất nếu không chạy lại ở đây.
                    if (_isWakeRequested)
                        Wake();
                }
            }
        }

        private static async UniTask<BugReportSendResult> SendAsync(BugReportPayload payload, BugReportTarget target)
        {
            var transport = BugLoggerConfig.Transport ?? (_defaultTransport ??= new BugHubTransport());
            try
            {
                return await transport.SendAsync(payload, target, _lifetimeCts.Token);
            }
            catch (Exception ex)
            {
                return BugReportSendResult.NetworkError($"transport threw {ex.GetType().Name}");
            }
        }

        private static async UniTask HandleResultAsync(BugReportOutbox outbox, string id, BugReportSendResult result, int generation)
        {
            await UniTask.SwitchToThreadPool();
            var outcome = Outcome.Retry;
            var reason = result.Reason;
            var isSettled = true;
            var attempts = 0;
            var pending = BugReportOutbox.UNKNOWN_COUNT;
            Exception ioError = null;
            try
            {
                switch (result.Status)
                {
                    case BugReportSendStatus.Success:
                    case BugReportSendStatus.Duplicate:
                        outcome = Outcome.Delivered;
                        isSettled = outbox.Complete(id);
                        break;
                    case BugReportSendStatus.Permanent:
                        outcome = Outcome.Rejected;
                        isSettled = outbox.MoveToFailed(id, reason, result.StatusCode);
                        break;
                    default:
                        attempts = outbox.IncrementAttempt(id, reason, out var firstAttemptUtc);
                        if (attempts > 0 && BugBackoffPolicy.ShouldGiveUp(attempts, firstAttemptUtc, BugLoggerClock.UtcNow))
                        {
                            outcome = Outcome.GaveUp;
                            reason = $"gave up after {attempts} attempts: {result.Reason}";
                            isSettled = outbox.MoveToFailed(id, reason, result.StatusCode, true);
                        }

                        break;
                }

                pending = outbox.CountReady();
            }
            catch (Exception ex)
            {
                ioError = ex;
                isSettled = false;
            }

            await UniTask.SwitchToMainThread();

            // Bị dừng/reset giữa lúc làm IO: sự kiện giờ thuộc về cấu hình mới.
            if (generation != _generation)
                return;

            // Công bố số đếm TRƯỚC sự kiện để host đọc PendingCount trong handler thấy giá trị mới.
            if (pending != BugReportOutbox.UNKNOWN_COUNT)
                BugReportOutbox.PublishPendingCount(pending);

            if (isSettled)
                _localFailureStreak = 0;

            if (outcome != Outcome.Retry)
                _retryNotBefore.Remove(id);

            switch (outcome)
            {
                // Event chỉ raise khi sổ sách local đã xong: folder còn kẹt thì vòng sau gửi lại (Worker trả
                // trùng / từ chối lần nữa) và raise lúc đó — host không thấy một report bị đếm hai lần.
                case Outcome.Delivered:
                    Debug.Log($"[BugLogger] Report {id} delivered as #{result.Number} ({result.Reason}).");
                    if (isSettled)
                        BugLogger.RaiseReportSent(result.Number);
                    break;
                case Outcome.Rejected:
                    Debug.LogError($"[BugLogger] Report {id} rejected ({reason}) - moved to {BugReportOutbox.FAILED_FOLDER}/, not retried.");
                    if (isSettled)
                        BugLogger.RaiseReportFailed(id, reason);
                    break;
                case Outcome.GaveUp:
                    Debug.LogError($"[BugLogger] Report {id} {reason} - moved to {BugReportOutbox.FAILED_FOLDER}/.");
                    if (isSettled)
                        BugLogger.RaiseReportFailed(id, reason);
                    break;
                default:
                    if (ioError != null)
                    {
                        Debug.LogWarning($"[BugLogger] Could not record the attempt for {id} ({ioError.GetType().Name}).");
                        attempts = Math.Max(attempts, ++_localFailureStreak);
                    }

                    var delay = BugBackoffPolicy.GetRetryDelaySeconds(Math.Max(1, attempts), result.RetryAfterSeconds);
                    Debug.Log($"[BugLogger] Report {id} not sent ({reason}) - retry {attempts} in {delay} s.");
                    RaiseRetryScheduled(id, delay);

                    // Phần Retry-After là lệnh của server: trigger (submit mới, app quay lại) không được cắt ngắn,
                    // nếu không mỗi lần submit trong lúc bị rate limit lại gửi ngay và ăn thêm một lần thử.
                    var hold = Math.Min(result.RetryAfterSeconds, delay);
                    if (hold > 0)
                        await WaitAsync(hold, generation, false);

                    // Không có response (mất mạng / timeout): report nào gửi lúc này cũng hỏng y vậy → cả vòng chờ chung.
                    if (result.StatusCode <= 0)
                    {
                        await WaitAsync(delay - hold, generation);
                        return;
                    }

                    // Server có trả lời: chỉ report này chờ phần backoff còn lại, vòng gửi đi tiếp sang report sau nó.
                    // Trigger tới trong lúc hold thì bỏ chờ — giống WaitAsync cắt ngắn khi có trigger.
                    if (generation == _generation && !_isWakeRequested && delay > hold)
                        _retryNotBefore[id] = Time.realtimeSinceStartupAsDouble + (delay - hold) * (double)WaitTimeScale;

                    return;
            }

            if (isSettled)
                return;

            // Report vẫn còn trong outbox (folder bị khoá / chuyển thất bại): thiếu bước này thì vòng sau
            // gửi lại ngay, liên tục.
            Debug.LogWarning($"[BugLogger] Report {id} is still in the outbox after {result.Status} ({ioError?.GetType().Name ?? "folder locked"}) - backing off.");
            await BackOffAsync(outbox, id, "local outbox update failed", generation);
        }

        /// <summary>Ghi một lần thử thất bại vì lỗi local (disk) rồi chờ theo backoff.</summary>
        private static async UniTask BackOffAsync(BugReportOutbox outbox, string id, string reason, int generation)
        {
            await UniTask.SwitchToThreadPool();
            var attempts = 0;
            try
            {
                attempts = outbox.IncrementAttempt(id, reason, out _);
            }
            catch (Exception)
            {
                // attempt.json không ghi được: _localFailureStreak trong RAM giữ backoff tiếp tục tăng.
            }

            await UniTask.SwitchToMainThread();
            if (generation != _generation)
                return;

            attempts = Math.Max(attempts, ++_localFailureStreak);
            var delay = BugBackoffPolicy.GetDelaySeconds(attempts);
            Debug.Log($"[BugLogger] Report {id}: {reason} - retry in {delay} s.");
            await WaitAsync(delay, generation);
        }

        /// <summary>
        /// Chờ realtime. <paramref name="isWakeable"/> = true: <see cref="Wake"/> cắt ngắn được; false (Retry-After):
        /// chỉ Stop/app thoát cắt được. Chỉ vòng đang sống được giữ lần chờ.
        /// </summary>
        private static UniTask WaitAsync(float seconds, int generation, bool isWakeable = true)
        {
            return WaitRealtimeAsync(seconds * (double)WaitTimeScale, generation, isWakeable);
        }

        /// <summary>Như <see cref="WaitAsync"/> nhưng <paramref name="realSeconds"/> là giây thật, không nhân <see cref="WaitTimeScale"/>.</summary>
        private static async UniTask WaitRealtimeAsync(double realSeconds, int generation, bool isWakeable = true)
        {
            if (realSeconds <= 0d || generation != _generation)
                return;

            // Có trigger tới trong lúc đang gửi: thử lại ngay thay vì ngủ.
            if (isWakeable && _isWakeRequested)
                return;

            var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            if (isWakeable)
                _waitCts = cts;
            else
                _holdCts = cts;

            await UniTask.Delay(TimeSpan.FromSeconds(realSeconds), DelayType.Realtime, PlayerLoopTiming.Update, cts.Token)
                .SuppressCancellationThrow();

            if (_waitCts == cts)
                _waitCts = null;
            if (_holdCts == cts)
                _holdCts = null;

            cts.Dispose();
        }

        /// <summary>Id còn trong hạn chờ (bỏ luôn mục đã hết hạn), hoặc null khi không có. Main thread.</summary>
        private static HashSet<string> TakeCoolingIds()
        {
            if (_retryNotBefore.Count == 0)
                return null;

            var now = Time.realtimeSinceStartupAsDouble;
            HashSet<string> cooling = null;
            List<string> expired = null;
            foreach (var pair in _retryNotBefore)
            {
                if (pair.Value > now)
                    (cooling ??= new HashSet<string>()).Add(pair.Key);
                else
                    (expired ??= new List<string>()).Add(pair.Key);
            }

            if (expired != null)
            {
                foreach (var id in expired)
                    _retryNotBefore.Remove(id);
            }

            return cooling;
        }

        /// <summary>Số giây thật tới khi report chờ sớm nhất được gửi lại; 0 khi không còn report nào chờ. Main thread.</summary>
        private static double GetSecondsUntilEarliestRetry()
        {
            if (_retryNotBefore.Count == 0)
                return 0d;

            return Math.Max(0d, _retryNotBefore.Values.Min() - Time.realtimeSinceStartupAsDouble);
        }

        private static void RaiseRetryScheduled(string id, int seconds)
        {
            try
            {
                RetryScheduled?.Invoke(id, seconds);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private static void OnApplicationQuitting()
        {
            _isQuitting = true;
            _generation++;
            _isRunning = false;
            _lifetimeCts?.Cancel();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _generation++;
            _isRunning = false;
            _isWakeRequested = false;
            _isQuitting = false;
            _localFailureStreak = 0;
            _retryNotBefore.Clear();
            _waitCts = null;
            _holdCts = null;
            _lifetimeCts?.Cancel();
            _lifetimeCts = new CancellationTokenSource();
            _defaultTransport = null;
            WaitTimeScale = 1f;
            RetryScheduled = null;

            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
        }

        #endregion
    }
}
