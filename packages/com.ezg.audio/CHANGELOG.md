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