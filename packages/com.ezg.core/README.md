# EZG Core

Shared runtime foundation for EZG Unity projects. Provides an asset-loading adapter, reusable UI helper components, device/security helpers, common MonoBehaviour & Unity extensions, and shared utilities.

## What's inside

| Area | Contents |
|------|----------|
| **Adapter** | `ResLoader`, `AssetRef`, `AssetBundleManager` — load assets from Resources, AssetBundles, or Google Play Asset Delivery. PAD/Bundle paths are gated behind the `USE_PAD` / `USE_BUNDLE` scripting defines (Resources-only by default). |
| **UI** | Layout & interaction helpers: `SafeArea`, `UILayoutAdjuster`, `UI_GridLayoutGroup`, `UI_CircleLayoutGroup`, `UI_ScrollViewSnap`, `UI_TweenMove`, `UI_TransformRandom`, `UI_CanvasGroupFade`, `UI_CloseWithFade`, `UI_FadeImageOnScroll`, `CurvedTextLegacy`, `CinemachineLockCamera`, `DragDropController`, `PropogateDrag`, button extensions, and the `JumpInJumpOut` animation component. |
| **Extensions** | `MonoBehaviourHelper`, `BezierMove`, `AutoHideGameObject`, `RebuildUILayoutHelper`, `ClearTrailRedererWhenDisable` (+ editor-only `#if UNITY_EDITOR` helpers). |
| **Security** | `SecuritySystems`, `SecuredServiceBase`, `DetectDevice`. |
| **Utils** | `ColorUtils`, `CoreUtils`, `UpdateManager`/`IUpdateManager`, `BundleNameAttribute`, and the shared `EnumBase` enum collection. |
| **Models** | `Resource`. |

Package folder `Runtime/` mirrors the original `Assets/_Project/Core` source tree.

## Requirements

- Unity 6000.2 or later

## Install (scoped registry)

In `Packages/manifest.json`:

```json
{
  "scopedRegistries": [
    {
      "name": "Easygoing code base",
      "url": "https://upm-registry-worker.developer-a1f.workers.dev",
      "scopes": ["com.ezg"]
    }
  ],
  "dependencies": {
    "com.ezg.core": "0.1.0"
  }
}
```

## Dependencies (auto-resolved via the registry)

- `com.ezg.singleton` — `Singleton<T>` base used by `UpdateManager` and `AssetBundleManager`.
- `com.unity.nuget.newtonsoft-json` — JSON serialization.

## Peer requirements (consumer must provide these)

These are referenced by the `Ezg.Core` assembly but are **not** declared as package dependencies (they ship as Asset/Git imports, not registry packages). The consuming project must already have them:

| Library | Source |
|---------|--------|
| **UniTask** (Cysharp) | UPM `com.cysharp.unitask` |
| **DOTween** | [Demigiant Asset Store](https://assetstore.unity.com/packages/tools/animation/dotween-hotween-v2-27676) |
| **Cinemachine** | Unity Package Manager |

### Optional

- **Odin Inspector** — all Odin attributes are guarded behind `#if ODIN_INSPECTOR`. Without Odin, the components still compile and run; only the custom inspector decoration is dropped.
- **Google Play Asset Delivery** — only required if you enable the `USE_PAD` / `USE_BUNDLE` defines for `AssetBundleManager`.

## Known debt

- **`Utils/EnumBase` carries game/genre-specific enums** (`BattleModes`, `HeroClass`, `QuestTypes`, `SkillTypes`, `MoneyTypes`, etc.) that are not generic Core infrastructure. They are shipped as-is to keep this package a drop-in replacement for the in-project `Assets/_Project/Core`. A future cleanup should relocate game-domain enums to a game-specific assembly.

## Assembly

- `Ezg.Core` — runtime assembly (auto-referenced).
- `Ezg.Core.JumpInJumpOut.Editor` — editor-only assembly for the JumpInJumpOut helper.
