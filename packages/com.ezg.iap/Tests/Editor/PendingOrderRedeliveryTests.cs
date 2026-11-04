using NUnit.Framework;

namespace Ezg.Feature.IAP.Tests
{
    /// <summary>
    /// Store giao lại cùng một giao dịch (cùng lượt fetch, cùng phiên, phiên sau): không bao giờ cấp quà lần hai; đơn cấp thất
    /// bại thì lượt giao sau cấp đúng 1 lần rồi confirm đúng 1 lần.
    /// </summary>
    public class PendingOrderRedeliveryTests : PendingOrderProcessorFixture
    {
        [Test]
        public void SameInstanceForwardedTwice_GrantsAndConfirmsOnce()
        {
            // Unity route đơn lúc fetch purchases, rồi OnPurchasesFetched forward lại ĐÚNG instance đó.
            var session = NewSession();
            var order = Delivery();

            session.Process(order);
            session.Process(order);

            Assert.That(Host.GrantCalls.Count, Is.EqualTo(1));
            Assert.That(Host.Confirmed.Count, Is.EqualTo(1));
        }

        [Test]
        public void RedeliveredInSameSession_IsNotGrantedTwice_OnlyReconfirmed()
        {
            var session = NewSession();
            session.Process(Delivery());
            var again = Delivery();

            session.Process(again);

            Assert.That(Host.GrantCalls.Count, Is.EqualTo(1));
            Assert.That(Host.Confirmed[Host.Confirmed.Count - 1], Is.SameAs(again));
        }

        [Test]
        public void RedeliveredNextLaunch_LedgerBlocksSecondGrant()
        {
            NewSession().Process(Delivery()); // cấp + ghi ledger; app chết trước khi confirm tới store
            var next = Delivery();

            NewSession().Process(next);

            Assert.That(Host.GrantCalls.Count, Is.EqualTo(1));
            Assert.That(Host.Analytics.Count, Is.EqualTo(1));
            Assert.That(Host.Confirmed[Host.Confirmed.Count - 1], Is.SameAs(next));
        }

        [Test]
        public void GrantFailedThenRedelivered_IsGrantedOnceAndConfirmedOnce()
        {
            Host.GrantBehaviour = ThrowSaveNotLoaded;
            NewSession().Process(Delivery()); // lần mở 1: save chưa load
            Assert.That(Host.Confirmed, Is.Empty);

            Host.GrantBehaviour = _ => true;
            var redelivered = Delivery();
            var launch2 = NewSession();
            launch2.Process(redelivered); // lần mở 2: store giao lại
            launch2.Process(redelivered); // forward lại cùng instance

            Assert.That(Host.Granted, Is.EqualTo(new[] { redelivered }));
            Assert.That(Host.Confirmed, Is.EqualTo(new[] { redelivered }));

            NewSession().Process(Delivery()); // lần mở 3: confirm trước chưa tới store → giao lại
            Assert.That(Host.Granted.Count, Is.EqualTo(1), "ledger phải chặn cấp lần hai");
        }

        [Test]
        public void GrantFailedInSession_RetriedOnNextDeliveryOfSameSession()
        {
            Host.GrantBehaviour = ThrowSaveNotLoaded;
            var session = NewSession();
            var first = Delivery();
            session.Process(first);
            session.Process(first); // forward lại cùng instance → không thử cấp lần nữa

            Assert.That(Host.GrantCalls.Count, Is.EqualTo(1));
            Assert.That(Host.Confirmed, Is.Empty);

            Host.GrantBehaviour = _ => true;
            var resumed = Delivery(); // quay lại app → fetch purchases → instance mới
            session.Process(resumed);

            Assert.That(Host.Granted, Is.EqualTo(new[] { resumed }));
            Assert.That(Host.Confirmed, Is.EqualTo(new[] { resumed }));
        }

        [Test]
        public void DeferredConfirm_IsLeftToGate_AndRedeliveryIsNotConfirmedTwice()
        {
            Host.DeferConfirm = true; // iOS StoreKit 2: gate confirm sau khi Firebase ghi
            var session = NewSession();

            session.Process(Delivery());
            session.Process(Delivery());

            Assert.That(Host.Deferred.Contains(Tx), Is.True);
            Assert.That(Host.Confirmed, Is.Empty);
            Assert.That(Host.GrantCalls.Count, Is.EqualTo(1));
        }

        [Test]
        public void AlreadyGrantedOrderWithoutProduct_IsStillConfirmed()
        {
            Ledger.Granted[Tx] = Pack; // cấp ở phiên trước, app chết trước confirm
            var order = new FakeOrder(null, Tx);

            NewSession().Process(order);

            Assert.That(Host.Confirmed, Is.EqualTo(new[] { order }), "ledger báo đã cấp → đóng đơn dù cart không có product");
            Assert.That(Host.GrantCalls, Is.Empty);
        }
    }
}
