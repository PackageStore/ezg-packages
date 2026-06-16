using System;
using System.Collections.Generic;

namespace Ezg.Feature.IAP
{
    public interface IPurchasing
    {
        Action<bool> OnTransactionRestored { get; set; }

        Action<string> OnPurchaseFailed { get; set; }

        /// <summary>
        /// Gọi trước khi call action callback purchase
        /// </summary>
        Action<string> OnPurchaseCompleteBeforeCallback { get; set; }

        /// <summary>
        /// Gọi sau khi call action callback purchase
        /// </summary>
        Action<string> OnPurchaseComplete { get; set; }

        List<string> GetConsumableProducts();

        List<string> GetNonConsumableProducts();

        void RestoreItem();
    }
}
