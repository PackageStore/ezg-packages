---
description: Rules for organizing utility classes and constants.
---
# Utils & Constants Rule

## Helpers — check before creating
- Before writing a helper, check `Utils.cs` (`Assets/_Project/Features/_Shared/Systems/Utils.cs`) — it may already exist.
- For time, use `TimeManager` (same folder); never `DateTime.Now`.

## Resource paths — `PathUtils`
- Store all **resource paths** in `PathUtils` (`Assets/_Project/Features/_Shared/Systems/PathUtils.cs`, a `partial class`).
- Example: `public const string PathPrefabVFX = "Prefabs/VFX/";`

## Game constants
- There is **no** central `GameConstants` class. Define constants where they are used (feature-local static/const), in `SCREAMING_SNAKE_CASE`. No magic numbers.
- Example: `private const int MAX_LEVEL = 100;`

## Usage
```csharp
// Paths
var path = PathUtils.PathPrefabVFX + "MyVfx";

// Constants (feature-local)
if (level >= MAX_LEVEL) { }
```
