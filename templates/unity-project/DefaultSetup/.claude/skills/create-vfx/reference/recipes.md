# Recipes: catalogue, parameters, writing new ones, QA

## 1. Request → recipe

Element default = the recipe's when neither the dev nor ArtStyle §6b `default` names one. Group = the prefab name
group (game-vfx 8.2) the recipe uses unless `--group` overrides it.

| Request says… | Recipe | Element default | Notes |
|---|---|---|---|
| impact / hit / trúng đòn / va chạm, fire / physical / bullet | `impact_star` | fire | needle star + ring + crescents + shards |
| impact of ice / electric / crystal / "crisp" / critical | `impact_shatter` | frost | lens streak + double ring + diamond shards |
| muzzle / bắn / nòng súng / gun flash | `muzzle_flower` | fire | points **+X**, pivot = barrel tip |
| magic muzzle / energy bolt / cast-shot | `muzzle_beam` | arcane | points **+X**, pivot = emitter |
| explode / nổ / bomb / fireball | `explode_burst` | fire | scalloped ball, rays, hollow shell, soot rim |
| explode of ice / energy / magic / nova / shock | `explode_nova` | frost | spiky shell, crystals, outer ring |
| cast / niệm phép / skill circle / summon / buff start | `cast_sigil` | gold | ground circle in 3/4 + pillar; pivot = feet |
| charge / tụ lực / channel / cast-release / ultimate | `cast_charge` | arcane | spiral motes → orb → star burst |
| slash / chém / swipe / melee | `slash_arc` | fire | faces **+X**; rotate root to aim |
| projectile / đạn / orb / bullet body | `orb_loop` | arcane | loop + world-space trail motes |
| aura / buff / vùng / zone / under-feet | `aura_loop` | holy | loop, ground layer, pivot = feet |
| buff EXP / x2 kinh nghiệm / level gain / growth / tăng tiến | `aura_ascend` | frost | loop, ground layer, pivot = feet; up-chevrons + rising crystals beside the hero, intro 0.3 s |
| meteor / thiên thạch / sao băng / sky strike / falling bomb | `meteor_strike` | fire | multi-sheet (head + ground mark + impact + crater); pivot = impact point |
| spin attack / xoay người / lốc kiếm / bão dao / whirlwind / blade storm zone around the hero | `blade_storm` | rose | loop; top-down whirl squashed to 3/4 + particle spin, daggers flung out (stream); pivot = hero |
| heal zone / vòng hồi máu / totem / regen field | `aura_heal` | toxic (green heal) | loop; edge ring = heal radius at 0.8 × size/2; inward pulse + rising plus-crosses; pivot = zone centre |
| slow / chậm thời gian / hourglass / time warp pulse | `time_ripple` | gold | one-shot; clock face with whirling hands + 3 rings rolling out; pivot = centre |
| shield / khiên / barrier bubble around the hero | `shield_bubble` | electric | loop; hex-facet bubble drawn in front of the hero; pivot = body centre |
| invincible / bất tử / divine / god mode aura | `divine_aura` | holy | loop; sunburst + halo BEHIND the hero, ground ring at feet; pivot = body centre |
| stomp / dậm đất / shockwave / knockback ring / push | `ground_slam` | frost | one-shot; ROUND ring to 0.82 × size/2 (push radius) + 3/4 inner wave + cracks; pivot = stomp centre |

Element words: lửa/fire/blaze → `fire`; vàng/thánh/holy/heal/light → `holy` or `gold`; băng/ice/frost/nước → `frost`;
phép/arcane/magic/tím → `arcane`; độc/poison/toxic → `toxic`; bóng tối/dark/shadow/curse → `shadow`;
điện/lightning/electric/thunder → `electric`; hồng/pink/rose → `rose`. A ramp the game declared in ArtStyle §6b
(`ramp <name>: …`) is used by its name like any built-in element, and overrides a built-in of the same name.

Naming / output folder (SKILL.md "Kết quả", game-vfx 8.2):
- `--name <snake_name>` (+ optional `--subject <snake>`, `--group <group>`) → prefab `fx_[<subject>_]<group>_<name>` in
  `<vfxRoot>/<Base>/`, sheet `FX_TX_<Name>_<cols>x<rows>.png`, sprites `FX_TX_<Name>_<cols>x<rows>_<i>`, layer sheets
  `FX_TX_<Name><Layer>_<cols>x<rows>.png`, stream sprites `FX_TX_<Name><Stream>.png`. Examples: `--recipe explode_nova
  --element frost --name nova_frost` → `fx_hit_nova_frost`; `--recipe cast_charge --name fireball --subject hero` →
  `fx_hero_cast_fireball`.
- Put the element in the name when the game keeps several colours of one effect (`_fire`, `_frost`: 8.2 variant).

## 2. Catalogue and defaults (`python3 make_vfx.py list`)

