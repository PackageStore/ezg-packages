#if (UNITY_ANDROID || UNITY_IPHONE || UNITY_IOS) && !UNITY_EDITOR
#define RECEIPT_VALIDATION
#endif

using Ezg.Package.Singleton;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Security;
using AppsFlyerSDK;


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

    /// <summary>
    /// In-app purchase manager built on Unity Purchasing v5 (UnityIAPServices / StoreController,
    /// event-driven Order flow). Migrated from the v4 IStoreListener API. The v5 flow is:
    /// Connect() -> OnStoreConnected -> FetchProducts -> OnProductsFetched ->
    /// PurchaseProduct -> OnPurchasePending (validate + grant + ConfirmPurchase) -> OnPurchaseConfirmed.
    /// Calling ConfirmPurchase is mandatory to finalize the transaction (this is what the legacy
    /// bridge failed to do on iOS).
    /// </summary>
    public class InAppManager : Singleton<InAppManager>
    {
        public List<string> nonConsume = new List<string>();

        // Unity Purchasing v5: single unified, event-driven controller.
        private StoreController m_StoreController;

        private Action callbackPay;

        private string productId;
        private string sourcePurchase;
        private string sourcePurchaseId;

        private bool m_PurchaseInProgress;
        private bool m_IsGooglePlayStoreSelected;
        private bool m_IsAppleStoreSelected;
        private bool m_StoreConnected;
        private bool m_ProductsFetched;
        private bool m_Connecting;
        private bool m_RestoreInProgress;
        private bool isTestIAP = false;

        // Các dependency game được inject qua Configure() — module không gắn cứng code game.
        private IPurchasing _purchasing;
        private IIapProfile _profile;
        private IIapReporter _reporter;
        private IapSecurityConfig _config;

        private CultureInfo cultureInfo;
        private AppsFlyerListener _listener;

        private const string k_Environment = "production";

        #region Initialize

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

            // v5: tạo controller + đăng ký event 1 lần. Connect() được gọi ở Init() sau Configure().
            CreateStoreController();
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

        /// <summary>
        /// Tạo StoreController v5 và đăng ký toàn bộ event. Idempotent — chỉ chạy 1 lần.
        /// </summary>
        private void CreateStoreController()
        {
            if (m_StoreController != null)
            {
                return;
            }

            m_StoreController = UnityIAPServices.StoreController();

            m_StoreController.OnStoreConnected += OnStoreConnected;
            m_StoreController.OnStoreDisconnected += OnStoreDisconnected;

            m_StoreController.OnProductsFetched += OnProductsFetched;
            m_StoreController.OnProductsFetchFailed += OnProductsFetchFailed;

            m_StoreController.OnPurchasePending += OnPurchasePending;
            m_StoreController.OnPurchaseConfirmed += OnPurchaseConfirmed;
            m_StoreController.OnPurchaseFailed += OnPurchaseFailed;
            m_StoreController.OnPurchaseDeferred += OnPurchaseDeferred;

            m_StoreController.OnPurchasesFetched += OnPurchasesFetched;
            m_StoreController.OnPurchasesFetchFailed += OnPurchasesFetchFailed;
        }

        #endregion

        #region Public

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

        public void SetIsTestIAP(bool isTest)
        {
            isTestIAP = isTest;
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

            CreateStoreController();

            // Đã connect rồi → chỉ cần (re)fetch catalog.
            if (m_StoreConnected)
            {
                FetchProducts();
                return;
            }

            ConnectStore();
        }

        /// <summary>
        /// "Initialized" theo nghĩa sẵn sàng mua: store đã connect VÀ products đã fetch xong.
        /// </summary>
        public bool IsInitialized()
        {
            return m_StoreController != null && m_StoreConnected && m_ProductsFetched;
        }

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

                if (m_StoreController.GetProductById(productID) == null)
                {
                    Debug.LogError("No product has id " + productID);
                    unSuccess?.Invoke();
                    return;
                }

                m_PurchaseInProgress = true;
                Debug.Log("[IAP] Purchasing product: " + productID);

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

                if (m_IsAppleStoreSelected)
                {
                    // Apple: StoreKit restore qua callback.
                    m_StoreController.RestoreTransactions(OnTransactionsRestored);
                }
                else
                {
                    // Google Play (và store khác): entitlement được khôi phục bằng cách fetch purchases.
                    m_RestoreInProgress = true;
                    m_StoreController.FetchPurchases();
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }

        public string GetPricingLocalize(string productID)
        {
            var defaultCost = GetDefaultPriceText();
            if (m_StoreController == null) return defaultCost;

            var product = m_StoreController.GetProductById(productID);
            if (product != null && product.metadata != null)
            {
                return product.metadata.localizedPriceString;
            }

            return defaultCost;
        }

        public string GetPriceWithSale(string productID, float sale)
        {
            var defaultCost = GetDefaultPriceText();
            if (m_StoreController == null) return defaultCost;

            var product = m_StoreController.GetProductById(productID);
            if (product != null && product.metadata != null)
            {
                if (cultureInfo == null)
                {
                    cultureInfo = CultureInfo.CurrentCulture;
                }

                var val = product.metadata.localizedPrice * (decimal)sale;
                string formattedAmount = string.Format(cultureInfo, "{0:C}", val);
                return formattedAmount;
            }

            return defaultCost;
        }

        public string GetPriceStringById(string id)
        {
            if (string.IsNullOrEmpty(id) || m_StoreController == null)
            {
                return "";
            }

            var product = m_StoreController.GetProductById(id);
            if (product == null || product.metadata == null)
            {
                return "";
            }

            return product.metadata.localizedPriceString;
        }

        internal void FakeProcessPurchase(string productId)
        {
            Debug.Log(string.Format("[IAP] ProcessPurchase: PASS. Product: '{0}'", productId));
            callbackPay = null;
            m_PurchaseInProgress = false;
        }

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

        #endregion

        #region Private

        private bool IsConfigured()
        {
            if (_purchasing == null || _config == null)
            {
                Debug.LogError("[IAP] InAppManager chưa được Configure(). Hãy gọi Configure() trước Init()/Buy().");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Kết nối store (v5). Kết quả trả về qua event OnStoreConnected / OnStoreDisconnected.
        /// </summary>
        private void ConnectStore()
        {
            if (m_Connecting || m_StoreConnected)
            {
                return;
            }

            m_Connecting = true;
            Debug.Log("[IAP] Connecting to store...");

            // Không dùng async void (theo convention dự án) và giữ package standalone (không thêm
            // dependency UniTask). Kết quả connect đến qua event OnStoreConnected/OnStoreDisconnected;
            // ContinueWith chỉ để quan sát/log exception của Task (tránh unobserved task exception).
            try
            {
                m_StoreController.Connect().ContinueWith(OnConnectTaskCompleted);
            }
            catch (Exception e)
            {
                m_Connecting = false;
                Debug.LogError("[IAP] Store connect failed: " + e);
            }
        }

        private void OnConnectTaskCompleted(Task task)
        {
            if (task.IsFaulted)
            {
                m_Connecting = false;
                Debug.LogError("[IAP] Store connect task faulted: " + task.Exception);
            }
        }

        /// <summary>
        /// Build danh sách ProductDefinition từ game rồi fetch metadata/giá từ store.
        /// </summary>
        private void FetchProducts()
        {
            if (!IsConfigured() || m_StoreController == null)
            {
                return;
            }

            nonConsume.Clear();

            var definitions = new List<ProductDefinition>();

            var consumableIds = _purchasing.GetConsumableProducts();
            foreach (var id in consumableIds)
            {
                definitions.Add(new ProductDefinition(id, ProductType.Consumable));
            }

            var nonConsumableIds = _purchasing.GetNonConsumableProducts();
            foreach (var id in nonConsumableIds)
            {
                definitions.Add(new ProductDefinition(id, ProductType.NonConsumable));
                nonConsume.Add(id);
            }

            Debug.Log("[IAP] Fetching " + definitions.Count + " products");
            m_StoreController.FetchProducts(definitions);
        }

        void BuyProductID(string productId)
        {
            // If Purchasing has been initialized ...
            if (IsInitialized())
            {
                // ... look up the Product reference with the general product identifier.
                Product product = m_StoreController.GetProductById(productId);

                // If the look up found a product for this device's store and that product is ready to be sold ...
                if (product != null && product.availableToPurchase)
                {
                    Debug.Log(string.Format("Purchasing product asychronously: '{0}'", product.definition.id));
                    // ... buy the product. Expect a response through OnPurchasePending / OnPurchaseFailed asynchronously.
                    m_StoreController.PurchaseProduct(product);
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

        /// <summary>
        /// Validate receipt của order (thay cho logic trong ProcessPurchase ở v4).
        /// Trả về true nếu hợp lệ; false nếu receipt giả mạo / bị huỷ / hoàn tiền.
        /// Ném exception cho lỗi tạm thời (để caller giữ order ở trạng thái pending → retry).
        /// </summary>
        private bool ValidatePurchase(Order order)
        {
            bool validPurchase = true; // Presume valid for platforms with no R.V.

    #if RECEIPT_VALIDATION
            // Receipt validation chỉ chạy trên device thật (xem macro RECEIPT_VALIDATION ở đầu file).
            var validator = new CrossPlatformValidator(_config.GooglePlayTangle,
                _config.AppleTangle, Application.identifier);

            try
            {
                // On Google Play, result has a single product ID.
                // On Apple stores, receipts contain multiple products.
                var result = validator.Validate(order.Info.Receipt);
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
                    }
                }

                // Yêu cầu có ít nhất một transactionID hợp lệ.
                if (!result.Any(x => !string.IsNullOrEmpty(x.transactionID)))
                {
                    validPurchase = false;
                }
            }
            catch (IAPSecurityException)
            {
                Debug.Log("Invalid receipt, not unlocking content");
                validPurchase = false;
            }
    #endif

            return validPurchase;
        }

        private void BuyCompleted(Product product, IOrderInfo orderInfo)
        {
            productId = product.definition.id;

            if (m_StoreController == null)
            {
                Debug.LogError("Purchasing is not initialized");
                return;
            }

            if (m_StoreController.GetProductById(productId) == null)
            {
                Debug.LogError("No product has id " + productId);
                return;
            }

            m_PurchaseInProgress = false;

            _purchasing.OnPurchaseCompleteBeforeCallback?.Invoke(productId);

            callbackPay?.Invoke();
            callbackPay = null;

            var info = BuildPurchaseInfo(product, orderInfo);

            _reporter?.OnPurchaseValidated(info);

            ValidateAndSend(product, orderInfo);

            _purchasing.OnPurchaseComplete?.Invoke(productId);

            _profile?.RecordPurchase(product.metadata.localizedPrice);

            _reporter?.RequestSync();
        }

        /// <summary>
        /// Parse receipt thô của Unity IAP thành DTO độc lập SDK để chuyển ra game.
        /// v5: receipt + transactionID nằm trên Order (IOrderInfo), không còn trên Product.
        /// </summary>
        private IapPurchaseInfo BuildPurchaseInfo(Product product, IOrderInfo orderInfo)
        {
            var receiptString = orderInfo != null ? orderInfo.Receipt : product.receipt;

            var info = new IapPurchaseInfo
            {
                ProductId = product.definition.id,
                Source = sourcePurchase,
                SourceId = sourcePurchaseId,
                LocalizedPrice = product.metadata.localizedPrice,
                IsoCurrencyCode = product.metadata.isoCurrencyCode,
                Receipt = receiptString,
            };

            try
            {
    #if UNITY_ANDROID
                Receipt receiptAndroid = JsonUtility.FromJson<Receipt>(receiptString);
                PayloadAndroid receiptPayload = JsonUtility.FromJson<PayloadAndroid>(receiptAndroid.Payload);
                info.PayloadJson = receiptPayload.json;
                info.Signature = receiptPayload.signature;
    #elif UNITY_IPHONE
                Receipt receiptiOS = JsonUtility.FromJson<Receipt>(receiptString);
                info.PayloadJson = receiptiOS.Payload;
                info.TransactionId = orderInfo != null ? orderInfo.TransactionID : product.transactionID;
    #endif
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }

            return info;
        }

        private void ValidateAndSend(Product product, IOrderInfo orderInfo)
        {
            try
            {
                string price = product.metadata.localizedPrice.ToString(CultureInfo.InvariantCulture);

                string currency = product.metadata.isoCurrencyCode;

                var receiptString = orderInfo != null ? orderInfo.Receipt : product.receipt;

                var receipt = (Dictionary<string, object>)AFMiniJSON.Json.Deserialize(receiptString);
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
                    var tranactionId = orderInfo != null ? orderInfo.TransactionID : product.transactionID;

                    AppsFlyer.validateAndSendInAppPurchase(productIdentifier, price, currency, tranactionId,
                        null, Listener);
    #endif
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

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

        private void LogProductDefinitions()
        {
            var products = m_StoreController.GetProducts();
            foreach (var product in products)
            {
                Debug.Log(string.Format("id: {0}\nstore-specific id: {1}\ntype: {2}\nenabled: {3}\n", product.definition.id,
                    product.definition.storeSpecificId, product.definition.type.ToString(),
                    product.definition.enabled ? "enabled" : "disabled"));
            }
        }

        private Product GetFirstProductInOrder(Order order)
        {
            return order?.CartOrdered?.Items()?.FirstOrDefault()?.Product;
        }

        #endregion

        #region Events

        private void OnStoreConnected()
        {
            m_StoreConnected = true;
            Debug.Log("[IAP] OnStoreConnected");
            FetchProducts();
        }

        private void OnStoreDisconnected(StoreConnectionFailureDescription description)
        {
            m_StoreConnected = false;
            m_ProductsFetched = false;
            Debug.Log("[IAP] OnStoreDisconnected: " + description.message);
        }

        private void OnProductsFetched(List<Product> products)
        {
            m_ProductsFetched = true;
            Debug.Log("[IAP] OnProductsFetched: " + products.Count);
            LogProductDefinitions();
        }

        private void OnProductsFetchFailed(ProductFetchFailed failure)
        {
            Debug.LogError("[IAP] OnProductsFetchFailed: " + failure.FailureReason);
        }

        private void OnPurchasePending(PendingOrder order)
        {
            // Một purchase đã được store chấp nhận và đang chờ app xử lý + xác nhận.
            m_PurchaseInProgress = false;

            var product = GetFirstProductInOrder(order);
            if (product == null)
            {
                Debug.LogError("[IAP] OnPurchasePending: product not found in order, confirming to close transaction.");
                callbackPay = null;
                m_StoreController.ConfirmPurchase(order);
                return;
            }

            bool validPurchase;
            try
            {
                validPurchase = ValidatePurchase(order);
            }
            catch (Exception e)
            {
                // Lỗi tạm thời (vd: deserialize/validator) → KHÔNG confirm, để store re-deliver lần sau
                // (tương đương PurchaseProcessingResult.Pending ở v4). Chưa grant nên không lo double-grant.
                Debug.LogError("[IAP] Validation error, leaving purchase pending: " + e);
                return;
            }

            // Khi đã quyết định finalize: ConfirmPurchase PHẢI chạy đúng 1 lần — kể cả khi BuyCompleted
            // ném exception SAU khi đã grant — để transaction không bị re-deliver lần sau → double-grant.
            try
            {
                if (validPurchase)
                {
                    BuyCompleted(product, order.Info);
                    Debug.Log(string.Format("[IAP] ProcessPurchase: PASS. Product: '{0}'", product.definition.id));
                }
                else
                {
                    callbackPay = null;
                    Debug.Log("[IAP] Invalid receipt, not unlocking content.");
                }
            }
            finally
            {
                // v5: ConfirmPurchase finalize transaction (tương đương return Complete ở v4).
                // BẮT BUỘC trên iOS — bỏ bước này là nguyên nhân purchase iOS không hoàn tất ở legacy bridge.
                m_StoreController.ConfirmPurchase(order);
            }
        }

        private void OnPurchaseConfirmed(Order order)
        {
            var product = GetFirstProductInOrder(order);
            switch (order)
            {
                case ConfirmedOrder:
                    Debug.Log("[IAP] OnPurchaseConfirmed: " + (product != null ? product.definition.id : "?"));
                    break;
                case FailedOrder failedOrder:
                    Debug.LogError("[IAP] Purchase confirmation failed: " + failedOrder.FailureReason + " / " +
                                   failedOrder.Details);
                    break;
                default:
                    Debug.Log("[IAP] OnPurchaseConfirmed: unknown result");
                    break;
            }
        }

        private void OnPurchaseFailed(FailedOrder order)
        {
            try
            {
                var product = GetFirstProductInOrder(order);
                Debug.Log(string.Format("[IAP] OnPurchaseFailed. Product: '{0}', Reason: {1}, Details: {2}",
                    product != null ? product.definition.storeSpecificId : "?", order.FailureReason, order.Details));

                callbackPay = null;
                _purchasing.OnPurchaseFailed?.Invoke(order.FailureReason.ToString());
                m_PurchaseInProgress = false;
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }

        private void OnPurchaseDeferred(DeferredOrder order)
        {
            // Purchase bị hoãn (vd: chờ phụ huynh phê duyệt). Không grant, không khoá flow.
            var product = GetFirstProductInOrder(order);
            Debug.Log("[IAP] OnPurchaseDeferred: " + (product != null ? product.definition.id : "?"));
            m_PurchaseInProgress = false;
        }

        private void OnPurchasesFetched(Orders orders)
        {
            // Chỉ xử lý khi đang trong luồng restore (Google Play).
            if (!m_RestoreInProgress)
            {
                return;
            }

            m_RestoreInProgress = false;
            Debug.Log("[IAP] OnPurchasesFetched (restore). Confirmed: " + orders.ConfirmedOrders.Count);
            _purchasing.RestoreItem();
            _purchasing.OnTransactionRestored?.Invoke(true);
        }

        private void OnPurchasesFetchFailed(PurchasesFetchFailureDescription failure)
        {
            if (!m_RestoreInProgress)
            {
                return;
            }

            m_RestoreInProgress = false;
            Debug.LogError("[IAP] OnPurchasesFetchFailed (restore): " + failure.message);
            _purchasing.OnTransactionRestored?.Invoke(false);
        }

        private void OnTransactionsRestored(bool success, string error)
        {
            Debug.Log("Transactions restored." + success + (string.IsNullOrEmpty(error) ? "" : " Error: " + error));
            if (success)
            {
                _purchasing.RestoreItem();
            }

            _purchasing.OnTransactionRestored?.Invoke(success);
        }

        #endregion
    }
}
