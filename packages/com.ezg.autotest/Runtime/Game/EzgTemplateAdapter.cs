using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Adapter cho project dựng trên template EZG: UIManager (Show/CloseFeature theo enum GameEnums.Features),
    ///     PlayerResource + EnumBase.MoneyTypes, DataPlayer (com.ezg.factory), RewardsService, PurchaseManager,
    ///     DataManager. Mọi thứ qua reflection theo tên type trong settings → không phụ thuộc version template;
    ///     phần nào không tìm thấy thì tắt đúng capability đó.
    /// </summary>
    public class EzgTemplateAdapter : GenericGameAdapter, IFeaturePrefabResolver
    {
        const long RESOURCE_TYPE_MONEY_FALLBACK = 1;
        const string SOURCE_TAG = "autotest";

        AdapterCapabilities _caps = AdapterCapabilities.ReadyState;

        // UI
        Type _uiType;
        Type _featuresEnum;
        MethodInfo _show;
        MethodInfo _closeFeature;
        MethodInfo _isFeatureShowing;
        MethodInfo _getFeatureObject;
        MethodInfo _getAllActive;
        FieldInfo _outFeaturesField;
        MethodInfo _hasShowingInGroup;
        object _mainGroupValue;
        readonly List<AutoTestFeature> _features = new();
        readonly Dictionary<long, AutoTestFeature> _featureByValue = new();

        // Economy
        Type _resourceType;
        Type _moneyEnum;
        MethodInfo _isEnough;
        MethodInfo _removeCurrency;
        MethodInfo _removeCurrencyGuarded;
        MethodInfo _addCurrency;
        MethodInfo _setCurrency;
        MethodInfo _getCurrencyValue;
        PropertyInfo _resourceModuleProp;
        readonly List<AutoTestCurrency> _currencies = new();

        // Data
        Type _dataPlayerType;
        Type _dataPlayerBaseType;
        Type _playerDataManagerType;

        // Reward / purchase / config
        MethodInfo _receiveReward;
        Type _rewardResourceType;
        long _moneyResourceType = RESOURCE_TYPE_MONEY_FALLBACK;
        MethodInfo _purchaseOffline;
        Type _dataManagerType;

        public override string Name => "EZG Template";
        public override int Priority => 50;
        public override AdapterCapabilities Capabilities => _caps;

        public override bool Initialize(AutoTestConfig config)
        {
            _config = config;
            _diagnostics.Clear();
            var cfg = config.adapter;

            ResolveUi(cfg);
            if (_uiType == null)
            {
                _diagnostics.Add($"Không tìm thấy '{cfg.uiManagerType}' có hàm Show(enum…) → không phải template EZG.");
                return false;
            }

            ResolveEconomy(cfg);
            ResolveData(cfg);
            ResolveRewardAndPurchase(cfg);
            ResolveConfig(cfg);
            _diagnostics.Insert(0, $"Template EZG: capabilities = {_caps}");
            return true;
        }

        #region Resolve

        void ResolveUi(AdapterConfig cfg)
        {
            foreach (var t in AutoTestRegistry.FindTypes(cfg.uiManagerType))
            {
                var show = Reflect.FindMethod(t, "Show", ps => ps.Length >= 1 && ps[0].ParameterType.IsEnum, false);
                if (show == null) continue;
                _uiType = t;
                _show = show;
                break;
            }

            if (_uiType == null) return;

            // Enum feature = kiểu tham số đầu của Show (chắc chắn đúng hơn tên trong settings).
            _featuresEnum = _show.GetParameters()[0].ParameterType;
            var configured = AutoTestRegistry.FindTypes(cfg.featuresEnumType).FirstOrDefault(t => t.IsEnum);
            if (configured != null && configured != _featuresEnum)
                _diagnostics.Add($"Lưu ý: Show() nhận {_featuresEnum.FullName}, khác '{cfg.featuresEnumType}' trong settings.");

            bool FirstIsFeature(ParameterInfo[] ps) => ps.Length >= 1 && ps[0].ParameterType == _featuresEnum;
            _closeFeature = Reflect.FindMethod(_uiType, "CloseFeature", FirstIsFeature);
            _isFeatureShowing = Reflect.FindMethod(_uiType, "IsFeatureShowing", FirstIsFeature) ??
                                Reflect.FindMethod(_uiType, "IsFeatureActiving", FirstIsFeature);
            _getFeatureObject = Reflect.FindMethod(_uiType, "GetFeatureObject", FirstIsFeature);
            _getAllActive = Reflect.FindMethod(_uiType, "GetAllFeatureActivating", ps => ps.Length == 0);
            _outFeaturesField = _uiType.GetField("_outFeaturesActiving", Reflect.ALL_INSTANCE);
            _hasShowingInGroup = Reflect.FindMethod(_uiType, "HasShowingFeatureInGroup",
                ps => ps.Length == 1 && ps[0].ParameterType.IsEnum);
            if (_hasShowingInGroup != null)
            {
                var groupEnum = _hasShowingInGroup.GetParameters()[0].ParameterType;
                var names = Enum.GetNames(groupEnum);
                var main = names.FirstOrDefault(n => n.StartsWith("Main", StringComparison.OrdinalIgnoreCase));
                if (main != null) _mainGroupValue = Enum.Parse(groupEnum, main);
            }

            foreach (var name in Enum.GetNames(_featuresEnum))
            {
                var raw = Enum.Parse(_featuresEnum, name);
                var value = Convert.ToInt64(raw);
                if (_featureByValue.ContainsKey(value)) continue; // alias cùng giá trị
                var f = new AutoTestFeature { Name = name, Value = value, Raw = raw };
                _features.Add(f);
                _featureByValue[value] = f;
            }

            if (_closeFeature != null && (_getFeatureObject != null || _isFeatureShowing != null))
                _caps |= AdapterCapabilities.Features;
            _diagnostics.Add($"UI: {_uiType.FullName}, enum {_featuresEnum.FullName} ({_features.Count} feature), " +
                             $"Close={(_closeFeature != null)}, GetObject={(_getFeatureObject != null)}");
        }

        void ResolveEconomy(AdapterConfig cfg)
        {
            foreach (var t in AutoTestRegistry.FindTypes(cfg.playerResourceType))
            {
                var isEnough = Reflect.FindMethod(t, "IsEnough",
                    ps => ps.Length >= 2 && ps[0].ParameterType.IsEnum && IsNumeric(ps[1].ParameterType));
                if (isEnough == null) continue;
                _resourceType = t;
                _isEnough = isEnough;
                _moneyEnum = isEnough.GetParameters()[0].ParameterType;
                break;
            }

            if (_resourceType == null)
            {
                _diagnostics.Add($"Economy: không tìm thấy '{cfg.playerResourceType}.IsEnough(enum, số)'.");
                return;
            }

            bool EnumThenNumber(ParameterInfo[] ps) =>
                ps.Length >= 2 && ps[0].ParameterType == _moneyEnum && IsNumeric(ps[1].ParameterType);

            // Ưu tiên overload có callback (bản "an toàn" tự kiểm IsEnough — đúng đường game tiêu tiền); không có
            // thì dùng overload thô (caller tự kiểm) và suy kết quả từ số dư.
            _removeCurrencyGuarded = Reflect.FindMethod(_resourceType, "RemoveCurrency",
                ps => EnumThenNumber(ps) && ps.Length >= 3 && IsCallback(ps[2].ParameterType));
            _removeCurrency = Reflect.FindMethod(_resourceType, "RemoveCurrency",
                                  ps => EnumThenNumber(ps) && (ps.Length == 2 || ps[2].ParameterType == typeof(string))) ??
                              Reflect.FindMethod(_resourceType, "RemoveCurrency", EnumThenNumber);
            _addCurrency = Reflect.FindMethod(_resourceType, "AddCurrency", EnumThenNumber);
            _setCurrency = Reflect.FindMethod(_resourceType, "SetCurrency", EnumThenNumber);
            _getCurrencyValue = Reflect.FindMethod(_resourceType, "GetCurrencyValue",
                ps => ps.Length == 1 && ps[0].ParameterType == _moneyEnum);

            _playerDataManagerType = AutoTestRegistry.FindTypes(cfg.playerDataManagerType).FirstOrDefault();
            _resourceModuleProp = _playerDataManagerType?.GetProperties(Reflect.ALL_STATIC)
                .FirstOrDefault(p => p.PropertyType == _resourceType);

            foreach (var name in Enum.GetNames(_moneyEnum))
            {
                if (AutoTestFilters.MatchesAny(name, cfg.pseudoCurrencies.Select(p => "^" + p + "$"))) continue;
                var raw = Enum.Parse(_moneyEnum, name);
                var value = Convert.ToInt64(raw);
                if (_currencies.Any(c => c.Value == value)) continue;
                _currencies.Add(new AutoTestCurrency { Name = name, Value = value, Raw = raw });
            }

            if (_getCurrencyValue != null && _addCurrency != null && _removeCurrency != null)
                _caps |= AdapterCapabilities.Economy;
            _diagnostics.Add($"Economy: {_resourceType.FullName}, enum {_moneyEnum.FullName} " +
                             $"({_currencies.Count} loại), Add={(_addCurrency != null)}, Remove={(_removeCurrency != null)}, " +
                             $"Get={(_getCurrencyValue != null)}, module={_resourceModuleProp?.Name ?? "?"}");
        }

        void ResolveData(AdapterConfig cfg)
        {
            _dataPlayerType = AutoTestRegistry.FindTypes("DataPlayer").FirstOrDefault(t =>
                Reflect.FindMethod(t, "SaveAllData", ps => ps.Length == 0, true) != null);
            _dataPlayerBaseType = AutoTestRegistry.FindTypes("DataPlayerBase").FirstOrDefault();
            if (_dataPlayerType != null) _caps |= AdapterCapabilities.PlayerData;
            _diagnostics.Add($"PlayerData: DataPlayer={(_dataPlayerType?.FullName ?? "không có")}, " +
                             $"{GetSaveKeys().Count} key lưu");
        }

        void ResolveRewardAndPurchase(AdapterConfig cfg)
        {
            var rewards = AutoTestRegistry.FindTypes(cfg.rewardsServiceType).FirstOrDefault();
            if (rewards != null)
            {
                _receiveReward = Reflect.FindMethod(rewards, "ReceiveReward",
                    ps => ps.Length >= 1 && ps[0].ParameterType.Name == "Resource" && !ps[0].ParameterType.IsArray,
                    true);
                _rewardResourceType = _receiveReward?.GetParameters()[0].ParameterType;
                var resTypes = AutoTestRegistry.FindTypes("EnumBase+ResourceTypes").FirstOrDefault();
                var moneyField = resTypes?.GetField("Money", Reflect.ALL_STATIC);
                if (moneyField != null) _moneyResourceType = Convert.ToInt64(moneyField.GetValue(null));
                if (_receiveReward != null && _rewardResourceType != null &&
                    _rewardResourceType.GetConstructor(new[] { typeof(int), typeof(int), typeof(long) }) != null)
                    _caps |= AdapterCapabilities.Rewards;
            }

            var purchase = AutoTestRegistry.FindTypes(cfg.purchaseManagerType).FirstOrDefault(t =>
                Reflect.FindMethod(t, "PurchaseOffline", null, true) != null);
            if (purchase != null && _moneyEnum != null)
            {
                _purchaseOffline = Reflect.FindMethod(purchase, "PurchaseOffline",
                    ps => ps.Length >= 2 && ps[0].ParameterType == _moneyEnum && IsNumeric(ps[1].ParameterType), true);
                if (_purchaseOffline != null) _caps |= AdapterCapabilities.PurchaseOffline;
            }

            _diagnostics.Add($"Reward={(_receiveReward != null)}, PurchaseOffline={(_purchaseOffline != null)}");
        }

        void ResolveConfig(AdapterConfig cfg)
        {
            _dataManagerType = AutoTestRegistry.FindTypes(cfg.dataManagerType).FirstOrDefault(t =>
                t.GetProperties(Reflect.ALL_STATIC).Any(p => typeof(Object).IsAssignableFrom(p.PropertyType)));
            if (_dataManagerType != null) _caps |= AdapterCapabilities.Config;
        }

        static bool IsNumeric(Type t)
        {
            return t == typeof(int) || t == typeof(long) || t == typeof(double) || t == typeof(float) ||
                   t == typeof(decimal);
        }

        #endregion

        #region Vòng đời

        /// <summary>UIManager đã khởi tạo mà KHÔNG gọi Instance (Instance tự tạo bản rỗng nếu chưa có).</summary>
        Object FindUiManager()
        {
            return _uiType == null ? null : Object.FindFirstObjectByType(_uiType);
        }

        public override bool IsGameReady()
        {
            var scene = SceneManager.GetActiveScene();
            if (IsBootScene(scene)) return false;
            var ui = FindUiManager();
            if (ui == null) return false;
            if (Reflect.HasStatic(_uiType, "IsEnableTouch") && !(bool)Reflect.GetStatic(_uiType, "IsEnableTouch"))
                return false;
            if (Reflect.HasStatic(_uiType, "IsShowLoading") && (bool)Reflect.GetStatic(_uiType, "IsShowLoading"))
                return false;
            if (IsLoadingScene()) return false;
            if (_hasShowingInGroup != null && _mainGroupValue != null)
                return (bool)Reflect.Invoke(_hasShowingInGroup, ui, new[] { _mainGroupValue });
            return GetOpenFeatures().Count > 0;
        }

        /// <summary>GameSystems.OverviewCanvasController.IsLoadingScene nếu project có.</summary>
        bool IsLoadingScene()
        {
            try
            {
                var gs = AutoTestRegistry.FindTypes("GameSystems").FirstOrDefault();
                var overview = Reflect.GetStatic(gs, "OverviewCanvasController");
                var loading = Reflect.GetMember(overview, "IsLoadingScene");
                return loading is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        public override string DescribeState()
        {
            var sb = new StringBuilder();
            var scene = SceneManager.GetActiveScene();
            sb.Append($"scene='{scene.name}' (boot={IsBootScene(scene)}), ");
            var ui = FindUiManager();
            sb.Append($"UIManager={(ui != null ? "có" : "chưa có")}");
            if (Reflect.HasStatic(_uiType, "IsEnableTouch")) sb.Append($", touch={Reflect.GetStatic(_uiType, "IsEnableTouch")}");
            if (Reflect.HasStatic(_uiType, "IsShowLoading")) sb.Append($", loading={Reflect.GetStatic(_uiType, "IsShowLoading")}");
            sb.Append($", loadingScene={IsLoadingScene()}");
            if (ui != null) sb.Append($", màn đang mở=[{string.Join(", ", GetOpenFeatures().Select(f => f.Name))}]");
            return sb.ToString();
        }

        #endregion

        #region Màn hình

        public override IReadOnlyList<AutoTestFeature> GetFeatures()
        {
            return _features;
        }

        public AutoTestFeature FindFeature(string name)
        {
            return _features.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public override async Task<GameObject> ShowFeature(AutoTestFeature feature, object data, float timeoutSeconds,
            CancellationToken ct)
        {
            var ui = FindUiManager();
            if (ui == null) throw new InvalidOperationException("UIManager chưa khởi tạo.");
            var named = new List<(string, object)>();
            if (data != null) named.Add(("data", data));
            var awaitable = Reflect.Invoke(_show, ui, new[] { feature.Raw }, named.ToArray());
            var result = await ReflectionAwait.Await(awaitable, timeoutSeconds, ct);
            if (result is GameObject go && go != null) return go;
            return GetFeatureObject(feature);
        }

        public override void CloseFeature(AutoTestFeature feature)
        {
            var ui = FindUiManager();
            if (ui == null || _closeFeature == null) return;
            Reflect.Invoke(_closeFeature, ui, new[] { feature.Raw });
        }

        public override bool IsFeatureShowing(AutoTestFeature feature)
        {
            var ui = FindUiManager();
            if (ui == null) return false;
            if (_isFeatureShowing != null) return (bool)Reflect.Invoke(_isFeatureShowing, ui, new[] { feature.Raw });
            return GetFeatureObject(feature) != null;
        }

        public override GameObject GetFeatureObject(AutoTestFeature feature)
        {
            var ui = FindUiManager();
            if (ui == null || _getFeatureObject == null) return null;
            var go = Reflect.Invoke(_getFeatureObject, ui, new[] { feature.Raw }) as GameObject;
            return go != null ? go : null;
        }

        public override IReadOnlyList<AutoTestFeature> GetOpenFeatures()
        {
            var list = new List<AutoTestFeature>();
            var ui = FindUiManager();
            if (ui == null) return list;
            void AddFrom(object dict)
            {
                if (!(dict is IDictionary d)) return;
                foreach (DictionaryEntry e in d)
                {
                    if (e.Value is Object o && o == null) continue;
                    if (_featureByValue.TryGetValue(Convert.ToInt64(e.Key), out var f) && !list.Contains(f)) list.Add(f);
                }
            }

            if (_getAllActive != null) AddFrom(Reflect.Invoke(_getAllActive, ui, null));
            if (_outFeaturesField != null) AddFrom(_outFeaturesField.GetValue(ui));
            return list;
        }

        /// <summary>Quy ước template EZG: "screen_" + snake_case tên enum (Utils.ToSnakeCase).</summary>
        public string ResolvePrefabName(AutoTestFeature feature)
        {
            return "screen_" + ToSnakeCase(feature.Name);
        }

        public static string ToSnakeCase(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length + 8);
            for (var i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i])) sb.Append('_');
                sb.Append(s[i]);
            }

            return sb.ToString().ToLowerInvariant();
        }

        #endregion

        #region Economy

        public override IReadOnlyList<AutoTestCurrency> GetCurrencies()
        {
            return _currencies;
        }

        object ResourceModule()
        {
            return _resourceModuleProp?.GetValue(null);
        }

        public override double GetBalance(AutoTestCurrency currency)
        {
            if (_getCurrencyValue == null) throw new NotSupportedException("Không có GetCurrencyValue.");
            return Convert.ToDouble(Reflect.Invoke(_getCurrencyValue, null, new[] { currency.Raw }));
        }

        public override void AddCurrency(AutoTestCurrency currency, double amount)
        {
            if (_addCurrency == null) throw new NotSupportedException("Không có AddCurrency.");
            var target = _addCurrency.IsStatic ? null : ResourceModule();
            if (!_addCurrency.IsStatic && target == null) throw new InvalidOperationException("Module PlayerResource null.");
            Reflect.Invoke(_addCurrency, target, new[] { currency.Raw, amount },
                ("isRunAnimation", false), ("updateResource", true));
        }

        public override bool RemoveCurrency(AutoTestCurrency currency, double amount)
        {
            if (_removeCurrencyGuarded != null) return RemoveGuarded(currency, amount);
            if (_removeCurrency == null) throw new NotSupportedException("Không có RemoveCurrency.");
            var before = GetBalance(currency);
            var target = _removeCurrency.IsStatic ? null : ResourceModule();
            var ret = Reflect.Invoke(_removeCurrency, target, new[] { currency.Raw, amount }, ("source", SOURCE_TAG));
            if (ret is bool b) return b;
            // Template trả void → thành công khi số dư thật sự giảm.
            return GetBalance(currency) < before - double.Epsilon || amount <= 0;
        }

        /// <summary>Gọi overload có callback: thành công ⇔ callback thành công được gọi.</summary>
        bool RemoveGuarded(AutoTestCurrency currency, double amount)
        {
            var success = false;
            var ps = _removeCurrencyGuarded.GetParameters();
            var named = new List<(string, object)> { ("source", SOURCE_TAG) };
            object onSuccess = MakeCallback(ps[2].ParameterType, () => success = true);
            foreach (var p in ps)
                if (p.Position > 2 && IsCallback(p.ParameterType) &&
                    p.Name.IndexOf("success", StringComparison.OrdinalIgnoreCase) >= 0)
                    named.Add((p.Name, MakeCallback(p.ParameterType, () => { })));
            var target = _removeCurrencyGuarded.IsStatic ? null : ResourceModule();
            Reflect.Invoke(_removeCurrencyGuarded, target, new[] { currency.Raw, amount, onSuccess }, named.ToArray());
            return success;
        }

        static bool IsCallback(Type t)
        {
            return t == typeof(UnityEngine.Events.UnityAction) || t == typeof(Action);
        }

        static object MakeCallback(Type t, Action body)
        {
            if (t == typeof(UnityEngine.Events.UnityAction)) return new UnityEngine.Events.UnityAction(body);
            return body;
        }

        public override bool IsEnough(AutoTestCurrency currency, double amount)
        {
            return (bool)Reflect.Invoke(_isEnough, null, new[] { currency.Raw, amount });
        }

        public override void SetCurrency(AutoTestCurrency currency, double amount)
        {
            if (_setCurrency == null) throw new NotSupportedException("Không có SetCurrency.");
            var target = _setCurrency.IsStatic ? null : ResourceModule();
            Reflect.Invoke(_setCurrency, target, new[] { currency.Raw, amount });
        }

        public override Type BalanceType(AutoTestCurrency currency)
        {
            return _getCurrencyValue?.ReturnType ?? typeof(double);
        }

        public override bool GrantReward(AutoTestCurrency currency, double amount)
        {
            if (_receiveReward == null || _rewardResourceType == null) return false;
            var reward = Activator.CreateInstance(_rewardResourceType,
                (int)_moneyResourceType, (int)currency.Value, (long)amount);
            Reflect.Invoke(_receiveReward, null, new[] { reward }, ("isShowPopup", false), ("source", SOURCE_TAG),
                ("isSpawnCurrency", false));
            return true;
        }

        public override Task<bool?> PurchaseOffline(AutoTestCurrency currency, double cost, float timeoutSeconds,
            CancellationToken ct)
        {
            if (_purchaseOffline == null) return Task.FromResult<bool?>(null);
            var ret = Reflect.Invoke(_purchaseOffline, null, new[] { currency.Raw, cost }, ("isShowMes", false),
                ("isOpenShop", false), ("source", SOURCE_TAG));
            return Task.FromResult<bool?>(ret is bool b ? b : (bool?)null);
        }

        #endregion

        #region Dữ liệu

        public override void SaveAll()
        {
            if (_dataPlayerType != null)
                Reflect.Invoke(Reflect.FindMethod(_dataPlayerType, "SaveAllData", ps => ps.Length == 0, true), null,
                    null);
            else if (ResourceModule() is { } module)
                Reflect.Invoke(Reflect.FindMethod(module.GetType(), "Save", ps => ps.Length == 0, false), module, null);
            PlayerPrefs.Save();
        }

        /// <summary>
        ///     Mỗi module DataPlayerBase lưu ở key = tên type đầy đủ (GetType().ToString()) + key gộp "ALL_DATA".
        ///     Tính bằng reflection nên gọi được ở Edit mode để sandbox sao lưu trước khi vào Play.
        /// </summary>
        public override IReadOnlyList<string> GetSaveKeys()
        {
            var keys = new List<string>();
            if (_config != null) keys.AddRange(_config.general.extraSaveKeys);
            if (_dataPlayerBaseType == null) return keys;
            foreach (var t in AutoTestRegistry.FindSubclasses(_dataPlayerBaseType))
                keys.Add(t.ToString());
            if (_dataPlayerType != null)
            {
                var allDataConst = _dataPlayerType.GetField("ALL_DATA", Reflect.ALL_STATIC);
                keys.Add(allDataConst?.GetValue(null) as string ?? "ALL_DATA");
            }

            return keys.Distinct().ToList();
        }

        public override bool ReloadPlayerData()
        {
            if (_dataPlayerType == null) return false;
            var reload = Reflect.FindMethod(_dataPlayerType, "ReloadAllData", ps => ps.Length == 0, true) ??
                         Reflect.FindMethod(_dataPlayerType, "LoadAllData", ps => ps.Length == 0, true);
            if (reload == null) return false;
            Reflect.Invoke(reload, null, null);
            var clear = Reflect.FindMethod(_playerDataManagerType, "ClearCachedModules", ps => ps.Length == 0, true);
            if (clear != null) Reflect.Invoke(clear, null, null);
            return true;
        }

        #endregion

        #region Config

        /// <summary>Mọi property static của DataManager trả về ScriptableObject (accessor sinh từ CSV).</summary>
        public override IReadOnlyList<AutoTestConfigTable> GetConfigCollections()
        {
            var list = new List<AutoTestConfigTable>();
            if (_dataManagerType == null) return list;
            foreach (var p in _dataManagerType.GetProperties(Reflect.ALL_STATIC))
            {
                if (!typeof(ScriptableObject).IsAssignableFrom(p.PropertyType) || p.GetIndexParameters().Length > 0)
                    continue;
                Object asset;
                try
                {
                    asset = p.GetValue(null) as Object;
                }
                catch (Exception e)
                {
                    list.Add(new AutoTestConfigTable { Name = p.Name + " (lỗi load: " + e.GetBaseException().Message + ")" });
                    continue;
                }

                var collection = new AutoTestConfigTable { Name = p.Name, Asset = asset };
                if (asset != null) FillItems(collection, asset);
                list.Add(collection);
            }

            return list;
        }

        static void FillItems(AutoTestConfigTable collection, Object asset)
        {
            foreach (var f in asset.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var ft = f.FieldType;
                Type item = null;
                if (ft.IsArray) item = ft.GetElementType();
                else if (ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(List<>))
                    item = ft.GetGenericArguments()[0];
                if (item == null || typeof(Object).IsAssignableFrom(item) || item.IsPrimitive || item == typeof(string))
                    continue;
                collection.ItemType = item;
                collection.Items = f.GetValue(asset) as IList ?? Array.Empty<object>();
                return;
            }

            // Bảng một dòng (vd GeneralConfig): field class serializable đầu tiên.
            foreach (var f in asset.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var ft = f.FieldType;
                if (!ft.IsClass || ft == typeof(string) || typeof(Object).IsAssignableFrom(ft)) continue;
                collection.ItemType = ft;
                var v = f.GetValue(asset);
                collection.Items = v != null ? new List<object> { v } : new List<object>();
                return;
            }
        }

        #endregion
    }
}
