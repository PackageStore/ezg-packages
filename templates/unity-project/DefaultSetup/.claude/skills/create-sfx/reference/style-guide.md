# SFX style guide: how this skill's sounds are built, and the default numbers

The game's own sound identity (reference clips, default element, loudness offset, approved sounds, rejected
directions) lives in `.claude/docs/ArtStyle.md` §6c, not here: this file ships with the skill and is overwritten on
update. Here: how the template plays sound, the vocabulary every recipe uses, and the **default** targets. They
were measured with `make_sfx.py analyze` on a shipped casual/arcade mobile game and a toon FX pack. A game whose
own clips sit louder or quieter shifts them with `offset` in §6c.

## 1. How the game plays sound (template, `com.ezg.audio`)

- One shared `AudioSource` (`AudioService.Default.SoundSource`) and **`PlayOneShot` for every SFX**: no mixer, no
  ducking, no 3D, all SFX sum on one voice. Loud clips stack into distortion in busy scenes. That is why hits and
  shots are quieter than UI jingles and carry a cooldown (§4). `PlaySound(clip, isLockOneShot: true)` also exists:
  the same clip will not overlap itself.
- **`SoundConfig`** (`Features/_Shared/Resources/SoundConfig.asset`, `DataManager.SoundConfig`) holds single
  `AudioClip` fields that template code plays:

  | Field | Played by |
  |---|---|
  | `OpenPopup`, `ClosePopup` | `FeatureBaseController` show/hide, when the screen has `_isPlaySoundTransition` on |
  | `ButtonSelect` | `TabHelper` tab change |
  | `PurchaseItem` | `ShopItemRawPack` purchase |
  | `OpenShop` | `ScreenShopController` open |
  | `PurchaseSuccess`, `GoldFlyStart`, `GoldFlyEnd`, `DiamondFlyStart`, `MainMenuMusics` | declared for the game; nothing plays them yet |

  An empty field (`{fileID: 0}`) is silent. `$M config` shows which fields are empty.
- **`SoundPlayController`** (com.ezg.audio) on a prefab holds `_soundCustomList`. Each entry is an array of clips
  played at random (the variant pool) + `DelayStart` + `Cooldown` + `IsLoop`. The template's button and tab cell
  prefabs call `PlaySoundCustom()` from `Button.onClick`: that is the UI click sound. `_playOnEnable` plays the list
  when the object is enabled, which suits a pooled VFX / projectile / enemy prefab.
- Gameplay code is per project. A game that triggers sounds per activation / projectile / hit wires them with
  `--event fire|shot|hit` into its own sound component (`sfxEventFields` in the profile) or a `SoundPlayController`.
- Target: **phone speakers** first (Android primary), headphones second.

## 2. Vocabulary (build every sound from these, in this order)

| Layer | Job | Typical recipe parts |
|---|---|---|
| **Transient** | makes the sound read on sample 0 (no lag after the visual) | 2–6 ms high noise tick, sine snap 1.3–1.9 kHz, bandpassed crack |
| **Body** | weight + pitch identity | 150–450 Hz knock (never only sub), noise body swept low-pass, tonal notes, FM |
| **Tail** | sparkle / room, the last thing to vanish | bell partials, crackle debris, shimmer noise > 5 kHz, small synthetic reverb |
| **Tint** | element identity | fire crackle · gold coin pings · frost glass · arcane FM shimmer · toxic bubbles · holy chord · shadow drone · electric gated buzz · rose chimes |

Musical UI jingles use **C major / pentatonic** notes (C6 E6 G6 C7 = 1047 / 1319 / 1568 / 2093 Hz) shifted by the
element register. `gold` (default for rewards) lifts them ~4 semitones for a brighter coin feel.

## 3. Reference clips (what `review` puts above your sound)

`review` stacks the game's own clips of the same kind (ArtStyle.md §6c, `ref <kind>: <path>[, <path>…]`) above the
new sound. Pick 1–3 per kind from the clips the game already ships, after `$M analyze` on them:

