---
description: Rules for organizing utility classes and constants.
---
# Utils & Constants Rule

## Helpers — check before creating
- Before writing a helper, check `Utils.cs` (`Assets/_Project/Features/_Shared/Systems/Utils.cs`) — it may already exist.
- For time, use `TimeManager` (same folder); never `DateTime.Now`.

## Resource paths — `PathUtils`
- Store **shared resource paths** in `PathUtils` (`Assets/_Project/Features/_Shared/Systems/PathUtils.cs`, a `partial class`). It only holds paths with a real call site — e.g. `PathPrefabIconFeature`, `UserAvatar`, `UserFrame`, `StatImgPath`.
- A path used by exactly one feature belongs in that feature, not in `PathUtils`.
- Example: `public const string PathPrefabIconFeature = "Images/IconFeature/";`

## Game constants
- App-wide constants (store links, package names, PlayerPrefs keys, layer names) live in `GameConstant` (`Assets/_Project/Features/_Shared/Config/GameConstant.cs`). Per-app values there are placeholders marked `// TODO: [Setup]` — fill them in per project.
- Everything else: define constants where they are used (feature-local `static`/`const`), in `SCREAMING_SNAKE_CASE`. No magic numbers.
- Example: `private const int MAX_LEVEL = 100;`
- Secrets / per-environment values (dev keys, backend URLs, store ids) go in the `AppSecretsConfig` asset, read via `AppSecrets.*` — never hardcoded.

## Usage
```csharp
// Shared paths
var path = PathUtils.PathPrefabIconFeature + "icon_shop";

// App-wide constant
Application.OpenURL(GameConstant.LinkStoreFree);

// Constants (feature-local)
if (level >= MAX_LEVEL) { }
```
