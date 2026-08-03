using System;
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

        private Action closeInter;
        private Action failInter;
        private string _adPlacement;

        private string sourceRewardAds;

        public string AppKey => MediationConstant.Max.SdkKey;

        public bool IsShowReward { get; set; }
        public bool CanShowInterstitial { get; set; }
        public int CountTimeShowInterstitialAds { get; set; }
        public int TimeDelayShowInterstitialAds { get; set; }
        public bool IsShowInterstitialAds { get; set; }
        public int ShowInterstitialAdsFromLevel { get; set; }
        public bool IsShowBannerAds { get; set; }
        public int ShowBannerAdsFromLevel { get; set; }

        private bool isInit;
        private bool isBannerReady;

        private Vector2 referenceResolution = new Vector2(1080, 2400);
        private float referenceBannerWidth = 1080f;

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

            CanShowInterstitial = true;
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
        /// Hiển thị rewarded video. Trên Editor sẽ invoke onFinish ngay lập tức (MAX SDK không serve ad
        /// thật ngoài device). Muốn chủ động chạy chế độ auto-nhận-thưởng thì bật debugAds trong AdsConfig
        /// — khi đó <see cref="DebugAdsProvider" /> thay thế adapter này hoàn toàn.
        /// </summary>
        /// <param name="onFinish">Callback khi xem xong và nhận thưởng.</param>
        /// <param name="onClose">Callback khi đóng ad.</param>
        /// <param name="onFail">Callback khi ad thất bại.</param>
        /// <param name="source">Placement/source định danh nơi gọi ad.</param>
        public void ShowRewardVideo(Action onFinish = null, Action onClose = null, Action onFail = null,
            string source = null)
        {
            if (!Supports(AdFormats.Rewarded))
            {
                onFail?.Invoke();
                return;
            }

            sourceRewardAds = source;
    #if UNITY_EDITOR
            onFinish?.Invoke();
    #else
            finishVideo = onFinish;
            closeVideo = onClose;
            failVideo = onFail;
            _adPlacement = source;
            IsShowReward = true;

    #if MEDIATION_MAX
                if (IsReadyVideoAds())
                {
                    _tracker.OnRewardShow(sourceRewardAds);
                    MaxSdk.ShowRewardedAd(MediationConstant.Max.RewardedStringId, source);
                }
                else
                {
                    Debug.Log("unity-script: MAX isRewardedVideoAvailable - False");
                }
    #endif
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
        /// <param name="onFail">Callback khi ad thất bại.</param>
        /// <param name="source">Placement/source định danh nơi gọi ad.</param>
        public void ShowInterstitial(Action onFinish = null, Action onClose = null, Action onFail = null,
            string source = null)
        {
            if (!Supports(AdFormats.Interstitial))
            {
                onClose?.Invoke();
                return;
            }

    #if UNITY_EDITOR
            closeInter?.Invoke();
    #else
            closeInter = onClose;
            failInter = onFail;
            _adPlacement = source;
    #endif

            if (_currentLevelProvider() < ShowInterstitialAdsFromLevel) return;
            if (!IsShowInterstitialAds) return;
            if (!CanShowInterstitial) return;
            if (IsInterstitialReady())
            {
    #if MEDIATION_MAX
                MaxSdk.ShowInterstitial(MediationConstant.Max.InterstitialStringId);
    #endif
            }
            else
            {
                LoadInterstitial();
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
        /// <returns>True nếu <see cref="CanShowInterstitial"/> đang bật.</returns>
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
        /// <returns>Struct <see cref="AdRevenueInfo"/> đã được điền đầy đủ dữ liệu.</returns>
        private AdRevenueInfo BuildRevenueInfo(string adUnitId, MaxSdkBase.AdInfo adInfo, AdFormat format)
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
                Source = _adPlacement,
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
            _tracker.OnAdRevenuePaid(BuildRevenueInfo(adUnitId, adInfo, AdFormat.Interstitial));
    #endif
        }

        private void OnAdRevenuePaidEventBanner(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
    #if MEDIATION_MAX
            _tracker.OnAdRevenuePaid(BuildRevenueInfo(adUnitId, adInfo, AdFormat.Banner));
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
            _tracker.OnRewardDisplayed(sourceRewardAds);
        }

        private void OnRewardedAdFailedToDisplayEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo,
            MaxSdkBase.AdInfo adInfo)
        {
            // Rewarded ad failed to display. AppLovin recommends that you load the next ad.
            failVideo?.Invoke();
            IsShowReward = false;

            LoadRewardAds();
        }

        private void OnRewardedAdClickedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
        }

        private void OnRewardedAdHiddenEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            // Rewarded ad is hidden. Pre-load the next ad
            CountTimeShowInterstitialAds = 0;
            CanShowInterstitial = false;
            LoadRewardAds();
        }

        private void OnRewardedAdReceivedRewardEvent(string adUnitId, MaxSdk.Reward reward, MaxSdkBase.AdInfo adInfo)
        {
            // The rewarded ad displayed and the user should receive the reward.
            finishVideo?.Invoke();
            finishVideo = null;

            CountTimeShowInterstitialAds = 0;
            CanShowInterstitial = false;
            IsShowReward = false;

            _tracker.OnRewardCompleted(sourceRewardAds);
        }

        private void OnRewardedAdRevenuePaidEvent(string adUnitId, MaxSdkBase.AdInfo impressionData)
        {
            // Ad revenue paid. Use this callback to track user revenue.
            _tracker.OnAdRevenuePaid(BuildRevenueInfo(adUnitId, impressionData, AdFormat.Rewarded));
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
            CountTimeShowInterstitialAds = 0;
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
            CountTimeShowInterstitialAds = 0;
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
