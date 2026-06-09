# Changelog

## [0.1.0] - 2026-06-09

- Initial release: game-agnostic `AudioService` (music fade, one-shot SFX, volume control),
  `ISoundSettings` persistence bridge, `PlaySound` + `SoundPlayController` MonoBehaviours,
  and serializable sound models. Odin attributes guarded by `#if ODIN_INSPECTOR`; UniTask
  is a required peer dependency.
