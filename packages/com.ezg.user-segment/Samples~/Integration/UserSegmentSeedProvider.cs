using Ezg.Feature.Shared.GameData;
using Ezg.UserSegment;

namespace Ezg.Feature.System.UserSegment
{
    /// <summary>Seed state cho user hiện hữu từ player data của template — §4.6.</summary>
    public static class UserSegmentSeedProvider
    {
        /// <returns>null khi chưa có dữ liệu (SDK sẽ chờ Seed() gọi sau).</returns>
        public static SeedData Build()
        {
            var login = PlayerDataManager.LoginActivity;
            var shop = PlayerDataManager.PlayerShop;
            if (login?.dataBase == null || login.InstallTime <= 0)
            {
                if (UnityEngine.Debug.isDebugBuild) UnityEngine.Debug.Log("[UserSegment] SeedProvider: chưa có installTime → trả null");
                return null;
            }

            return new SeedData
            {
                InstallAt = login.InstallTime,
                LastActiveAt = login.dataBase.lastLoginTime,
                PurchaseCount = shop?.dataBase?.IAPCount ?? 0,
                // TODO: [UserSegment] IAPRevenue đang cộng giá LOCAL (localizedPrice), không phải USD catalog.
                // Khi project có bảng giá USD theo productId thì tính lại tổng ở đây.
                TotalSpendUsd = (double)(shop?.dataBase?.IAPRevenue ?? 0),
                // TODO: [UserSegment] map ProgressMax sang tiến độ thật của gameplay khi project có gameplay.
                ProgressMax = 0
            };
        }
    }
}
