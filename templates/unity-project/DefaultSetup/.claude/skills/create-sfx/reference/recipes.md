# Recipes: map a request to a sound, tune it, write a new one

`$M list` prints the live table (recipe · kind · default element · default event · life · LUFS · variants).

## 1. Request → recipe

### UI (`--name <Name>`, usually `--config <SoundConfig field>` or `--prefab` of a button/popup)

| Request says | Recipe | Element | Where it usually goes (template) |
|---|---|---|---|
| click / tap / button | `ui_click` | neutral | `SoundPlayController` of `button_template.prefab` (style-guide §1) |
| select / toggle / tab / pick item | `ui_select` | neutral | `ButtonSelect`, tab cell templates' `SoundPlayController` |
| tick / roll / spin step / counter | `ui_tick` | neutral | game code (a loop of ticks: `aura_loop` or many ticks) |
| popup open / show panel | `ui_pop` | neutral | `OpenPopup` |
| popup close / back | `ui_close` | neutral | `ClosePopup` |
| slide / transition / swipe / open a big screen | `ui_whoosh` | neutral | `OpenShop`, `GoldFlyStart`, `DiamondFlyStart` (or `ui_pop`) |
| coin / gem / currency pickup or arrival | `ui_coin` | gold | `GoldFlyEnd` |
| error / denied / not enough | `ui_error` | neutral | game code |
| unlock / equip / slot in / buy item | `ui_unlock` | gold | `PurchaseItem` |
| claim reward / receive / purchase done | `ui_reward` | gold | `PurchaseSuccess` |
| level up / upgrade done | `ui_levelup` | gold | game code |
| celebration / tier up / rank up / victory sting | `ui_fanfare` | gold | game code |
| ability picked / card chosen | `ui_unlock` or `ui_reward` | arcane / gold | game code |
| big warning (boss, big wave) | `ui_error --pitch -5 --life 0.6` or `cast_charge --element shadow` | shadow | game code |

### Gameplay (`--name <Name> --event fire|shot|hit`, `--prefab` when a prefab plays it)

| Action does | fire (activation) | shot (each projectile / spawn) | hit (each collision) |
|---|---|---|---|
| bullets / energy bolts | — | `shot_energy` | `hit_impact` / `hit_magic` |
| daggers / arrows / thrown blades | — | `shot_throw` | `hit_slash` |
| sword / claw melee sweep | `shot_throw` (swing) | — | `hit_slash` |
| cannon / rocket / bomb | — | `shot_heavy` | `explode` |
| fireball / meteor | `cast_charge` (fire) | `flame_burst` | `explode` |
| flamethrower / breath | `flame_burst` | — | `hit_impact --element fire` |
| lightning / chain / laser | `cast_charge` (electric) | `zap` | `zap` |
| ice / frost nova / crystal | `cast_charge` (frost) | `shot_energy --element frost` | `shatter` |
| poison / acid cloud | `spawn` (toxic) | `shot_energy --element toxic` | `hit_magic --element toxic` |
| holy / heal / shield / buff | `heal` | — | `hit_magic --element holy` |
| summon / clone / portal | `spawn` | — | per summon's attack |
| aura / orbit / persistent field | `aura_loop` (careful, unity-integration §6) | — | `hit_magic` |
| shadow / void / curse | `cast_charge --element shadow` | `shot_energy --element shadow` | `hit_magic --element shadow` |

Pick the element from the thing's VFX colour / name (same names as create-vfx), then ArtStyle.md §6c `default`. The
element changes register, filter brightness and partial set, and adds its tint layer: `hit_magic --element frost`
and `--element fire` are different sounds, not a recolour. A VFX prefab from create-vfx that should sound when it
spawns: `--prefab <fx prefab> --event hit|fire --play-on-enable` after adding a `SoundPlayController` to its root.

## 2. Parameters

| Flag | Effect | Typical |
|---|---|---|
| `--variants N` | random pool size (event mode default per recipe, free mode 1) | hit/shot 3–4 |
| `--life S` | scales every duration in the recipe | ±40 % of default |
| `--pitch ST` | semitones on top of the element register | −7…+7 |
| `--tint A` | element layer amount (default per element 0.5–0.7) | 0 = no tint, 1.2 = heavy |
| `--lufs L` | loudness target override (default: recipe target + §6c `offset`) | stay inside style-guide §4 |
| `--seed N` | different take with the same settings (default = hash of the base name) | — |
| `--prefab P` / `--field F` | wire the pool into that prefab's list (default field: `sfxEventFields[event]`, else `_soundCustomList`) | — |
| `--cooldown S` | cooldown written into the prefab entry | hit 0.06, shot 0.05, fire 0 |
| `--play-on-enable` | also set `SoundPlayController._playOnEnable` | pooled VFX / projectiles |
| `--part TAG --append` | extra layer for the same event (another entry in the list, own clip pool) | `--part boom` |
| `--config F` / `--asset A` | wire one clip into an `AudioClip` field (default asset: profile `soundConfigAsset`) | `--config OpenPopup` |
| `--dry-run` | wire without writing the prefab / asset | — |

