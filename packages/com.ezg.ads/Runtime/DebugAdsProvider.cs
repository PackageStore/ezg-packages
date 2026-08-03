using System;
using UnityEngine;

namespace Ezg.Package.AdsManager
{
    /// <summary>
    /// Provider giả dùng cho debug ads (<see cref="AdsConfig.IsDebugAds" />).
    /// KHÔNG khởi tạo mediation SDK và KHÔNG dùng ad-unit-id thật — mọi lệnh show ad trả kết quả
    /// thành công ngay lập tức để dev nhận thưởng mà không phải xem quảng cáo.
    /// <para>
    /// Banner là no-op (không có gì để hiển thị). Các cờ remote config vẫn được lưu để code gate
    /// đọc/ghi bình thường, nhưng không ảnh hưởng gì tới hành vi ở đây.
    /// </para>
    /// </summary>
    public class DebugAdsProvider : IAdProvider, IBannerAds, IInterstitialAds, IRewardedAds, IMRecAds,
        IRemoteConfigAdvertising
    {
        #region Fields

        public event Action OnBannerLoaded;
        public event Action OnBannerFailed;

        private AdFormats _formats = AdFormats.None;

        /// <summary> Debug ads không dùng key thật. </summary>
        public string AppKey => string.Empty;

        public bool IsShowReward { get; set; }
        public bool CanShowInterstitial { get; set; } = true;
        public int CountTimeShowInterstitialAds { get; set; }
        public int TimeDelayShowInterstitialAds { get; set; }
        public bool IsShowInterstitialAds { get; set; } = true;
        public int ShowInterstitialAdsFromLevel { get; set; }
        public bool IsShowBannerAds { get; set; } = true;
        public int ShowBannerAdsFromLevel { get; set; }

        #endregion

        #region Public Methods

        /// <summary> Format này có được bật hay không. </summary>
        /// <param name="format">Format cần kiểm tra.</param>
        /// <returns>True nếu format đang bật.</returns>
        public bool Supports(AdFormats format) => (_formats & format) != 0;

        /// <summary>
        /// Ghi nhận tập format được bật. KHÔNG init SDK nào cả.
        /// </summary>
        /// <param name="formats">Bitmask format mà project bật.</param>
        /// <param name="tracker">Bỏ qua — debug ads không bắn tracking quảng cáo.</param>
        /// <param name="currentLevelProvider">Bỏ qua — debug ads không gate theo level.</param>
        public void Initialize(AdFormats formats, IAdsTracker tracker, Func<int> currentLevelProvider)
        {
            _formats = formats;
            Debug.Log($"[Ads] DEBUG ADS — bỏ qua mediation SDK. Format bật: {formats}. " +
                      "Mọi lệnh show ad sẽ trả thành công ngay và không bắn tracking.");
        }

        /// <summary> Không có SDK nên không cần xử lý pause. </summary>
        /// <param name="isPause">True nếu app đang bị pause.</param>
        public void OnApplicationPause(bool isPause)
        {
        }

        #region Rewarded

        /// <summary> Debug ads luôn sẵn sàng (khi format bật). </summary>
        /// <returns>True nếu format Rewarded đang bật.</returns>
        public bool IsReadyVideoAds() => Supports(AdFormats.Rewarded);

        /// <summary> No-op — không có gì để load. </summary>
        public void LoadRewardAds()
        {
        }

        /// <summary> Trả thưởng ngay, không hiển thị gì. </summary>
        /// <param name="onFinish">Callback nhận thưởng — luôn được gọi khi format bật.</param>
        /// <param name="onClose">Callback đóng ad — gọi sau onFinish.</param>
        /// <param name="onFail">Callback lỗi — chỉ gọi khi format Rewarded bị tắt.</param>
        /// <param name="source">Placement/source định danh nơi gọi ad.</param>
        public void ShowRewardVideo(Action onFinish = null, Action onClose = null, Action onFail = null,
            string source = null)
        {
            if (!Supports(AdFormats.Rewarded))
            {
                onFail?.Invoke();
                return;
            }

            Debug.Log($"[Ads] DEBUG ADS — Rewarded auto-finish (source: {source})");
            onFinish?.Invoke();
            onClose?.Invoke();
        }

        #endregion

        #region Interstitial

        /// <summary> Debug ads luôn sẵn sàng (khi format bật). </summary>
        /// <returns>True nếu format Interstitial đang bật.</returns>
        public bool IsInterstitialReady() => Supports(AdFormats.Interstitial);

        /// <summary> Debug ads không gate cooldown. </summary>
        /// <returns>True nếu format Interstitial đang bật.</returns>
        public bool CanShowInter() => Supports(AdFormats.Interstitial);

        /// <summary> No-op — không có gì để load. </summary>
        public void LoadInterstitial()
        {
        }

        /// <summary> Kết thúc ngay, không hiển thị gì. </summary>
        /// <param name="onFinish">Callback hoàn thành — luôn được gọi khi format bật.</param>
        /// <param name="onClose">Callback đóng ad — gọi sau onFinish.</param>
        /// <param name="onFail">Callback lỗi — chỉ gọi khi format Interstitial bị tắt.</param>
        /// <param name="source">Placement/source định danh nơi gọi ad.</param>
        public void ShowInterstitial(Action onFinish = null, Action onClose = null, Action onFail = null,
            string source = null)
        {
            if (!Supports(AdFormats.Interstitial))
            {
                onFail?.Invoke();
                return;
            }

            Debug.Log($"[Ads] DEBUG ADS — Interstitial auto-close (source: {source})");
            onFinish?.Invoke();
            onClose?.Invoke();
        }

        #endregion

        #region Banner / MRec

        /// <summary> No-op — debug ads không hiển thị banner. </summary>
        public void ShowBannerAds()
        {
        }

        /// <summary> No-op — debug ads không hiển thị banner. </summary>
        public void HideBannerAds()
        {
        }

        /// <summary> No-op — debug ads không hiển thị MRec. </summary>
        /// <param name="pos">Bỏ qua.</param>
        public void CreateMRec(Vector2 pos)
        {
        }

        /// <summary> No-op — debug ads không hiển thị MRec. </summary>
        public void LoadMrec()
        {
        }

        /// <summary> No-op — debug ads không hiển thị MRec. </summary>
        public void ShowMRec()
        {
        }

        /// <summary> No-op — debug ads không hiển thị MRec. </summary>
        public void HideMRec()
        {
        }

        #endregion

        #endregion
    }
}
