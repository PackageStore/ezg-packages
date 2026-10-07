# fxpaint API (scripts/fxpaint.py)

## Model

- Coordinates are normalised to **[-1, 1]** with the centre at 0 and **y pointing up**. Frames are square, `px` final
  pixels, supersampled 2× (`ss`).
- A `Frame` holds named **layers** (bottom first). Each layer has a scalar light field `E` (HDR, unbounded),
  a colour LUT (ramp), `alpha_gain` and `bloom`. Primitives **add** intensity to a layer, so overlaps get brighter.
- `finish()`, per layer:
  1. Bloom: `E += gaussian(E, r) * s` for each `(r, s)`.
  2. `u = 1 - exp(-E)`, then colour = `LUT[u]`. Dim maps to deep hue, bright to white.
  3. `alpha = 1 - exp(-E * alpha_gain)`.
  4. Composite over the previous layers, then the `ink` layer (dark, alpha-composited, e.g. soot) on top.
  5. Premultiplied 2× box downsample, then un-premultiply (no fringes).

  Output: an RGBA PIL image (straight alpha) for `Mobile/Particles/Alpha Blended` (the project `_mat_ab` material).
- Intensity guide (default ramps): **0.2–0.5** deep hue, faint glow · **0.8–1.3** signature colour ·
  **1.8–2.5** pale, near white · **3+** white-hot core.

Element ramps: `vfx_recipes.PALETTES` (built-in starting points) plus whatever the game declares in ArtStyle §6b;
`make_vfx` registers those with `vfx_recipes.register_palette(name, stops, spark=None, soot=None, haze=None)` (missing
parts are derived from the ramp). `palette(name)` turns an entry into LUTs for painting.

`vfx_recipes.frame(pal, px, haze=False)` builds a frame with the standard layer `"fx"`
(ramp = element, alpha_gain 2.2, bloom ((0.025, 0.45), (0.09, 0.22))), plus an optional faint `"haze"` layer below it.

## Primitives (all take `layer, intensity`)

| Call | Draws |
|---|---|
| `dot(cx, cy, r, layer, I, hard=0)` | Gaussian blob; `hard` > 0 adds a crisp disc core (specks, cores) |
| `oval(cx, cy, rx, ry, layer, I, hard=0)` | Anisotropic blob. For sprites Unity stretches (stretched billboards) paint round shapes pre-compressed along x (`rx = r / lengthScale`). `disc(squash > 1)` gets cut by its box, use this instead |
| `needle(cx, cy, ang, length, width, layer, I, back=0, power=1)` | Tapered ray from the root along `ang` (rad); `back` extends behind (diamond shard); `power` < 1 = blunter |
| `star4(x, y, ang, length, width, layer, I, short=0.55, diag=0)` | 4-point needle star (long and short pairs) plus optional diagonals |
| `ring(cx, cy, R, w, layer, I, squash=1, mod=None, rot=0, sx=1)` | Glowing ring. `squash` < 1 = ground ellipse; `sx` < 1 = vertical ring; `mod(theta)` = per-angle multiplier (break-up, draw-in mask) |
| `disc(cx, cy, R, layer, I, edge, core, bumps, squash, power, rim, rimw)` | Filled body brightening toward the centre. `bumps(theta)` reshapes the silhouette; `rim` adds an edge band (shell); fall `core` and raise `power` over time to hollow it |
| `crescent(cx, cy, R, a0, a1, w, layer, I, squash=1, peak=0.5)` | Arc stroke from `a0` to `a1` (CCW), sharp ends; `peak` = where it is thickest (0.5 moon, 0.65–0.8 slash with a heavy head) |
| `petal(cx, cy, ang, length, width, layer, I, hot=0.5)` | Teardrop flame petal from the root along `ang`; `hot` = tip brightness relative to the root. |
| `beam(x0, y0, x1, y1, width, layer, I, fade=1)` | Soft constant-width column fading toward (x1, y1) (light pillar) |
| `polyline(pts, width, layer, I)` | Thin glowing zigzag or polygon (runes, hexagram, lightning) |
| `specks(seed, count, cx, cy, r_in, r_out, size, layer, I, squash=1, twinkle=None)` | Sparkly dust with fixed per-seed positions; growing `r_out` makes it drift outward |
| `ink_ring(cx, cy, R, w, color_rgb, alpha, bumps, squash, breakup)` | Dark rim (soot) on the ink layer |
| `ink_blob(cx, cy, R, color_rgb, alpha, bumps, edge, squash=1)` | Dark filled blob on the ink layer. Ink is laid OVER the light, so a blob under a glowing core turns it grey and dull: keep ink to thin rims |

## Shape helpers (call once, before the frame loop, with the recipe's rng)

- `bumpy(rng, count, amp)` → `theta -> radius multiplier`: scalloped cloud silhouette.
- `spiky(rng, count, amp, width)` → starburst or spiky-shell silhouette.
- `breakup(rng, count, lo, hi, sharp)` → `theta -> 0..1` gaps for rings that break apart.

## Timing helpers

- `window(t, a, b, c, d)`: 0 before `a`, smooth rise to 1 at `b`, hold, smooth fall to 0 from `c` to `d`.
- `ease_out(t, k)`: fast start, slow settle (growth, expansion). `ease_in(t, p)`: slow start. `smooth(t)`: smoothstep.
- `cyc(t, k, phase)`: periodic 0..1 (loops; integer `k` keeps the loop seamless).
- `twinkles(i)`: per-speck brightness multipliers for frame `i`.
- `sheet(frames, cols)`: row-major grid from the top-left (Unity texture-sheet order).
- `ramp(stops)`: `[(pos, "#hex"), ...]` → LUT.
