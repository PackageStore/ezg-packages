#if (UNITY_ANDROID || UNITY_IPHONE || UNITY_IOS) && !UNITY_EDITOR
#define RECEIPT_VALIDATION
#endif

using Ezg.Package.Singleton;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Security;
using AppsFlyerSDK;
using UnityEngine.Purchasing.Extension;


namespace Ezg.Feature.IAP
{
    public class Receipt {

        public string Store;
        public string TransactionID;
        public string Payload;

        public Receipt()
        {
            Store = TransactionID = Payload = "";
        }

        public Receipt(string store, string transactionID, string payload)
        {
            Store = store;
            TransactionID = transactionID;
            Payload = payload;
        }
    }

    public class PayloadAndroid
    {
        public string json;
        public string signature;

        public PayloadAndroid()
        {
            json = signature = "";
        }

        public PayloadAndroid(string _json, string _signature)
        {
            json = _json;
            signature = _signature;
        }
    }

    public class InAppManager : Singleton<InAppManager>, IDetailedStoreListener// IStoreListener
    {
        public List<string> nonConsume = new List<string>();

        private static IStoreController m_StoreController; // The Unity Purchasing system.
        private static IExtensionProvider m_StoreExtensionProvider; // The store-specific Purchasing subsystems.
        private IAppleExtensions m_AppleExtensions;
        private IGooglePlayStoreExtensions m_GooglePlayStoreExtensions;
        private ITransactionHistoryExtensions m_TransactionHistoryExtensions;

        private Action callbackPay;

        private string productId;
        private string sourcePurchase;
        private string sourcePurchaseId;

        private bool m_PurchaseInProgress;
        private bool m_IsGooglePlayStoreSelected;
        private bool m_IsAppleStoreSelected;
        private bool isTestIAP = false;

        // Các dependency game được inject qua Configure() — module không gắn cứng code game.
        private IPurchasing _purchasing;
        private IIapProfile _profile;
        private IIapReporter _reporter;
        private IapSecurityConfig _config;

        private const string k_Environment = "production";

        void Awake()
        {
            // Chỉ khởi tạo Unity Services ở Awake (không phụ thuộc game).
            // IAP product setup (Init) phải chờ Configure() được gọi từ game.
            void OnSucces()
            {
                Debug.Log("---------INIT unity service success--------");
            }
            void OnFail(string e)
            {
                Debug.LogError("---------INIT unity service fail-------\n" + e);
            }
            Initialize(OnSucces, OnFail);
        }

        /// <summary>
        /// Inject các dependency game vào module. PHẢI gọi trước Init()/Buy().
        /// </summary>
        public void Configure(IPurchasing purchasing, IIapProfile profile, IIapReporter reporter, IapSecurityConfig config)
        {
            _purchasing = purchasing;
            _profile = profile;
            _reporter = reporter;
            _config = config;
        }

        private bool IsConfigured()
        {
            if (_purchasing == null || _config == null)
            {
                Debug.LogError("[IAP] InAppManager chưa được Configure(). Hãy gọi Configure() trước Init()/Buy().");
                return false;
            }

            return true;
        }

        private void Initialize(Action onSuccess, Action<string> onError)
        {
            try
            {
                var options = new InitializationOptions().SetEnvironmentName(k_Environment);

                UnityServices.InitializeAsync(options).ContinueWith(task => onSuccess());
            }
            catch (Exception exception)
            {
                onError(exception.Message);
            }
        }

        public void SetIsTestIAP(bool isTest)
        {
            isTestIAP = isTest;
        }

        #region InitData

        private void AddConsumableIds(ConfigurationBuilder builder)
        {
            void OnAddConsumableIds(string packNameId, ProductType type = ProductType.Consumable)
            {
                builder.AddProduct(packNameId, type);
                if (type == ProductType.NonConsumable)
                {
                    nonConsume.Add(packNameId);
                }
            }

            var productIds = _purchasing.GetConsumableProducts();
            foreach (var product in productIds)
            {
                OnAddConsumableIds(product, ProductType.Consumable);
            }

            var nonConsumeProductIds = _purchasing.GetNonConsumableProducts();
            foreach (var product in nonConsumeProductIds)
            {
                OnAddConsumableIds(product, ProductType.NonConsumable);
            }
        }

