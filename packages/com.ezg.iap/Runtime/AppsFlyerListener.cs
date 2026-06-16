using AppsFlyerSDK;
using UnityEngine;

namespace Ezg.Feature.IAP
{
    public class AppsFlyerListener : MonoBehaviour, IAppsFlyerConversionData
    {
        /// <summary>Kênh báo cáo ra game; được InAppManager set khi tạo listener.</summary>
        public IIapReporter Reporter;

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
    }
}
