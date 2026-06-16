## [0.3.0] - 2026-06-16

- **Create > Ezg > Audio > Project setup** now also creates a `SoundConfig.asset` automatically once the generated scripts compile (deferred via `[DidReloadScripts]`, since the type does not exist until then). Skips creation if a `SoundConfig.asset` already exists.
- Removed the `[CreateAssetMenu]` (`Game/Sound Config`) attribute from the generated `SoundConfig` template — the asset is now created by Project setup, so there is no separate menu entry.


## [0.2.0] - 2026-06-16

- Add **Create > Ezg > Audio > Project setup** editor menu: scaffolds the three game-side glue scripts (`GameAudioBootstrap`, `GameSoundSettings`, `SoundConfig`) into `Assets/_Project/Features/_Shared/AudioGame` as portable, PlayerPrefs-backed templates with TODO markers. Checks for existing files and asks to confirm before overwriting. Adds a new Editor-only `Ezg.Package.Audio.Editor` assembly.


## [0.1.2] - 2026-06-15

- Add "Bootstrap" sample (`Samples~/Bootstrap`) exposed via Package Manager "Import Sample": a minimal `AudioService` bootstrap plus a PlayerPrefs-backed `ISoundSettings` example


## [0.1.1] - 2026-06-14

- Add missing .meta files for README.md, CHANGELOG.md, package.json to fix Unity "immutable folder" import errors


# Changelog

## [0.1.0] - 2026-06-09

- Initial release: game-agnostic `AudioService` (music fade, one-shot SFX, volume control),
  `ISoundSettings` persistence bridge, `PlaySound` + `SoundPlayController` MonoBehaviours,
  and serializable sound models. Odin attributes guarded by `#if ODIN_INSPECTOR`; UniTask
  is a required peer dependency.