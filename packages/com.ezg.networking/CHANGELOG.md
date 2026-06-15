# Changelog

All notable changes to **com.ezg.networking** are documented here.

## [0.1.0] - 2026-06-16

### Added
- Initial release. Extracted from the game's `Core/Infrastructure/Networking` module.
- **Cloudflare** — `CloudflareDB` static entry point + `CloudflareQuery<T>` builder
  (`Where` / `Get` / `Upsert` / `Delete`, LINQ-expression filtering, configurable timeout)
  and the `ApiResponse<T>` wrapper.
- **Supabase** — `SupabaseManager<T>` singleton wrapper (init / refresh / shutdown) and
  `UnitySession` (`IGotrueSessionPersistence`) for local session persistence on
  `Application.persistentDataPath`.
- **Config** — `CloudflareSettings` and `SupabaseSettings` ScriptableObjects loaded from `Resources/`.
- **Editor** — scaffolder at **Create ▸ Ezg ▸ Networking ▸ Project setup** that generates the
  `GameNetworkManager` facade.
