using System;
using NUnit.Framework;

namespace Ezg.Feature.IAP.Tests
{
    /// <summary>Dựng chung cho test của <see cref="PendingOrderProcessor{TOrder}"/>: host giả, ledger bền vững giả, một product.</summary>
    public abstract class PendingOrderProcessorFixture
    {
        protected const string Pack = "gem_pack_1";
        protected const string Tx = "GPA.3301-2207-1405-91822";

        internal FakePendingOrderHost Host;
        internal FakeLedger Ledger;

        [SetUp]
        public void SetUpHost()
        {
            Ledger = new FakeLedger();
            Host = new FakePendingOrderHost { Ledger = Ledger };
            Host.Catalog.Add(Pack);
        }

        /// <summary>Một phiên game (mở app): bộ nhớ trong phiên mới, ledger giữ nguyên.</summary>
        internal PendingOrderProcessor<FakeOrder> NewSession()
        {
            return new PendingOrderProcessor<FakeOrder>(Host, null, null, null);
        }

        /// <summary>Store giao (lại) giao dịch <see cref="Tx"/> — mỗi lượt fetch là một instance mới.</summary>
        internal static FakeOrder Delivery()
        {
            return new FakeOrder(Pack, Tx);
        }

        /// <summary>Handler cấp quà của game ném trước khi lưu (save chưa load, SKU không map ra gói…).</summary>
        internal static bool ThrowSaveNotLoaded(FakeOrder order)
        {
            throw new InvalidOperationException("save not loaded");
        }
    }
}