| Recipe | Group · role | Kind | Frames (grid) @px | Life s | Size u | Offset (×size) | Extras |
|---|---|---|---|---|---|---|---|
| impact_star | hit · impact | impact | 12 (4×3) @256 | 0.34 | 4.2 | 0, 0 (random rot) | 7 sparks |
| impact_shatter | hit · impact | impact | 12 (4×3) @256 | 0.36 | 4.2 | 0, 0 (random rot) | 7 sparks |
| muzzle_flower | muzzle · fire | muzzle | 8 (4×2) @256 | 0.16 | 4.0 | +0.31, 0 (align Local) | 5 sparks, 40° fan |
| muzzle_beam | muzzle · flash | muzzle | 8 (4×2) @256 | 0.18 | 4.0 | +0.31, 0 (align Local) | 5 sparks, 40° fan |
| explode_burst | hit · fire | explode | 16 (4×4) @320 | 0.72 | 8.5 | 0, 0 (random rot) | 12 sparks + 10 drift motes |
| explode_nova | hit · shockwave | explode | 16 (4×4) @320 | 0.72 | 8.5 | 0, 0 (random rot) | 12 sparks + 10 drift motes |
| cast_sigil | cast · ring | cast | 20 (5×4) @320 | 1.1 | 7.5 | 0, +0.18 | rising motes |
| cast_charge | cast · glow | cast | 16 (4×4) @320 | 0.9 | 6.5 | 0, 0 | rising motes + release sparks |
| slash_arc | slash · trail | slash | 10 (5×2) @320 | 0.26 | 5.0 | 0, 0 (align Local) | 6 sparks, 120° fan |
| orb_loop | proj · glow | projectile | 8 (4×2) @256, loop | 0.4/cycle | 2.2 | 0, 0 | trail motes (world, per distance) |
| aura_loop | aura · ring | aura | 12 (4×3) @320, loop | 1.2/cycle | 5.0 | 0, +0.18, ground layer | rising motes (loop) |
| aura_ascend | buff · ring | aura | 16 (4×4) @320, loop | 1.2/cycle | 5.0 | 0, +0.18, ground layer, intro 0.3 s | two rising mote streams (loop) |
| meteor_strike | skill · impact | explode | 20 (5×4) @320, delay 0.42 | 0.9 | 10.5 | 0, 0 (ground-aligned, no rot) | layers Head (8f loop, stretched, falls from (3,10) in 0.42 s, 2 birth trails) · Mark (12f, ground layer) · Crater (12f, 1.2 s, ground layer); 26 sparks + drift + rise motes |
| blade_storm | aoe · trail | aura | 6 (3×2) @320, loop | 0.3/cycle | 10.0 | 0, 0, squash 0.62, spin 576°/s CCW, intro 0.22 s | stream Dagger (16/s, sweeps CCW 1.6 loops/s, orbital 110°/s, stretched ×2.4) · 16 start sparks |
| aura_heal | heal · ring | aura | 16 (4×4) @320, loop | 1.2/cycle | 5.0 | 0, 0 | rising motes (loop); ring radius 0.8 × size/2 |
| time_ripple | skill · wave | explode | 20 (5×4) @320 | 0.9 | 12.0 | 0, 0 | sand specks |
| shield_bubble | shield · glow | aura | 16 (4×4) @320, loop | 1.0/cycle | 6.8 | 0, 0 (front of hero) | crawling arcs |
| divine_aura | buff · glow | aura | 16 (4×4) @320, loop | 1.2/cycle | 7.0 | 0, 0 (behind hero) | rising sparkles |
| ground_slam | hit · shockwave | explode | 20 (5×4) @320 | 0.6 | 9.75 | 0, 0 | light shards; ring 0.82 × size/2 = 4 u at 9.75 |

`meteor_strike` total ≈ 1.65 s: head + ground mark 0 → 0.42 s, impact at 0.42 s, crater cools until ≈ 1.62 s. Fall
timing and start point live in `METEOR_FALL` / `METEOR_FROM` in `vfx_recipes.py`.

`blade_storm`: the sheet is painted **top-down and not rotated in the frames** (they only breathe and shimmer). The
prefab squashes the flipbook to the 3/4 ellipse (`squash`) and turns the particle (`spin`), so the whirl spins perfectly
smoothly with few frames: Unity applies the transform scale after the particle rotation, so a turning circle stays a
correct ground ellipse. Blades lead counter-clockwise, keep `spin` > 0. Size 10 puts the boundary ring (r 0.85) at
4.25 u; nest the prefab under the zone's hitbox root so the zone grows with the hitbox (2.4.4).

CLI overrides:
- `--size` scales spark/mote radius, speed and size automatically.
- `--life`, `--frames` (the grid is re-chosen so cols×rows == frames; the sheet name carries the grid, so a new grid
  leaves the old sheet listed as `old`), `--px` (frame resolution; keep 256–320), `--seed` (a different but same-style
  variant), `--layer` (main sorting layer), `--out` (root folder instead of `vfxRoot`).
- `--name` / `--subject` / `--group`: prefab name (see §1).

## 3. Variants without code

