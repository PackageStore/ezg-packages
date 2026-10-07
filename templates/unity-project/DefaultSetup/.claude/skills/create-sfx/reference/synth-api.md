# Synth API (`scripts/sfxsynth.py` + recipe helpers)

Mono float64 at `SR = 44100`. Durations in seconds, frequencies in Hz. Any argument documented as "scalar/array"
accepts a per-sample curve (e.g. from `glide` or `pts`) for sweeps.

## Time & envelopes

| Call | Returns |
|---|---|
| `ns(d)` / `tt(d)` | sample count / time axis of `d` s |
| `perc(d, a=0.002, k=5, hold=0)` | linear attack `a`, exponential decay reaching e^-k at the end (k ≥ 5 = clean tail) |
| `ar(d, a, r_shape=2)` | swell to 1 at fraction `a`, power-curve release to 0 (whooshes, swells) |
| `pts(d, [(t_frac, v), …], log=False)` | breakpoint curve (amplitude or cutoff; `log=True` for frequencies) |
| `glide(f0, f1, d, mode="exp"\|"lin"\|"fast", tau)` | pitch/cutoff sweep; `fast` snaps from f0 toward f1 with time constant `tau` (kick/knock drop) |
| `semis(st)` | frequency ratio of `st` semitones |

## Sources

| Call | Sound |
|---|---|
| `osc(freq, d, shape, phase=0, duty=.5)` | band-limited `sine` / `tri` / `saw` / `square` / `pulse` (PolyBLEP, no aliasing on sweeps) |
| `fm(freq, d, ratio, index)` | 2-op FM; inharmonic ratios (2.76, 3.5) = bells, index envelope = brightness over time |
| `noise(d, rng, color)` | `white` / `pink` / `brown` |
| `partials(f0, d, ratios, amps, decays, rng, detune)` | modal synthesis: each partial its own decay (bells, metal, glass) |
| `crackle(d, rng, density, bright, decay)` | sparse pops: fire crackle, debris, sparks |
| `sample_hold(d, rng, rate, lo, hi)` | stepped random curve (gating, glitches) |

Partial sets in `sfx_recipes.py`: `BELL` [1, 2.76, 5.40, 8.93], `GLASS` [1, 2.32, 4.25, 6.63, 9.38], `COIN`
[1, 2, 2.76, 4.07], `HARM` [1, 2, 3, 4].

## Processing

| Call | Use |
|---|---|
| `lp(x, fc)` `hp(x, fc)` `bp(x, lo, hi)` | static Butterworth filters |
| `vfilter(x, "lp"\|"hp"\|"bp", fc, q)` | resonant biquad with a moving cutoff (sweeps, swooshes); `fc` scalar/array |
| `drive(x, k)` | tanh saturation: adds harmonics so low bodies survive phone speakers |
| `ring(x, f)` | ring modulation (electric / metallic) |
| `reverb(x, rng, decay, wet, damp)` | small synthetic room (recipes declare it via `reverb=(decay, wet)`; make_sfx applies it) |
| `mix(sig, (sig, at_s, gain), …, length=n)` | place layers in time |
| `loop_seam(x, xfade)` | seamless loop from a buffer of loop + xfade |
| `resample(x, ratio)` | pitch+tempo shift |
| `fade(x, fin, fout)` | de-click (make_sfx applies 0.5 ms in / 6 ms out) |

## Mastering & analysis (make_sfx calls these)

| Call | |
|---|---|
| `loudness(x)` | momentary-max LUFS (BS.1770 K-weighting, 400 ms window) |
| `master(x, target_lufs)` | 25 Hz high-pass → loudness gain → soft-knee limit to −1 dBFS (returns dB shaved) |
| `trim_tail(x, -60)` | drop trailing silence |
| `analyze(x)` | `dur, peak_db, lufs, onset_ms, attack_ms, tail_s, centroid_hz` (power-weighted), `phone_loss_db`, `dc`, `start_abs`, `end_abs`, `end_level_db` |
| `read_wav(p)` / `write_wav(p, x)` | RIFF PCM 8/16/24/32 + float 32/64 in; 16-bit TPDF-dithered mono out |

## Recipe context `c` (`sfx_recipes.Ctx`)

| Member | |
|---|---|
| `c.hz(f)` | f × element register × `--pitch` × variant jitter |
| `c.dur(d)` | d × (`--life` / recipe life) |
| `c.cut(f)` | filter cutoff × element brightness (clamped 60 Hz–18 kHz) |
| `c.noise(d, color=None)` | noise in the element's colour |
| `c.j(lo, hi)` | variant-to-variant random value |
| `c.tint(d, gain)` | the element's signature layer (already faded out over its last 35 %) |
| `c.el`, `c.element`, `c.rng`, `c.amt` | raw element dict, name, generator, tint amount |

Recipe helpers: `tick(c, d, f, g)` (transient), `note(c, f, d, shape, a, k, harm, amps)` (pitched note).
