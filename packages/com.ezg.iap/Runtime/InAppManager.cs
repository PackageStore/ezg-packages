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
        private bool m_PendingRecoveryFetch;      // đang fetch để recover pending/deferred order (khác luồng restore)
        private bool m_ProcessPendingConfigured;  // đã set ProcessPendingOrdersOnPurchasesFetched(true) chưa
        private bool isTestIAP = false;

        // Các dependency game được inject qua Configure() — module không gắn cứng code game.
        private IPurchasing _purchasing;
        private IIapProfile _profile;
        private IIapReporter _reporter;
        private IapSecurityConfig _config;
        private IIapOrderLedger _ledger;

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

            if (!m_ProcessPendingConfigured)
            {
                // Khi fetch purchases, các order pending/deferred-approved sẽ được đẩy vào OnPurchasePending
                // để grant + confirm. Đây là cơ chế recover chính thức cho CẢ Android lẫn iOS.
                m_StoreController.ProcessPendingOrdersOnPurchasesFetched(true);
                m_ProcessPendingConfigured = true;
            }
        }

        /// <summary>
        /// Quay lại foreground: hỏi lại store xem có order nào chưa giao không (deferred iOS Ask-to-Buy
        /// được approve lúc background, hoặc mua rồi rời app). Chạy cho cả Android và iOS.
        /// </summary>
        private void OnApplicationPause(bool paused)
        {
            if (!paused && m_StoreConnected && m_ProductsFetched)
            {
                RecoverPendingPurchases();
            }
        }

        #endregion

        #region Public

        /// <summary>
        /// Inject các dependency game vào module. PHẢI gọi trước Init()/Buy().
        /// </summary>
        public void Configure(IPurchasing purchasing, IIapProfile profile, IIapReporter reporter,
            IapSecurityConfig config, IIapOrderLedger ledger = null)
        {
            _purchasing = purchasing;
            _profile = profile;
            _reporter = reporter;
            _config = config;
            _ledger = ledger;
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

                // isTestIAP CHỈ có hiệu lực khi host cho phép cheat (IIapProfile.IsCheatEnabled — ở build
                // store host trả false). Bản cũ nhánh này không kiểm isCheatEnabled → bảng cheat/bất kỳ
                // code nào gọi SetIsTestIAP(true) là mua sạch mọi gói miễn phí trên bản release.
                if (isTestIAP && isCheatEnabled)
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

        /// <summary>
        /// Kéo các order chưa confirm (pending / deferred đã approve / mua bị gián đoạn) từ store về
        /// OnPurchasePending để grant + ConfirmPurchase. Dùng chung Android & iOS.
        /// Khác RestorePurchases(): restore là hành động do user bấm để lấy lại non-consumable (iOS
        /// cần RestoreTransactions); còn đây là recover tự động các giao dịch đang treo.
        /// </summary>
        private void RecoverPendingPurchases()
        {
            if (m_StoreController == null || !m_StoreConnected)
            {
                return;
            }

            // Không chồng lên luồng restore (nút Restore) đang chạy — tránh double xử lý OnPurchasesFetched.
            if (m_RestoreInProgress || m_PendingRecoveryFetch)
            {
                return;
            }

            m_PendingRecoveryFetch = true;
            Debug.Log("[IAP] Recover pending purchases...");
            m_StoreController.FetchPurchases();
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
        /// Trả về true nếu hợp lệ; false nếu receipt giả mạo / bị huỷ / hoàn tiền / không khớp product đang mua.
        /// Ném exception cho lỗi tạm thời (để caller giữ order ở trạng thái pending → retry).
        ///
        /// Apple StoreKit 2 (iOS ≥ 15, mặc định của Unity Purchasing 5): <see cref="CrossPlatformValidator"/>
        /// CỐ Ý trả mảng RỖNG cho Apple vì SDK đã verify JWS của transaction ở tầng native (changelog
        /// com.unity.purchasing: "CrossPlatformValidator is no longer used for Apple since StoreKit2 does it").
        /// Bản 0.3.0 đòi "≥1 receipt có transactionID" nên coi MỌI order iOS là receipt giả →
        /// ConfirmPurchase (Apple đã thu tiền) mà không cấp quà. Giờ nhánh SK2 kiểm bằng dữ liệu
        /// trên <see cref="IAppleOrderInfo"/> (transactionID + jwsRepresentation) thay vì result.
        ///
        /// Google Play / StoreKit 1: giữ verify chữ ký bằng tangle + bundle id (trong validator), thêm
        /// kiểm receipt phải chứa ĐÚNG product đang mua và transactionID không rỗng. Thiếu tangle
        /// (MissingStoreSecretException) → fail-closed, không cấp quà — host phải giữ Tangle khỏi bị strip.
        /// </summary>
        private bool ValidatePurchase(Order order, Product product)
        {
            bool validPurchase = true; // Presume valid for platforms with no R.V.

    #if RECEIPT_VALIDATION
            var orderInfo = order.Info;
            var receipt = orderInfo != null ? orderInfo.Receipt : null;
            var transactionId = orderInfo != null ? orderInfo.TransactionID : null;
            var expectedProductId = product.definition.storeSpecificId;

            if (string.IsNullOrEmpty(receipt) || string.IsNullOrEmpty(transactionId))
            {
                Debug.LogError("[IAP] Order thiếu receipt hoặc transactionID → không cấp quà. Product: " + expectedProductId);
                return false;
            }

            try
            {
                // Receipt validation chỉ chạy trên device thật (xem macro RECEIPT_VALIDATION ở đầu file).
                var validator = new CrossPlatformValidator(_config.GooglePlayTangle,
                    _config.AppleTangle, Application.identifier);

                var result = validator.Validate(receipt);

                if (result.Length == 0)
                {
                    // Chỉ Apple StoreKit 2 rơi vào đây: Google và StoreKit 1 luôn trả ≥1 receipt hoặc ném
                    // IAPSecurityException. Mọi store khác mà trả rỗng thì coi là bất thường → từ chối.
                    validPurchase = ValidateAppleStoreKit2Order(orderInfo, expectedProductId);
                }
                else
                {
                    var matchedProduct = false;
                    foreach (IPurchaseReceipt productReceipt in result)
                    {
                        Debug.Log("[IAP] receipt: " + productReceipt.productID + " / " + productReceipt.transactionID +
                                  " / " + productReceipt.purchaseDate);

                        if (productReceipt is GooglePlayReceipt google)
                        {
                            switch (google.purchaseState)
                            {
                                case GooglePurchaseState.Cancelled:
                                    Debug.Log("[IAP] Google purchaseState = Cancelled");
                                    validPurchase = false;
                                    break;
                                case GooglePurchaseState.Refunded:
                                    Debug.Log("[IAP] Google purchaseState = Refunded");
                                    validPurchase = false;
                                    break;
                            }
                        }

                        // Apple StoreKit 1 app receipt chứa nhiều product; Google chỉ một. Receipt hợp lệ
                        // nhưng KHÔNG chứa product đang mua = receipt của đơn khác bị đem dùng lại.
                        if (!string.IsNullOrEmpty(productReceipt.transactionID) &&
                            productReceipt.productID == expectedProductId)
                        {
                            matchedProduct = true;
                        }
                    }

                    if (!matchedProduct)
                    {
                        Debug.LogError("[IAP] Receipt hợp lệ nhưng không chứa product đang mua '" + expectedProductId +
                                       "' → không cấp quà.");
                        validPurchase = false;
                    }
                }
            }
            catch (IAPSecurityException e)
            {
                // Chữ ký sai / bundle id lệch / thiếu tangle / receipt không parse được — đều là từ chối dứt
                // khoát (không retry). Lỗi khác (không phải IAPSecurityException) ném ra ngoài để caller giữ
                // order pending và store re-deliver lần sau.
                Debug.LogError("[IAP] Invalid receipt (" + e.GetType().Name + "): " + e.Message);
                validPurchase = false;
            }
    #endif

            return validPurchase;
        }

    #if RECEIPT_VALIDATION
        /// <summary>
        /// Apple StoreKit 2: JWS của transaction đã được StoreKit + Unity native verify trước khi order tới
        /// đây; client chỉ còn kiểm order có đúng hình dạng một transaction SK2 thật (order info kiểu Apple,
        /// có jwsRepresentation, JWS payload khai đúng productId + bundle id). Chống spoof ở tầng native/jailbreak
        /// thì chỉ server-side validation (App Store Server API) mới làm được — ngoài phạm vi module này.
        /// </summary>
        private static bool ValidateAppleStoreKit2Order(IOrderInfo orderInfo, string expectedProductId)
        {
            if (!(orderInfo is IAppleOrderInfo apple))
            {
                Debug.LogError("[IAP] Validator trả rỗng nhưng order không phải Apple → từ chối.");
                return false;
            }

            if (string.IsNullOrEmpty(apple.jwsRepresentation))
            {
                Debug.LogError("[IAP] Order Apple StoreKit 2 không có jwsRepresentation → từ chối.");
                return false;
            }

            // Đọc payload JWS (phần giữa, base64url) — chữ ký đã được native verify, ở đây chỉ đối chiếu nội dung.
            try
            {
                var parts = apple.jwsRepresentation.Split('.');
                if (parts.Length != 3)
                {
                    Debug.LogError("[IAP] jwsRepresentation không đúng dạng JWS → từ chối.");
                    return false;
                }

                var payloadJson = System.Text.Encoding.UTF8.GetString(DecodeBase64Url(parts[1]));
                // JsonUtility thay MiniJson (MiniJson của Unity Purchasing không public ra ngoài assembly).
                var payload = JsonUtility.FromJson<AppleJwsPayload>(payloadJson);
                if (payload == null)
                {
                    Debug.LogError("[IAP] JWS payload không parse được → từ chối.");
                    return false;
                }

                if (payload.productId != expectedProductId)
                {
                    Debug.LogError("[IAP] JWS productId '" + payload.productId + "' ≠ product đang mua '" + expectedProductId + "' → từ chối.");
                    return false;
                }

                if (!string.IsNullOrEmpty(payload.bundleId) && payload.bundleId != Application.identifier)
                {
                    Debug.LogError("[IAP] JWS bundleId '" + payload.bundleId + "' ≠ app '" + Application.identifier + "' → từ chối.");
                    return false;
                }

                if (payload.revocationDate > 0)
                {
                    Debug.LogError("[IAP] Transaction đã bị revoke → từ chối.");
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[IAP] Không đọc được JWS payload: " + e.Message + " → từ chối.");
                return false;
            }

            return true;
        }

        /// <summary>Các field cần đối chiếu trong payload JWS của StoreKit 2 (JWSTransactionDecodedPayload).</summary>
        [Serializable]
        private class AppleJwsPayload
        {
            public string productId;
            public string bundleId;
            public long revocationDate; // ms epoch; 0 = chưa revoke
        }

        private static byte[] DecodeBase64Url(string input)
        {
            var s = input.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }
    #endif

        /// <summary>
        /// Cấp quà + lưu bền vững (KHÔNG analytics). Trả về true nếu đã cấp thành công.
        /// Tách khỏi analytics để OnPurchasePending có thể ghi ledger NGAY sau khi grant + save,
        /// TRƯỚC khi bắn analytics — thu hẹp khe crash double-grant và tránh analytics bắn trùng.
        /// </summary>
        private bool GrantRewards(Product product)
        {
            productId = product.definition.id;

            if (m_StoreController == null)
            {
                Debug.LogError("Purchasing is not initialized");
                return false;
            }

            if (m_StoreController.GetProductById(productId) == null)
            {
                Debug.LogError("No product has id " + productId);
                return false;
            }

            m_PurchaseInProgress = false;

            _purchasing.OnPurchaseCompleteBeforeCallback?.Invoke(productId);

            callbackPay?.Invoke();
            callbackPay = null;

            // Cấp quà thật + Save (game xử lý trong OnPurchaseComplete → GrantIapByProductId → ReceiveRewards).
            _purchasing.OnPurchaseComplete?.Invoke(productId);

            return true;
        }

        /// <summary>
        /// Bắn analytics/đồng bộ cho một giao dịch đã grant. Gọi SAU khi đã MarkGranted (ledger),
        /// nên nếu app chết trước bước này, phiên sau order re-deliver sẽ bị guard chặn grant lại
        /// → analytics KHÔNG bắn trùng.
        /// </summary>
        private void SendPurchaseAnalytics(Product product, IOrderInfo orderInfo)
        {
            var info = BuildPurchaseInfo(product, orderInfo);

            _reporter?.OnPurchaseValidated(info);

            // ROI360: doanh thu IAP do AppsFlyer Purchase Connector tự validate + log.
            // KHÔNG gọi ValidateAndSend nữa để tránh đếm trùng doanh thu. Xem GameInitialize.InitPurchaseConnector.
            // ValidateAndSend(product, orderInfo);

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

            // Recover order treo (deferred approve / mua gián đoạn) NGAY khi store connect — độc lập với
            // FetchProducts. Trước đây recover chỉ gọi trong OnProductsFetched; khi fetch products FAIL
            // một phần ("could not retrieve the attached subset") thì luồng recover không chạy → deferred
            // không nhận quà. FetchPurchases chỉ cần store đã connect, không cần products fetch xong.
            RecoverPendingPurchases();
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

            // Recover TRƯỚC khi log — recover là chức năng quan trọng (kéo order deferred/interrupted về
            // để grant quà), KHÔNG được phụ thuộc vào LogProductDefinitions (chỉ để debug, có thể ném
            // exception với product thiếu metadata khi fetch fail một phần → trước đây nuốt luôn recover).
            RecoverPendingPurchases();

            try
            {
                LogProductDefinitions();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[IAP] LogProductDefinitions error (bỏ qua): " + e);
            }
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

            // Chụp transactionID NGAY BÂY GIỜ — sau ConfirmPurchase, IOrderInfo.TransactionID sẽ rỗng.
            string transactionId = order.Info != null ? order.Info.TransactionID : null;

            // Idempotent guard: order này đã grant + save ở phiên trước (app chết trước ConfirmPurchase,
            // giờ store re-deliver) → CHỈ confirm lại để đóng transaction, KHÔNG grant lần hai.
            if (_ledger != null && !string.IsNullOrEmpty(transactionId) && _ledger.IsGranted(transactionId))
            {
                Debug.Log("[IAP] Order đã grant trước đó, chỉ confirm lại (chống double-grant): " + transactionId);
                callbackPay = null;
                m_StoreController.ConfirmPurchase(order);
                return;
            }

            bool validPurchase;
            try
            {
                validPurchase = ValidatePurchase(order, product);
            }
            catch (Exception e)
            {
                // Lỗi tạm thời (vd: deserialize/validator) → KHÔNG confirm, để store re-deliver lần sau
                // (tương đương PurchaseProcessingResult.Pending ở v4). Chưa grant nên không lo double-grant.
                Debug.LogError("[IAP] Validation error, leaving purchase pending: " + e);
                return;
            }

            // Khi đã quyết định finalize: ConfirmPurchase PHẢI chạy đúng 1 lần — kể cả khi grant/analytics
            // ném exception SAU khi đã grant — để transaction không bị re-deliver lần sau → double-grant.
            try
            {
                if (validPurchase)
                {
                    // THỨ TỰ QUAN TRỌNG (chống double-grant + analytics trùng):
                    // 1) Cấp quà + Save.  2) Ghi ledger "đã grant".  3) Bắn analytics.
                    // Nếu app chết giữa (1) và (2): phiên sau re-deliver → guard trên KHÔNG chặn → grant lại
                    //   (khe hẹp nhất có thể — chỉ giữa hai lần Save, không còn xen analytics).
                    // Nếu app chết giữa (2) và (3): phiên sau re-deliver → guard CHẶN → không grant lại,
                    //   analytics cũng không bắn trùng (nó nằm sau ledger).
                    var granted = GrantRewards(product);

                    if (granted && _ledger != null && !string.IsNullOrEmpty(transactionId))
                        _ledger.MarkGranted(transactionId, product.definition.id);

                    if (granted)
                        SendPurchaseAnalytics(product, order.Info);

                    Debug.Log(string.Format("[IAP] ProcessPurchase: PASS. Product: '{0}'", product.definition.id));
                }
                else
                {
                    callbackPay = null;
                    Debug.Log("[IAP] Invalid receipt, not unlocking content.");
                    // Báo cho UI biết đơn bị từ chối — trước đây im lặng, người chơi thấy như "bấm mua không ra gì".
                    _purchasing.OnPurchaseFailed?.Invoke(PurchaseFailureReason.ValidationFailure.ToString());
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
            Debug.Log(string.Format(
                "[IAP] OnPurchasesFetched. Pending: {0}, Deferred: {1}, Confirmed: {2} (restore={3}, recover={4})",
                orders.PendingOrders.Count, orders.DeferredOrders.Count, orders.ConfirmedOrders.Count,
                m_RestoreInProgress, m_PendingRecoveryFetch));

            var wasRestore = m_RestoreInProgress;
            m_RestoreInProgress = false;
            m_PendingRecoveryFetch = false;

            // Chủ động forward từng PendingOrder vào OnPurchasePending để grant + ConfirmPurchase.
            // KHÔNG dựa hoàn toàn vào ProcessPendingOrdersOnPurchasesFetched auto-route: theo report của
            // Unity (IAP v5), có trường hợp FetchPurchases không tự route pending order → deferred approved
            // không nhận được quà. Đây là workaround Unity staff khuyến nghị (tự đẩy pending order ra listener).
            // OnPurchasePending có guard idempotent theo transactionId nên forward lại KHÔNG gây double-grant.
            if (orders.PendingOrders != null)
            {
                foreach (var pending in orders.PendingOrders)
                {
                    try
                    {
                        OnPurchasePending(pending);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("[IAP] Error forwarding pending order: " + e);
                    }
                }
            }

            // Luồng Restore (user bấm nút): báo game khôi phục item.
            if (wasRestore)
            {
                _purchasing.RestoreItem();
                _purchasing.OnTransactionRestored?.Invoke(true);
            }
        }

        private void OnPurchasesFetchFailed(PurchasesFetchFailureDescription failure)
        {
            if (m_RestoreInProgress)
            {
                m_RestoreInProgress = false;
                Debug.LogError("[IAP] OnPurchasesFetchFailed (restore): " + failure.message);
                _purchasing.OnTransactionRestored?.Invoke(false);
                return;
            }

            if (m_PendingRecoveryFetch)
            {
                m_PendingRecoveryFetch = false;
                Debug.LogWarning("[IAP] OnPurchasesFetchFailed (recover): " + failure.message);
            }
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