| Kind | Pick clips that are… | Default numbers (no ref yet) |
|---|---|---|
| `ui_short` | button click, tab select, small pickup | −25…−19 LUFS, 0.1–0.3 s |
| `ui_jingle` | reward, level up, equip | −16…−11 LUFS, 0.9–1.9 s |
| `shot` | projectile launch / weapon fire | −22…−15 LUFS, 0.25–0.6 s, brightness 1.2–1.5 kHz |
| `hit` | the most frequent impact sound | −23…−19 LUFS, 0.25–0.4 s, brightness 1.6–3.2 kHz |
| `explode` | blast / big impact | −21…−11 LUFS, 0.5–0.8 s |
| `cast` | charge / spell start | −20…−15 LUFS |
| `loop` | a foreground loop (spin, hum) | −14 LUFS |

Brightness = power-weighted spectral centroid. MP3/OGG references decode through macOS `afconvert` or `ffmpeg`;
without either, only WAV references show (still enough to compare).

## 4. Loudness, length, density

- Loudness = **momentary-max LUFS** (BS.1770 K-weighting, 400 ms window). Every recipe masters to its own target (+
  the §6c `offset` of its kind) and the peak is limited to −1 dBFS, because Unity's Vorbis re-encode overshoots a
  little.

| Kind | Target LUFS | Length | Variants | Cooldown |
|---|---|---|---|---|
| UI tick (repeats) | −24 | ≤ 0.05 s | 1 | — |
| UI click / select / close / whoosh | −22…−21 | 0.07–0.35 s | 1 | — |
| UI pop / coin / error | −20 | 0.12–0.45 s | 1 | — |
| UI unlock / level up / reward / fanfare | −15…−12 | 0.7–1.6 s | 1 | — |
| Shot (per projectile) | −21…−18 | 0.2–0.4 s | 3–4 | 0.05 s |
| Hit (per collision) | −21…−18 | 0.2–0.65 s | 3–4 | 0.06 s |
| Explosion (per blast) | −15 | 0.8–1.2 s | 3 | 0.06 s |
| Cast / charge / heal / spawn (per activation) | −19…−18 | 0.5–1.3 s | 1–2 | 0 |
| Aura loop (background) | −24 | 1 s cycle | 1 | — |

- `burst.wav` simulates a busy scene: random triggers (hit 10/s, shot 6/s, explode 3/s) through the cooldown,
  summed like `PlayOneShot`. QA compares its peak with the same simulation of the reference clips (`BURST_LOUD` if
  more than 3 dB hotter).
- **Phone speakers reproduce almost nothing under ~300 Hz.** QA measures `phone_loss_db`, the loudness lost after a
  300 Hz high-pass. Good hits/shots/UI lose ≤ 2.2 dB; explosions lose 3–8 dB. Weight must live in 150–450 Hz knocks
  and 1–4 kHz grit, never in a 50 Hz sine (`PHONE_WEAK` blocks above 4 dB for hits/shots/UI, above 8 dB otherwise).

## 5. Timing

- **Hits, shots and clicks read on sample 0**: onset ≤ 12 ms (`LATE_ONSET`). No anticipation.
- Whooshes, charges and spawns swell in on purpose (recipe flag `soft`).
- Charge/cast: build-up to ~0.8 of life, release beat, short sparkle.
- Every sound decays to silence inside its buffer (`CUT_TAIL` if it is still sounding when cut). Loops wrap with an
  equal-power crossfade and integer LFO cycles (`LOOP_SEAM`).

## 6. Forbidden (for every game; the game's own bans go in ArtStyle.md §6c / §9)

- Sub-only booms (everything under 150 Hz): silent on phones, mud on headphones.
- Harsh hiss-heavy hits (brightness above 5 kHz on hit/shot = `HARSH`): fatiguing at 10 hits per second.
- One identical clip for a frequent event (`SAME_VARIANTS`): the "machine-gun" repetition effect.
- Peak-normalised clips (everything at 0 dBFS): loudness must follow §4, not the peak. Keep Unity's `Normalize`
  import option off.
- Long reverb tails on frequent sounds: they smear into a wash when stacked.
- Realistic recording-style sounds (gunshots with room reverb, gore, voice) unless the game's §6c says otherwise.
  The recipes are built for cartoon/arcade games.
