using System;
using System.Collections;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using Unity.Advertisement.IosSupport;
#endif

namespace Ezg.Package.AdsManager
{
    /// <summary>
    /// Adapter quảng cáo dùng AppLovin MAX SDK.
    /// Chỉ khởi tạo/đăng ký callback cho những format được bật qua <see cref="Initialize" />
    /// (xem <see cref="AdsConfig.EnabledFormats" />) — format không bật thì SDK không tạo, không load.
    /// Toàn bộ MAX-specific code được guard bằng #if MEDIATION_MAX.
    /// </summary>
    public class MaxAdsvertising : IAdProvider, IBannerAds, IInterstitialAds, IRewardedAds, IMRecAds,
        IRemoteConfigAdvertising
    {
        #region Fields

        // Key extra parameter của AppLovin để bật/tắt consent flow (CMP).
        private const string EXTRA_PARAM_CONSENT_FLOW_ENABLED = "consent_flow_enabled";

        public event Action OnBannerLoaded;
        public event Action OnBannerFailed;

        private IAdsTracker _tracker = new NullAdsTracker();
        private Func<int> _currentLevelProvider = () => int.MaxValue;
        private AdFormats _formats = AdFormats.None;

        private Action finishVideo;
        private Action skipVideo;
        private Action closeVideo;
        private Action failVideo;

        /// <summary>
        /// Chờ bao lâu (giây, realtime) sau <c>OnAdHiddenEvent</c> rồi mới kết luận "đóng trước khi nhận
        /// thưởng". Thứ tự reward / hidden tới Unity KHÔNG cố định: plugin MAX đẩy từng event qua một
        /// thread pool (Android: ScheduledThreadPoolExecutor 3 luồng, iOS: dispatch_async lên global
        /// queue) nên reward có thể tới sau hidden dù SDK gốc gọi đúng thứ tự. Gọi onClose ngay lúc
        /// hidden thì người chơi xem trọn ad mà có lúc vẫn mất thưởng. 2 s = bằng khoảng chờ các game đang tự
        /// làm (S004 / S001 LilyAdService), để onClose của package không chốt sớm hơn watchdog của game.
        /// </summary>
        private const float LATE_REWARD_GRACE_SECONDS = 2f;

        /// <summary>
        /// Một lượt show đã gửi cho MAX mà chưa hiện / chưa lỗi: lệnh show thứ hai trong khoảng này (bấm đúp)
        /// TIẾP QUẢN ad đang mở như 0.3.1 thay vì gọi MAX lần nữa hay trả onFail. Có hạn để một event bị
        /// nuốt không khiến mọi lượt sau dính vào một ad đã chết.
        /// </summary>
        private const float SHOW_REQUEST_GUARD_SECONDS = 3f;

        /// <summary>
        /// MonoBehaviour chạy coroutine chờ reward trễ (AdsManager, DontDestroyOnLoad). Null thì hidden
        /// quyết định ngay, không có khoảng chờ.
        /// </summary>
        private readonly MonoBehaviour _coroutineHost;

        /// <summary> Id lượt rewarded gần nhất đã gửi cho MAX — tăng mỗi lần show thật. </summary>
        private int _rewardShowId;

        /// <summary> Id lượt rewarded gần nhất đã nhận <c>OnAdReceivedRewardEvent</c>. </summary>
        private int _rewardEarnedShowId = -1;

        /// <summary> Mốc realtime lúc gửi lệnh show; <c>NegativeInfinity</c> khi ad đã hiện / lỗi / đóng. </summary>
        private float _rewardRequestedAt = float.NegativeInfinity;

        private Action closeInter;
        private Action failInter;

        /// <summary>
        /// Placement của lượt interstitial gần nhất. Tách riêng khỏi rewarded vì revenue callback của
        /// format này không được mượn placement của format kia (trước 0.3.1 dùng chung một field).
        /// </summary>
        private string _interstitialPlacement;

        private string sourceRewardAds;

        public string AppKey => MediationConstant.Max.SdkKey;

        public bool IsShowReward { get; set; }

        /// <summary>
        /// Giãn cách mặc định khi host không cấu hình <see cref="TimeDelayShowInterstitialAds" />.
        /// Có số thật thay vì 0 để một game chưa gắn remote config vẫn có sàn chống hai ad dính nhau.
        /// </summary>
        private const int DEFAULT_INTERSTITIAL_DELAY_SECONDS = 30;

        /// <summary>
        /// Mốc (realtime) của fullscreen ad gần nhất — mọi lệnh <c>CanShowInterstitial = false</c>
        /// đóng dấu vào đây. <c>NegativeInfinity</c> = chưa có ad nào, interstitial đầu tiên đi ngay.
        /// </summary>
        private float _lastFullscreenAdAt = float.NegativeInfinity;

        /// <summary>
        /// Đã hết giãn cách sau fullscreen ad gần nhất hay chưa.
        /// <para>
        /// Setter CHỈ nhận <c>false</c> (= "vừa có ad, đếm lại"); gán <c>true</c> là NO-OP CÓ CHỦ Ý.
        /// Trước 0.3.0 cờ này bật <c>true</c> đúng một lần lúc init rồi bị tắt ở mọi callback ad mà
        /// không ai mở lại, nên host buộc phải tự force <c>true</c> trước mỗi lần show — và cú force
        /// đó xoá luôn phần chặn "vừa xem rewarded thì không được dính interstitial ngay". Giãn cách
        /// là bất biến của module, không phải thứ tắt được bằng một phép gán.
        /// </para>
        /// </summary>
        public bool CanShowInterstitial
        {
            get => Time.realtimeSinceStartup - _lastFullscreenAdAt >= TimeDelayShowInterstitialAds;
            set
            {
                if (!value) _lastFullscreenAdAt = Time.realtimeSinceStartup;
            }
        }

        /// <summary>
        /// Giãn cách tối thiểu (giây) giữa một fullscreen ad và interstitial kế tiếp.
        /// </summary>
        public int TimeDelayShowInterstitialAds { get; set; } = DEFAULT_INTERSTITIAL_DELAY_SECONDS;
        public bool IsShowInterstitialAds { get; set; }
        public int ShowInterstitialAdsFromLevel { get; set; }
        public bool IsShowBannerAds { get; set; }
        public int ShowBannerAdsFromLevel { get; set; }

        private bool isInit;
        private bool isBannerReady;

        private Vector2 referenceResolution = new Vector2(1080, 2400);
        private float referenceBannerWidth = 1080f;

        #endregion

        #region Constructors

        /// <summary> Tạo adapter MAX. </summary>
        /// <param name="coroutineHost">MonoBehaviour sống suốt session để chạy khoảng chờ reward trễ của
        /// rewarded (AdsManager truyền chính nó). Null thì onClose được quyết định ngay lúc hidden.</param>
        public MaxAdsvertising(MonoBehaviour coroutineHost = null)
        {
            _coroutineHost = coroutineHost;
        }

        #endregion

        #region Public Methods

        /// <summary> Format này có được project bật hay không. </summary>
        /// <param name="format">Format cần kiểm tra.</param>
        /// <returns>True nếu format đang bật.</returns>
        public bool Supports(AdFormats format) => (_formats & format) != 0;

        /// <summary>
        /// Inject tracker analytics, hàm lấy level hiện tại và tập format được bật, rồi khởi tạo MAX SDK.
        /// Chỉ những format nằm trong <paramref name="formats" /> mới được tạo/đăng ký callback.
        /// </summary>
        /// <param name="formats">Bitmask format mà project bật.</param>
        /// <param name="tracker">Tracker để ghi nhận sự kiện quảng cáo.</param>
        /// <param name="currentLevelProvider">Hàm trả về level hiện tại của người chơi.</param>
        public void Initialize(AdFormats formats, IAdsTracker tracker, Func<int> currentLevelProvider)
        {
            _formats = formats;
            if (tracker != null) _tracker = tracker;
            if (currentLevelProvider != null) _currentLevelProvider = currentLevelProvider;
            InitAds();
        }

        /// <summary>
        /// Đăng ký MAX SDK callbacks cho những format đang bật. Gọi sau khi MAX SDK init xong.
        /// </summary>
        public void InitEvent()
        {
    #if MEDIATION_MAX

            isBannerReady = false;

            if (isInit)
            {
                return;
            }

            isInit = true;

            #region Reward Video

            if (Supports(AdFormats.Rewarded))
            {
                MaxSdkCallbacks.Rewarded.OnAdLoadedEvent += OnRewardedAdLoadedEvent;
                MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent += OnRewardedAdLoadFailedEvent;
                MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent += OnRewardedAdDisplayedEvent;
                MaxSdkCallbacks.Rewarded.OnAdClickedEvent += OnRewardedAdClickedEvent;
                MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent += OnRewardedAdRevenuePaidEvent;
                MaxSdkCallbacks.Rewarded.OnAdHiddenEvent += OnRewardedAdHiddenEvent;
                MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += OnRewardedAdFailedToDisplayEvent;
                MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += OnRewardedAdReceivedRewardEvent;

                // Load the first rewarded ad
                LoadRewardAds();
            }

            #endregion

            #region Interstitial

            if (Supports(AdFormats.Interstitial))
            {
                MaxSdkCallbacks.Interstitial.OnAdLoadedEvent += OnInterstitialLoadedEvent;
                MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += OnInterstitialLoadFailedEvent;
                MaxSdkCallbacks.Interstitial.OnAdDisplayedEvent += OnInterstitialDisplayedEvent;
                MaxSdkCallbacks.Interstitial.OnAdClickedEvent += OnInterstitialClickedEvent;
                MaxSdkCallbacks.Interstitial.OnAdHiddenEvent += OnInterstitialHiddenEvent;
                MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += OnInterstitialAdFailedToDisplayEvent;
                MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent += OnAdRevenuePaidEventInterstitial;

                LoadInterstitial();
            }

            #endregion

            #region Banner

            if (Supports(AdFormats.Banner))
            {
                MaxSdkCallbacks.Banner.OnAdLoadedEvent += OnBannerAdLoadedEvent;
                MaxSdkCallbacks.Banner.OnAdLoadFailedEvent += OnBannerAdLoadFailedEvent;
                MaxSdkCallbacks.Banner.OnAdClickedEvent += OnBannerAdClickedEvent;
                MaxSdkCallbacks.Banner.OnAdRevenuePaidEvent += OnBannerAdRevenuePaidEvent;
                MaxSdkCallbacks.Banner.OnAdExpandedEvent += OnBannerAdExpandedEvent;
                MaxSdkCallbacks.Banner.OnAdCollapsedEvent += OnBannerAdCollapsedEvent;
                MaxSdkCallbacks.Banner.OnAdRevenuePaidEvent += OnAdRevenuePaidEventBanner;
            }

            #endregion

            _tracker.OnAdsInitialized();
    #endif
        }

        /// <summary>
        /// Khởi tạo MAX SDK và thiết lập SDK key. Banner chỉ được tạo nếu format Banner đang bật.
        /// </summary>
        public void InitAds()
        {
    #if MEDIATION_MAX
            MaxSdkCallbacks.OnSdkInitializedEvent += (MaxSdkBase.SdkConfiguration sdkConfiguration) => { InitEvent(); };

            MaxSdk.SetSdkKey(AppKey);
            //MaxSdk.SetUserId("USER_ID");

            // Phải gọi TRƯỚC InitializeSdk — set sau khi init thì extra parameter không còn tác dụng.
            DisableConsentFlowIfAttDenied();

            MaxSdk.InitializeSdk();

            if (Supports(AdFormats.Banner))
            {
                MaxSdk.CreateBanner(MediationConstant.Max.BannerStringId, MaxSdkBase.BannerPosition.BottomCenter);
            }

            // float currentWidth = Screen.width;
            // float referenceWidth = referenceResolution.x;
            // float scaleFactor = currentWidth / referenceWidth;
            //
            // float adjustedBannerWidth = referenceBannerWidth * scaleFactor;
            // adjustedBannerWidth = Mathf.Max(adjustedBannerWidth, 320f);
            //
            // MaxSdk.SetBannerWidth(MediationConstant.Max.BannerStringId, adjustedBannerWidth);
            //
            // float adaptiveHeight = MaxSdkUtils.GetAdaptiveBannerHeight(adjustedBannerWidth);
            // Debug.Log("Adaptive Banner Height: " + adaptiveHeight);
            // MaxSdk.SetBannerExtraParameter(MediationConstant.Max.BannerStringId, "adaptive_banner", "false");
            //
            // MaxSdk.SetBannerBackgroundColor(MediationConstant.Max.BannerStringId, new Color(1f, 1f, 1f, 0f));

    #endif
        }

        /// <summary>
        /// Kiểm tra xem rewarded video có sẵn sàng không (chỉ khi có kết nối mạng).
        /// </summary>
        /// <returns>True nếu rewarded ad đã sẵn sàng.</returns>
        public bool IsReadyVideoAds()
        {
            if (!Supports(AdFormats.Rewarded)) return false;

            if (Application.internetReachability == NetworkReachability.ReachableViaLocalAreaNetwork ||
                Application.internetReachability == NetworkReachability.ReachableViaCarrierDataNetwork)
            {
    #if MEDIATION_MAX
                return MaxSdk.IsRewardedAdReady(MediationConstant.Max.RewardedStringId);
    #endif
            }

            return false;
        }

        /// <summary>
        /// Load rewarded ad từ MAX SDK.
        /// </summary>
        public void LoadRewardAds()
        {
            if (!Supports(AdFormats.Rewarded)) return;
    #if MEDIATION_MAX
            MaxSdk.LoadRewardedAd(MediationConstant.Max.RewardedStringId);
    #endif
        }

        /// <summary>
        /// Hiển thị rewarded video. Mỗi lượt gọi kết thúc bằng ĐÚNG MỘT trong ba callback:
        /// onFinish (nhận thưởng), onClose (đóng trước khi nhận thưởng) hoặc onFail (không chiếu được).
        /// Trên Editor sẽ invoke onFinish ngay lập tức (MAX SDK không serve ad thật ngoài device).
        /// Muốn chủ động chạy chế độ auto-nhận-thưởng thì bật debugAds trong AdsConfig
        /// — khi đó <see cref="DebugAdsProvider" /> thay thế adapter này hoàn toàn.
        /// </summary>
        /// <param name="onFinish">Callback khi xem xong và nhận thưởng.</param>
        /// <param name="onClose">Callback khi người chơi đóng ad trước khi nhận thưởng.</param>
        /// <param name="onFail">Callback khi ad chưa sẵn sàng hoặc hiển thị thất bại.</param>
        /// <param name="source">Placement/source định danh nơi gọi ad.</param>
        public void ShowRewardVideo(Action onFinish = null, Action onClose = null, Action onFail = null,
            string source = null)
        {
            if (!Supports(AdFormats.Rewarded))
            {
                onFail?.Invoke();
                return;
            }

    #if UNITY_EDITOR
            sourceRewardAds = source;
            onFinish?.Invoke();
    #else
            sourceRewardAds = source;

            // Lượt trước còn đang chờ MAX hiện ad (bấm đúp): lượt này tiếp quản ad đó — callback của lượt
            // trước bị thay, như 0.3.1 (game đang dựa vào hành vi này, vd. sm007 RewardedAdSession chốt lượt
            // trước rồi mới show lại). KHÔNG gọi MAX lần nữa: lần show thứ hai chỉ nhận FullscreenAdAlreadyShowing.
            if (IsShowReward && Time.realtimeSinceStartup - _rewardRequestedAt < SHOW_REQUEST_GUARD_SECONDS)
            {
                Debug.Log("unity-script: MAX rewarded - previous show still pending, this call takes it over");
                finishVideo = onFinish;
                closeVideo = onClose;
                failVideo = onFail;
                return;
            }
    #if MEDIATION_MAX
            if (IsReadyVideoAds())
            {
                finishVideo = onFinish;
                closeVideo = onClose;
                failVideo = onFail;
                _rewardShowId++;
                _rewardRequestedAt = Time.realtimeSinceStartup;
                IsShowReward = true;

                _tracker.OnRewardShow(sourceRewardAds);
                MaxSdk.ShowRewardedAd(MediationConstant.Max.RewardedStringId, source);
                return;
            }
    #endif

            // Trước 0.3.2 nhánh "chưa có ad" chỉ log rồi return: caller đã khoá UI chờ mãi một callback
            // không tới và IsShowReward kẹt true. Giờ gọi onFail ĐỒNG BỘ (như nhánh format tắt) và kích
            // load lại — load lỗi trước đó không ai retry nên rewarded có thể chết cả session.
            Debug.Log("unity-script: MAX isRewardedVideoAvailable - False");
            LoadRewardAds();
            onFail?.Invoke();
    #endif
        }

        /// <summary>
        /// Kiểm tra xem interstitial có sẵn sàng không (chỉ khi có kết nối mạng).
        /// </summary>
        /// <returns>True nếu interstitial ad đã sẵn sàng.</returns>
        public bool IsInterstitialReady()
        {
            if (!Supports(AdFormats.Interstitial)) return false;

            if (Application.internetReachability == NetworkReachability.ReachableViaLocalAreaNetwork ||
                Application.internetReachability == NetworkReachability.ReachableViaCarrierDataNetwork)
            {
    #if MEDIATION_MAX
                return MaxSdk.IsInterstitialReady(MediationConstant.Max.InterstitialStringId);
    #endif
            }

            return false;
        }

        /// <summary>
        /// Load interstitial ad từ MAX SDK.
        /// </summary>
        public void LoadInterstitial()
        {
            if (!Supports(AdFormats.Interstitial)) return;
    #if MEDIATION_MAX
            MaxSdk.LoadInterstitial(MediationConstant.Max.InterstitialStringId);
    #endif
        }

        /// <summary>
        /// Hiển thị interstitial ad nếu đủ điều kiện (level, remote config, cooldown).
        /// Trên Editor sẽ invoke closeInter ngay lập tức.
        /// </summary>
        /// <param name="onFinish">Callback khi ad hoàn thành.</param>
        /// <param name="onClose">Callback khi người dùng đóng ad.</param>
        /// <param name="onFail">Callback khi ad thất bại HOẶC bị gate chặn (level / remote config /
        /// giãn cách / chưa fill) — mọi nhánh không hiển thị đều gọi nó.</param>
        /// <param name="source">Placement/source định danh nơi gọi ad.</param>
        public void ShowInterstitial(Action onFinish = null, Action onClose = null, Action onFail = null,
            string source = null)
        {
            if (!Supports(AdFormats.Interstitial))
            {
                onClose?.Invoke();
                return;
            }

            // Gate TRƯỚC khi giữ callback: nhánh bị chặn không được để lại closeInter/failInter treo
            // cho một event ads sau đó nhặt nhầm. Và mọi nhánh KHÔNG show đều phải gọi onFail —
            // trước 0.3.0 chúng return im lặng nên caller đã khoá UI sẽ chờ mãi một callback không tới.
            if (_currentLevelProvider() < ShowInterstitialAdsFromLevel)
            {
                onFail?.Invoke();
                return;
            }

            if (!IsShowInterstitialAds)
            {
                onFail?.Invoke();
                return;
            }

            if (!CanShowInterstitial)
            {
                onFail?.Invoke();
                return;
            }

    #if UNITY_EDITOR
            closeInter?.Invoke();
    #else
            closeInter = onClose;
            failInter = onFail;
            _interstitialPlacement = source;
    #endif

            if (IsInterstitialReady())
            {
    #if MEDIATION_MAX
                // Truyền placement cho MAX giống rewarded — thiếu nó thì báo cáo doanh thu của MAX và
                // adInfo.Placement (AppsFlyer ad revenue) để trống placement cho mọi interstitial.
                MaxSdk.ShowInterstitial(MediationConstant.Max.InterstitialStringId, source);
    #endif
            }
            else
            {
                LoadInterstitial();
                onFail?.Invoke();
            }
        }

        /// <summary>
        /// Hiển thị banner nếu đủ điều kiện (remote config, level).
        /// </summary>
        public void ShowBannerAds()
        {
            if (!Supports(AdFormats.Banner)) return;

            Debug.Log("Request Show Banner Ads");
            if (!IsShowBannerAds) return;
            if (_currentLevelProvider() < ShowBannerAdsFromLevel) return;

    #if MEDIATION_MAX
            Debug.Log("Show Banner Ads");
            MaxSdk.ShowBanner(MediationConstant.Max.BannerStringId);
    #endif
        }

        /// <summary>
        /// Ẩn banner ad.
        /// </summary>
        public void HideBannerAds()
        {
            if (!Supports(AdFormats.Banner)) return;
    #if MEDIATION_MAX
            MaxSdk.HideBanner(MediationConstant.Max.BannerStringId);
    #endif
        }

        /// <summary>
        /// Kiểm tra xem banner đã sẵn sàng chưa.
        /// </summary>
        /// <returns>True nếu banner đã load xong.</returns>
        public bool IsBannerReady() => isBannerReady;

        /// <summary>
        /// Tạo MRec tại vị trí chỉ định. Chưa hỗ trợ trên MAX adapter.
        /// </summary>
        /// <param name="pos">Vị trí hiển thị MRec.</param>
        public void CreateMRec(Vector2 pos)
        {
            // MRec chưa hỗ trợ trên MAX adapter.
        }

        /// <summary>
        /// Load MRec ad. Chưa hỗ trợ trên MAX adapter.
        /// </summary>
        public void LoadMrec()
        {
            // MRec chưa hỗ trợ trên MAX adapter.
        }

        /// <summary>
        /// Hiển thị MRec ad.
        /// </summary>
        public void ShowMRec()
        {
    #if MEDIATION_MAX
            //Debug.Log("Show MRec");
            //MaxSdk.ShowMRec(MediationConstant.Max.MRecStringId);
            //MaxSdk.StartMRecAutoRefresh(MediationConstant.Max.MRecStringId);
    #endif
        }

        /// <summary>
        /// Ẩn MRec ad.
        /// </summary>
        public void HideMRec()
        {
    #if MEDIATION_MAX
            //MaxSdk.HideMRec(MediationConstant.Max.MRecStringId);
    #endif
        }

        /// <summary>
        /// Load MRec ad (override).
        /// </summary>
        public void LoadMRec()
        {
    #if MEDIATION_MAX
            //MaxSdk.StopMRecAutoRefresh(MediationConstant.Max.MRecStringId);
            //MaxSdk.LoadMRec(MediationConstant.Max.MRecStringId);
    #endif
        }

        /// <summary>
        /// Xử lý sự kiện khi app bị pause/resume.
        /// </summary>
        /// <param name="isPause">True nếu app đang bị pause.</param>
        public void OnApplicationPause(bool isPause)
        {
        }

        /// <summary>
        /// Khởi tạo MRec ads. Chưa hỗ trợ trên MAX adapter.
        /// </summary>
        public void InitializeMRecAds()
        {
            // MRec chưa hỗ trợ trên MAX adapter.
        }

        /// <summary>
        /// Bật/tắt hiển thị MRec. Chưa hỗ trợ trên MAX adapter.
        /// </summary>
        public void ToggleMRecVisibility()
        {
            // MRec chưa hỗ trợ trên MAX adapter.
        }

        /// <summary>
        /// Kiểm tra xem có thể hiển thị interstitial hay không.
        /// </summary>
        /// <returns>True nếu format bật VÀ đã hết giãn cách sau fullscreen ad gần nhất.</returns>
        public bool CanShowInter()
        {
            return Supports(AdFormats.Interstitial) && CanShowInterstitial;
        }

        #endregion

        #region Private Methods

    #if MEDIATION_MAX
        /// <summary>
        /// Tắt consent flow (CMP/GDPR) của AppLovin khi user đã từ chối ATT trên iOS.
        /// Từ chối ATT nghĩa là user không cho tracking, nên KHÔNG hiện lại dialog GDPR nữa
        /// và báo luôn cho MAX là không có consent.
        /// Lưu ý: ở lần chạy đầu tiên ATT status còn là NOT_DETERMINED lúc SDK init, nên guard này
        /// chỉ có hiệu lực từ lần mở app sau — khi trạng thái DENIED/RESTRICTED đã được iOS lưu.
        /// </summary>
        private static void DisableConsentFlowIfAttDenied()
        {
        #if UNITY_IOS && !UNITY_EDITOR
            var attStatus = ATTrackingStatusBinding.GetAuthorizationTrackingStatus();
            if (attStatus == ATTrackingStatusBinding.AuthorizationTrackingStatus.DENIED ||
                attStatus == ATTrackingStatusBinding.AuthorizationTrackingStatus.RESTRICTED)
            {
                MaxSdk.SetExtraParameter(EXTRA_PARAM_CONSENT_FLOW_ENABLED, "false");
                MaxSdk.SetHasUserConsent(false);
            }
        #endif
        }

        /// <summary>
        /// Tạo <see cref="AdRevenueInfo"/> từ dữ liệu MAX SDK để đẩy cho tracker.
        /// </summary>
        /// <param name="adUnitId">Ad unit ID.</param>
        /// <param name="adInfo">Thông tin impression từ MAX SDK.</param>
        /// <param name="format">Format quảng cáo.</param>
        /// <param name="source">Placement game đã truyền khi show CHÍNH format này; null nếu format không
        /// nhận placement (banner) — không mượn placement của lượt fullscreen gần nhất.</param>
        /// <returns>Struct <see cref="AdRevenueInfo"/> đã được điền đầy đủ dữ liệu.</returns>
        private AdRevenueInfo BuildRevenueInfo(string adUnitId, MaxSdkBase.AdInfo adInfo, AdFormat format,
            string source)
        {
            return new AdRevenueInfo
            {
                Format = format,
                AdPlatform = "AppLovin",
                NetworkName = adInfo.NetworkName,
                AdUnitId = adUnitId,
                AdUnitIdentifier = adInfo.AdUnitIdentifier,
                AdFormatLabel = adInfo.AdFormat,
                Placement = adInfo.Placement,
                Source = source,
                CountryCode = MaxSdk.GetSdkConfiguration().CountryCode,
                Revenue = adInfo.Revenue,
                Currency = "USD",
            };
        }
    #endif

        #endregion

        #region Event Handlers

        private void OnAdRevenuePaidEventInterstitial(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
    #if MEDIATION_MAX
            _tracker.OnAdRevenuePaid(BuildRevenueInfo(adUnitId, adInfo, AdFormat.Interstitial,
                _interstitialPlacement));
    #endif
        }

        private void OnAdRevenuePaidEventBanner(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
    #if MEDIATION_MAX
            _tracker.OnAdRevenuePaid(BuildRevenueInfo(adUnitId, adInfo, AdFormat.Banner, null));
    #endif
        }

    #if MEDIATION_MAX
        // ------------------------------------------- REWARD ADS ----------------------------------------------------

        private void OnRewardedAdLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            // Rewarded ad is ready for you to show. MaxSdk.IsRewardedAdReady(adUnitId) now returns 'true'.

            // Reset retry attempt
        }

        private void OnRewardedAdLoadFailedEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo)
        {
            // Rewarded ad failed to load
            // AppLovin recommends that you retry with exponentially higher delays, up to a maximum delay (in this case 64 seconds).
            _tracker.OnRewardLoadFailed(sourceRewardAds);
        }

        private void OnRewardedAdDisplayedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            _rewardRequestedAt = float.NegativeInfinity;
            _tracker.OnRewardDisplayed(sourceRewardAds);
        }

        private void OnRewardedAdFailedToDisplayEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo,
            MaxSdkBase.AdInfo adInfo)
        {
            // Rewarded ad failed to display. AppLovin recommends that you load the next ad.
            // Dọn state TRƯỚC khi gọi onFail để caller show lại ngay trong onFail không bị chặn / bị xoá
            // callback mới. finishVideo giữ nguyên như 0.3.1.
            var onFail = failVideo;
            closeVideo = null;
            failVideo = null;
            _rewardRequestedAt = float.NegativeInfinity;
            IsShowReward = false;

            onFail?.Invoke();

            LoadRewardAds();
        }

        private void OnRewardedAdClickedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
        }

        private void OnRewardedAdHiddenEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            // Rewarded ad is hidden. Pre-load the next ad
            CanShowInterstitial = false;
            _rewardRequestedAt = float.NegativeInfinity;
            IsShowReward = false;
            LoadRewardAds();

            // Trước 0.3.2 hidden không đụng closeVideo nên nhánh "đóng trước khi nhận thưởng" của game
            // không bao giờ chạy trên device. Đã nhận thưởng thì lượt này đã xong ở onFinish.
            var showId = _rewardShowId;
            if (_rewardEarnedShowId == showId)
            {
                closeVideo = null;
                failVideo = null;
                return;
            }

            // Chưa có reward: chờ reward trễ (xem LATE_REWARD_GRACE_SECONDS) rồi mới gọi onClose.
            var onClose = closeVideo;
            if (_coroutineHost != null && _coroutineHost.isActiveAndEnabled)
            {
                _coroutineHost.StartCoroutine(CloseAfterLateRewardGrace(showId, onClose));
            }
            else
            {
                CloseIfNotRewarded(showId, onClose);
            }
        }

        private void OnRewardedAdReceivedRewardEvent(string adUnitId, MaxSdk.Reward reward, MaxSdkBase.AdInfo adInfo)
        {
            // The rewarded ad displayed and the user should receive the reward.
            // Một số network bắn reward 2 lần cho một ad: lần sau bỏ qua, không đếm tracking lần nữa.
            if (_rewardEarnedShowId == _rewardShowId) return;
            _rewardEarnedShowId = _rewardShowId;

            var onFinish = finishVideo;
            finishVideo = null;

            CanShowInterstitial = false;
            IsShowReward = false;

            onFinish?.Invoke();
            _tracker.OnRewardCompleted(sourceRewardAds);
        }

        /// <summary>
        /// Chờ <see cref="LATE_REWARD_GRACE_SECONDS" /> rồi quyết định onClose cho lượt <paramref name="showId" />.
        /// Coroutine chạy SAU Update của MaxEventExecutor trong cùng frame, nên reward đã vào hàng đợi
        /// main thread trước mốc chờ luôn được xử lý trước khi quyết định.
        /// </summary>
        /// <param name="showId">Id lượt rewarded vừa đóng.</param>
        /// <param name="onClose">onClose của lượt đó — giữ riêng để lượt show kế tiếp không ghi đè.</param>
        private IEnumerator CloseAfterLateRewardGrace(int showId, Action onClose)
        {
            yield return new WaitForSecondsRealtime(LATE_REWARD_GRACE_SECONDS);
            CloseIfNotRewarded(showId, onClose);
        }

        /// <summary>
        /// Gọi onClose nếu lượt <paramref name="showId" /> chưa nhận thưởng. finishVideo không bị xoá: reward
        /// tới trễ hơn cả khoảng chờ vẫn trao thưởng (như 0.3.1) thay vì người chơi xem trọn ad mà mất quà.
        /// </summary>
        /// <param name="showId">Id lượt rewarded vừa đóng.</param>
        /// <param name="onClose">onClose của lượt đó.</param>
        private void CloseIfNotRewarded(int showId, Action onClose)
        {
            if (_rewardShowId == showId)
            {
                closeVideo = null;
                failVideo = null;
            }

            if (_rewardEarnedShowId == showId) return;
            onClose?.Invoke();
        }

        private void OnRewardedAdRevenuePaidEvent(string adUnitId, MaxSdkBase.AdInfo impressionData)
        {
            // Ad revenue paid. Use this callback to track user revenue.
            _tracker.OnAdRevenuePaid(BuildRevenueInfo(adUnitId, impressionData, AdFormat.Rewarded,
                sourceRewardAds));
        }

        // ------------------------------------------- Interstitial ADS ----------------------------------------------------

        private void OnInterstitialLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            // Interstitial ad is ready for you to show. MaxSdk.IsInterstitialReady(adUnitId) now returns 'true'
        }

        private void OnInterstitialLoadFailedEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo)
        {
            // Interstitial ad failed to load
        }

        private void OnInterstitialDisplayedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            CanShowInterstitial = false;
        }

        private void OnInterstitialAdFailedToDisplayEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo,
            MaxSdkBase.AdInfo adInfo)
        {
            // Interstitial ad failed to display. AppLovin recommends that you load the next ad.
            LoadInterstitial();
            failInter?.Invoke();
        }

        private void OnInterstitialClickedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
        }

        private void OnInterstitialHiddenEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            // Interstitial ad is hidden. Pre-load the next ad.
            CanShowInterstitial = false;

            LoadInterstitial();
            closeInter?.Invoke();
        }

        // ------------------------------------------- Banner ADS ----------------------------------------------------

        private void OnBannerAdLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            Debug.Log("Loaded Banner");
            OnBannerLoaded?.Invoke();
            isBannerReady = true;
        }

        private void OnBannerAdLoadFailedEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo)
        {
            Debug.Log("Load Banner Fail: " + errorInfo.Message);
            OnBannerFailed?.Invoke();
            isBannerReady = false;
        }

        private void OnBannerAdClickedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            OnBannerLoaded?.Invoke();
            isBannerReady = true;
        }

        private void OnBannerAdRevenuePaidEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            OnBannerLoaded?.Invoke();
            isBannerReady = true;
        }

        private void OnBannerAdExpandedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            Debug.Log("Load Expended Banner");
            OnBannerLoaded?.Invoke();
            isBannerReady = true;
        }

        private void OnBannerAdCollapsedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            Debug.Log("Load Collapsed Banner");
        }

        // ------------------------------------------- MRec ADS ----------------------------------------------------

        public void OnMRecAdLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            Debug.Log("-------------Loaded MRec");
        }

        public void OnMRecAdLoadFailedEvent(string adUnitId, MaxSdkBase.ErrorInfo error)
        {
            Debug.Log("-------------Loaded Fail MRec");
        }

        public void OnMRecAdClickedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
        }

        public void OnMRecAdRevenuePaidEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
        }

        public void OnMRecAdExpandedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            Debug.Log("------------Expand MRec");
        }

        public void OnMRecAdCollapsedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            Debug.Log("-------------Collapsed MRec");
        }

    #endif

        #endregion
    }
}
