# EZG Audio (`com.ezg.audio`)

Game-agnostic audio service for Unity: background music (with fade in/out), one-shot
sound effects (with optional non-overlap lock), and volume control. Volume persistence is
delegated to the host game through `ISoundSettings`, so this package never depends on a
specific game's data layer.

## Package ↔ source

Vendored from `Assets/_Project/Features/_Shared/Sound` of the Merge Two project.
Assembly: **`Ezg.Package.Audio`** · Namespace: `Ezg.Package.Audio`.

## Peer requirements (consumer must provide these)

These are referenced by the assembly but are **not** `package.json` dependencies — install
them in the consuming project yourself:

| Lib | Required? | Why |
|-----|-----------|-----|
| **UniTask** (`com.cysharp.unitask`, assembly `UniTask`) | **Required** | Music fade lerp, delayed playback, cooldown timers, cancellation. |
| **Odin Inspector** (Sirenix) | Optional | Inspector polish only — all Odin attributes are wrapped in `#if ODIN_INSPECTOR`, so the package compiles fine without it. |

## Getting started

> **Tip:** the fastest start is **Package Manager → EZG Audio → Samples → "Bootstrap" → Import**.
> It drops a ready-to-run `SampleAudioBootstrap` + a PlayerPrefs-backed `SampleSoundSettings`
> into your project — press Play and `AudioService.Default` is wired up. Then swap
> `SampleSoundSettings` for your own data layer. The manual steps below do the same thing.

The audio service is a plain C# class — you bootstrap it once at startup.

1. Implement `ISoundSettings` to read/persist volumes from your own data layer:

```csharp
public sealed class MySoundSettings : ISoundSettings
{
    public float GetMusicVolume() => MyData.MusicVolume;
    public float GetSoundVolume() => MyData.SoundVolume;
    public void  SetMusicVolume(float v) => MyData.MusicVolume = v;
    public void  SetSoundVolume(float v) => MyData.SoundVolume = v;
    public void  Save() => MyData.Save();
}
```

2. Create and register the service once (e.g. in your bootstrap):

```csharp
var audio = new AudioService(new MySoundSettings());
audio.Initialize();          // creates DontDestroyOnLoad music/sound AudioSources
AudioService.Default = audio; // service-locator used by PlaySound / SoundPlayController
```

3. Play audio anywhere:

```csharp
AudioService.Default.PlayMusic(themeClip, isLoop: true);
AudioService.Default.PlaySound(clickClip);
```

> If `AudioService.Default` is never assigned, the `PlaySound` MonoBehaviour and
> `SoundPlayController` no-op safely (null-guarded) instead of throwing.

## Components

- `AudioService` / `IAudioService` — the service itself.
- `ISoundSettings` — host-implemented volume persistence bridge.
- `PlaySound` — simple MonoBehaviour: play a clip on enable / stop on disable.
- `SoundPlayController` — list-driven player with delay, cooldown, loop and custom-clip modes.
- `SoundPlayModel` / `SoundPlayCustomModel` — serializable sound entries.
