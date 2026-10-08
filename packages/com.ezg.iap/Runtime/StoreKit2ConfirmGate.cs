using System;
using System.Collections.Generic;

namespace Ezg.Feature.IAP
{
    /// <summary>Phía native của <see cref="StoreKit2ConfirmGate{TOrder}"/> — bản thật là <see cref="FirebaseStoreKit2Bridge"/>.</summary>
    internal interface IStoreKit2FirebaseNative
    {
        /// <returns>requestId &gt; 0, hoặc 0 nếu không gửi được.</returns>
        int Begin(string transactionId, int maxWaitMs);

        bool TryTakeResult(int requestId, out int resultCode);

        void Abandon(int requestId);
    }

    /// <summary>
    /// Giữ ConfirmPurchase của đơn iOS StoreKit 2 VỪA cấp quà tới khi Firebase ghi xong giao dịch (hoặc hết hạn), rồi confirm
    /// ĐÚNG 1 lần. Confirm = finish, consumable đã finish biến khỏi lịch sử StoreKit 2 nên Firebase phải ghi trước.
    /// <para>Không phụ thuộc UnityEngine / Unity IAP — thời gian, native, confirm và log đều được inject — để test được
    /// ngoài Unity. Không bao giờ ném exception ra caller.</para>
    /// </summary>
    internal sealed class StoreKit2ConfirmGate<TOrder> where TOrder : class
    {
        #region Fields

        private sealed class Entry
        {
            public TOrder Order;
            public string TransactionId;
            public float Deadline;
            public int RequestId; // 0 = chưa gửi / đang chờ thử lại
            public float RetryAt;
            public float RetryDelay; // nhân đôi mỗi lần Firebase chưa configure — mỗi lần kiểm, Firebase log một dòng lỗi
        }

        private readonly IStoreKit2FirebaseNative _native;
        private readonly Action<TOrder> _confirm;
        private readonly Action<string> _log;
        private readonly Action<string> _logWarning;
        private readonly Action<string> _logError;
        private readonly float _timeoutSeconds;
        private readonly int _searchMaxMs;
        private readonly float _callbackMarginSeconds;
        private readonly float _notReadyRetrySeconds;

        /// <summary>Lượt thử cuối khi Firebase chưa configure phải còn ít nhất khoảng tra này mới đáng gửi.</summary>
        private const float MIN_RETRY_SEARCH_SECONDS = 0.5f;

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        private readonly List<Entry> _finished = new List<Entry>(); // tái dùng giữa các Tick, không alloc mỗi frame

        #endregion

        #region Initialize

        /// <param name="timeoutSeconds">Thời gian tối đa giữ confirm của một đơn.</param>
        /// <param name="searchMaxMs">Khung tối đa plugin tra lịch sử StoreKit 2 cho một lượt gửi.</param>
        /// <param name="callbackMarginSeconds">Dự trữ cho callback native về sau khi plugin hết khung tra.</param>
        /// <param name="notReadyRetrySeconds">Firebase chưa configure → gửi lại sau khoảng này (nhân đôi sau mỗi lần).</param>
        internal StoreKit2ConfirmGate(IStoreKit2FirebaseNative native, Action<TOrder> confirm, Action<string> log,
            Action<string> logWarning, Action<string> logError, float timeoutSeconds, int searchMaxMs,
            float callbackMarginSeconds, float notReadyRetrySeconds)
        {
            _native = native ?? throw new ArgumentNullException(nameof(native));
            _confirm = confirm ?? throw new ArgumentNullException(nameof(confirm));
            _log = log ?? (_ => { });
            _logWarning = logWarning ?? (_ => { });
            _logError = logError ?? (_ => { });
            _timeoutSeconds = timeoutSeconds;
            _searchMaxMs = searchMaxMs;
            _callbackMarginSeconds = callbackMarginSeconds;
            _notReadyRetrySeconds = notReadyRetrySeconds;
        }

        #endregion

