using System;
using System.Collections;
using Ezg.Package.Singleton;
using UnityEngine;

namespace Ezg.Package.AdsManager
{
    /// <summary>
    /// Singleton quản lý toàn bộ quảng cáo trong game (rewarded, interstitial, banner, mrec).
    /// Delegate thực thi cho <see cref="IAdvertising"/> adapter (mặc định là MaxAdsvertising).
    /// </summary>
    public class AdsManager : Singleton<AdsManager>
    {
        #region Fields

        public IAdvertising advertising;
        public IRemoteConfigAdvertising advertisingRemoteConfig;
        public Vector2 size;
        public float intervalTime;
        public bool canShowInter;
        public int count;

        private bool _canLoadBanner = false;
        private bool _isTestAds = false;

        private IAdsTracker _tracker = new NullAdsTracker();
        private Func<int> _currentLevelProvider = () => int.MaxValue;

        public event Action OnBannerLoaded;
        public event Action OnBannerFailed;

        #endregion

        #region Initialize

        private void Awake()
        {
            InitAds();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Inject tracker analytics + nguồn lấy level hiện tại từ host project. Gọi 1 lần lúc bootstrap.
        /// </summary>
        /// <param name="tracker">Tracker analytics để ghi nhận sự kiện quảng cáo.</param>
        /// <param name="currentLevelProvider">Hàm trả về level hiện tại của người chơi.</param>
        public void Configure(IAdsTracker tracker, Func<int> currentLevelProvider)
        {
            if (tracker != null) _tracker = tracker;
            if (currentLevelProvider != null) _currentLevelProvider = currentLevelProvider;
            advertising?.Bind(_tracker, _currentLevelProvider);
        }

        /// <summary>
        /// Bật/tắt chế độ test ads.
        /// </summary>
        /// <param name="isTest">True để bật test ads.</param>
        public void SetTestAds(bool isTest)
        {
            _isTestAds = isTest;
        }

        /// <summary>
        /// Kiểm tra xem có thể hiển thị interstitial hay không (dựa theo cooldown và điều kiện adapter).
        /// </summary>
        /// <returns>True nếu đủ điều kiện hiển thị interstitial.</returns>
        public bool CanShowInter()
        {
            return advertising.CanShowInter() && canShowInter;
        }

        #region Reward Ads

        /// <summary>
        /// Kiểm tra xem rewarded video có sẵn sàng để hiển thị không.
        /// </summary>
        /// <returns>True nếu rewarded ad đã load xong.</returns>
        public bool IsVideoRewardReady()
        {
            return advertising.IsReadyVideoAds();
        }

        /// <summary>
        /// Hiển thị rewarded video với callback khi hoàn thành.
        /// </summary>
        /// <param name="sourceId">Placement/source định danh nơi gọi ad.</param>
        /// <param name="onFinish">Callback khi người dùng xem xong và nhận thưởng.</param>
        public void ShowRewardedVideo(string sourceId, Action onFinish = null)
        {
            _tracker.OnRewardClick(sourceId);
            advertising.ShowRewardVideo(onFinish, source: sourceId);
        }

        /// <summary>
        /// Hiển thị rewarded video với đầy đủ callback.
        /// </summary>
        /// <param name="sourceId">Placement/source định danh nơi gọi ad.</param>
        /// <param name="onFinish">Callback khi người dùng xem xong và nhận thưởng.</param>
        /// <param name="onClose">Callback khi người dùng đóng ad trước khi hoàn thành.</param>
        /// <param name="onFail">Callback khi ad thất bại.</param>
        public void ShowRewardedVideo(string sourceId, Action onFinish = null, Action onClose = null, Action onFail = null)
        {
            advertising.ShowRewardVideo(onFinish, onClose, onFail, source: sourceId);
        }

        #endregion

        #region Interstitial Ads

        /// <summary>
        /// Kiểm tra xem interstitial có sẵn sàng để hiển thị không.
        /// </summary>
        /// <returns>True nếu interstitial ad đã load xong.</returns>
        public bool IsInterstitialReady()
        {
            return advertising.IsInterstitialReady();
        }

        /// <summary>
        /// Load interstitial nếu chưa sẵn sàng.
        /// </summary>
        public void LoadInterstitial()
        {
            if (!IsInterstitialReady())
            {
                advertising.LoadInterstitial();
            }
        }

        /// <summary>
        /// Hiển thị interstitial ad.
        /// </summary>
        /// <param name="onFinish">Callback khi ad hoàn thành.</param>
        /// <param name="onClose">Callback khi người dùng đóng ad.</param>
        /// <param name="onFail">Callback khi ad thất bại.</param>
        /// <param name="source">Placement/source định danh nơi gọi ad.</param>
        public void ShowInterstitial(Action onFinish = null, Action onClose = null, Action onFail = null,
            string source = null)
        {
    #if UNITY_EDITOR
            //GoogleAdMobController.Instance.OnAdInterClosedEvent?.Invoke();
            Debug.Log("Show Inter");
            count = 0;
            canShowInter = false;
            onFinish?.Invoke();
            return;
    #endif
            advertising.ShowInterstitial(onFinish, onClose, onFail, source);
        }

        #endregion

        #region Banner Ads

        /// <summary>
        /// Load banner ad.
        /// </summary>
        public void LoadBanner()
        {
        }

        /// <summary>
        /// Ẩn banner ad.
        /// </summary>
        public void HideBanner()
        {
            advertising.HideBannerAds();
        }

        #endregion

        #region Mrec

        /// <summary>
        /// Tạo MRec tại vị trí chỉ định.
        /// </summary>
        /// <param name="pos">Vị trí hiển thị MRec.</param>
        public void CreateMRec(Vector2 pos)
        {
            advertising.CreateMRec(pos);
        }

        /// <summary>
        /// Hiển thị MRec ad.
        /// </summary>
        public void ShowMrec()
        {
            advertising.ShowMRec();
        }

        /// <summary>
        /// Ẩn MRec ad.
        /// </summary>
        public void HideMrec()
        {
            advertising.HideMRec();
        }

        #endregion

        #endregion

        #region Private Methods

        /// <summary>
        /// Khởi tạo advertising adapter và đăng ký các event.
        /// </summary>
        protected void InitAds()
        {
            advertising = new MaxAdsvertising();
            advertisingRemoteConfig = advertising as IRemoteConfigAdvertising;
            advertising.Bind(_tracker, _currentLevelProvider);
            advertising.OnBannerLoaded += HandleBannerLoaded;
            advertising.OnBannerFailed += HandleBannerFailed;
            advertising.InitAds();
        }

        /// <summary>
        /// Tăng bộ đếm thời gian để tính cooldown giữa các interstitial.
        /// </summary>
        private void IntervalTime()
        {
            advertisingRemoteConfig.CountTimeShowInterstitialAds++;
            if (advertisingRemoteConfig.CountTimeShowInterstitialAds >=
                advertisingRemoteConfig.TimeDelayShowInterstitialAds)
            {
                advertisingRemoteConfig.CanShowInterstitial = true;
            }
        }

        /// <summary>
        /// Coroutine đếm cooldown interstitial theo giây.
        /// </summary>
        private IEnumerator Cooldown()
        {
            while (true)
            {
                count++;
                yield return new WaitForSeconds(1);
                if (count >= intervalTime)
                {
                    canShowInter = true;
                    count = 0;
                }
            }
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (advertising != null)
            {
                advertising.OnApplicationPause(isPaused);
            }
        }

        #endregion

        #region Event Handlers

        private void HandleBannerLoaded() => OnBannerLoaded?.Invoke();

        private void HandleBannerFailed() => OnBannerFailed?.Invoke();

        #endregion
    }
}
