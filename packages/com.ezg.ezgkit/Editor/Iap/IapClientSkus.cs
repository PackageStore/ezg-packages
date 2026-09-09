#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Ezg.Editor.Shared.Iap
{
    /// <summary>Hai nền tảng store + khoá (nền tảng, productId) dùng chung cho cả phía client và phía store.</summary>
    internal static class IapPlatform
    {
        internal const string ANDROID = "android";
        internal const string IOS = "ios";

        internal static readonly string[] All = { ANDROID, IOS };

        internal static string Key(string platform, string productId) =>
            (platform ?? string.Empty).Trim().ToLowerInvariant() + "|" + (productId ?? string.Empty).Trim();
    }

    /// <summary>Một gói bán đọc được trong data của project, kèm phán quyết "client CÓ đăng ký hay không".</summary>
    internal sealed class IapClientSku
    {
        /// <summary>Tên asset collection chứa gói (CoinPack, VipPassPackage…).</summary>
        internal string Collection;

        internal string AssetPath;
        internal int Id;
        internal string PackName;
        internal string GoogleId;
        internal string PremiumId;
        internal string AppleId;
        internal float Cost;

        /// <summary>
        ///     Id mà getter <c>ProductId</c> sinh ra khi gói CHƯA điền id nào (<c>&lt;snake_type&gt;_&lt;id&gt;</c>)
        ///     — không phải id thật trên store, chỉ dùng để nhận ra ca đó.
        /// </summary>
        internal string FallbackId;

        /// <summary>Id mà <c>GetAllProductId()</c> thật sự trả về cho gói này. Rỗng = client KHÔNG đăng ký.</summary>
        internal string RegisteredId;

        /// <summary>Client đăng ký gói này bằng <see cref="FallbackId" /> vì data chưa điền product id.</summary>
        internal bool GeneratedId;

        /// <summary>Client khai báo id này là Non-Consumable (theo <c>GetNonConsumableProducts()</c>).</summary>
        internal bool NonConsumable;

        internal bool Registered => !string.IsNullOrEmpty(RegisteredId);

        internal bool HasStoreId => !string.IsNullOrEmpty(GoogleId) || !string.IsNullOrEmpty(AppleId);

        internal string Label => string.IsNullOrEmpty(PackName)
            ? "#" + Id.ToString(CultureInfo.InvariantCulture)
            : PackName;

        /// <summary>Id của một nền tảng. Rỗng = data chưa điền cho nền tảng đó.</summary>
        internal string IdOf(string platform) => platform == IapPlatform.IOS ? AppleId : GoogleId;
    }

    /// <summary>Kết quả MỘT lượt đọc phía client, đóng băng lại cho lúc vẽ.</summary>
    internal sealed class IapClientCatalog
    {
        /// <summary>Mọi gói bán tìm thấy trong data — kể cả gói client không đăng ký (dữ kiện drift).</summary>
        internal readonly List<IapClientSku> Skus = new();

        /// <summary>Đúng những gì <c>GetAllProductId()</c> trả về: giữ thứ tự, đã bỏ trùng.</summary>
        internal readonly List<string> RegisteredIds = new();

        /// <summary>Id đăng ký mà không map được về gói nào trong data.</summary>
        internal readonly List<string> UnmatchedIds = new();

        /// <summary>Id xuất hiện nhiều hơn một lần trong danh sách đăng ký = hai gói dùng chung một SKU.</summary>
        internal readonly List<string> DuplicateIds = new();

        /// <summary>Câu lỗi hiện thẳng cho dev. Null = đọc được.</summary>
        internal string Error;

        /// <summary>Hàm đang được dùng làm nguồn chuẩn, ghi đủ namespace để không ai đoán.</summary>
        internal string SourceLabel;

        /// <summary>Hàm cho cờ Non-Consumable. Null = project không có, tab không suy đoán hộ.</summary>
        internal string NonConsumableSource;

        internal int NonConsumableCount;

        /// <summary>Bundle id Editor đang mang — quyết định getter <c>ProductId</c> trả id thường hay premium.</summary>
        internal string BundleId;

        /// <summary>Số asset collection đã đọc để tìm gói.</summary>
        internal int PackAssetCount;

        internal bool IsValid => Error == null;

        internal int RegisteredCount => RegisteredIds.Count;
    }

    /// <summary>
    ///     Nguồn chuẩn của tab IAP: <c>ShopService.GetAllProductId()</c> gọi qua reflection — ĐÚNG danh
    ///     sách client gửi cho store lúc boot, không phải một asset khai báo song song có thể lệch.
    ///     <para>
    ///         <b>Vì sao reflection:</b> kit là package dùng chung, không tham chiếu assembly game được.
    ///         Hàm nằm trong assembly của project nên chỉ tìm được lúc chạy trong Editor. Gọi được là vì
    ///         <c>DataManager.Get&lt;T&gt;</c> đi qua <c>Resources.Load</c>, thứ chạy bình thường ngoài
    ///         Play mode.
    ///     </para>
    ///     <para>
    ///         <b>Reflection chỉ trả về CHUỖI id</b>, mà tab còn cần tên gói, giá và id nền tảng còn lại
    ///         (trong Editor getter <c>ProductId</c> chỉ trả nhánh Android/premium theo bundle hiện tại).
    ///         Nên sau khi có danh sách chuẩn, <see cref="Collect" /> đọc thêm các asset collection trong
    ///         project để map id → gói. Asset chỉ để TRA CỨU: "có đăng ký hay không" luôn do danh sách
    ///         của <c>GetAllProductId()</c> quyết.
    ///     </para>
    ///     <para>
    ///         Asset nào chứa gói bán được tìm theo CẤU TRÚC, không theo đường dẫn hay tên: một type
    ///         <see cref="ScriptableObject" /> mà graph field của nó chạm tới một class có cả
    ///         <c>googleProductId</c> lẫn <c>appleProductId</c>. Nhờ vậy không cần biết project đặt tên
    ///         collection là gì, cũng không phải load 400 asset trong <c>Resources</c> để dò.
    ///     </para>
    /// </summary>
    internal static class IapClientSkus
    {
        #region Constants

        private const string SERVICE_TYPE = "ShopService";
        private const string SERVICE_METHOD = "GetAllProductId";
        private const string NON_CONSUMABLE_METHOD = "GetNonConsumableProducts";
        private const string PURCHASING_INTERFACE = "IPurchasing";

        private const string FIELD_GOOGLE = "googleProductId";
        private const string FIELD_GOOGLE_PREMIUM = "googleProductIdPremium";
        private const string FIELD_APPLE = "appleProductId";
        private const string FIELD_PACK_NAME = "packName";
        private const string FIELD_COST = "iapCost";
        private const string FIELD_ID = "id";

        private const BindingFlags FIELDS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>
        ///     Trần số object duyệt trong MỘT asset — chặn asset data khổng lồ làm treo lượt đọc. Để rộng
        ///     tay: một collection pack thật chỉ tốn vài trăm node, nên trần này chỉ chạm khi có type
        ///     collection lạ trỏ vào một asset data lớn — và lúc đó thà đọc chậm hơn là bỏ mất gói.
        /// </summary>
        private const int WALK_BUDGET = 100000;

        private const int WALK_DEPTH = 6;

        /// <summary>Độ sâu dò graph field lúc tìm type collection (đủ cho <c>dataGroups.packs[]</c>).</summary>
        private const int REACH_DEPTH = 4;

        #endregion

        #region Cache theo domain

        private static HashSet<Type> _packModels;
        private static List<Type> _collectionTypes;

        #endregion

        #region Đọc

        internal static IapClientCatalog Collect()
        {
            var catalog = new IapClientCatalog { BundleId = Application.identifier };

            var method = FindStaticMethod(SERVICE_TYPE, SERVICE_METHOD);
            if (method == null)
            {
                catalog.Error = $"Không tìm thấy `{SERVICE_TYPE}.{SERVICE_METHOD}()` trong project. Tab này lấy "
                                + "đúng hàm đó làm nguồn chuẩn (danh sách client gửi store lúc boot) và cố ý "
                                + "KHÔNG đoán bằng nguồn khác — sai nguồn thì mọi phán quyết bên dưới đều sai.";
                return catalog;
            }

            catalog.SourceLabel = (method.DeclaringType?.FullName ?? SERVICE_TYPE) + "." + method.Name + "()";

            var ids = Invoke(method, out var error);
            if (error != null)
            {
                catalog.Error = $"Gọi `{catalog.SourceLabel}` bị lỗi: {error}. Data thiếu asset hay CSV chưa "
                                + "import? Lỗi này cũng làm store fetch thất bại lúc boot.";
                return catalog;
            }

            var registered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in ids)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;

                var trimmed = id.Trim();
                if (registered.Add(trimmed)) catalog.RegisteredIds.Add(trimmed);
                else if (!catalog.DuplicateIds.Contains(trimmed)) catalog.DuplicateIds.Add(trimmed);
            }

            HarvestPacks(catalog);
            Match(catalog, registered);
            ApplyNonConsumable(catalog);
            return catalog;
        }

        /// <summary>
        ///     Gắn từng id đăng ký về đúng gói trong data, theo cùng thứ tự getter <c>ProductId</c> chọn:
        ///     id Google → id Google bản premium → id Apple → id sinh tự động khi gói chưa điền gì. Bước
        ///     cuối tra bằng ĐỘ KHỚP với danh sách đăng ký nên không có chỗ cho suy đoán: id sinh ra mà
        ///     không nằm trong danh sách thì gói đó đơn giản là không được đăng ký.
        /// </summary>
        private static void Match(IapClientCatalog catalog, HashSet<string> registered)
        {
            var claimed = new HashSet<string>(StringComparer.Ordinal);

            foreach (var sku in catalog.Skus)
            {
                var match = First(registered, sku.GoogleId, sku.PremiumId, sku.AppleId);
                if (match != null)
                {
                    sku.RegisteredId = match;
                    claimed.Add(match);
                    continue;
                }

                if (string.IsNullOrEmpty(sku.FallbackId) || !registered.Contains(sku.FallbackId)) continue;

                sku.RegisteredId = sku.FallbackId;
                sku.GeneratedId = true;
                claimed.Add(sku.FallbackId);
            }

            foreach (var id in catalog.RegisteredIds)
                if (!claimed.Contains(id)) catalog.UnmatchedIds.Add(id);
        }

        private static string First(HashSet<string> registered, params string[] candidates)
        {
            foreach (var candidate in candidates)
                if (!string.IsNullOrEmpty(candidate) && registered.Contains(candidate))
                    return candidate;

            return null;
        }

        private static void ApplyNonConsumable(IapClientCatalog catalog)
        {
            var ids = NonConsumableIds(out var source);
            if (ids == null) return;

            catalog.NonConsumableSource = source;
            catalog.NonConsumableCount = ids.Count;

            foreach (var sku in catalog.Skus)
                if (sku.Registered && ids.Contains(sku.RegisteredId))
                    sku.NonConsumable = true;
        }

        #endregion

        #region Gọi vào assembly của project

        private static List<string> Invoke(MethodInfo method, out string error)
        {
            error = null;
            try
            {
                return ToStrings(method.Invoke(null, null));
            }
            catch (Exception e)
            {
                error = Unwrap(e).Message;
                return new List<string>();
            }
        }

        /// <summary>
        ///     Cờ Non-Consumable lấy từ ĐÚNG hàm client dùng lúc dựng ProductDefinition, không suy từ tên
        ///     gói: đoán "gói tên có chữ remove_ads thì chắc là vĩnh viễn" là thêm một nguồn sai mới.
        ///     Project không có hàm đó → trả null và tab nói rõ là không biết.
        /// </summary>
        private static HashSet<string> NonConsumableIds(out string source)
        {
            source = null;

            foreach (var type in ProjectTypes())
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (!Implements(type, PURCHASING_INTERFACE)) continue;

                var method = type.GetMethod(NON_CONSUMABLE_METHOD,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (method == null) continue;

                try
                {
                    var target = method.IsStatic ? null : Activator.CreateInstance(type);
                    var result = ToStrings(method.Invoke(target, null));
                    source = type.Name + "." + NON_CONSUMABLE_METHOD + "()";
                    return new HashSet<string>(result, StringComparer.Ordinal);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EzgKit] Không đọc được {type.Name}.{NON_CONSUMABLE_METHOD}(): "
                                     + Unwrap(e).Message);
                    return null;
                }
            }

            return null;
        }

        private static MethodInfo FindStaticMethod(string typeName, string methodName)
        {
            MethodInfo fallback = null;

            foreach (var type in ProjectTypes())
            {
                var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null,
                    Type.EmptyTypes, null);
                if (method == null) continue;

                // Tên type khớp thì lấy ngay; không thì giữ làm đường chót — project đổi tên service vẫn
                // chạy được, mà tab thì bày đủ tên type ra để dev tự thấy nó gọi cái gì.
                if (type.Name == typeName) return method;
                fallback ??= method;
            }

            return fallback;
        }

        private static bool Implements(Type type, string interfaceName)
        {
            foreach (var contract in type.GetInterfaces())
                if (contract.Name == interfaceName)
                    return true;

            return false;
        }

        private static List<string> ToStrings(object value)
        {
            var list = new List<string>();
            if (value is IEnumerable enumerable && !(value is string))
                foreach (var item in enumerable)
                    if (item is string text)
                        list.Add(text);

            return list;
        }

        private static Exception Unwrap(Exception e) =>
            e is TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException
                : e;

        #endregion

        #region Đọc gói từ asset data

        private static void HarvestPacks(IapClientCatalog catalog)
        {
            var seenPath = new HashSet<string>(StringComparer.Ordinal);

            foreach (var type in CollectionTypes())
            foreach (var guid in AssetDatabase.FindAssets("t:" + type.Name))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !seenPath.Add(path)) continue;

                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset == null || !type.IsInstanceOfType(asset)) continue;

                catalog.PackAssetCount++;
                var budget = WALK_BUDGET;
                Walk(asset, asset.name, path, catalog.Skus, new HashSet<object>(ReferenceComparer.Instance),
                    ref budget, 0);
            }
        }

        /// <summary>
        ///     Duyệt graph object của một asset để lấy mọi gói bán. KHÔNG đi vào tham chiếu
        ///     <see cref="UnityEngine.Object" /> (sprite, prefab, collection khác) — vừa là nơi không có
        ///     gói bán, vừa là đường duy nhất để graph phình ra vô hạn.
        /// </summary>
        private static void Walk(object node, string collection, string path, List<IapClientSku> into,
            HashSet<object> seen, ref int budget, int depth)
        {
            if (node == null || depth > WALK_DEPTH || budget <= 0) return;
            budget--;

            var type = node.GetType();
            if (PackModels().Contains(type))
            {
                into.Add(ReadPack(node, type, collection, path));
                return;
            }

            if (node is IEnumerable list && !(node is string))
            {
                foreach (var item in list)
                {
                    if (budget <= 0) return;
                    if (!Worth(item, seen)) continue;
                    Walk(item, collection, path, into, seen, ref budget, depth + 1);
                }

                return;
            }

            foreach (var field in type.GetFields(FIELDS))
            {
                if (budget <= 0) return;

                object value;
                try
                {
                    value = field.GetValue(node);
                }
                catch (Exception)
                {
                    continue;
                }

                if (!Worth(value, seen)) continue;
                Walk(value, collection, path, into, seen, ref budget, depth + 1);
            }
        }

        private static bool Worth(object value, HashSet<object> seen)
        {
            if (value == null) return false;

            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string || value is decimal) return false;
            if (value is UnityEngine.Object) return false;

            return seen.Add(value);
        }

        private static IapClientSku ReadPack(object node, Type type, string collection, string path)
        {
            var id = ReadInt(node, type, FIELD_ID);
            return new IapClientSku
            {
                Collection = collection,
                AssetPath = path,
                Id = id,
                PackName = ReadString(node, type, FIELD_PACK_NAME),
                GoogleId = ReadString(node, type, FIELD_GOOGLE),
                PremiumId = ReadString(node, type, FIELD_GOOGLE_PREMIUM),
                AppleId = ReadString(node, type, FIELD_APPLE),
                Cost = ReadFloat(node, type, FIELD_COST),
                FallbackId = SnakeCase(type.Name) + "_" + id.ToString(CultureInfo.InvariantCulture),
            };
        }

        private static string ReadString(object node, Type type, string name)
        {
            var field = type.GetField(name, FIELDS);
            if (field == null || field.FieldType != typeof(string)) return string.Empty;
            return (field.GetValue(node) as string ?? string.Empty).Trim();
        }

        private static float ReadFloat(object node, Type type, string name)
        {
            var field = type.GetField(name, FIELDS);
            var value = field?.GetValue(node);
            return value switch
            {
                float f => f,
                double d => (float)d,
                int i => i,
                _ => 0f,
            };
        }

        private static int ReadInt(object node, Type type, string name)
        {
            var field = type.GetField(name, FIELDS);
            var value = field?.GetValue(node);
            return value is int i ? i : 0;
        }

        /// <summary>
        ///     Cùng phép biến đổi mà getter <c>ProductId</c> dùng để sinh id chữa cháy
        ///     (<c>Utils.ToSnakeCase</c>): chèn <c>_</c> trước mỗi chữ hoa từ ký tự thứ hai, rồi hạ hết
        ///     về chữ thường. Lệch phép này là không nhận ra được ca "gói chưa điền product id".
        /// </summary>
        private static string SnakeCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            var sb = new StringBuilder(name.Length + 8);
            for (var i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i])) sb.Append('_');
                sb.Append(char.ToLowerInvariant(name[i]));
            }

            return sb.ToString();
        }

        #endregion

        #region Tìm type theo cấu trúc

        /// <summary>Class mô tả một gói bán: có cả <c>googleProductId</c> lẫn <c>appleProductId</c>.</summary>
        private static HashSet<Type> PackModels()
        {
            if (_packModels != null) return _packModels;

            _packModels = new HashSet<Type>();
            foreach (var type in ProjectTypes())
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (typeof(UnityEngine.Object).IsAssignableFrom(type)) continue;
                if (StringField(type, FIELD_GOOGLE) == null || StringField(type, FIELD_APPLE) == null) continue;

                _packModels.Add(type);
            }

            return _packModels;
        }

        /// <summary>Type ScriptableObject mà graph field chạm tới một <see cref="PackModels" />.</summary>
        private static List<Type> CollectionTypes()
        {
            if (_collectionTypes != null) return _collectionTypes;

            var models = PackModels();
            _collectionTypes = new List<Type>();
            if (models.Count == 0) return _collectionTypes;

            foreach (var type in ProjectTypes())
            {
                if (type.IsAbstract || !typeof(ScriptableObject).IsAssignableFrom(type)) continue;
                if (Reaches(type, models, 0, new HashSet<Type>())) _collectionTypes.Add(type);
            }

            return _collectionTypes;
        }

        private static bool Reaches(Type type, HashSet<Type> models, int depth, HashSet<Type> seen)
        {
            if (depth > REACH_DEPTH || !seen.Add(type)) return false;

            foreach (var field in type.GetFields(FIELDS))
            {
                var candidate = ElementType(field.FieldType);
                if (candidate == null) continue;
                if (models.Contains(candidate)) return true;
                if (Reaches(candidate, models, depth + 1, seen)) return true;
            }

            return false;
        }

        /// <summary>Kiểu đáng đi tiếp: mở array + <c>List&lt;T&gt;</c>, bỏ primitive/chuỗi/UnityEngine.Object.</summary>
        private static Type ElementType(Type type)
        {
            if (type.IsArray) type = type.GetElementType();
            else if (type.IsGenericType && type.GetGenericArguments().Length == 1
                                       && typeof(IEnumerable).IsAssignableFrom(type))
                type = type.GetGenericArguments()[0];

            if (type == null || type.IsPrimitive || type.IsEnum || type == typeof(string)
                || type == typeof(decimal)) return null;
            if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return null;

            return type;
        }

        private static FieldInfo StringField(Type type, string name)
        {
            var field = type.GetField(name, FIELDS);
            return field != null && field.FieldType == typeof(string) ? field : null;
        }

        /// <summary>Type của project + package, bỏ assembly của Unity/.NET/thư viện.</summary>
        private static IEnumerable<Type> ProjectTypes()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                if (Skip(assembly.GetName().Name)) continue;

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var type in types)
                    if (type != null)
                        yield return type;
            }
        }

        private static bool Skip(string name) =>
            name.StartsWith("Unity", StringComparison.Ordinal)
            || name.StartsWith("System", StringComparison.Ordinal)
            || name.StartsWith("Mono.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft", StringComparison.Ordinal)
            || name.StartsWith("JetBrains", StringComparison.Ordinal)
            || name.StartsWith("Sirenix", StringComparison.Ordinal)
            || name.StartsWith("Newtonsoft", StringComparison.Ordinal)
            || name.StartsWith("nunit", StringComparison.Ordinal)
            || name == "mscorlib" || name == "netstandard";

        /// <summary>
        ///     So sánh theo THAM CHIẾU. Model của game có thể override <c>Equals</c> (so theo id), lúc đó
        ///     một HashSet mặc định sẽ coi hai gói khác nhau là một và bỏ mất gói.
        /// </summary>
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new();

            public new bool Equals(object a, object b) => ReferenceEquals(a, b);

            public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
        }

        #endregion
    }
}
#endif
