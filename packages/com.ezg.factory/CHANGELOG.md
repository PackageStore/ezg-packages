# Changelog

## [0.1.3] - 2026-11-12

Fixed — a module read before `DataPlayer.Init()` no longer wipes the save.
- `DataPlayer.GetModule<T>()` / `GetModule(string)` read before `Init()` now run `Init()` first, so the module comes back
  loaded from PlayerPrefs. Before, the base class built the registry itself (Activator.CreateInstance, no `Load()`) and
  handed out empty modules: `Save()` on one wrote `"null"` over the real save (next launch → defaults), and a module that
  builds defaults when empty (lazy getters) saved a new-player save over the real one. Seen on a QA device: a pause
  handler alive during Splash (offline: Splash stops before `Init()`) read a module and the chapter-30 save went back to
  a new player.
- The first explicit `Init()` after such an early read reloads the same instances from PlayerPrefs instead of replacing
  them, so references cached meanwhile (`PlayerDataManager`'s `_x ??= DataPlayer.GetModule<X>()`) stay the modules the
  registry saves. Later `Init()` calls rebuild every module as before (soft restart, cloud restore).
- `DataPlayerBaseGeneric<T>.Save()` skips a module whose `dataBase` is null (warning) instead of writing `"null"`.
- `SaveAllData()` before `Init()` is a no-op (warning) instead of a NullReferenceException; `LoadAllData()` /
  `ReloadAllData()` before `Init()` run `Init()`; `ClearAllData()` no longer throws before `Init()`.
- `DataPlayer.IsInitialized`: true once the registry exists.
- Unchanged: `GetRegisteredModules()` still returns null before `Init()` (code uses it as an "initialized yet?" probe),
  and the base `CacheFactoryGeneric` / `CacheFactoryGenericString` behave exactly as before for other registries.

## [0.1.2]

Signed re-publish (Unity 6.3+ package signature); no code change.

## [0.1.1] - 2026-06-15

Add missing `.meta` files for `CHANGELOG.md`, `README.md`, and `package.json` so Unity no longer warns about meta-less assets in the immutable package folder.

## [0.1.0] - 2026-06-14

Initial publish. Extracted from `Assets/_Project/Core/Patterns/Factory`:
- `FactoryGeneric`, `CacheFactoryGeneric`, `CacheFactoryGenericString`
- `DataWithOption`, `DataWithOptionString`
- `DataPlayerBase`, `DataPlayerBaseGeneric<T>`, `DataPlayer`
- `CrossAssemblySerializationBinder`
