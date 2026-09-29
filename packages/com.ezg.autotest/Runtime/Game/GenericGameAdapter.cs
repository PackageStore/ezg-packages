using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Adapter "không biết gì về game": chỉ dựa vào scene + EventSystem. Luôn dùng được — là lưới an toàn khi
    ///     project không theo template EZG. Suite cần màn hình/economy sẽ tự bỏ qua case với lý do rõ ràng.
    /// </summary>
    public class GenericGameAdapter : IGameAdapter
    {
        protected readonly List<string> _diagnostics = new();
        protected AutoTestConfig _config;
        float _sceneChangedAt;
        string _lastScene;

        public virtual string Name => "Generic (scene + EventSystem)";
        public virtual int Priority => 0;
        public virtual AdapterCapabilities Capabilities => AdapterCapabilities.ReadyState;
        public IReadOnlyList<string> Diagnostics => _diagnostics;

        public virtual bool Initialize(AutoTestConfig config)
        {
            _config = config;
            _diagnostics.Add("Adapter chung: chỉ kiểm tra scene/EventSystem, không điều khiển được màn hình/tiền tệ.");
            return true;
        }

        /// <summary>Sẵn sàng khi đã rời scene boot, có EventSystem và scene đứng yên đủ lâu.</summary>
        public virtual bool IsGameReady()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != _lastScene)
            {
                _lastScene = scene.path;
                _sceneChangedAt = Time.realtimeSinceStartup;
            }

            if (IsBootScene(scene)) return false;
            if (EventSystem.current == null) return false;
            return Time.realtimeSinceStartup - _sceneChangedAt >= _config.general.settleSecondsAfterBoot;
        }

        public virtual string DescribeState()
        {
            var scene = SceneManager.GetActiveScene();
            return $"scene='{scene.name}' (index {scene.buildIndex}), EventSystem={(EventSystem.current != null)}";
        }

        protected bool IsBootScene(Scene scene)
        {
            var boot = _config?.general.bootScenePath;
            if (!string.IsNullOrEmpty(boot)) return scene.path == boot;
            return scene.buildIndex == 0 && SceneManager.sceneCountInBuildSettings > 1;
        }

        public virtual IReadOnlyList<AutoTestFeature> GetFeatures()
        {
            return Array.Empty<AutoTestFeature>();
        }

        public virtual Task<GameObject> ShowFeature(AutoTestFeature feature, object data, float timeoutSeconds,
            CancellationToken ct)
        {
            throw new NotSupportedException("Adapter không hỗ trợ mở màn hình.");
        }

        public virtual void CloseFeature(AutoTestFeature feature)
        {
        }

        public virtual bool IsFeatureShowing(AutoTestFeature feature)
        {
            return false;
        }

        public virtual GameObject GetFeatureObject(AutoTestFeature feature)
        {
            return null;
        }

        public virtual IReadOnlyList<AutoTestFeature> GetOpenFeatures()
        {
            return Array.Empty<AutoTestFeature>();
        }

        public virtual IReadOnlyList<AutoTestCurrency> GetCurrencies()
        {
            return Array.Empty<AutoTestCurrency>();
        }

        public virtual double GetBalance(AutoTestCurrency currency)
        {
            throw new NotSupportedException();
        }

        public virtual void AddCurrency(AutoTestCurrency currency, double amount)
        {
            throw new NotSupportedException();
        }

        public virtual bool RemoveCurrency(AutoTestCurrency currency, double amount)
        {
            throw new NotSupportedException();
        }

        public virtual bool IsEnough(AutoTestCurrency currency, double amount)
        {
            throw new NotSupportedException();
        }

        public virtual void SetCurrency(AutoTestCurrency currency, double amount)
        {
            throw new NotSupportedException();
        }

        public virtual Type BalanceType(AutoTestCurrency currency)
        {
            return typeof(double);
        }

        public virtual bool GrantReward(AutoTestCurrency currency, double amount)
        {
            return false;
        }

        public virtual Task<bool?> PurchaseOffline(AutoTestCurrency currency, double cost, float timeoutSeconds,
            CancellationToken ct)
        {
            return Task.FromResult<bool?>(null);
        }

        public virtual void SaveAll()
        {
            PlayerPrefs.Save();
        }

        public virtual IReadOnlyList<string> GetSaveKeys()
        {
            return _config?.general.extraSaveKeys ?? new List<string>();
        }

        public virtual bool ReloadPlayerData()
        {
            return false;
        }

        public virtual IReadOnlyList<AutoTestConfigTable> GetConfigCollections()
        {
            return Array.Empty<AutoTestConfigTable>();
        }
    }
}
