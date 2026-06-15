# Changelog

## [0.1.0] - 2026-06-15

Initial publish. Extracted the AppLovin MAX ads mediation module from the game source.

- Rewarded, interstitial and banner ads via the AppLovin MAX adapter (`MaxAdsvertising`, guarded by `MEDIATION_MAX`).
- Level-based and remote-config gating for interstitial/banner.
- Analytics decoupled through the `IAdsTracker` interface (host maps to Firebase/AppsFlyer/GameAnalytics).
- SDK keys/ad-unit-ids read from an injectable `AdsConfig` ScriptableObject (`Resources/AdsConfig`) instead of hardcoded constants.
- Depends on `com.ezg.singleton`. AppLovin MAX SDK and the `MEDIATION_MAX` define are peer requirements.
