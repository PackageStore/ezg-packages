#if UNITY_EDITOR || EZG_AUTOTEST
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ezg.AutoTest.Samples
{
    /// <summary>
    ///     MẪU THAM KHẢO cách viết project hooks. Class này cố ý để <c>abstract</c> nên runner KHÔNG BAO GIỜ tạo
    ///     nó (một project chỉ được có một class hooks cụ thể — hai class sẽ tranh nhau).
    ///     Muốn dùng: copy sang thư mục AutoTests của project, đổi tên, bỏ <c>abstract</c>, thay các service
    ///     giả (<c>MyTutorialService</c>…) bằng service thật của game. Hoặc để Editor sinh sẵn khung:
    ///     <c>AutoTestScaffolder.CreateProjectHooks()</c>.
    /// </summary>
    public abstract class ExampleProjectHooksTemplate : AutoTestProjectHooks
    {
        /// <summary>Chạy một lần mỗi phiên Play, sau khi game boot xong và trước case đầu tiên.</summary>
        public override async Task OnSessionStarted(AutoTestContext ctx)
        {
            // Bỏ qua tutorial để smoke/button sweep không bị bước hướng dẫn chặn tay:
            //   MyTutorialService.DoneAll();
            // Tắt âm thanh cho đỡ ồn khi chạy lâu:
            //   MyAudioService.SetMute(true);
            // Chờ game xử lý xong (vd popup phần thưởng tutorial bật ra) — luôn chờ có điều kiện + timeout:
            //   await ctx.WaitUntil(() => !MyPopupQueue.HasPending, 5, "Hàng đợi popup rỗng", failOnTimeout: false);
            ctx.Log("Hooks mẫu: OnSessionStarted (chưa làm gì).");
            await ctx.NextFrame();
        }

        /// <summary>null = để adapter tự quyết game đã sẵn sàng chưa.</summary>
        public override bool? IsGameReady()
        {
            // Chỉ override khi adapter đoán sai, vd game còn scene loading riêng:
            //   return MySceneLoader.IsIdle && MyHud.IsShown;
            // KHÔNG gọi UIManager.Instance ở đây — Instance tự tạo bản rỗng khi game chưa khởi tạo.
            return null;
        }

        /// <summary>Data mẫu truyền vào khi mở màn cần data (smoke / UI audit / button sweep dùng).</summary>
        public override object GetFeatureData(string featureName)
        {
            switch (featureName)
            {
                // case "RewardPopup": return new MyRewardPopupData(MyRewardFactory.Sample());
                // case "StationInfo": return MyStationService.GetFirstUnlockedStation();
                default: return null;
            }
        }

        /// <summary>Đóng popup chặn (offer theo giờ, rating, level up…) giữa các case.</summary>
        public override Task DismissBlockingPopups(AutoTestContext ctx)
        {
            //   MyPopupQueue.Clear();
            return Task.CompletedTask;
        }

        /// <summary>Feature (đúng tên enum) không mở độc lập được ⇒ loại khỏi smoke / UI audit / button sweep.</summary>
        public override IEnumerable<string> ExtraExcludedFeatures()
        {
            // return new[] { "StationInfo", "BattleResult" };
            return Array.Empty<string>();
        }
    }
}
#endif
