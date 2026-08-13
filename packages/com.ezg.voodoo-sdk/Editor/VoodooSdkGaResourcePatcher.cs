#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ezg.VoodooSdk.Editor
{
    /// <summary>
    /// Khai báo trước danh sách currency + item type cho resource event của GameAnalytics.
    ///
    /// GameAnalytics VỨT BỎ mọi resource event có <c>currency</c> hoặc <c>itemType</c> chưa nằm trong
    /// <c>Settings.asset</c> tại thời điểm SDK init (xem <c>GAValidator.ValidateResourceEvent</c>) — và
    /// chỉ ghi 1 dòng log, không báo lỗi. Danh sách mặc định là RỖNG, nên project mới nào cũng rơi vào
    /// trạng thái "rớt 100% resource event" mà không ai biết cho tới lúc soi dashboard.
    ///
    /// Sửa qua <see cref="SerializedObject"/> thay vì tham chiếu kiểu <c>GameAnalyticsSDK.Setup.Settings</c>:
    /// package này phải compile được cả khi TinySauce CHƯA import (import .unitypackage là bước thủ công),
    /// nên không được phép tham chiếu assembly của GA lúc biên dịch.
    /// </summary>
    public static class VoodooSdkGaResourcePatcher
    {
        #region Fields

        private const string CurrenciesProperty = "ResourceCurrencies";
        private const string ItemTypesProperty = "ResourceItemTypes";

        #endregion

        #region Public

        /// <summary>
        /// Bổ sung các giá trị còn thiếu vào Settings.asset của GameAnalytics. Chỉ THÊM, không xoá thứ
        /// project đã tự khai. Idempotent.
        /// </summary>
        /// <param name="currencies">Tên các loại tiền ảo, ví dụ Gold/Gem.</param>
        /// <param name="itemTypes">Tên các nhóm item, ví dụ Reward/Shop.</param>
        public static void Apply(IEnumerable<string> currencies, IEnumerable<string> itemTypes)
        {
            string[] currencyList = Clean(currencies);
            string[] itemTypeList = Clean(itemTypes);

            if (currencyList.Length == 0 && itemTypeList.Length == 0)
                return;

            if (!File.Exists(VoodooSdkPaths.Absolute(VoodooSdkPaths.GaSettingsAsset)))
            {
                Debug.LogWarning($"[VoodooSdk] Không thấy {VoodooSdkPaths.GaSettingsAsset} — " +
                                 "mở Window > GameAnalytics một lần để SDK tự sinh asset, rồi chạy lại Install. " +
                                 "Chưa có asset thì mọi resource event sẽ bị GA loại bỏ.");
                return;
            }

            var settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(VoodooSdkPaths.GaSettingsAsset);
            if (settings == null)
            {
                Debug.LogWarning($"[VoodooSdk] {VoodooSdkPaths.GaSettingsAsset} chưa load được " +
                                 "(script GameAnalytics chưa compile xong?) — chạy lại Install sau khi compile.");
                return;
            }

            var serialized = new SerializedObject(settings);
            bool changed = AddMissing(serialized, CurrenciesProperty, currencyList);
            changed |= AddMissing(serialized, ItemTypesProperty, itemTypeList);

            if (!changed)
                return;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);

            Debug.Log($"[VoodooSdk] Đã khai báo resource cho GameAnalytics — " +
                      $"currencies: [{string.Join(", ", currencyList)}], " +
                      $"itemTypes: [{string.Join(", ", itemTypeList)}].");
        }

        #endregion

        #region Private

        /// <summary>Thêm các giá trị chưa có vào 1 mảng string của SerializedObject.</summary>
        private static bool AddMissing(SerializedObject serialized, string propertyName, string[] values)
        {
            if (values.Length == 0)
                return false;

            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null || !property.isArray)
            {
                Debug.LogWarning($"[VoodooSdk] Settings.asset của GameAnalytics không có mảng '{propertyName}' " +
                                 "— bản SDK có thể đã đổi cấu trúc. Khai báo tay trong Inspector.");
                return false;
            }

            var existing = new HashSet<string>();
            for (int i = 0; i < property.arraySize; i++)
                existing.Add(property.GetArrayElementAtIndex(i).stringValue);

            bool changed = false;
            foreach (string value in values)
            {
                if (!existing.Add(value))
                    continue;

                property.InsertArrayElementAtIndex(property.arraySize);
                property.GetArrayElementAtIndex(property.arraySize - 1).stringValue = value;
                changed = true;
            }

            return changed;
        }

        /// <summary>Bỏ giá trị rỗng và trùng lặp, giữ nguyên thứ tự.</summary>
        private static string[] Clean(IEnumerable<string> values)
        {
            if (values == null)
                return System.Array.Empty<string>();

            return values.Where(v => !string.IsNullOrWhiteSpace(v))
                         .Select(v => v.Trim())
                         .Distinct()
                         .ToArray();
        }

        #endregion
    }
}
#endif
