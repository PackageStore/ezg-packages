# Unity integration: import, wiring, verification

No C# is involved: `make` writes the WAV and its `.meta`, then edits one serialized field. Unity only has to import.

## 1. Import settings (written into the `.meta` of every new clip)

| Setting | Value | Why |
|---|---|---|
| Load Type | Decompress On Load (`loadType: 0`) | short SFX, no decode cost at play time |
| Compression | Vorbis, quality 0.3 (`compressionFormat: 1`) | small on device, transparent for SFX |
| Sample rate | preserve (44.1 kHz mono) | |
| Force To Mono / Normalize | off / off | the file is already mono; Unity's normalize would undo the loudness mastering |
| Preload Audio Data | off | |
| Asset bundle | ArtStyle.md §6c `bundle:`, else the majority of the project's audio `.meta` files (template: none); `--bundle NAME\|none` overrides | a game that loads its sounds from a bundle keeps new clips in it |

An existing `.meta` is never rewritten. Re-making a sound keeps its GUID, so references survive, and keeps any import
tweak a human made.

## 2. Folders

- `<sfxRoot>/<Name>/`, with `sfxRoot` taken from `.claude/project-profile.json` (default
  `Assets/_Project/Visual/ArtAsset/Shared/Sounds/Generated`). `--out` replaces the root, for example with a
  feature's `Visuals/Sounds`.
- Files: `sfx_<name>[_<part>][_<i>].wav` (free mode) or `sfx_<name>_<event>[_<part>]_<i>.wav` (event pool), plus
  `<base>.sfx.json`.
- The `.sfx.json` spec (TextAsset, not in any bundle) records the exact `command`, seed, files, GUIDs and QA.
  Re-running that command regenerates byte-identical audio.
- Keep generated sounds out of `Resources/` unless the game loads them by path.

## 3. Wiring

### 3a. `SoundConfig` field (`make … --config <Field>` or `wire --config <Field> --file <wav>`)

Target: the profile's `soundConfigAsset` (default `Assets/_Project/Features/_Shared/Resources/SoundConfig.asset`),
or `--asset` for any other ScriptableObject. It must be a **scalar** `AudioClip` field: `OpenPopup`, `ClosePopup`,
`ButtonSelect`, `PurchaseItem`, `PurchaseSuccess`, `OpenShop`, `GoldFlyStart`, `GoldFlyEnd`, `DiamondFlyStart` (who
plays each one: style-guide §1). A field the asset has not serialized yet (added to the class later) is appended
when the script declares `AudioClip <Field>`. Arrays (`MainMenuMusics`) are set in Unity.

### 3b. Prefab list (`make … --prefab <path> [--event E] [--field F]` or `wire --prefab … --name <Name> [--event E]`)

Target field = `--field`, else the profile's `sfxEventFields[<event>]`, else `_soundCustomList`. The default is the
Customs list of **`Ezg.Package.Audio.SoundPlayController`**. The field must be a `List<SoundPlayCustomModel>` (`Clip`
array + `DelayStart` + `Cooldown` + `IsLoop`), so a game's own sound component with one list per event works the
same once its field names are declared:

```json
"sfxEventFields": { "fire": "_fireSounds", "shot": "_projectileSounds", "hit": "_hitSounds" }
```

The list is replaced by ONE entry:
- `Clip` = all variants (one is picked at random per play);
- `DelayStart 0`;
- `Cooldown` = `--cooldown`, else the event default (hit 0.06 / shot 0.05 / fire 0), else (free mode) the replaced
  entry's cooldown;
- `IsLoop 0`.

`--append` keeps the old entries and adds yours as another layer. Each entry plays, each with its own cooldown.

On a `SoundPlayController`:
- `_soundTypes` is flipped 0 → 1 (Customs) only when `_soundList` is empty, so the list shows in the Inspector.
- `--play-on-enable` sets `_playOnEnable: 1`, so the list plays every time the object is enabled (pooled VFX,
  projectile, enemy spawn).
