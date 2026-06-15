# Bootstrap Sample

A minimal, self-contained example of wiring up **EZG Audio**.

| File | What it shows |
|------|---------------|
| `SampleSoundSettings.cs` | An `ISoundSettings` implementation backed by `PlayerPrefs`. Swap this for your own data layer (save system / player profile). |
| `SampleAudioBootstrap.cs` | A `[RuntimeInitializeOnLoadMethod]` that builds the service, calls `Initialize()` + `UpdateVolumes()`, and assigns `AudioService.Default` before the first scene loads. |

## After importing

Press Play — `AudioService.Default` is ready. Call it from anywhere:

```csharp
AudioService.Default.PlayMusic(myThemeClip, isLoop: true);
AudioService.Default.PlaySound(myClickClip);
```

> These scripts compile into `Assembly-CSharp` and reach the package via its
> auto-referenced assembly, so no extra asmdef is required for the sample.