Per-variant variation comes for free: each variant gets its own random stream and a pitch jitter of ±`vary` (recipe
setting, 3–8 %). Variant 1 is always the exact requested pitch.

## 3. Elements

| Element | Register | Brightness | Tint layer |
|---|---|---|---|
| neutral | ×1.00 | ×1.00 | none |
| fire | ×0.85 | ×0.80 | crackle pops + low rumble |
| gold | ×1.25 | ×1.25 | small coin pings |
| frost | ×1.30 | ×1.40 | glass partials + crystal ticks |
| arcane | ×1.00 | ×1.10 | detuned FM bells with tremolo |
| toxic | ×0.80 | ×0.60 | wet rising bubbles |
| holy | ×1.20 | ×1.20 | major-chord pad + air |
| shadow | ×0.70 | ×0.50 | low detuned drone + reverse swell |
| electric | ×1.10 | ×1.30 | gated saw buzz + arc crackle |
| rose | ×1.15 | ×1.10 | soft high chimes + breath |

`shadow` and `toxic` sit low. Check `phone_loss_db` / `BASS_HEAVY` on them and raise `--pitch` 3–5 semitones if a
hit/shot gets flagged (`hit_impact`, `shot_heavy` and `ui_error` with `shadow`, and `shot_heavy` with `toxic`, hit
`PHONE_WEAK` at default pitch). The shadow drone cannot fade inside a very short UI sound (< 0.1 s): use `--tint 0`
there.

## 4. Writing a new recipe

1. In `scripts/sfx_recipes.py` add a function decorated with
   `@recipe(name, kind, life, lufs, desc, element=…, event=…, variants=…, reverb=(decay, wet)|None, loop=False, vary=…, soft=False)`.
   `kind` ∈ `ui_short | ui_jingle | shot | hit | explode | cast | loop` picks the QA limits and the REF clips (§6c).
2. Build it as **transient + body + tail + `c.tint(d)`** (style-guide §2), with the helpers of `synth-api.md`.
   - Every pitch through `c.hz(f)`, every duration through `c.dur(d)`, every filter cutoff through `c.cut(f)`.
   - Every random choice through `c.j(lo, hi)` / `c.rng` (never `np.random` globals), so `--seed` reproduces it and
     variants differ.
   - Decays must finish inside the buffer: use `perc(d, a, k)` with k ≥ 5, partial decays ≈ 0.1–0.25 × length.
3. `make --recipe <r> --name T_<recipe> --variants 4 --out Temp/SfxTest`, then read QA, open the review PNG next to
   REF, and iterate. Sweep a few `--seed` values and the elements the recipe will be used with: a borderline decay
   fails on some seeds only.
4. Add the row to §1 and the request words to SKILL.md's description if it covers a new request type. A recipe that
   is only for one game stays in that game's repo; only generic recipes go upstream.

## 5. QA flags and fixes

| Flag | Blocks | Meaning | Fix |
|---|---|---|---|
| `LOUDNESS` | yes | mastered loudness is more than 1.5 LU off target (heavy limiting ate it) | lower the transient vs body ratio, or raise the `--lufs` target only if the kind allows |
| `CLICK_START` | yes | first sample not near zero | keep the 0.5 ms fade (never bypass `fade`) |
| `CUT_TAIL` | yes | still sounding 6–16 ms before the end: truncated | higher decay `k`, shorter partial decays, steeper `ar` release |
| `LATE_ONSET` | yes | more than 12 ms of silence before a hit/shot/click | remove delays/swell; mark `soft=True` only for real swells |
| `TOO_LONG` | yes | longer than the kind allows | shorter `--life`, smaller reverb decay |
| `SILENT` | yes | peak below −30 dBFS | a layer is missing / zero gain |
| `LOOP_SEAM` | yes | loop end does not join its start | integer LFO cycles over the loop, `loop_seam` crossfade, no envelopes across the loop |
| `PHONE_WEAK` | yes | most loudness below 300 Hz | move the body up (150–450 Hz knock, mid grit), drive for harmonics, `--pitch +3…+5` |
| `BASS_HEAVY` | warn | same, milder | same |
| `HARSH` | warn | hit/shot brightness above 5 kHz | lower the noise high-pass / partial range, less hiss tint |
| `LIMITED` | warn | limiter shaved more than 6 dB (transient squashed) | reduce the click/tick gain vs body |
| `SAME_VARIANTS` | warn | two variants correlate above 0.97 | more `c.j()` randomness in the dominant layer |
| `BURST_LOUD` | warn | stacked busy-scene peak is more than 3 dB above the refs | raise `--cooldown`, lower `--lufs` by 2 |
| `DC` | warn | offset in the signal | a pulse/shape layer without high-pass (master removes < 25 Hz) |
