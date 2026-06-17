using System.Collections.Generic;
using UnityEngine;

namespace Ezg.Package.RpgStats
{
    /// <summary>
    ///     Asset cấu hình cho hệ thống stats. Lớp generic base nằm trong package; mỗi project
    ///     tạo một lớp con non-generic đóng <typeparamref name="TKey" /> và gắn
    ///     <c>[CreateAssetMenu]</c> để tạo asset (Unity không tạo được asset từ ScriptableObject generic).
    /// </summary>
    /// <typeparam name="TKey">Kiểu khóa stat của project (vd: enum RPGStatType).</typeparam>
    public abstract class RpgStatsConfigBase<TKey> : ScriptableObject
    {
        #region Fields

        [Tooltip("Sử dụng cơ chế rút gọn %. 1 = 100%")] [SerializeField]
        private bool _isCompactPercent = true;

        [Tooltip("Ràng buộc giới hạn chỉ số theo config hay không")] [SerializeField]
        private bool _forceLimitStat = true;

        [Tooltip("Các stat chỉ áp dụng dạng % (luôn cộng tổng %)")] [SerializeField]
        private List<TKey> _listStatsPercentOnly = new();

        [Tooltip("Các stat dạng Vital (có current/max, vd: Health). Khởi tạo bằng RPGVital và set current = max.")]
        [SerializeField]
        private List<TKey> _listVitalStats = new();

        [Tooltip("Giới hạn min/max cho từng stat")] [SerializeField]
        private StatLimitModel<TKey>[] _statLimits = System.Array.Empty<StatLimitModel<TKey>>();

        #endregion

        #region Properties

        public bool IsCompactPercent => _isCompactPercent;
        public bool ForceLimitStat => _forceLimitStat;
        public List<TKey> ListStatsPercentOnly => _listStatsPercentOnly;
        public List<TKey> ListVitalStats => _listVitalStats;
        public StatLimitModel<TKey>[] StatLimits => _statLimits;

        #endregion

        #region Public Methods

        /// <summary>
        ///     Nạp toàn bộ giá trị của asset này vào runtime config tĩnh.
        ///     Gọi một lần lúc khởi tạo (vd: trong bootstrap của project).
        /// </summary>
        public void Apply()
        {
            StatConfigs<TKey>.Init(this);
        }

        #endregion
    }
}
