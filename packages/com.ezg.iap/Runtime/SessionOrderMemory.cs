using System.Collections.Generic;

namespace Ezg.Feature.IAP
{
    /// <summary>Cách một đơn được chốt trong phiên. Chỉ đơn đã chốt dứt khoát mới có outcome.</summary>
    internal enum OrderOutcome
    {
        Granted,
        AlreadyGranted,
        Rejected
    }

    /// <summary>
    /// Bộ nhớ chống xử lý trùng trong phiên của <see cref="PendingOrderProcessor{TOrder}"/> (0.3.3). Cùng một PendingOrder tới
    /// OnPurchasePending 2 lần (Unity tự route khi fetch purchases, rồi OnPurchasesFetched forward lại đúng instance đó).
    /// Chỉ đơn đã CHỐT DỨT KHOÁT (đã cấp / ledger báo đã cấp / receipt bị từ chối) mới được nhớ theo transactionID; đơn chưa
    /// cấp được chỉ được nhớ theo instance, nên lượt giao sau (instance mới) được xử lý lại.
    /// </summary>
    internal sealed class SessionOrderMemory<TOrder> where TOrder : class
    {
        private readonly Dictionary<string, OrderOutcome> _byTransaction = new Dictionary<string, OrderOutcome>();
        private readonly Dictionary<TOrder, OrderOutcome> _byOrder = new Dictionary<TOrder, OrderOutcome>();

        // Instance vừa cấp quà thất bại → không cấp lại trong cùng lượt fetch.
        private readonly HashSet<TOrder> _grantFailed = new HashSet<TOrder>();

        /// <summary>Cùng instance này đã được xử lý (đã chốt, hoặc vừa cấp quà thất bại) — bỏ qua.</summary>
        internal bool IsHandledInstance(TOrder order)
        {
            return _byOrder.ContainsKey(order) || _grantFailed.Contains(order);
        }

        /// <summary>
        /// Giao dịch đã chốt trong phiên (store giao lại bằng instance mới): ghi instance mới theo kết quả cũ, trả true.
        /// </summary>
        internal bool TryAdoptSettled(TOrder order, string transactionId)
        {
            if (string.IsNullOrEmpty(transactionId) || !_byTransaction.TryGetValue(transactionId, out var earlier))
            {
                return false;
            }

            _byOrder[order] = earlier;
            return true;
        }

        internal void Settle(TOrder order, string transactionId, OrderOutcome outcome)
        {
            _byOrder[order] = outcome;
            if (!string.IsNullOrEmpty(transactionId))
            {
                _byTransaction[transactionId] = outcome;
            }
        }

        internal void MarkGrantFailed(TOrder order)
        {
            _grantFailed.Add(order);
        }

        /// <summary>Đơn đã được cấp quà trong phiên này (hoặc ledger báo đã cấp ở phiên trước).</summary>
        internal bool HasBeenGranted(TOrder order, string transactionId)
        {
            if (_byOrder.TryGetValue(order, out var outcome))
            {
                return outcome != OrderOutcome.Rejected;
            }

            return !string.IsNullOrEmpty(transactionId) && _byTransaction.TryGetValue(transactionId, out outcome) &&
                   outcome != OrderOutcome.Rejected;
        }
    }
}
