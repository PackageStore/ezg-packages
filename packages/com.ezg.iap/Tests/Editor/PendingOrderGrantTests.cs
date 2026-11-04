using System;
using NUnit.Framework;

namespace Ezg.Feature.IAP.Tests
{
    /// <summary>
    /// Luật confirm cho một lượt giao: chỉ confirm đơn đã chốt (đã cấp / receipt bị từ chối); cấp quà thất bại hoặc chưa cấp
    /// được thì KHÔNG confirm, KHÔNG ghi ledger — đơn giữ pending để store giao lại.
    /// </summary>
    public class PendingOrderGrantTests : PendingOrderProcessorFixture
    {
        [Test]
        public void GrantThrows_DoesNotConfirm_AndKeepsOrderPending()
        {
            Host.GrantBehaviour = ThrowSaveNotLoaded;
            var session = NewSession();
            var order = Delivery();

            Assert.DoesNotThrow(() => session.Process(order));

            Assert.That(Host.Confirmed, Is.Empty, "đơn chưa cấp quà không được confirm / consume");
            Assert.That(Ledger.Granted, Is.Empty, "không được ghi ledger 'đã cấp'");
            Assert.That(Host.Analytics, Is.Empty);
            Assert.That(session.HasBeenGranted(order), Is.False);
        }

        [Test]
        public void GrantReportsFailure_DoesNotConfirm_AndKeepsOrderPending()
        {
            Host.GrantBehaviour = _ => false; // game báo không cấp được (IIapGrantHandler trả false)

            NewSession().Process(Delivery());

            Assert.That(Host.Confirmed, Is.Empty);
            Assert.That(Ledger.Granted, Is.Empty);
            Assert.That(Host.Analytics, Is.Empty);
        }

        [Test]
        public void GrantSucceeds_ConfirmsExactlyOnce()
        {
            var session = NewSession();
            var order = Delivery();

            session.Process(order);

            Assert.That(Host.Confirmed, Is.EqualTo(new[] { order }));
            Assert.That(Host.Granted.Count, Is.EqualTo(1));
            Assert.That(Ledger.Granted[Tx], Is.EqualTo(Pack));
            Assert.That(Host.Analytics.Count, Is.EqualTo(1));
            Assert.That(session.HasBeenGranted(order), Is.True);
        }

        [Test]
        public void LedgerThrowsAfterGrant_StillConfirmsOnce()
        {
            Ledger.ThrowOnMark = true;
            var order = Delivery();

            Assert.Throws<InvalidOperationException>(() => NewSession().Process(order));

            Assert.That(Host.Granted.Count, Is.EqualTo(1));
            Assert.That(Host.Confirmed, Is.EqualTo(new[] { order }));
        }

        [Test]
        public void LedgerThrowsAfterGrant_ConfirmsAtOnce_EvenWhenConfirmCouldBeDeferred()
        {
            Ledger.ThrowOnMark = true;
            Host.DeferConfirm = true; // iOS StoreKit 2: confirm thường chờ Firebase ghi giao dịch
            var order = Delivery();

            Assert.Throws<InvalidOperationException>(() => NewSession().Process(order));

            Assert.That(Host.Confirmed, Is.EqualTo(new[] { order }), "ledger chưa ghi → confirm ngay (TransactionLog chặn cấp đôi)");
            Assert.That(Host.Deferred, Is.Empty);
        }

        [Test]
        public void OrderWithoutProduct_IsNotConfirmed()
        {
            NewSession().Process(new FakeOrder(null, Tx));

            Assert.That(Host.Confirmed, Is.Empty);
            Assert.That(Host.GrantCalls, Is.Empty);
        }

        [Test]
        public void ProductMissingFromCatalog_IsNotConfirmed()
        {
            Host.Catalog.Clear();

            NewSession().Process(Delivery());

            Assert.That(Host.Confirmed, Is.Empty);
            Assert.That(Host.AwaitingCatalog, Is.EqualTo(1));
        }

        [Test]
        public void ValidationThrows_IsNotConfirmed()
        {
            Host.ValidateBehaviour = _ => throw new FormatException("receipt json");

            NewSession().Process(Delivery());

            Assert.That(Host.Confirmed, Is.Empty);
            Assert.That(Host.GrantCalls, Is.Empty);
        }

        [Test]
        public void InvalidReceipt_IsConfirmedWithoutGrant_AndReportedOnce()
        {
            Host.ValidateBehaviour = _ => false;
            var session = NewSession();
            var order = Delivery();

            session.Process(order);
            session.Process(Delivery()); // giao lại trong phiên → chỉ confirm lại, không báo UI lần hai

            Assert.That(Host.GrantCalls, Is.Empty);
            Assert.That(Host.Confirmed.Count, Is.EqualTo(2));
            Assert.That(Host.RejectedReports, Is.EqualTo(1));
            Assert.That(session.HasBeenGranted(order), Is.False);
        }
    }
}
