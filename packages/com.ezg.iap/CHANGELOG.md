# Changelog

## [0.1.0] - 2026-06-16
### Added
- Initial release extracted from `Assets/_Project/Features/_Shared/InAppPurchase`.
- `InAppManager` singleton wrapping Unity IAP: initialize, buy, receipt validation (`CrossPlatformValidator`), restore, and localized pricing.
- Dependency-inversion seams `IPurchasing`, `IIapProfile`, `IIapReporter` plus injected `IapSecurityConfig` so the module carries no game code or secrets.
- `IapPurchaseInfo` DTO and `AppsFlyerListener` for forwarding AppsFlyer conversion data.
- Editor menu `Assets > Create > Ezg > IAP > Project Setup` generating the per-project integration template.
