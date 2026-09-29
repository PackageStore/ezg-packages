using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Ezg.AutoTest
{
    /// <summary>Khả năng adapter hỗ trợ — suite tự bỏ qua case khi thiếu.</summary>
    [Flags]
    public enum AdapterCapabilities
    {
        None = 0,
        ReadyState = 1 << 0,
        Features = 1 << 1,
        Economy = 1 << 2,
        PlayerData = 1 << 3,
        Config = 1 << 4,
        Rewards = 1 << 5,
        PurchaseOffline = 1 << 6
    }

    /// <summary>Một màn hình/feature của game (giá trị enum Features).</summary>
    public sealed class AutoTestFeature
    {
        public string Name;
        public long Value;
        public object Raw;

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>Một loại tiền tệ/tài nguyên.</summary>
    public sealed class AutoTestCurrency
    {
        public string Name;
        public long Value;
        public object Raw;

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>Một bảng config (CSV → ScriptableObject) đã load.</summary>
    public sealed class AutoTestConfigTable
    {
        public string Name;
        public UnityEngine.Object Asset;
        public Type ItemType;
        public IList Items;
    }

    /// <summary>
    ///     Cầu nối giữa hệ thống test và code game. Package ship sẵn <c>EzgTemplateAdapter</c> (template EZG,
    ///     phản chiếu theo tên type) và <c>GenericGameAdapter</c> (không biết gì về game — chỉ scene/UI chung).
    ///     Project khác kiến trúc: tự implement interface này trong thư mục AutoTests (có template sẵn).
    /// </summary>
    public interface IGameAdapter
    {
        string Name { get; }

        /// <summary>Độ ưu tiên khi tự chọn adapter (cao thắng). Adapter của project nên &gt; 100.</summary>
        int Priority { get; }

        AdapterCapabilities Capabilities { get; }

        /// <summary>Ghi chú resolve type (tìm thấy / thiếu gì) — hiện trong cửa sổ + report.</summary>
        IReadOnlyList<string> Diagnostics { get; }

        /// <summary>Resolve type/method. Trả false nếu project không dùng được adapter này.</summary>
        bool Initialize(AutoTestConfig config);

        // ---------------- Vòng đời ----------------

        /// <summary>Game đã boot xong, sẵn sàng thao tác chưa.</summary>
        bool IsGameReady();

        /// <summary>Mô tả trạng thái boot hiện tại (cho thông báo timeout).</summary>
        string DescribeState();

        // ---------------- Màn hình ----------------

        IReadOnlyList<AutoTestFeature> GetFeatures();

        /// <summary>Mở màn hình; trả GameObject gốc hoặc null nếu không mở được.</summary>
        Task<GameObject> ShowFeature(AutoTestFeature feature, object data, float timeoutSeconds, CancellationToken ct);

        void CloseFeature(AutoTestFeature feature);
        bool IsFeatureShowing(AutoTestFeature feature);
        GameObject GetFeatureObject(AutoTestFeature feature);
        IReadOnlyList<AutoTestFeature> GetOpenFeatures();

        // ---------------- Economy ----------------

        IReadOnlyList<AutoTestCurrency> GetCurrencies();
        double GetBalance(AutoTestCurrency currency);
        void AddCurrency(AutoTestCurrency currency, double amount);

        /// <summary>Trả false nếu game từ chối trừ (không đủ tiền…).</summary>
        bool RemoveCurrency(AutoTestCurrency currency, double amount);

        bool IsEnough(AutoTestCurrency currency, double amount);
        void SetCurrency(AutoTestCurrency currency, double amount);

        /// <summary>Kiểu số lưu số dư (int/long/double/…) — để test tràn số.</summary>
        Type BalanceType(AutoTestCurrency currency);

        /// <summary>Phát thưởng qua hệ thống reward của game. false = không hỗ trợ.</summary>
        bool GrantReward(AutoTestCurrency currency, double amount);

        /// <summary>Mua bằng tiền mềm. null = không hỗ trợ; true/false = giao dịch thành công hay không.</summary>
        Task<bool?> PurchaseOffline(AutoTestCurrency currency, double cost, float timeoutSeconds, CancellationToken ct);

        // ---------------- Dữ liệu ----------------

        void SaveAll();

        /// <summary>Key PlayerPrefs của dữ liệu người chơi — gọi được ở Edit mode (để sandbox sao lưu).</summary>
        IReadOnlyList<string> GetSaveKeys();

        /// <summary>Đọc lại dữ liệu người chơi từ storage (để test save/load). false = không hỗ trợ.</summary>
        bool ReloadPlayerData();

        // ---------------- Config ----------------

        IReadOnlyList<AutoTestConfigTable> GetConfigCollections();
    }

    /// <summary>
    ///     Adapter biết quy ước đặt tên prefab màn hình (vd template EZG: "screen_" + snake_case) — luật tĩnh
    ///     dùng để phát hiện feature thiếu prefab.
    /// </summary>
    public interface IFeaturePrefabResolver
    {
        /// <summary>Tên prefab (không đuôi) của màn hình, null nếu không xác định.</summary>
        string ResolvePrefabName(AutoTestFeature feature);
    }
}
