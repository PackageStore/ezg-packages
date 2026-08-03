using System;
using System.Collections.Generic;
using AppsFlyerSDK;
using UnityEngine;

namespace Ezg.Feature.IAP
{
    public class AppsFlyerListener : MonoBehaviour, IAppsFlyerConversionData, IAppsFlyerPurchaseValidation,
        IAppsFlyerPurchaseRevenueDataSource, IAppsFlyerPurchaseRevenueDataSourceStoreKit2
    {
        /// <summary>Kênh báo cáo ra game; được InAppManager set khi tạo listener.</summary>
        public IIapReporter Reporter;

        /// <summary>
        ///     Provider trả player id để đính vào event auto-log doanh thu của Purchase Connector (ROI360).
        ///     Game set qua đây để tránh coupling package IAP vào PlayerDataManager. Có thể null.
        /// </summary>
        public Func<string> PlayerIdProvider;

        public void onConversionDataSuccess(string conversionData)
        {
            AppsFlyer.AFLog("onConversionDataSuccess", conversionData);
            Reporter?.OnConversionData(conversionData);
        }

        public void onConversionDataFail(string error)
        {
            AppsFlyer.AFLog("onConversionDataFail", error);
        }

        public void onAppOpenAttribution(string attributionData)
        {
            AppsFlyer.AFLog("onAppOpenAttribution", attributionData);
            Reporter?.OnConversionData(attributionData);
        }

        public void onAppOpenAttributionFailure(string error)
        {
            AppsFlyer.AFLog("onAppOpenAttributionFailure", error);
        }

        #region Purchase Connector (ROI360) validation callback

        /// <summary>
        ///     Kết quả validate doanh thu từ Purchase Connector (native gọi về theo tên GameObject đã
        ///     đăng ký ở AppsFlyerPurchaseConnector.init). Bắt buộc phải có khi đã bật
        ///     setPurchaseRevenueValidationListeners(true), nếu không kết quả validate bị nuốt im lặng.
        /// </summary>
        public void didReceivePurchaseRevenueValidationInfo(string validationInfo)
        {
            AppsFlyer.AFLog("didReceivePurchaseRevenueValidationInfo", validationInfo);
        }

        /// <summary>Lỗi khi validate doanh thu IAP (receipt sai, sandbox/production lệch, mạng...).</summary>
        public void didReceivePurchaseRevenueError(string error)
        {
            AppsFlyer.AFLog("didReceivePurchaseRevenueError", error);
            Debug.LogWarning($"[AppsFlyer] Purchase revenue validation error: {error}");
        }

        #endregion

        #region Purchase Connector (ROI360) data source

        /// <summary>
        ///     Param bổ sung đính kèm mỗi event auto-log doanh thu (Android + iOS StoreKit1).
        ///     Purchase Connector gọi callback này khi tự log giao dịch.
        /// </summary>
        public Dictionary<string, object> PurchaseRevenueAdditionalParametersForProducts(
            HashSet<object> products, HashSet<object> transactions)
        {
            return BuildAdditionalParameters();
        }

        /// <summary>iOS StoreKit2 — trả cùng bộ param.</summary>
        public Dictionary<string, object> PurchaseRevenueAdditionalParametersStoreKit2ForProducts(
            HashSet<object> products, HashSet<object> transactions)
        {
            return BuildAdditionalParameters();
        }

        private Dictionary<string, object> BuildAdditionalParameters()
        {
            var parameters = new Dictionary<string, object>();
            var playerId = PlayerIdProvider?.Invoke();
            if (!string.IsNullOrEmpty(playerId))
                parameters["player_id"] = playerId;
            return parameters;
        }

        #endregion
    }
}
