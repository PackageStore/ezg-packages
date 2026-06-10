using UnityEngine;

namespace Ezg.Package.CsvReader
{
    /// <summary>
    ///     Resolver cho <see cref="CsvReaderConfig" />. Tìm asset config trong project (editor-only),
    ///     nếu không có thì fallback về một instance default in-memory (giá trị mặc định của
    ///     <see cref="CsvReaderConfig" />). Nhờ vậy module chạy được kể cả khi project chưa tạo config.
    /// </summary>
    public static class CsvReaderSettings
    {
        #region Fields

        private static CsvReaderConfig _cached;

        #endregion

        #region Public Methods

        /// <summary>
        ///     Config hiện hành. Cache lại sau lần resolve đầu tiên.
        /// </summary>
        public static CsvReaderConfig Current
        {
            get
            {
                if (_cached != null)
                    return _cached;

#if UNITY_EDITOR
                var guids = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(CsvReaderConfig)}");
                if (guids.Length > 0)
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    _cached = UnityEditor.AssetDatabase.LoadAssetAtPath<CsvReaderConfig>(path);
                    if (guids.Length > 1)
                        Debug.LogWarning(
                            $"[CsvReader] Tìm thấy {guids.Length} CsvReaderConfig. Đang dùng: {path}");
                }
#endif

                if (_cached == null)
                    _cached = ScriptableObject.CreateInstance<CsvReaderConfig>();

                return _cached;
            }
        }

        /// <summary>
        ///     Xoá cache để lần truy cập sau resolve lại (dùng khi tạo/sửa asset config trong editor).
        /// </summary>
        public static void Invalidate()
        {
            _cached = null;
        }

        #endregion
    }
}
