#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Ezg.Editor.Shared.Iap
{
    /// <summary>Một SKU IAP đọc từ <c>ShopPackCatalog</c> — đúng những gì client sẽ đăng ký với store.</summary>
    internal sealed class IapSku
    {
        internal string TableLabel;
        internal string PackName;
        internal string GoogleId;
        internal string AppleId;
        internal float Cost;
        internal bool NonConsumable;
        internal bool Enabled;

        /// <summary>Loại sản phẩm theo cách ghi trong sheet giá (cột <c>product_type</c>).</summary>
        internal string ProductType => NonConsumable ? "non_consumable" : "consumable";

        /// <summary>Tên đọc được cho cột <c>reference_name</c> / <c>title</c>: <c>net_pack_diamond</c> → "Net Pack Diamond".</summary>
        internal string ReferenceName => IapSkuCatalog.ReferenceName(PackName);
    }

    /// <summary>
    ///     Đọc danh sách SKU IAP từ asset <c>ShopPackCatalog</c> qua SerializedObject — không cần tham
    ///     chiếu assembly game (kit dùng chung nhiều dự án). Cùng cách đọc với Readiness: bảng
    ///     <c>tables[].isEnabled/isNonConsumable/table</c>, gói <c>dataGroups[].purchaseList[].purchaseType</c>
    ///     + <c>googleProductId/appleProductId/iapCost</c>.
    /// </summary>
    internal static class IapSkuCatalog
    {
        #region Constants

        private const int PURCHASE_TYPE_IAP = 3;
        private const string SHOP_CATALOG_TYPE = "ShopPackCatalog";

        /// <summary>Hậu tố thường gặp trong pack_name nhưng không ai muốn thấy trong tên gói trên store.</summary>
        private const string IAP_SUFFIX = "_iap";

        #endregion

        #region Read

        /// <summary>
        ///     Mọi gói IAP trong catalog (kể cả bảng đang tắt — cờ <see cref="IapSku.Enabled" /> nói bảng có
        ///     đăng ký với store hay không). <paramref name="catalogPath" /> null = project chưa có catalog.
        /// </summary>
        internal static List<IapSku> Collect(out string catalogPath)
        {
            var result = new List<IapSku>();
            catalogPath = null;

            var guids = AssetDatabase.FindAssets("t:" + SHOP_CATALOG_TYPE);
            if (guids.Length == 0) return result;

            catalogPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            var catalog = AssetDatabase.LoadAssetAtPath<ScriptableObject>(catalogPath);
            if (catalog == null) return result;

            var tables = new SerializedObject(catalog).FindProperty("tables");
            if (tables == null || !tables.isArray) return result;

            for (var t = 0; t < tables.arraySize; t++)
            {
                var entry = tables.GetArrayElementAtIndex(t);
                var label = entry.FindPropertyRelative("label")?.stringValue ?? $"bảng {t}";
                var enabled = entry.FindPropertyRelative("isEnabled")?.boolValue ?? true;
                var nonConsumable = entry.FindPropertyRelative("isNonConsumable")?.boolValue ?? false;
                var table = entry.FindPropertyRelative("table")?.objectReferenceValue as ScriptableObject;
                if (table == null) continue;

                var groups = new SerializedObject(table).FindProperty("dataGroups");
                if (groups == null || !groups.isArray) continue;

                for (var i = 0; i < groups.arraySize; i++)
                {
                    var pack = groups.GetArrayElementAtIndex(i);
                    if (!IsIap(pack.FindPropertyRelative("purchaseList"))) continue;

                    result.Add(new IapSku
                    {
                        TableLabel = label,
                        PackName = pack.FindPropertyRelative("packName")?.stringValue ?? $"#{i}",
                        GoogleId = pack.FindPropertyRelative("googleProductId")?.stringValue ?? string.Empty,
                        AppleId = pack.FindPropertyRelative("appleProductId")?.stringValue ?? string.Empty,
                        Cost = pack.FindPropertyRelative("iapCost")?.floatValue ?? 0f,
                        NonConsumable = nonConsumable,
                        Enabled = enabled,
                    });
                }
            }

            return result;
        }

        private static bool IsIap(SerializedProperty purchases)
        {
            if (purchases == null || !purchases.isArray) return false;
            for (var p = 0; p < purchases.arraySize; p++)
            {
                var type = purchases.GetArrayElementAtIndex(p).FindPropertyRelative("purchaseType");
                if (type != null && type.intValue == PURCHASE_TYPE_IAP) return true;
            }

            return false;
        }

        #endregion

        #region Naming

        /// <summary>
        ///     <c>boost_pack_iap</c> → "Boost Pack", <c>gem_pack_2</c> → "Gem Pack 2". Cùng quy ước với các
        ///     dòng GD đã điền tay trong sheet, để dòng tool sinh không lệch kiểu với dòng người.
        /// </summary>
        internal static string ReferenceName(string packName)
        {
            if (string.IsNullOrEmpty(packName)) return string.Empty;
            var name = packName.EndsWith(IAP_SUFFIX, StringComparison.Ordinal)
                ? packName.Substring(0, packName.Length - IAP_SUFFIX.Length)
                : packName;

            var sb = new StringBuilder(name.Length);
            foreach (var word in name.Split(new[] { '_', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(char.ToUpperInvariant(word[0]));
                if (word.Length > 1) sb.Append(word, 1, word.Length - 1);
            }

            return sb.ToString();
        }

        #endregion
    }
}
#endif