- Otherwise something must call `PlaySoundCustom()`: a `Button.onClick` event (that is how the template's button
  and tab prefabs play their click), an animation event, or game code.

The edit is a text edit of that MonoBehaviour field only, never Transform/children. The JSON report lists the
`replaced` GUIDs. Never delete a replaced clip that other prefabs still use (`stale_variants` only removes old
variants of the same base that nothing references).

**Template UI click:** `button_template.prefab` and the tab cell templates under `uiTemplatesRoot` each have a
`SoundPlayController` whose list plays on click. Re-wire those to change the click of every button built from the
templates. A screen already built from them is a prefab variant: it follows the template unless it overrode the
list.

## 4. Prefab without the component

`wire` stops with "has no '<field>' list". Add the component on the object that should play the sound, either with
MCP `unity_component_add` (type `Ezg.Package.Audio.SoundPlayController`) on the prefab or by hand. Then save the
prefab and re-run `wire`. A component inside a nested prefab must be wired in that nested prefab. Do not write a new
component.

## 5. Import + verify (Unity MCP)

1. `unity_editor_state` (not playing, not compiling) + `unity_agents_list` (no other session in Play mode).
2. `unity_execute_code`:

```csharp
var dir = "Assets/_Project/Visual/ArtAsset/Shared/Sounds/Generated/OpenPopup";   // the make "dir"
AssetDatabase.ImportAsset(dir, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ImportRecursive);
var sb = new System.Text.StringBuilder();
foreach (var g in AssetDatabase.FindAssets("t:AudioClip", new[] { dir })) {
    var p = AssetDatabase.GUIDToAssetPath(g);
    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
    var imp = (AudioImporter)AssetImporter.GetAtPath(p);
    sb.Append($"{System.IO.Path.GetFileName(p)} {clip.length:F2}s {imp.defaultSampleSettings.compressionFormat} bundle={imp.assetBundleName}; ");
}
// SoundConfig field:
var cfgPath = "Assets/_Project/Features/_Shared/Resources/SoundConfig.asset";
AssetDatabase.ImportAsset(cfgPath, ImportAssetOptions.ForceSynchronousImport);
var cfg = AssetDatabase.LoadAssetAtPath<SoundConfig>(cfgPath);
sb.Append($" OpenPopup={(cfg.OpenPopup ? cfg.OpenPopup.name : "NULL")};");
// prefab list (when --prefab was used):
// var pf = "<prefab path>"; AssetDatabase.ImportAsset(pf, ImportAssetOptions.ForceSynchronousImport);
// var snd = AssetDatabase.LoadAssetAtPath<GameObject>(pf).GetComponentInChildren<Ezg.Package.Audio.SoundPlayController>(true);
// foreach (var e in snd.SoundCustomList) sb.Append($" clips={e.Clip.Length} null={System.Array.FindAll(e.Clip, c => c == null).Length} cd={e.Cooldown};");
return sb.ToString();
```

Expect every clip to be `Vorbis` with the expected bundle, and `null=0`.

3. Hear it in game (optional, the real proof): enter Play mode and trigger the sound. The agent still cannot hear it;
   ask the user to.

## 6. Loops (`aura_loop`, `IsLoop`)

`wire` always writes `IsLoop: 0`. `SoundPlayController` honours `IsLoop` only on the `ISoundPlay.PlaySound(list,
controller)` path: a coroutine on `controller` replays `PlayOneShot` every `clip.length` until that GameObject is
disabled. `PlaySoundCustom()` and play-on-enable ignore `IsLoop`. Wire a loop only where the game calls that path,
or plays the clip on its own looping `AudioSource`, and the object turns off when the effect ends. Set `IsLoop` by
hand and say so in the report. The recipe guarantees a seamless join (`LOOP_SEAM` QA), but `PlayOneShot` restarts
are not sample-accurate: expect a tiny gap at each cycle.

## 7. Other places that hold AudioClips

`wire --config <Field> --asset <path> --file <wav>` sets any **scalar** `AudioClip` field of a single-object asset
(a feature's own sound config ScriptableObject). Arrays, and fields of components inside scenes, are set in Unity.
