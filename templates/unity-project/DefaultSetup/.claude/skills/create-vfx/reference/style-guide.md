# FX style guide: the look this generator paints, and how a game makes it its own

This file describes the **family of looks** the painter produces, so an agent knows what it can and cannot deliver.
It holds no game's values. A game's VFX style lives in `.claude/docs/ArtStyle.md` §6b (identity, element ramps,
review grounds, approved reference sheets, forbidden looks) and wins over everything here; the team's technical VFX
rules live in the game-vfx standard. When the dev rejects a look, log it in ArtStyle §9 and in the "Cấm" line of §6b
in the same turn (rule `art-style`); add it to §6 below only when it is a defect of the painter itself.

## 1. How the effects are built

- **Painted flipbook sheets** (3×2 … 5×4 grids, 256–320 px per frame), played by **one particle** with Texture Sheet
  Animation over its lifetime, plus a few small spark / mote particles for depth.
- Blend: alpha blend through the shared material `_mat_ab` (`Mobile/Particles/Alpha Blended`); the glow is **baked into
  the alpha**, so the sheets read as light on dark ground and keep their colour on bright ground (game-vfx 5.5).
- Sizes are in world units. Check the game's camera (ortho size, portrait or landscape) before choosing `--size`: on a
  portrait camera of ortho size 20 the screen is about 22.5 units wide, so a 4-unit hit is ~1/5 of the width and an
  8.5-unit explosion ~40 %. Match the effect's tier (game-vfx 2.1) and the hitbox (2.4).

## 2. Visual vocabulary (what the recipes are made of)

| Element | Look | Recipes |
|---|---|---|
| **White-hot core** | near-white centre with a saturated halo; the brightest thing on frames 0–2 | every hit and explosion |
| **Needle rays / star** | long thin tapered spikes, 4-point lens stars, short/long alternation | impact_*, explode_nova, cast_charge |
| **Thin glowing ring** | crisp ring with a soft halo, expanding with ease-out | impact_*, explode_nova, ground_slam, time_ripple |
| **Shell** | scalloped (fire) or spiky (energy/ice) body that **hollows out** into a bright rim | explode_burst, explode_nova |
| **Specks** | dozens of tiny bright dots with halos, twinkling, drifting outward, the last thing to vanish | almost all |
| **Crescents / swooshes** | thin arc strokes thick in the middle and sharp at the ends | impact_star, slash_arc, blade_storm |
| **Soot rim** | thin dark rim when fire dies (fire only, never a thick outline) | explode_burst |
| **Ground ring (3/4 view)** | ellipse squashed to about 0.42; flames or motes rise from it | cast_sigil, aura_*, meteor_strike |
| **Vertical rings** | ellipses squashed in X around an orb or beam (gyroscope / charge) | cast_charge, orb_loop |

A game whose identity is something else (pixel art, hard cel bands with outlines, smoke-heavy realism, hand-drawn
frame-by-frame fire) is **not** served by these recipes as they are: say so, and either write new recipes for that
identity (recipes.md §4) or use the game-vfx library / particles-and-shader route.

## 3. Reference sheets

The style gate compares a new effect with effects **the game already approved**: ArtStyle §6b lines
`ref <kind>: <sheet.png> <cols>x<rows> @<col>,<row>` (one representative frame each). `make_vfx.py compare` crops those
frames next to the new sheet. No `ref` yet (new game, or first VFX) → the gate is the dev's review of the review sheets;
once the dev approves an effect, add its sheet as a `ref` so the next effect has something to match.

## 4. Colour

- Every element is **one gradient ramp from a deep hue to white**: dim light shows as the deep hue, mid light as the
  signature colour, bright light burns to white. The painter does this automatically (`vfx_recipes.PALETTES`).
- Built-in ramps (starting points, not a game's palette): fire, gold, frost, arcane, toxic, holy, shadow, electric, rose.
  The game declares its own in §6b (`ramp <name>: 0:#… … 1:#… | spark #… #… #…`): a new name adds an element, a built-in
  name overrides it. game-vfx 5.3 gives element palettes measured from the source material as another starting point.
- **Contrast with the game's ground.** Elements close in hue to the gameplay ground (e.g. green on grass) need brighter
  cores and specks or they melt into the map: check both review grounds and say so in the report.
- No element named: ArtStyle §6b `default`, else the recipe's default (recipes.md §1).

## 5. Timing (what "smooth" means here; within game-vfx 3.1)

- **Hits read on frame 0.** Flash and star at t=0 (no anticipation for impacts, muzzles, explosions or slashes).
  Peak at about 0.05–0.15 of life, then a long tail of specks.
- **Explosions:** flash (0–0.2) → body grows with ease-out (≤0.45) → hollows into a shell (0.15–0.55) →
  rim/soot breaks up (0.5–1.0) → specks last.
- **Cast:** build-up is allowed (circle draws itself in, motes converge), then a release or brightening beat at
  about 0.6–0.7, then fade.
- Nothing pops: every element fades by t=1 (QA `POP_AT_END`). Loops must wrap seamlessly (QA `LOOP_SEAM`).
- Life guide: impact 0.3–0.4 s, muzzle 0.12–0.2 s, slash 0.2–0.3 s, explosion 0.6–0.8 s, cast 0.8–1.2 s,
  loops 0.4–1.2 s per cycle. Frames: 8–20 per sheet (no frame blending, so raise `--frames` toward 25–30 fps of life if
  a long effect steps), inside the texture ceiling of the effect's tier (game-vfx 6.2).

## 6. Painter defects to avoid (they read as wrong in any game using this look)

- Volumetric / 3D lighting: lambert-shaded billowy puffs, sphere shading, dark grey 3D smoke.
- Flat cel-cartoon fire: hard colour bands with thick dark outlines (a different identity, see §2).
- Rocks or debris with dark outlines, drawn as objects instead of light.
- Dull, desaturated or brown-beige fills (they come from per-channel tone mapping; always gradient-map).
- Muddy translucent haze covering the whole body late in an explosion: keep any haze to a trace.
- Effects cut by the frame edge (QA `CLIPPED`): a straight cut line shows in game, especially with random rotation.