        #region Public

        /// <summary>Số đơn đang giữ confirm.</summary>
        internal int Count => _entries.Count;

        /// <summary>Giao dịch này đang được giữ confirm (gate sẽ tự confirm).</summary>
        internal bool IsAwaiting(string transactionId)
        {
            return !string.IsNullOrEmpty(transactionId) && _entries.ContainsKey(transactionId);
        }

        /// <summary>Có đơn đang giữ thoả <paramref name="match"/> không (vd cùng product).</summary>
        internal bool IsAwaitingAny(Func<TOrder, bool> match)
        {
            if (match == null || _entries.Count == 0)
            {
                return false;
            }

            foreach (var entry in _entries.Values)
            {
                if (match(entry.Order))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Nhận giữ confirm của <paramref name="order"/> và gửi giao dịch cho Firebase.
        /// </summary>
        /// <returns>true = gate chịu trách nhiệm confirm; false = caller phải confirm ngay.</returns>
        internal bool TryDefer(TOrder order, string transactionId, float now)
        {
            if (order == null || string.IsNullOrEmpty(transactionId))
            {
                return false;
            }

            if (_entries.ContainsKey(transactionId))
            {
                // Cùng giao dịch đang giữ (đơn khác instance) — confirm của nó cũng đóng giao dịch này.
                return true;
            }

            var entry = new Entry
            {
                Order = order,
                TransactionId = transactionId,
                Deadline = now + _timeoutSeconds,
                RetryDelay = _notReadyRetrySeconds
            };

            if (!StartRequest(entry, now))
            {
                return false;
            }

            _entries[transactionId] = entry;
            return true;
        }

        /// <summary>Gọi mỗi frame: đọc kết quả native / thử lại / hết hạn → confirm đơn đã xong.</summary>
        internal void Tick(float now)
        {
            if (_entries.Count == 0)
            {
                return;
            }

            _finished.Clear();
            foreach (var entry in _entries.Values)
            {
                bool done;
                try
                {
                    done = Step(entry, now);
                }
                catch (Exception e)
                {
                    _logError("[IAP][FirebaseSK2] Lỗi khi chờ Firebase, confirm ngay: " + e);
                    done = true;
                }

                if (done)
                {
                    _finished.Add(entry);
                }
            }

            for (var i = 0; i < _finished.Count; i++)
            {
                Finish(_finished[i]);
            }

            _finished.Clear();
        }

        /// <summary>Confirm ngay mọi đơn đang giữ (object bị tắt / huỷ / thoát app).</summary>
        internal void ConfirmAll()
        {
            if (_entries.Count == 0)
            {
                return;
            }

            foreach (var entry in new List<Entry>(_entries.Values))
            {
                if (entry.RequestId != 0)
                {
                    SafeAbandon(entry.RequestId);
                    entry.RequestId = 0;
                }

                Finish(entry);
            }
        }

        #endregion

        #region Private

        /// <returns>true = đơn đã xong, cần confirm.</returns>
        private bool Step(Entry entry, float now)
        {
            if (entry.RequestId == 0)
            {
                // Đang chờ thử lại vì Firebase chưa configure.
                if (now < entry.RetryAt)
                {
                    return false;
                }

                return !StartRequest(entry, now);
            }

            if (_native.TryTakeResult(entry.RequestId, out var result))
            {
                entry.RequestId = 0;
                if (result == FirebaseStoreKit2Bridge.RESULT_FIREBASE_NOT_CONFIGURED)
                {
                    // Thử lại sau RetryDelay (nhân đôi mỗi lần), lượt cuối dời sớm lên để còn ít nhất
                    // MIN_RETRY_SEARCH_SECONDS khung tra trước hạn.
                    var wait = Math.Min(entry.RetryDelay,
                        entry.Deadline - _callbackMarginSeconds - MIN_RETRY_SEARCH_SECONDS - now);
                    if (wait > 0f)
                    {
                        entry.RetryAt = now + wait;
                        entry.RetryDelay *= 2f;
                        return false;
                    }
                }

                LogResult(entry.TransactionId, result);
                return true;
            }

            if (now >= entry.Deadline)
            {
                SafeAbandon(entry.RequestId);
                entry.RequestId = 0;
                _logWarning("[IAP][FirebaseSK2] Plugin không trả lời sau " + _timeoutSeconds +
                            "s, confirm không chờ nữa: tx=" + entry.TransactionId);
                return true;
            }

            return false;
        }

        /// <returns>true = đã gửi, đang chờ kết quả.</returns>
        private bool StartRequest(Entry entry, float now)
        {
            // Khung tra của plugin luôn ngắn hơn hạn chờ để callback kịp về.
            var remaining = entry.Deadline - _callbackMarginSeconds - now;
            if (remaining < 0f)
            {
                _logWarning("[IAP][FirebaseSK2] Hết thời gian chờ Firebase, confirm không ghi: tx=" + entry.TransactionId);
                return false;
            }

            int requestId;
            try
            {
                requestId = _native.Begin(entry.TransactionId, (int)Math.Min(_searchMaxMs, remaining * 1000f));
            }
            catch (Exception e)
            {
                _logError("[IAP][FirebaseSK2] Không gửi được giao dịch cho plugin: " + e);
                requestId = 0;
            }

            entry.RequestId = requestId;
            return requestId != 0;
        }

        /// <summary>Lấy đơn ra khỏi danh sách rồi confirm — đơn đã lấy ra thì không confirm lần nữa.</summary>
        private void Finish(Entry entry)
        {
            if (!_entries.Remove(entry.TransactionId))
            {
                return;
            }

            try
            {
                _confirm(entry.Order);
            }
            catch (Exception e)
            {
                _logError("[IAP][FirebaseSK2] ConfirmPurchase lỗi: " + e);
            }
        }

        private void SafeAbandon(int requestId)
        {
            try
            {
                _native.Abandon(requestId);
            }
            catch (Exception e)
            {
                _logError("[IAP][FirebaseSK2] Abandon lỗi: " + e);
            }
        }

        private void LogResult(string transactionId, int result)
        {
            switch (result)
            {
                case FirebaseStoreKit2Bridge.RESULT_LOGGED:
                    _log("[IAP][FirebaseSK2] Đã ghi in_app_purchase: tx=" + transactionId);
                    break;
                case FirebaseStoreKit2Bridge.RESULT_FIREBASE_UNAVAILABLE:
                    _log("[IAP][FirebaseSK2] App không có FirebaseAnalytics (hoặc build với EZG_IAP_DISABLE_FIREBASE_SK2) → bỏ qua.");
                    break;
                case FirebaseStoreKit2Bridge.RESULT_OS_UNSUPPORTED:
                    _log("[IAP][FirebaseSK2] iOS < 15 (StoreKit 1) → Firebase tự thu, bỏ qua: tx=" + transactionId);
                    break;
                case FirebaseStoreKit2Bridge.RESULT_NOT_FOUND:
                    _logWarning("[IAP][FirebaseSK2] Không thấy giao dịch trong lịch sử StoreKit 2, Firebase không ghi: tx=" +
                                transactionId);
                    break;
                case FirebaseStoreKit2Bridge.RESULT_FIREBASE_NOT_CONFIGURED:
                    _logWarning("[IAP][FirebaseSK2] Firebase chưa configure tới hạn chờ, không ghi: tx=" + transactionId);
                    break;
                case FirebaseStoreKit2Bridge.RESULT_INVALID_TRANSACTION_ID:
                    _logWarning("[IAP][FirebaseSK2] TransactionID không phải số, không ghi: tx=" + transactionId);
                    break;
                default:
                    _logWarning("[IAP][FirebaseSK2] Kết quả lạ " + result + ": tx=" + transactionId);
                    break;
            }
        }

        #endregion
    }
}