        public void Init()
        {
            if (!IsConfigured())
            {
                return;
            }

            var module = StandardPurchasingModule.Instance();
            m_IsGooglePlayStoreSelected =
                Application.platform == RuntimePlatform.Android && module.appStore == AppStore.GooglePlay;
            m_IsAppleStoreSelected = Application.platform == RuntimePlatform.IPhonePlayer &&
                                     module.appStore == AppStore.AppleAppStore;
            // If we have already connected to Purchasing ...
            if (IsInitialized())
            {
                return;
            }


            // If we haven't set up the Unity Purchasing reference
            if (m_StoreController == null)
            {
                // Begin to configure our connection to Purchasing
                try
                {
                    // Create a builder, first passing in a suite of Unity provided stores.
                    var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());

                    AddConsumableIds(builder);

                    // Kick off the remainder of the set-up with an asynchrounous call, passing the configuration
                    // and this class' instance. Expect a response either in OnInitialized or OnInitializeFailed.
                    Debug.Log("[IAP] Initialize IAP");
                    UnityPurchasing.Initialize(this, builder);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
            }
            else
            {
            }

            //InvokeCallbackInitIAP(false);
        }

        public bool IsInitialized()
        {
            //#if UNITY_IAP
            // Only say we are initialized if both the Purchasing references are set.
            return m_StoreController != null && m_StoreExtensionProvider != null;
            //#else
            //            return false;
            //#endif
        }

        #endregion

        #region Payment actions

        public void Buy(string productID, Action callBack, string source = "", string sourceId = "", Action unSuccess = null)
        {
            if (!IsConfigured())
            {
                unSuccess?.Invoke();
                return;
            }

            bool isCheatEnabled = _profile != null && _profile.IsCheatEnabled;

            if (isCheatEnabled && isTestIAP)
            {
                productId = productID;
                _purchasing.OnPurchaseCompleteBeforeCallback?.Invoke(productId);
                callBack?.Invoke();
                m_PurchaseInProgress = false;
                _purchasing.OnPurchaseComplete?.Invoke(productId);
                _reporter?.RequestSync();
                unSuccess?.Invoke();
                return;
            }

            sourcePurchase = source;
            sourcePurchaseId = sourceId;
            try
            {
                _reporter?.OnPurchaseClick(new IapPurchaseInfo
                {
                    Source = source,
                    SourceId = sourceId,
                    ProductId = productID
                });
            }
            catch { }


            try
            {
                if (m_PurchaseInProgress == true)
                {
                    Debug.Log("Please wait, purchase in progress");
                    unSuccess?.Invoke();
                    return;
                }

                if (m_StoreController == null)
                {
                    Debug.LogError("Purchasing is not initialized");
                    unSuccess?.Invoke();
                    return;
                }

                if (m_StoreController.products.WithID(productID) == null)
                {
                    Debug.LogError("No product has id " + productID);
                    unSuccess?.Invoke();
                    return;
                }

                m_PurchaseInProgress = true;
                Debug.Log("[IAP] Purchasing product: " + productId);

                callbackPay = callBack;
                productId = productID;

                if (isTestIAP)
                {
                    _purchasing.OnPurchaseCompleteBeforeCallback?.Invoke(productId);
                    callBack?.Invoke();
                    m_PurchaseInProgress = false;
                    _purchasing.OnPurchaseComplete?.Invoke(productId);
                    _reporter?.RequestSync();
                    unSuccess?.Invoke();
                    return;
                }

    #if UNITY_EDITOR
                _purchasing.OnPurchaseCompleteBeforeCallback?.Invoke(productId);
                callBack?.Invoke();
                m_PurchaseInProgress = false;
                _purchasing.OnPurchaseComplete?.Invoke(productId);
                _reporter?.RequestSync();
    #else
                BuyProductID(productID);
    #endif
            }
            catch
                (Exception e)
            {
                Debug.LogError(e);
            }
        }

