using System;
using System.Collections.Generic;

namespace Ezg.Feature.IAP.Tests
{
    /// <summary>Một lượt store giao đơn: mỗi lần fetch Unity tạo instance mới cho cùng giao dịch.</summary>
    internal sealed class FakeOrder
    {
        public readonly string ProductId;
        public readonly string TransactionId;

        public FakeOrder(string productId, string transactionId)
        {
            ProductId = productId;
            TransactionId = transactionId;
        }
    }

    /// <summary>Ledger bền vững giả — sống qua các "phiên" (processor mới) trong một test.</summary>
    internal sealed class FakeLedger : IIapOrderLedger
    {
        public readonly Dictionary<string, string> Granted = new Dictionary<string, string>();
        public bool ThrowOnMark;

        public bool IsGranted(string transactionId)
        {
            return Granted.ContainsKey(transactionId);
        }

        public void MarkGranted(string transactionId, string productId)
        {
            if (ThrowOnMark)
            {
                throw new InvalidOperationException("ledger save failed");
            }

            Granted[transactionId] = productId;
        }
    }

    /// <summary>Phía Unity IAP / game giả: ghi lại mọi lần cấp quà / confirm để test đếm.</summary>
    internal sealed class FakePendingOrderHost : IPendingOrderHost<FakeOrder>
    {
        public IIapOrderLedger Ledger { get; set; }

        public readonly HashSet<string> Catalog = new HashSet<string>();
        public readonly List<FakeOrder> Confirmed = new List<FakeOrder>();
        public readonly List<FakeOrder> GrantCalls = new List<FakeOrder>();
        public readonly List<FakeOrder> Granted = new List<FakeOrder>();
        public readonly List<FakeOrder> Analytics = new List<FakeOrder>();
        public readonly HashSet<string> Deferred = new HashSet<string>();

        /// <summary>Kết quả cấp quà của game (mặc định cấp được). Ném = handler OnPurchaseComplete ném.</summary>
        public Func<FakeOrder, bool> GrantBehaviour = _ => true;

        public Func<FakeOrder, bool> ValidateBehaviour = _ => true;
        public bool DeferConfirm;
        public int DroppedBuyCallbacks;
        public int RejectedReports;
        public int AwaitingCatalog;

        public string GetProductId(FakeOrder order)
        {
            return order.ProductId;
        }

        public string GetTransactionId(FakeOrder order)
        {
            return order.TransactionId;
        }

        public bool IsInCatalog(string productId)
        {
            return Catalog.Contains(productId);
        }

        public bool Validate(FakeOrder order)
        {
            return ValidateBehaviour(order);
        }

        public bool Grant(FakeOrder order)
        {
            GrantCalls.Add(order);
            var granted = GrantBehaviour(order);
            if (granted)
            {
                Granted.Add(order);
            }

            return granted;
        }

        public void SendAnalytics(FakeOrder order)
        {
            Analytics.Add(order);
        }

        public void Confirm(FakeOrder order)
        {
            Confirmed.Add(order);
        }

        public bool TryDeferConfirm(FakeOrder order, string transactionId)
        {
            if (!DeferConfirm)
            {
                return false;
            }

            Deferred.Add(transactionId);
            return true;
        }

        public bool IsConfirmDeferred(string transactionId)
        {
            return Deferred.Contains(transactionId);
        }

        public void OnRedeliveredWhileDeferred(string productId)
        {
        }

        public void DropBuyCallback()
        {
            DroppedBuyCallbacks++;
        }

        public void ReportRejected()
        {
            RejectedReports++;
        }

        public void OnAwaitingCatalog()
        {
            AwaitingCatalog++;
        }
    }
}
