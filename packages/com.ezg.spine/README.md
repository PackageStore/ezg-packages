# EZG Spine

UPM package for the Spine Unity runtime and editor integration copied from `Assets/Spine`.

## Source Mapping

- Source repo folder: `Assets/Spine`
- Package folder: `packages/com.ezg.spine`
- Runtime assemblies: `spine-csharp`, `spine-unity`
- Editor assembly: `spine-unity-editor`

The assembly names are intentionally preserved so existing serialized assets that reference Spine runtime classes continue to resolve when the game is switched to consume the package.

## Dependencies

This package does not declare registry package dependencies. It expects the consuming Unity project to provide the Unity modules already used by Spine:

- `com.unity.ugui`
- `com.unity.modules.animation`
- `com.unity.modules.physics`
- `com.unity.modules.physics2d`
- Unity 2D/SpriteAtlas APIs used through `UnityEngine.U2D`

## Notes

- This package preserves the Spine source `.meta` files from the game repo.
- No project-specific business code or SDK integration was found in `Assets/Spine` during packaging.
- Spine runtime licensing remains governed by Esoteric Software's Spine Runtimes License terms referenced in the source headers.