- Same look in another colour: `--element`.
- Bigger or slower hit: `--size 6 --life 0.45`.
- Two different-looking hits for variety: same recipe, different `--seed` and a variant name (`spark_fire_01`, `_02`).

## 4. Writing a new recipe (when nothing in §1 fits)

1. Study 1–2 in-game sheets of that kind (style-guide §3) and list their elements and beats.
2. Add a function to `scripts/vfx_recipes.py`:

```python
def my_effect(pal, seed, n, px):
    rng = np.random.default_rng(seed)                 # everything random is drawn ONCE, before the frame loop
    rays = [(rng.uniform(0, TAU), rng.uniform(0.5, 0.9)) for _ in range(12)]
    out = []
    for i in range(n):
        t = i / (n - 1)                              # one-shot: 0..1 inclusive. Loop: t = i / n (frame n == frame 0)
        f = frame(pal, px)                           # one light layer "fx" mapped through the element ramp
        fl = window(t, -0.01, 0.0, 0.08, 0.3)        # 0 -> 1 -> hold -> 0 envelope: flash on frame 0
        f.dot(0, 0, 0.15, "fx", 3.4 * fl)            # intensity ~3+ = white, ~1 = signature colour, ~0.3 = deep hue
        R = 0.2 + 0.4 * ease_out(t / 0.5)            # ease-out growth
        f.ring(0, 0, R, 0.016, "fx", 1.2 * window(t, 0, 0.05, 0.25, 0.6))
        for ang, ln in rays:
            f.needle(0, 0, ang, min(ln * ease_out(t / 0.3), 0.9), 0.012, "fx", 1.5 * fl)
        f.specks(seed, 20, 0, 0, 0.05, R, 0.012, "fx", 2.4 * window(t, 0.1, 0.3, 0.6, 1.0), twinkle=twinkles(i))
        out.append(f.finish())
    return out
```

3. Register it in `RECIPES` with `group` (8.2), `role` (8.4 role of the main flipbook), `seed`, `kind`, `frames`, `cols`,
   `px`, `life`, `size` and optionally `offset`, `align_local`, `random_rotation`, `loop`, `sorting_layer="ground"`
   (below characters), `extras` (`_sparks(...)` / `_motes(...)`), `desc`.
   **Multi-sheet effects** (several beats in different places / sorting layers, e.g. `meteor_strike`): add `delay`
   (main flipbook start) and `layers=[dict(name, role, fn, frames, cols, px, life, size, [secondary, loop, cycle, delay,
   position, velocity, lengthScale, velocityScale, fade, grow, sorting_layer, sorting_order, trail=[_motes("birth", ...)]])]`.
   Each layer gets its own sheet `FX_TX_<Name><Layer>_<c>x<r>.png`, its own QA and review sheet, and becomes a layer
   `<role>_ab[_n]` under `containers` in the prefab (`flipbooks[]` in the spec). A moving layer uses a constant `velocity` (exact landing);
   with `lengthScale` it is a stretched billboard, so paint it horizontally with the head on the LEFT and round
   shapes pre-compressed along x (`Frame.oval`).
4. Iterate in Python only: `make` → QA → open the `.sheet.png` → adjust. Then build in Unity.
5. Add the row to §1 and §2 of this file.

**Rules of thumb:**
- Keep everything within radius 0.9 of the frame (normalised −1..1). Remember that bloom adds about 0.1.
- Use `window()` for every element so it fades in and out. Hard on/off steps cause `JUMP`.
- Loops: drive motion with `cyc(t, k)` (integer k) and rotate by exactly `2π/symmetry` per loop. For moving
  particles use `frac = (t + phase) % 1` with an envelope `sin(π·frac)`.
- Directional effects (muzzle, slash) face +X. Anchor the emitter in the frame and set `offset` so it lands on the
  pivot (`MUZZLE_X = -0.62` → offset `+0.31`; ground `SIGIL_Y = -0.36` → offset `+0.18`).
- Ground circles: squash 0.42 (3/4 view), centre at `SIGIL_Y`.

## 5. QA codes (`make` prints them; `.vfx.json` stores them)

| Code | Meaning | Fix |
|---|---|---|
| CLIPPED | alpha > 0.06 on the cell border, so a straight cut line shows in game | clamp reach/radius (`min(..., 0.86 - r)`), fade particles near the edge, shrink shapes |
| SLOW_START | impact/muzzle/explode/slash frame 0 is almost empty | start the envelope at t=0 (`window(t, -0.01, 0, …)`) or begin a sweep at 15 % |
| POP_AT_END | last frame still visible (one-shot) | every `window()` must end ≤ 1.0 |
| JUMP | one frame-to-frame change > 3.5× the median | widen the fade window of whatever switches off there, or add frames |
| LOOP_SEAM | last→first frame differs a lot (loop) | use `t = i/n`, integer `cyc` periods, rotations of exactly one symmetry step |
| WASTED_SPACE | the effect spans < 45 % of the frame (info) | lower `--px` or enlarge shapes |
