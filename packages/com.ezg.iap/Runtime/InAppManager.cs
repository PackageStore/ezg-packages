#if (UNITY_ANDROID || UNITY_IPHONE || UNITY_IOS) && !UNITY_EDITOR
#define RECEIPT_VALIDATION
#endif

using Ezg.Package.Singleton;
using System;
using System.Collections;
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

        // 0.3.3 — tự kết nối lại store khi chưa sẵn sàng (mở app lúc mất mạng, store rớt kết nối, fetch products lỗi).
        private bool m_InitRequested;             // game đã gọi Init() ít nhất 1 lần — trước đó KHÔNG tự connect
        private bool m_FetchingProducts;          // đang chờ OnProductsFetched / OnProductsFetchFailed
        private float m_ProductsFetchStartedAt;

        // 0.3.3 — restore chốt kết quả khi danh sách đơn đã về (Apple: SAU callback RestoreTransactions).
        private bool m_RestoreAwaitingFetch;      // kết quả FetchPurchases kế tiếp = kết quả của lượt restore
        private float m_RestoreStartedAt;
        private int m_RestoreGeneration;          // mỗi lượt restore một số — callback / watchdog của lượt cũ bị bỏ qua
        private int m_RestoreFetchResultsToSkip;  // Apple: kết quả fetch recover đã chạy TRƯỚC khi StoreKit sync xong — không tính

        // 0.3.3 — fetch purchases để recover có thể không bao giờ trả về OnPurchasesFetched (Google tự fetch khi app
        // lấy lại focus và dùng CHUNG một callback slot) → cờ m_PendingRecoveryFetch phải có hạn.
        private float m_RecoveryFetchStartedAt;

        // 0.3.3 — có PendingOrder đang được GIỮ vì catalog chưa có product (fetch purchases về trước fetch products lúc
        // mở app). Catalog về thì fetch lại để cấp.
        private bool m_OrdersAwaitingCatalog;

        // 0.3.3 — cùng một PendingOrder tới OnPurchasePending 2 lần trong một phiên (Unity tự route khi fetch
        // purchases + OnPurchasesFetched forward lại). Chỉ đơn đã CHỐT DỨT KHOÁT (đã cấp quà / ledger báo đã cấp /
        // receipt bị từ chối) mới được nhớ; lần sau chỉ confirm lại. Đơn chưa cấp được thì KHÔNG nhớ → xử lý lại như cũ.
        private enum OrderOutcome
        {
            Granted,
            AlreadyGranted,
            Rejected
        }

        private readonly Dictionary<string, OrderOutcome> m_FinalizedTransactions = new Dictionary<string, OrderOutcome>();
        private readonly Dictionary<Order, OrderOutcome> m_FinalizedOrders = new Dictionary<Order, OrderOutcome>();

        /// <summary>
        /// Apple đang chờ người chơi xác nhận Restore (mật khẩu / 2FA) — không đặt hạn chặt. Quá mốc này mà bấm Restore lần
        /// nữa thì bắt đầu lượt mới (callback của lượt cũ về muộn sẽ bị bỏ qua).
        /// </summary>
        private const float k_RestoreStaleSeconds = 180f;

        /// <summary>Đang chờ danh sách đơn cho Restore: quá mốc này thì fetch lại một lần.</summary>
        private const float k_RestoreFetchRetrySeconds = 10f;

        /// <summary>Đang chờ danh sách đơn cho Restore: quá mốc này thì báo Restore thất bại (không treo im lặng).</summary>
        private const float k_RestoreFetchTimeoutSeconds = 20f;

        /// <summary>Fetch purchases để recover không có kết quả sau mốc này thì cho phép fetch lại.</summary>
        private const float k_RecoveryFetchStaleSeconds = 30f;

        /// <summary>Fetch products treo quá lâu thì cho phép fetch lại khi tự kết nối lại.</summary>
        private const float k_FetchProductsStaleSeconds = 30f;

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
            if (paused)
            {
                return;
            }

            if (m_StoreConnected && m_ProductsFetched)
            {
                RecoverPendingPurchases();
            }
            else
            {
                // 0.3.3: mở app lúc mất mạng / store rớt kết nối → quay lại app thì tự kết nối + fetch lại,
                // không bắt người chơi khởi động lại app mới mua được.
                TryReconnectStore();
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

            m_InitRequested = true;

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
                // 0.3.3: KHÔNG gọi unSuccess ở đây — mua (giả) đã thành công. Bản cũ gọi cả callBack lẫn unSuccess.
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


            var flagSetHere = false;
            try
            {
                if (m_PurchaseInProgress == true)
                {
                    // Giữ như cũ: chỉ báo unSuccess, KHÔNG bắn OnPurchaseFailed — đơn trước vẫn đang chạy, báo
                    // "failed" sẽ làm host nhả khoá UI / hiện toast lỗi cho chính đơn đang chờ store trả lời.
                    Debug.Log("Please wait, purchase in progress");
                    unSuccess?.Invoke();
                    return;
                }

                if (m_StoreController == null)
                {
                    Debug.LogError("Purchasing is not initialized");
                    FailBeforeStore(PurchaseFailureReason.PurchasingUnavailable, unSuccess);
                    return;
                }

                if (m_StoreController.GetProductById(productID) == null)
                {
                    // Products chưa fetch xong (store chưa sẵn sàng) khác với SKU không có trên store.
                    Debug.LogError("No product has id " + productID);
                    FailBeforeStore(IsInitialized()
                        ? PurchaseFailureReason.ProductUnavailable
                        : PurchaseFailureReason.PurchasingUnavailable, unSuccess);
                    return;
                }

                m_PurchaseInProgress = true;
                flagSetHere = true;
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
                    return;
                }

    #if UNITY_EDITOR
                _purchasing.OnPurchaseCompleteBeforeCallback?.Invoke(productId);
                callBack?.Invoke();
                m_PurchaseInProgress = false;
                _purchasing.OnPurchaseComplete?.Invoke(productId);
                _reporter?.RequestSync();
    #else
                BuyProductID(productID, unSuccess);
    #endif
            }
            catch
                (Exception e)
            {
                Debug.LogError(e);

                // 0.3.3: lỗi bất ngờ SAU khi chính lượt này đã bật cờ (vd PurchaseProduct ném) → bản cũ để cờ "đang mua"
                // kẹt tới khi tắt app, mọi lần bấm sau đều bị chặn im lặng. Giờ nhả cờ + báo lỗi. Cờ do đơn KHÁC bật
                // (nhánh "purchase in progress") thì không đụng tới.
                if (flagSetHere && m_PurchaseInProgress)
                {
                    FailBeforeStore(PurchaseFailureReason.Unknown, unSuccess);
                }
            }
        }

        /// <summary>
        /// Khôi phục giao dịch (nút Restore). Kết quả về qua <see cref="IPurchasing.RestoreItem"/> (khi thành công) rồi
        /// <see cref="IPurchasing.OnTransactionRestored"/> — hoặc <see cref="IIapRestoreListener.OnRestoreCompleted"/> nếu
        /// host implement interface đó. Kết quả luôn được báo SAU khi danh sách đơn của store đã về.
        /// </summary>
        public void RestorePurchases()
        {
            try
            {
                if (m_RestoreInProgress && !IsRestoreStale())
                {
                    Debug.Log("[IAP] RestorePurchases: đang restore, bỏ qua lần gọi trùng.");
                    return;
                }

                // Lượt mới: callback / watchdog của lượt trước (nếu còn treo) bị bỏ qua nhờ số lượt.
                var generation = ++m_RestoreGeneration;
                m_RestoreInProgress = false;
                m_RestoreAwaitingFetch = false;
                m_RestoreFetchResultsToSkip = 0;

                // If Purchasing has not yet been set up ...
                if (!IsInitialized())
                {
                    // 0.3.3: bản cũ chỉ log rồi return → nút Restore bấm không ra gì. Giờ báo thất bại + thử kết nối lại.
                    Debug.Log("[IAP] RestorePurchases FAIL. Not initialized.");
                    TryReconnectStore();
                    ReportRestoreResult(false, null);
                    return;
                }

                m_RestoreInProgress = true;
                m_RestoreStartedAt = Time.realtimeSinceStartup;

                if (m_IsAppleStoreSelected)
                {
                    // Apple: StoreKit restore qua callback. Unity gọi FetchPurchases() rồi mới gọi callback → kết quả
                    // chốt ở OnPurchasesFetched kế tiếp SAU callback (xem OnTransactionsRestored).
                    m_StoreController.RestoreTransactions((success, error) =>
                        OnTransactionsRestored(generation, success, error));
                }
                else
                {
                    // Google Play (và store khác): entitlement được khôi phục bằng cách fetch purchases.
                    BeginRestoreFetchWait(generation);
                    m_StoreController.FetchPurchases();
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                if (m_RestoreInProgress)
                {
                    m_RestoreInProgress = false;
                    m_RestoreAwaitingFetch = false;
                    FinishRestore(false, null);
                }
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
            m_FetchingProducts = true;
            m_ProductsFetchStartedAt = Time.realtimeSinceStartup;
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

            // 0.3.3: kết quả fetch có thể không bao giờ về OnPurchasesFetched (Google tự fetch khi app lấy lại focus,
            // dùng chung một callback slot) → bản cũ để cờ này kẹt và không bao giờ recover lại trong phiên.
            if (m_PendingRecoveryFetch &&
                Time.realtimeSinceStartup - m_RecoveryFetchStartedAt > k_RecoveryFetchStaleSeconds)
            {
                Debug.LogWarning("[IAP] Recover fetch không có kết quả sau " + k_RecoveryFetchStaleSeconds + "s → fetch lại.");
                m_PendingRecoveryFetch = false;
            }

            // Đang chờ danh sách đơn cho Restore thì fetch đó đã đủ (kết quả của nó cũng forward pending order).
            // Restore của Apple còn ở bước chờ người chơi xác nhận thì KHÔNG chặn: fetch lúc đó xử lý như fetch thường.
            if ((m_RestoreInProgress && m_RestoreAwaitingFetch) || m_PendingRecoveryFetch)
            {
                return;
            }

            m_PendingRecoveryFetch = true;
            m_RecoveryFetchStartedAt = Time.realtimeSinceStartup;
            m_OrdersAwaitingCatalog = false; // fetch này sẽ đưa đơn đang giữ tới lần nữa
            Debug.Log("[IAP] Recover pending purchases...");
            m_StoreController.FetchPurchases();
        }

        void BuyProductID(string productId, Action unSuccess)
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
                    return;
                }

                // 0.3.3: bản cũ chỉ log ở 2 nhánh lỗi dưới → cờ "đang mua" (bật ở Buy) không bao giờ được nhả,
                // không callback nào được gọi: nút mua treo + mọi lần bấm sau bị chặn "purchase in progress".
                Debug.Log(
                    "BuyProductID: FAIL. Not purchasing product, either is not found or is not available for purchase");
                FailBeforeStore(PurchaseFailureReason.ProductUnavailable, unSuccess);
                return;
            }

            // ... report the fact Purchasing has not succeeded initializing yet. FailBeforeStore thử kết nối lại
            // để lần bấm sau mua được khi mạng / store đã về.
            Debug.Log("BuyProductID FAIL. Not initialized.");
            FailBeforeStore(PurchaseFailureReason.PurchasingUnavailable, unSuccess);
        }

        /// <summary>
        /// Lượt mua bị từ chối TRƯỚC khi tới store (store chưa sẵn sàng / SKU không có / lỗi bất ngờ): nhả cờ đang mua,
        /// báo <paramref name="unSuccess"/> của caller VÀ <see cref="IPurchasing.OnPurchaseFailed"/> (host hiện toast,
        /// tracking, nhả khoá UI) — bản cũ im lặng. Store chưa sẵn sàng thì thử kết nối lại. Không bao giờ ném ra ngoài.
        /// KHÔNG dùng cho nhánh "đơn khác đang chạy" (đơn đó vẫn còn sống).
        /// </summary>
        private void FailBeforeStore(PurchaseFailureReason reason, Action unSuccess)
        {
            m_PurchaseInProgress = false;
            callbackPay = null;

            Debug.Log("[IAP] Purchase rejected before reaching the store: " + reason);

            if (!IsInitialized())
            {
                TryReconnectStore();
            }

            try
            {
                unSuccess?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }

            try
            {
                _purchasing?.OnPurchaseFailed?.Invoke(reason.ToString());
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }

        /// <summary>
        /// Kéo store về trạng thái sẵn sàng mua: chưa connect → connect lại; connect rồi mà products chưa fetch được →
        /// fetch lại. Chỉ chạy sau khi game đã gọi <see cref="Init"/> (không tự connect trước khi game muốn).
        /// </summary>
        private void TryReconnectStore()
        {
            if (!m_InitRequested || _purchasing == null || _config == null || m_StoreController == null)
            {
                return;
            }

            try
            {
                if (!m_StoreConnected)
                {
                    ConnectStore();
                }
                else if (!m_ProductsFetched &&
                         (!m_FetchingProducts ||
                          Time.realtimeSinceStartup - m_ProductsFetchStartedAt > k_FetchProductsStaleSeconds))
                {
                    FetchProducts();
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[IAP] Reconnect store failed: " + e);
            }
        }

        private bool IsRestoreStale()
        {
            return Time.realtimeSinceStartup - m_RestoreStartedAt > k_RestoreStaleSeconds;
        }

        /// <summary>
        /// Lượt restore chuyển sang bước chờ danh sách đơn: kết quả FetchPurchases kế tiếp là kết quả của lượt này. Có
        /// watchdog vì kết quả fetch có thể bị Unity chuyển sang callback khác (Google tự fetch khi app lấy lại focus).
        /// </summary>
        private void BeginRestoreFetchWait(int generation)
        {
            m_RestoreAwaitingFetch = true;
            if (!isActiveAndEnabled)
            {
                // StartCoroutine trên object tắt không ném lỗi mà chỉ log → không có watchdog. Singleton luôn active.
                Debug.LogWarning("[IAP] InAppManager không active → restore không có watchdog.");
                return;
            }

            try
            {
                StartCoroutine(RestoreFetchWatchdog(generation));
            }
            catch (Exception e)
            {
                Debug.LogError("[IAP] Không chạy được watchdog restore: " + e);
            }
        }

        private bool IsAwaitingRestoreFetch(int generation)
        {
            return generation == m_RestoreGeneration && m_RestoreInProgress && m_RestoreAwaitingFetch;
        }

        private IEnumerator RestoreFetchWatchdog(int generation)
        {
            yield return new WaitForSecondsRealtime(k_RestoreFetchRetrySeconds);
            if (!IsAwaitingRestoreFetch(generation))
            {
                yield break;
            }

            Debug.LogWarning("[IAP] Restore: chưa có danh sách đơn sau " + k_RestoreFetchRetrySeconds + "s → fetch lại.");
            try
            {
                m_StoreController?.FetchPurchases();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }

            yield return new WaitForSecondsRealtime(k_RestoreFetchTimeoutSeconds - k_RestoreFetchRetrySeconds);
            if (!IsAwaitingRestoreFetch(generation))
            {
                yield break;
            }

            Debug.LogWarning("[IAP] Restore: không nhận được danh sách đơn → báo thất bại.");
            m_RestoreInProgress = false;
            m_RestoreAwaitingFetch = false;
            FinishRestore(false, null);
        }

        /// <summary>
        /// Chốt một lượt restore: thành công → <see cref="IPurchasing.RestoreItem"/> rồi báo kết quả. Caller tự hạ cờ
        /// m_RestoreInProgress / m_RestoreAwaitingFetch TRƯỚC khi gọi (host có thể bắt đầu lượt restore mới ngay trong callback).
        /// </summary>
        private void FinishRestore(bool success, List<string> restoredProductIds)
        {
            if (success && _purchasing != null)
            {
                try
                {
                    _purchasing.RestoreItem();
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
            }

            ReportRestoreResult(success, restoredProductIds);
        }

        /// <summary>
        /// Host implement <see cref="IIapRestoreListener"/> → nhận kết quả kèm danh sách product (THAY cho
        /// OnTransactionRestored). Host cũ → OnTransactionRestored(success) như trước.
        /// </summary>
        private void ReportRestoreResult(bool success, List<string> restoredProductIds)
        {
            if (_purchasing == null)
            {
                return;
            }

            try
            {
                if (_purchasing is IIapRestoreListener listener)
                {
                    listener.OnRestoreCompleted(success,
                        (IReadOnlyList<string>)restoredProductIds ?? Array.Empty<string>());
                }
                else
                {
                    _purchasing.OnTransactionRestored?.Invoke(success);
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }

        /// <summary>
        /// Product mà lượt restore trả về: non-consumable / subscription người chơi đang sở hữu (đơn confirmed) + đơn
        /// pending vừa được cấp quà trong phiên này. Rỗng = không có gì để khôi phục.
        /// </summary>
        private List<string> CollectRestoredProductIds(Orders orders)
        {
            var ids = new List<string>();
            if (orders == null)
            {
                return ids;
            }

            if (orders.ConfirmedOrders != null)
            {
                foreach (var confirmed in orders.ConfirmedOrders)
                {
                    AddOrderProductIds(confirmed, ids, true);
                }
            }

            if (orders.PendingOrders != null)
            {
                foreach (var pending in orders.PendingOrders)
                {
                    if (HasBeenGranted(pending))
                    {
                        AddOrderProductIds(pending, ids, false);
                    }
                }
            }

            return ids;
        }

        private static void AddOrderProductIds(Order order, List<string> ids, bool durableOnly)
        {
            var items = order?.CartOrdered?.Items();
            if (items == null)
            {
                return;
            }

            foreach (var item in items)
            {
                var definition = item?.Product?.definition;
                if (definition == null || string.IsNullOrEmpty(definition.id))
                {
                    continue;
                }

                if (durableOnly && definition.type == ProductType.Consumable)
                {
                    continue;
                }

                if (!ids.Contains(definition.id))
                {
                    ids.Add(definition.id);
                }
            }
        }

        /// <summary>Đơn đã được cấp quà (trong phiên này, hoặc ledger báo đã cấp ở phiên trước).</summary>
        private bool HasBeenGranted(PendingOrder order)
        {
            if (order == null)
            {
                return false;
            }

            OrderOutcome outcome;
            if (m_FinalizedOrders.TryGetValue(order, out outcome))
            {
                return outcome != OrderOutcome.Rejected;
            }

            var transactionId = order.Info != null ? order.Info.TransactionID : null;
            return !string.IsNullOrEmpty(transactionId) &&
                   m_FinalizedTransactions.TryGetValue(transactionId, out outcome) &&
                   outcome != OrderOutcome.Rejected;
        }

        private void MarkFinalized(Order order, string transactionId, OrderOutcome outcome)
        {
            if (order != null)
            {
                m_FinalizedOrders[order] = outcome;
            }

            if (!string.IsNullOrEmpty(transactionId))
            {
                m_FinalizedTransactions[transactionId] = outcome;
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

            // 0.3.3: callback UI ném lỗi (vd đụng object UI đã huỷ) không được làm mất bước cấp quà bên dưới — bản cũ
            // bỏ qua OnPurchaseComplete nhưng finally vẫn ConfirmPurchase → người chơi trả tiền mà không có quà.
            try
            {
                _purchasing.OnPurchaseCompleteBeforeCallback?.Invoke(productId);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }

            var uiCallback = callbackPay;
            callbackPay = null;
            try
            {
                uiCallback?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }

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
            m_Connecting = false;
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
            // 0.3.3: Unity báo connect thất bại bằng event này (task Connect() vẫn hoàn tất bình thường, không fault)
            // → bản cũ để m_Connecting = true mãi, ConnectStore()/Init() không bao giờ kết nối lại được trong phiên.
            m_Connecting = false;
            m_FetchingProducts = false;
            Debug.Log("[IAP] OnStoreDisconnected: " + description.message);
        }

        private void OnProductsFetched(List<Product> products)
        {
            m_ProductsFetched = true;
            m_FetchingProducts = false;
            Debug.Log("[IAP] OnProductsFetched: " + products.Count);

            if (m_OrdersAwaitingCatalog)
            {
                // Có đơn đang giữ vì catalog chưa về → fetch lại ngay (không chờ lượt recover đang treo, nếu có).
                // Cờ chỉ hạ khi fetch thực sự được gọi (RecoverPendingPurchases) hoặc khi danh sách đơn kế tiếp về.
                m_PendingRecoveryFetch = false;
            }

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
            // Fetch lỗi một phần (vài SKU chưa có trên store) thì OnProductsFetched vẫn đã chạy cho phần lấy được.
            m_FetchingProducts = false;
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

            // 0.3.3 — chống xử lý trùng trong phiên. Khi fetch purchases, Unity tự route mỗi PendingOrder vào đây, rồi
            // OnPurchasesFetched forward lại ĐÚNG instance đó: bản cũ chỉ còn ledger của host chặn cấp quà lần hai, còn
            // đơn bị từ chối thì báo OnPurchaseFailed 2 lần (2 toast lỗi).
            if (m_FinalizedOrders.ContainsKey(order))
            {
                // Cùng instance vừa chốt (cùng một lần fetch) → ConfirmPurchase đã gọi rồi, bỏ qua.
                return;
            }

            if (!string.IsNullOrEmpty(transactionId) &&
                m_FinalizedTransactions.TryGetValue(transactionId, out var earlierOutcome))
            {
                // Store giao lại đơn đã chốt dứt khoát trong phiên (confirm lần trước chưa xong) → chỉ confirm lại.
                Debug.Log("[IAP] Order đã xử lý trong phiên này, chỉ confirm lại: " + transactionId);
                m_FinalizedOrders[order] = earlierOutcome;
                m_StoreController.ConfirmPurchase(order);
                return;
            }

            // Idempotent guard: order này đã grant + save ở phiên trước (app chết trước ConfirmPurchase,
            // giờ store re-deliver) → CHỈ confirm lại để đóng transaction, KHÔNG grant lần hai.
            if (_ledger != null && !string.IsNullOrEmpty(transactionId) && _ledger.IsGranted(transactionId))
            {
                Debug.Log("[IAP] Order đã grant trước đó, chỉ confirm lại (chống double-grant): " + transactionId);
                callbackPay = null;
                MarkFinalized(order, transactionId, OrderOutcome.AlreadyGranted);
                m_StoreController.ConfirmPurchase(order);
                return;
            }

            // 0.3.3: catalog chưa có product này (lúc mở app, fetch purchases thường về TRƯỚC fetch products; Google dựng
            // product kiểu Unknown) → bản cũ vẫn ConfirmPurchase mà GrantRewards trả false: người chơi trả tiền, không có
            // quà (và với kiểu Unknown, Google chỉ acknowledge chứ không consume). Giờ GIỮ đơn ở pending; catalog về thì
            // OnProductsFetched fetch lại → đơn tới lần nữa với đúng kiểu product → cấp quà + confirm.
            if (m_StoreController.GetProductById(product.definition.id) == null)
            {
                Debug.LogWarning("[IAP] Catalog chưa có product '" + product.definition.id +
                                 "' → giữ order pending, cấp khi catalog đã fetch.");
                m_OrdersAwaitingCatalog = true;
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
            var granted = false;
            var rejected = false;
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
                    granted = GrantRewards(product);

                    if (granted && _ledger != null && !string.IsNullOrEmpty(transactionId))
                        _ledger.MarkGranted(transactionId, product.definition.id);

                    if (granted)
                        SendPurchaseAnalytics(product, order.Info);

                    Debug.Log(string.Format("[IAP] ProcessPurchase: PASS. Product: '{0}'", product.definition.id));
                }
                else
                {
                    rejected = true;
                    callbackPay = null;
                    Debug.Log("[IAP] Invalid receipt, not unlocking content.");
                    // Báo cho UI biết đơn bị từ chối — trước đây im lặng, người chơi thấy như "bấm mua không ra gì".
                    _purchasing.OnPurchaseFailed?.Invoke(PurchaseFailureReason.ValidationFailure.ToString());
                }
            }
            finally
            {
                // Chỉ nhớ đơn đã chốt dứt khoát. GrantRewards trả false / ném lỗi → KHÔNG nhớ, để lần giao sau được xử lý
                // lại như bản cũ (đơn không bao giờ bị "confirm-only" khi quà chưa tới tay người chơi).
                if (granted)
                {
                    MarkFinalized(order, transactionId, OrderOutcome.Granted);
                }
                else if (rejected)
                {
                    MarkFinalized(order, transactionId, OrderOutcome.Rejected);
                }

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

                // 0.3.3: nhả cờ TRƯỚC khi gọi host — handler của host ném lỗi thì cờ không bị kẹt cả phiên.
                callbackPay = null;
                m_PurchaseInProgress = false;
                _purchasing.OnPurchaseFailed?.Invoke(order.FailureReason.ToString());
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

            // Lượt restore chỉ chốt ở lần fetch "của nó": Google = fetch do RestorePurchases gọi; Apple = fetch SAU
            // callback RestoreTransactions. Fetch về trước đó (vd recover lúc app quay lại) xử lý như fetch thường.
            var wasRestore = m_RestoreInProgress && m_RestoreAwaitingFetch;
            if (wasRestore && m_RestoreFetchResultsToSkip > 0)
            {
                // Apple: kết quả của fetch recover đã chạy từ trước khi StoreKit sync xong → xử lý như fetch thường.
                m_RestoreFetchResultsToSkip--;
                wasRestore = false;
            }

            if (wasRestore)
            {
                m_RestoreInProgress = false;
                m_RestoreAwaitingFetch = false;
            }

            m_PendingRecoveryFetch = false;
            m_OrdersAwaitingCatalog = false; // forward bên dưới giữ lại (và bật cờ lại) nếu catalog vẫn chưa có

            // Chủ động forward từng PendingOrder vào OnPurchasePending để grant + ConfirmPurchase.
            // KHÔNG dựa hoàn toàn vào ProcessPendingOrdersOnPurchasesFetched auto-route: theo report của
            // Unity (IAP v5), có trường hợp FetchPurchases không tự route pending order → deferred approved
            // không nhận được quà. Đây là workaround Unity staff khuyến nghị (tự đẩy pending order ra listener).
            // Order Unity đã route trong cùng lần fetch bị OnPurchasePending bỏ qua (chống trùng trong phiên),
            // đơn của phiên trước thì ledger của host chặn → forward lại KHÔNG gây double-grant.
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

            // Luồng Restore (user bấm nút): báo game khôi phục item — lúc này entitlement đã về đủ.
            if (wasRestore)
            {
                FinishRestore(true, CollectRestoredProductIds(orders));
            }
        }

        private void OnPurchasesFetchFailed(PurchasesFetchFailureDescription failure)
        {
            if (m_RestoreInProgress && m_RestoreAwaitingFetch && m_RestoreFetchResultsToSkip > 0)
            {
                // Lỗi của fetch recover cũ (Apple) — không phải của lượt restore.
                m_RestoreFetchResultsToSkip--;
            }
            else if (m_RestoreInProgress && m_RestoreAwaitingFetch)
            {
                m_RestoreInProgress = false;
                m_RestoreAwaitingFetch = false;
                Debug.LogError("[IAP] OnPurchasesFetchFailed (restore): " + failure.message);
                FinishRestore(false, null);
                return;
            }

            if (m_PendingRecoveryFetch)
            {
                m_PendingRecoveryFetch = false;
                Debug.LogWarning("[IAP] OnPurchasesFetchFailed (recover): " + failure.message);
            }
        }

        private void OnTransactionsRestored(int generation, bool success, string error)
        {
            Debug.Log("Transactions restored." + success + (string.IsNullOrEmpty(error) ? "" : " Error: " + error));

            if (generation != m_RestoreGeneration || !m_RestoreInProgress || m_RestoreAwaitingFetch)
            {
                // Callback của lượt restore cũ (đã có lượt mới) hoặc lặp lại — bỏ qua.
                return;
            }

            if (!success)
            {
                m_RestoreInProgress = false;
                m_RestoreAwaitingFetch = false;
                FinishRestore(false, null);
                return;
            }

            // 0.3.3: bản cũ gọi RestoreItem + báo kết quả NGAY tại đây — lúc danh sách đơn khôi phục CHƯA về (Unity gọi
            // FetchPurchases() rồi mới gọi callback này) → game đọc quyền sở hữu cũ, báo "restored" dù chưa có gì.
            // Giờ chốt ở OnPurchasesFetched / OnPurchasesFetchFailed kế tiếp. Tự fetch thêm 1 lần để chắc chắn có một
            // lần fetch về SAU thời điểm này (không phụ thuộc thứ tự nội bộ của Unity).
            // Fetch recover chạy từ lúc app quay lại (vd sau hộp thoại Apple ID) có thể còn đang chờ: danh sách của nó lấy
            // TRƯỚC khi StoreKit sync xong → không được tính là kết quả restore.
            m_RestoreFetchResultsToSkip = m_PendingRecoveryFetch ? 1 : 0;
            BeginRestoreFetchWait(generation);
            try
            {
                m_StoreController.FetchPurchases();
            }
            catch (Exception e)
            {
                Debug.LogError("[IAP] FetchPurchases after restore failed: " + e);
            }
        }

        #endregion
    }
}
