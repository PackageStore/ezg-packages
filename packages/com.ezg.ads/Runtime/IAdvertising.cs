using System;
using UnityEngine;

namespace Ezg.Package.AdsManager
{
    /// <summary>
    /// Interface định nghĩa các chức năng quảng cáo (banner, interstitial, rewarded, mrec).
    /// Mỗi mediation adapter (MAX, IronSource, ...) phải implement interface này.
    /// </summary>
    public interface IAdvertising
    {
        /// <summary>
        /// Banner load/ready thành công.
        /// </summary>
        event Action OnBannerLoaded;

        /// <summary>
        /// Banner load thất bại / cần ẩn.
        /// </summary>
        event Action OnBannerFailed;

        /// <summary>
        /// Key của mediation.
        /// </summary>
        string AppKey { get; }

        /// <summary>
        /// Inject tracker analytics + nguồn lấy level hiện tại (để gating). Gọi trước/ngay khi init.
        /// </summary>
        /// <param name="tracker">Tracker analytics để ghi nhận sự kiện quảng cáo.</param>
        /// <param name="currentLevelProvider">Hàm trả về level hiện tại của người chơi.</param>
        void Bind(IAdsTracker tracker, Func<int> currentLevelProvider);

        /// <summary>
        /// Khởi tạo các event của ads inter, banner, reward,...
        /// </summary>
        void InitEvent();

        /// <summary>
        /// Khởi tạo sdk mediation.
        /// </summary>
        void InitAds();

        /// <summary>
        /// Kiểm tra xem video ads có sẵn sàng không.
        /// </summary>
        /// <returns>True nếu rewarded ad đã sẵn sàng để hiển thị.</returns>
        bool IsReadyVideoAds();

        /// <summary>
        /// Load rewarded ad.
        /// </summary>
        void LoadRewardAds();

        /// <summary>
        /// Hiển thị rewarded video ad.
        /// </summary>
        /// <param name="onFinish">Callback khi người dùng xem xong và nhận thưởng.</param>
        /// <param name="onClose">Callback khi người dùng đóng ad.</param>
        /// <param name="onFail">Callback khi ad thất bại.</param>
        /// <param name="source">Placement/source do game truyền khi show ad.</param>
        void ShowRewardVideo(Action onFinish = null, Action onClose = null, Action onFail = null, string source = null);

        /// <summary>
        /// Kiểm tra xem interstitial đã sẵn sàng chưa.
        /// </summary>
        /// <returns>True nếu interstitial ad đã sẵn sàng để hiển thị.</returns>
        bool IsInterstitialReady();

        /// <summary>
        /// Load interstitial ad.
        /// </summary>
        void LoadInterstitial();

        /// <summary>
        /// Tạo MRec tại vị trí chỉ định.
        /// </summary>
        /// <param name="pos">Vị trí hiển thị MRec.</param>
        void CreateMRec(Vector2 pos);

        /// <summary>
        /// Load MRec ad.
        /// </summary>
        void LoadMrec();

        /// <summary>
        /// Hiển thị MRec ad.
        /// </summary>
        void ShowMRec();

        /// <summary>
        /// Ẩn MRec ad.
        /// </summary>
        void HideMRec();

        /// <summary>
        /// Hiển thị interstitial ad.
        /// </summary>
        /// <param name="onFinish">Callback khi ad hoàn thành.</param>
        /// <param name="onClose">Callback khi người dùng đóng ad.</param>
        /// <param name="onFail">Callback khi ad thất bại.</param>
        /// <param name="source">Placement/source do game truyền khi show ad.</param>
        void ShowInterstitial(Action onFinish = null, Action onClose = null, Action onFail = null, string source = null);

        /// <summary>
        /// Hiển thị banner ad.
        /// </summary>
        void ShowBannerAds();

        /// <summary>
        /// Ẩn banner ad.
        /// </summary>
        void HideBannerAds();

        /// <summary>
        /// Xử lý sự kiện khi app bị pause/resume.
        /// </summary>
        /// <param name="isPause">True nếu app đang bị pause.</param>
        void OnApplicationPause(bool isPause);

        /// <summary>
        /// Khởi tạo MRec ads.
        /// </summary>
        void InitializeMRecAds();

        /// <summary>
        /// Bật/tắt hiển thị MRec.
        /// </summary>
        void ToggleMRecVisibility();

        /// <summary>
        /// Kiểm tra xem có thể hiển thị interstitial hay không.
        /// </summary>
        /// <returns>True nếu đủ điều kiện hiển thị interstitial.</returns>
        bool CanShowInter();
    }
}
