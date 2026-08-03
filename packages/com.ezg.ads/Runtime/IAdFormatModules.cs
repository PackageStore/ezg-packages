using System;
using UnityEngine;

namespace Ezg.Package.AdsManager
{
    /// <summary>
    /// Phần bắt buộc của một adapter mediation: khởi tạo SDK và nhận sự kiện pause.
    /// Adapter chỉ implement thêm những interface format mà nó thật sự hỗ trợ
    /// (<see cref="IBannerAds" />, <see cref="IInterstitialAds" />, <see cref="IRewardedAds" />, ...).
    /// </summary>
    public interface IAdProvider
    {
        /// <summary> Key của mediation. </summary>
        string AppKey { get; }

        /// <summary>
        /// Inject tracker analytics + nguồn lấy level hiện tại (để gating), và tập format được bật.
        /// Adapter chỉ được khởi tạo/đăng ký callback cho các format nằm trong <paramref name="formats" />.
        /// </summary>
        /// <param name="formats">Bitmask format mà project bật.</param>
        /// <param name="tracker">Tracker analytics để ghi nhận sự kiện quảng cáo.</param>
        /// <param name="currentLevelProvider">Hàm trả về level hiện tại của người chơi.</param>
        void Initialize(AdFormats formats, IAdsTracker tracker, Func<int> currentLevelProvider);

        /// <summary> Xử lý sự kiện khi app bị pause/resume. </summary>
        /// <param name="isPause">True nếu app đang bị pause.</param>
        void OnApplicationPause(bool isPause);
    }

    /// <summary> Adapter hỗ trợ banner. </summary>
    public interface IBannerAds
    {
        /// <summary> Banner load/ready thành công. </summary>
        event Action OnBannerLoaded;

        /// <summary> Banner load thất bại / cần ẩn. </summary>
        event Action OnBannerFailed;

        /// <summary> Hiển thị banner (adapter tự gate theo remote config + level). </summary>
        void ShowBannerAds();

        /// <summary> Ẩn banner. </summary>
        void HideBannerAds();
    }

    /// <summary> Adapter hỗ trợ interstitial. </summary>
    public interface IInterstitialAds
    {
        /// <summary> Interstitial đã load xong và sẵn sàng hiển thị. </summary>
        bool IsInterstitialReady();

        /// <summary> Đủ điều kiện hiển thị interstitial (cooldown, remote config). </summary>
        bool CanShowInter();

        /// <summary> Load interstitial. </summary>
        void LoadInterstitial();

        /// <summary> Hiển thị interstitial. </summary>
        /// <param name="onFinish">Callback khi ad hoàn thành.</param>
        /// <param name="onClose">Callback khi người dùng đóng ad.</param>
        /// <param name="onFail">Callback khi ad thất bại.</param>
        /// <param name="source">Placement/source định danh nơi gọi ad.</param>
        void ShowInterstitial(Action onFinish = null, Action onClose = null, Action onFail = null,
            string source = null);
    }

    /// <summary> Adapter hỗ trợ rewarded video. </summary>
    public interface IRewardedAds
    {
        /// <summary> Rewarded video đã load xong và sẵn sàng hiển thị. </summary>
        bool IsReadyVideoAds();

        /// <summary> Load rewarded video. </summary>
        void LoadRewardAds();

        /// <summary> Hiển thị rewarded video. </summary>
        /// <param name="onFinish">Callback khi người dùng xem xong và nhận thưởng.</param>
        /// <param name="onClose">Callback khi người dùng đóng ad.</param>
        /// <param name="onFail">Callback khi ad thất bại.</param>
        /// <param name="source">Placement/source định danh nơi gọi ad.</param>
        void ShowRewardVideo(Action onFinish = null, Action onClose = null, Action onFail = null, string source = null);
    }

    /// <summary> Adapter hỗ trợ MRec. </summary>
    public interface IMRecAds
    {
        /// <summary> Tạo MRec tại vị trí chỉ định. </summary>
        /// <param name="pos">Vị trí hiển thị MRec.</param>
        void CreateMRec(Vector2 pos);

        /// <summary> Load MRec. </summary>
        void LoadMrec();

        /// <summary> Hiển thị MRec. </summary>
        void ShowMRec();

        /// <summary> Ẩn MRec. </summary>
        void HideMRec();
    }
}
