namespace Ezg.Feature.IAP
{
    /// <summary>
    /// Trạng thái/định danh người chơi mà module IAP cần đọc từ game.
    /// Game implement (vd: map sang PlayerDataManager) và inject qua
    /// <see cref="InAppManager.Configure"/>.
    /// </summary>
    public interface IIapProfile
    {
        /// <summary>Id tài khoản dùng cho tracking (af player_id...).</summary>
        string AccountId { get; }

        /// <summary>Cheat IAP có đang bật không (đọc động lúc runtime).</summary>
        bool IsCheatEnabled { get; }

        /// <summary>Ghi nhận một lượt mua thành công (đếm số lượt + doanh thu + lưu).</summary>
        void RecordPurchase(decimal localizedPrice);
    }
}
