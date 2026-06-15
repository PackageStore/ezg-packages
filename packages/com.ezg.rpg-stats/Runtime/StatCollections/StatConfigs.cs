using System;
using System.Collections.Generic;

namespace Ezg.Package.RpgStats
{
    /// <summary>
    ///     Cấu hình toàn cục (không phụ thuộc kiểu khóa stat) cho hệ thống stats.
    ///     Các modifier dùng <see cref="IsCompactPercent" /> mà không cần biết TKey.
    /// </summary>
    public static class StatConfigs
    {
        #region Fields

        /// <summary>
        ///     Ràng buộc giới hạn chỉ số theo config hay không
        /// </summary>
        private static bool _isForceLimitStat = true;

        /// <summary>
        ///     Sử dụng cơ chế rút gọn %. 1 = 100%
        /// </summary>
        public static bool IsCompactPercent = true;

        /// <summary>
        ///     Trạng thái ràng buộc giới hạn stats hiện tại (đọc bởi StatConfigs&lt;TKey&gt;.ValidStat)
        /// </summary>
        public static bool ForceLimitStat => _isForceLimitStat;

        #endregion

        #region Functions

        /// <summary>
        ///     Kích hoạt trạng thái giới hạn stats
        /// </summary>
        /// <param name="state"></param>
        public static void EnableStatsLimit(bool state)
        {
            _isForceLimitStat = state;
        }

        #endregion
    }

    /// <summary>
    ///     Cấu hình mở rộng cho hệ thống stats, phụ thuộc kiểu khóa <typeparamref name="TKey" />.
    /// </summary>
    public static class StatConfigs<TKey>
    {
        #region Fields

        /// <summary>
        ///     Danh sách các stat chỉ áp dụng dạng % (luôn cộng tổng %)
        /// </summary>
        public static List<TKey> ListStatsPercentOnly = new();

        /// <summary>
        ///     Stat limit data
        /// </summary>
        public static Dictionary<TKey, StatLimitModel<TKey>> StatLimitData;

        #endregion

        #region Functions

        /// <summary>
        ///     Khởi tạo các giá trị giới hạn cho stats
        /// </summary>
        /// <param name="data"></param>
        public static void InitStatLimit(StatLimitModel<TKey>[] data)
        {
            StatConfigs.EnableStatsLimit(true);
            StatLimitData = new Dictionary<TKey, StatLimitModel<TKey>>();
            if (data == null) return;
            foreach (var da in data) StatLimitData[da.type] = da;
        }

        /// <summary>
        ///     Ràng buộc giới hạn trước khi trả về stats
        /// </summary>
        /// <param name="type"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static float ValidStat(TKey type, float value)
        {
            if (!StatConfigs.ForceLimitStat || StatLimitData == null || StatLimitData.Count == 0)
                return value;

            if (!StatLimitData.TryGetValue(type, out var result))
                return value;

            return value < result.minValue ? result.minValue : value > result.maxValue ? result.maxValue : value;
        }

        /// <summary>
        ///     Nạp toàn bộ cấu hình từ asset <see cref="RpgStatsConfigBase{TKey}" />.
        /// </summary>
        /// <param name="config"></param>
        public static void Init(RpgStatsConfigBase<TKey> config)
        {
            if (config == null) return;

            ListStatsPercentOnly = config.ListStatsPercentOnly != null
                ? new List<TKey>(config.ListStatsPercentOnly)
                : new List<TKey>();

            InitStatLimit(config.StatLimits ?? Array.Empty<StatLimitModel<TKey>>());

            // EnableStatsLimit phải gọi sau InitStatLimit (InitStatLimit luôn bật) để config quyết định cuối cùng.
            StatConfigs.IsCompactPercent = config.IsCompactPercent;
            StatConfigs.EnableStatsLimit(config.ForceLimitStat);
        }

        #endregion
    }
}