        void BuyProductID(string productId)
        {
            // If Purchasing has been initialized ...
            if (IsInitialized())
            {
                // ... look up the Product reference with the general product identifier and the Purchasing
                // system's products collection.
                Product product = m_StoreController.products.WithID(productId);

                // If the look up found a product for this device's store and that product is ready to be sold ...
                if (product != null && product.availableToPurchase)
                {
                    Debug.Log(string.Format("Purchasing product asychronously: '{0}'", product.definition.id));
                    // ... buy the product. Expect a response either through ProcessPurchase or OnPurchaseFailed
                    // asynchronously.
                    m_StoreController.InitiatePurchase(product);
                }
                // Otherwise ...
                else
                {
                    // ... report the product look-up failure situation
                    Debug.Log(
                        "BuyProductID: FAIL. Not purchasing product, either is not found or is not available for purchase");
                }
            }
            // Otherwise ...
            else
            {
                // ... report the fact Purchasing has not succeeded initializing yet. Consider waiting longer or
                // retrying initiailization.
                Debug.Log("BuyProductID FAIL. Not initialized.");
            }
        }

        public void RestorePurchases()
        {
            try
            {
                // If Purchasing has not yet been set up ...
                if (!IsInitialized())
                {
                    // ... report the situation and stop restoring. Consider either waiting longer, or retrying initialization.
                    Debug.Log("[IAP] RestorePurchases FAIL. Not initialized.");
                    return;
                }

                if (m_IsGooglePlayStoreSelected)
                {
                    m_GooglePlayStoreExtensions.RestoreTransactions(OnTransactionsRestored);
                }
                else if (m_IsAppleStoreSelected)
                {
                    m_AppleExtensions.RestoreTransactions(OnTransactionsRestored);
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }

        private void OnTransactionsRestored(bool success)
        {
            Debug.Log("Transactions restored." + success);
            if (success)
            {
                _purchasing.RestoreItem();
            }

            _purchasing.OnTransactionRestored?.Invoke(success);
        }

        #endregion

        #region Payment event listener

        public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
        {
            // Purchasing has succeeded initializing. Collect our Purchasing references.
            Debug.Log("[IAP] OnInitialized: PASS");

            // Overall Purchasing system, configured with products for this application.
            m_StoreController = controller;
            // Store specific subsystem, for accessing device-specific store features.
            m_StoreExtensionProvider = extensions;

            m_AppleExtensions = m_StoreExtensionProvider.GetExtension<IAppleExtensions>();
            m_TransactionHistoryExtensions = m_StoreExtensionProvider.GetExtension<ITransactionHistoryExtensions>();
            m_GooglePlayStoreExtensions = m_StoreExtensionProvider.GetExtension<IGooglePlayStoreExtensions>();

            LogProductDefinitions();
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureDescription failureDescription)
        {
            try
            {
                // A product purchase attempt did not succeed. Check failureReason for more detail. Consider sharing
                // this reason with the user to guide their troubleshooting actions.
                Debug.Log(string.Format("OnPurchaseFailed: FAIL. Product: '{0}', PurchaseFailureReason: {1}",
                    product.definition.storeSpecificId, failureDescription.message));

                // Detailed debugging information
                Debug.Log("Store specific error code: " +
                          m_TransactionHistoryExtensions.GetLastStoreSpecificPurchaseErrorCode());
                if (m_TransactionHistoryExtensions.GetLastPurchaseFailureDescription() != null)
                {
                    Debug.Log("Purchase failure description message: " +
                              m_TransactionHistoryExtensions.GetLastPurchaseFailureDescription().message);
                }

                callbackPay = null;
                _purchasing.OnPurchaseFailed?.Invoke(failureDescription.message);
                m_PurchaseInProgress = false;
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }

        public void OnInitializeFailed(InitializationFailureReason error)
        {
            // Purchasing set-up has not succeeded. Check error for reason. Consider sharing this reason with the user.
            Debug.Log("[IAP] Billing failed to initialize!");
            switch (error)
            {
                case InitializationFailureReason.AppNotKnown:
                    Debug.LogError("[Buy] Is your App correctly uploaded on the relevant publisher console?");
                    break;

                case InitializationFailureReason.PurchasingUnavailable:
                    // Ask the user if billing is disabled in device settings.
                    Debug.Log("[Buy] Billing disabled!");
                    break;

                case InitializationFailureReason.NoProductsAvailable:
                    // Developer configuration error; check product metadata.
                    Debug.Log("[Buy] No products available for purchase!");
                    break;
            }
        }

        public void OnInitializeFailed(InitializationFailureReason error, string? message)
        {
            // Purchasing set-up has not succeeded. Check error for reason. Consider sharing this reason with the user.
            Debug.Log("[IAP] Billing failed to initialize!");
            switch (error)
            {
                case InitializationFailureReason.AppNotKnown:
                    Debug.LogError("[Buy] Is your App correctly uploaded on the relevant publisher console?");
                    break;

                case InitializationFailureReason.PurchasingUnavailable:
                    // Ask the user if billing is disabled in device settings.
                    Debug.Log("[Buy] Billing disabled!");
                    break;

                case InitializationFailureReason.NoProductsAvailable:
                    // Developer configuration error; check product metadata.
                    Debug.Log("[Buy] No products available for purchase!");
                    break;
            }
        }

        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
        {
            bool validPurchase = true; // Presume valid for platforms with no R.V.
            m_PurchaseInProgress = false;

            // Unity IAP's validation logic is only included on these platforms.
            var validator = new CrossPlatformValidator(_config.GooglePlayTangle,
                _config.AppleTangle, Application.identifier);

    #if UNITY_ANDROID || UNITY_IOS || UNITY_STANDALONE_OSX
            // Prepare the validator with the secrets we prepared in the Editor
            // obfuscation window.

            try
            {
                // On Google Play, result has a single product ID.
                // On Apple stores, receipts contain multiple products.
                var result = validator.Validate(args.purchasedProduct.receipt);
                // For informational purposes, we list the receipt(s)
                // Debug.Log("Receipt is valid. Contents:");
                //DataAnalyticsManager.BuyIAP(args.purchasedProduct.receipt, result);
                foreach (IPurchaseReceipt productReceipt in result)
                {
                    Debug.Log(productReceipt.productID);
                    Debug.Log(productReceipt.purchaseDate);
                    Debug.Log(productReceipt.transactionID);
                    if (productReceipt is GooglePlayReceipt google)
                    {
                        switch (google.purchaseState)
                        {
                            case GooglePurchaseState.Cancelled:
                                Debug.Log("Canceled");
                                validPurchase = false;
                                break;
                            case GooglePurchaseState.Refunded:
                                Debug.Log("Refunded");
                                validPurchase = false;
                                break;
                        }

                        if (google.purchaseState == GooglePurchaseState.Deferred)
                        {
                            Debug.Log("Pending");
                            return PurchaseProcessingResult.Pending;
                        }
                    }
                }
            }
            catch (IAPSecurityException)
            {
                Debug.Log("Invalid receipt, not unlocking content");
                validPurchase = false;
            }
    #endif

            try
            {
                var result = validator.Validate(args.purchasedProduct.receipt);

                if (validPurchase && result.Any(x => !string.IsNullOrEmpty(x.transactionID)))
                {

                    // Unlock the appropriate content here.
                    var product = args.purchasedProduct;

                    BuyCompleted(product);

                    Debug.Log(string.Format("[IAP] ProcessPurchase: PASS. Product: '{0}'", productId));
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                return PurchaseProcessingResult.Pending;
            }

            // Return a flag indicating whether this product has completely been received, or if the application needs
            // to be reminded of this purchase at next app launch. Use PurchaseProcessingResult.Pending when still
            // saving purchased products to the cloud, and when that save is delayed.
            return PurchaseProcessingResult.Complete;
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
        {
            try
            {
                // A product purchase attempt did not succeed. Check failureReason for more detail. Consider sharing
                // this reason with the user to guide their troubleshooting actions.
                Debug.Log(string.Format("OnPurchaseFailed: FAIL. Product: '{0}', PurchaseFailureReason: {1}",
                    product.definition.storeSpecificId, failureReason));

                // Detailed debugging information
                Debug.Log("Store specific error code: " +
                          m_TransactionHistoryExtensions.GetLastStoreSpecificPurchaseErrorCode());
                if (m_TransactionHistoryExtensions.GetLastPurchaseFailureDescription() != null)
                {
                    Debug.Log("Purchase failure description message: " +
                              m_TransactionHistoryExtensions.GetLastPurchaseFailureDescription().message);
                }

                callbackPay = null;
                _purchasing.OnPurchaseFailed?.Invoke(failureReason.ToString());
                m_PurchaseInProgress = false;
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }

        #endregion

        private string GetDefaultPriceText()
        {
            try
            {
                return _config?.DefaultPriceTextProvider?.Invoke() ?? "";
            }
            catch
            {
                return "";
            }
        }

        public string GetPricingLocalize(string productID)
        {
            var defaultCost = GetDefaultPriceText();
            if (m_StoreController == null) return defaultCost;

            var product = m_StoreController.products;

            if (product != null)
            {
                var productId = product.WithID(productID);
                if (productId != null)
                {
                    var metaData = productId.metadata;
                    if (metaData != null)
                    {
                        //Debug.Log(productID + " lay duoc gia : " + metaData.localizedPriceString);
                        return metaData.localizedPriceString;
                    }
                }
            }
            //Debug.LogError(productID + " khong co trong store controller");
            return defaultCost;
        }

        private CultureInfo cultureInfo;

        public string GetPriceWithSale(string productID, float sale)
        {
            var defaultCost = GetDefaultPriceText();
            if (m_StoreController == null) return defaultCost;

            var product = m_StoreController.products;

            if (product != null)
            {
                var productId = product.WithID(productID);
                if (productId != null)
                {
                    var metaData = productId.metadata;
                    if (metaData != null)
                    {
                        if (cultureInfo == null)
                        {
                            cultureInfo = CultureInfo.CurrentCulture;
                        }

                        var val = metaData.localizedPrice * (decimal)sale;
                        string formattedAmount = string.Format(cultureInfo, "{0:C}", val);
                        return formattedAmount;
                    }
                }
            }

            return defaultCost;
        }

        internal void FakeProcessPurchase(string productId)
        {
            Debug.Log(string.Format("[IAP] ProcessPurchase: PASS. Product: '{0}'", productId));
            callbackPay = null;
            m_PurchaseInProgress = false;
        }

        private void LogProductDefinitions()
        {
            var products = m_StoreController.products.all;
            foreach (var product in products)
            {
    #if UNITY_5_6_OR_NEWER
                Debug.Log(string.Format("id: {0}\nstore-specific id: {1}\ntype: {2}\nenabled: {3}\n", product.definition.id,
                    product.definition.storeSpecificId, product.definition.type.ToString(),
                    product.definition.enabled ? "enabled" : "disabled"));
    #else
                Debug.Log(string.Format("id: {0}\nstore-specific id: {1}\ntype: {2}\n", product.definition.id,
                    product.definition.storeSpecificId, product.definition.type.ToString()));
    #endif
            }
        }


        public string GetPriceStringById(string id)
        {
            try
            {
                if (id.Length == 0)
                {
                    return "";
                }

                return m_StoreController.products.WithID(id).metadata.localizedPriceString;
                //return isoCurrencyCode + localPrices[id];
            }
            catch (Exception e)
            {
                //Debug.LogError(e.ToString() + ": " + id);
                return "";
            }
        }

        private void BuyCompleted(Product product)
        {
            productId = product.definition.id;

            // Flag to cancel multi progress
            if (m_PurchaseInProgress == true)
            {
                Debug.Log("Please wait, purchase in progress");
                return;
            }

            if (m_StoreController == null)
            {
                Debug.LogError("Purchasing is not initialized");
                return;
            }

            if (m_StoreController.products.WithID(productId) == null)
            {
                Debug.LogError("No product has id " + productId);
                return;
            }

            m_PurchaseInProgress = false;

            _purchasing.OnPurchaseCompleteBeforeCallback?.Invoke(productId);

            callbackPay?.Invoke();

            var info = BuildPurchaseInfo(product);

            _reporter?.OnPurchaseValidated(info);

            ValidateAndSend(product);

            _purchasing.OnPurchaseComplete?.Invoke(productId);

            _profile?.RecordPurchase(product.metadata.localizedPrice);

            _reporter?.RequestSync();
        }

        /// <summary>
        /// Parse receipt thô của Unity IAP thành DTO độc lập SDK để chuyển ra game.
        /// </summary>
        private IapPurchaseInfo BuildPurchaseInfo(Product product)
        {
            var info = new IapPurchaseInfo
            {
                ProductId = product.definition.id,
                Source = sourcePurchase,
                SourceId = sourcePurchaseId,
                LocalizedPrice = product.metadata.localizedPrice,
                IsoCurrencyCode = product.metadata.isoCurrencyCode,
                Receipt = product.receipt,
            };

            try
            {
    #if UNITY_ANDROID
                Receipt receiptAndroid = JsonUtility.FromJson<Receipt>(product.receipt);
                PayloadAndroid receiptPayload = JsonUtility.FromJson<PayloadAndroid>(receiptAndroid.Payload);
                info.PayloadJson = receiptPayload.json;
                info.Signature = receiptPayload.signature;
    #elif UNITY_IPHONE
                Receipt receiptiOS = JsonUtility.FromJson<Receipt>(product.receipt);
                info.PayloadJson = receiptiOS.Payload;
                info.TransactionId = product.transactionID;
    #endif
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }

            return info;
        }

        private void ValidateAndSend(Product product)
        {
            try
            {
                string price = product.metadata.localizedPrice.ToString(CultureInfo.InvariantCulture);

                string currency = product.metadata.isoCurrencyCode;

                var receipt = (Dictionary<string, object>)AFMiniJSON.Json.Deserialize(product.receipt);
                var receiptPayload =
                    (Dictionary<string, object>)AFMiniJSON.Json.Deserialize((string)receipt["Payload"]);

    #if UNITY_ANDROID

                var purchaseData = (string)receiptPayload["json"];
                var signature = (string)receiptPayload["signature"];
                AppsFlyer.validateAndSendInAppPurchase(_config.AppsFlyerPublicKey,
                    signature,
                    purchaseData,
                    price,
                    currency,
                    null,
                    Listener);
    #elif UNITY_IOS
                    var productIdentifier = product.definition.id;
                    var tranactionId = product.transactionID;

                    AppsFlyer.validateAndSendInAppPurchase(productIdentifier, price, currency, tranactionId,
                        null, Listener);
    #endif
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private AppsFlyerListener _listener;
        public AppsFlyerListener Listener
        {
            get
            {
                if (_listener == null) _listener = transform.GetComponent<AppsFlyerListener>();
                if (_listener == null) _listener = gameObject.AddComponent<AppsFlyerListener>();
                _listener.Reporter = _reporter;
                return _listener;
            }
        }
    }
}
