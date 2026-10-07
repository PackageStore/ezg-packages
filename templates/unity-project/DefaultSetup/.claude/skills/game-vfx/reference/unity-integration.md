# Unity integration: kit, FX convention, spec, prefab anatomy, preview, gameplay use, gotchas

Rules come from the game-vfx standard (`GameVFX_QuyChuan.md`, found the way skill `game-vfx` §1 says); section
numbers below (7.1, 8.4…) point there. This file only says how the kit applies them.

## 1. Kit (installed by `make_vfx.py install`, idempotent)

| Skill file | Project path |
|---|---|
| `unity/GameVfxPrefabBuilder.cs` + `unity/Ezg.GameVfx.Editor.asmdef` | `<sourceRoot>/Editor/GameVfxKit/` (own Editor-only assembly `Ezg.GameVfx.Editor`, references `Unity.2D.Sprite.Editor`) |
| `unity/GameVfxShowcaseLoop.cs` (only with `install --showcase`) | `<vfxRoot>/_Kit/` (runtime, UniTask; compiles into whatever assembly owns that folder) |

- `sourceRoot` / `vfxRoot` come from `.claude/project-profile.json` (`make_vfx.py config` prints them).
- `install` overwrites a changed project copy with the skill copy. **Edit the skill copy, never the project copy.**
- After install: `Assets/Refresh` → wait for compile → `unity_get_compilation_errors severity:error` = 0.
- Needs package `com.unity.2d.sprite` (sprite slicing API); `install` warns when `packages-lock.json` lacks it.
- Shared particle sprites: `make_vfx.py shared` → `<vfxRoot>/_Shared/FX_TX_Spark.png`, `FX_TX_Mote.png`.
- No custom shader. Two shared materials (8.2, 8.4): `_mat_ab` (Mobile/Particles/Alpha Blended) for the **base** layers
  (every recipe layer today), `_mat_add` (Mobile/Particles/Additive) for optional **additive** layers drawn on top of them.

## 2. FX convention (applied by the builder on every build)

| Item | Setting |
|---|---|
| Sheet texture | Sprite (2D and UI), Multiple, Full Rect, pivot centre, PPU 100, no mipmaps, Clamp, Bilinear, CompressedHQ, max 2048; Android + iOS override ASTC 4×4 (4.5: smooth glow gradients; gate V-5) |
| Slicing | grid `cols × rows`, one sprite per frame, named `<texture>_<i>` row-major from the top-left. Existing sprite IDs are reused by name, so a rebuild never breaks references |
| Single sprites (spark, mote, stream sprites) | Sprite, Single, Full Rect, centre pivot; mipmaps ON (drawn tiny; deliberate exception to 4.5) |
| Flipbook particle | Texture Sheet Animation **Mode = Sprites**, every frame in order, Time Mode Lifetime, frameOverTime 0 → 0.9999 |
| Material | by the entry's `blend`: `ab` → the project's `_mat_ab`, `add` → `_mat_add` (name match, case-insensitive). Several → the one on the matching Mobile/Particles shader (Alpha Blended / Additive), then the one most prefabs reference. None → created as `<vfxRoot>/../Materials/_mat_ab.mat` / `_mat_add.mat` (Built-in only; SRP project → the builder stops and asks for one, or set `"material"` / `"materialAdd"` in the spec) |
| Sorting | spec layers (main / ground, from ArtStyle §6b `layers`, default `FX` / `FX_Ground`); a missing layer or `Default` is a warning (7.3, gate V-14) |

The look relies on alpha blending: the glow is baked into the sheet's alpha, so it reads on dark **and** bright ground
(5.5.1), which additive would not. So `ab` is the **base** layer of every effect. An `add` layer is optional: put one
on top (a flash, a hot core, a glow) only when the ab layers are not bright enough on the game's darkest ground, and
check it on the brightest ground too (additive washes out there). An effect never consists of add layers alone.

## 3. Spec (`<Base>.gamevfx.json`, written by `make`, read by `GameVfxPrefabBuilder`)

The spec sits next to its sheets; the builder resolves every file relative to the spec folder.

`name (= prefab name fx_…), subject, group, role, recipe, element, kind, seed, sheet, cols, rows, frames, life, size, delay,
loop, randomRotation, alignLocal, offsetX, offsetY (× size), sortingLayer, sortingOrder, blend, material (optional),
materialAdd (optional), sparkColors[4]
(white + 3 element hexes), sharedDir, sparks[], motes[], flipbooks[], streams[], spin, squash, intro, reviewGrounds[], qa[],
command (exact make_vfx command to regenerate the sheets), tool`

- `role`: 8.4 role of the main flipbook layer. `delay`: start delay of the main flipbook (multi-sheet effects).
- `blend` (spec, every `sparks[]` / `motes[]` / `flipbooks[]` / `streams[]` / `trail[]` entry): `ab` (default) or `add`.
  It picks the material (`_mat_ab` / `_mat_add`) and the layer suffix (`fire_ab`, `glow_add_sec`). The main flipbook
  stays `ab` (it is the base layer). `material` / `materialAdd`: explicit asset paths that win over the name lookup.
- `sparks[]`: `count, life[2], speed[2], size[2], arc, radius, delay, drag, gravity`. Stretched needles; an arc < 360
  is a fan centred on +X.
- `motes[]`: `mode` (`drift` = burst that floats off · `rise` = stream upward, `rate`/`delay`/`duration`, `scaleY` for a
  ground ellipse · `trail` = world-space, emitted per distance), plus `count, rate, life[2], speed[2], size[2],
  radius, rise[2], gravity, drag, noise, stretch` (`stretch` only matters inside a layer `trail`).
- `flipbooks[]` (extra layers): `name, role, secondary, sheet, cols, rows, frames, life, cycle` (> 0 = seconds per sheet
  loop), `size, delay, position[2]` (local units), `velocity[2]` (constant, local units/s), `randomRotation, lengthScale,
  velocityScale` (`lengthScale` > 0 = stretched billboard along the velocity), `fade[2]` (alpha in/out, fraction of
  life), `grow[2]` (size at birth/death), `sortingLayer` (empty = spec layer), `sortingOrder, trail[]` (motes with
  mode `birth`: Birth sub-emitters, world space, following the layer's particle for its whole life), `trailBack`
  (units behind the particle, along -velocity, where trails emit).
- `spin` (deg/s, + = CCW; the main flipbook particle turns), `squash` (y scale of the main flipbook transform: a
  top-down sheet seen in 3/4), `intro` (loops: seconds the main flipbook fades in and grows 0.55 → 1 after enable).
- `streams[]` (looping emitters of a painted single sprite, e.g. thrown daggers): `name, role, sprite` (png next to the
  spec, head on the LEFT), `rate, life[2], speed[2], size[2], radius, arcSpeed` (> 0: emission point sweeps the circle CCW,
  loops/s; 0: random), `orbital` (deg/s CCW curve of the flight), `drag, lengthScale` (stretched, velocityScale 0),
  `squash, fade[2], sortingOrder`. Colour comes from the sprite, the gradient only fades alpha.
- Hand-editing a spec is allowed (e.g. a different sorting order or an explicit `material`); rebuild afterwards. `make`
  overwrites it.

## 4. Builder API (`unity_execute_code`; no recompile per VFX)

```csharp
using B = Ezg.GameVfx.EditorTools.GameVfxPrefabBuilder;   // (write the full name in unity_execute_code snippets)
var p = B.Build("<vfxRoot>/fx_hit_explode_fire/fx_hit_explode_fire.gamevfx.json");   // import + slice + prefab
B.LastWarnings;                                         // missing sorting layer, material picked / created…
B.Describe(p);                                          // one line per system: names, stop action, max, sorting, material
B.BuildAll("Assets");                                   // every *.gamevfx.json in the project
B.EnsureSortingLayers("FX_Ground", "FX", "Default");    // only after the dev agreed on the names (7.3)
B.RenderFrames(p, "<abs>/Temp/GameVfx/<Base>/render_0", ortho, camY, 60f, life + 0.15f, 360, 7u, "#1c2027");
B.BuildShowcase(paths, "<vfxRoot>/_Showcase/VfxShowcase.unity", 18f, 3, 7.5f, 7f, "#1c2027");   // needs install --showcase
```

- Menus: `Tools/GameVFX/Build Prefab From Spec...`, `Tools/GameVFX/Rebuild All Specs` (every `*.gamevfx.json` under `Assets`).
- `BuildAll` re-imports every sheet. With many VFX it can outlast the MCP 120 s poll; the call keeps running in
  Unity, so poll `unity_editor_state` and check the results instead of re-running it.
- `RenderFrames` background: `"#rrggbb"` = solid colour, or a Sprite asset path = tiled real ground.
- In `unity_execute_code` snippets, write `UnityEngine.Object` explicitly (a bare `Object` is ambiguous there).

## 5. Prefab anatomy (`<Base>.prefab`, next to the spec; game-vfx 7.1 / 8.4)

```
fx_hit_explode_fire     control ParticleSystem (no emission, renderer off). duration = last layer end; playOnAwake;
│                       GameVFX module present → FXEffect + stop action Callback (7.8); else one-shots: Disable
│                       (a pool reclaims on OnDisable), loops: None (the caller stops / disables them)
└─ containers           control ParticleSystem, stop action None
   ├─ fire_ab           main flipbook: 1 particle, size = spec.size, life = spec.life (loops: 600 s + cycleCount),
   │                    Sprites mode with all frames, localPosition = offset × size, alignment Local for muzzle/slash
   ├─ spark_ab_sec      stretched needles (velocityScale 0.04, lengthScale 1.4), FX_TX_Spark
   ├─ glow_ab_sec       glow dots (drift / rise), FX_TX_Mote  ·  trail_ab_sec for world-space trail motes
   ├─ decal_ab …        extra flipbooks (flipbooks[]), each with its own role, sheet, delay, constant velocity
   │  └─ trail_ab_sec   Birth sub-emitter trail: stays a CHILD of its layer (Unity drives sub-emitters from their
   │                    owner) — the one place the tree is deeper than 7.1's flat layout
   ├─ debris_ab         streams[]: rate emitter of its own sprite, orbital velocity, drag, Stretch renderer
   └─ flash_add …       any entry with blend "add": same layer, `_mat_add`, suffix _add (optional, on top of ab)
```

- Layer names: `<role>_<ab|add>`, `_2`, `_3` when role + blend repeat, `_sec` for secondary layers (sparks, motes, trails), so
  `FXEffect.LowQuality` / the low tier drops them (2.2, 6.4).
- Max Particles: flipbooks 1, sparks 2 × count, motes 64, trails 96, streams rate × life + 2 (never the default 1000,
  gate V-3). Max Particle Size: 0.5 for small particles (7.3); **3 for flipbook layers**, which span up to half the
  screen and would be cut at 0.5 (deviation with reason; 7.3 trap: the cap cuts, it does not shrink).
- Sorting orders: flipbook 10, sparks +2, motes +3; extra layers / streams use their own order (trails one below
  their layer); an `add` extra layer without its own order gets 11, one above the ab layers it brightens. Scaling mode Hierarchy, so scaling the root scales everything (2.4.4: zone size from gameplay).
- **Spinning ground whirl (`spin` + `squash`):** the flipbook transform has localScale (1, squash, 1) and Rotation over
  Lifetime z = -spin (particle rotation is clockwise-positive). Unity applies the transform scale after the particle
  rotation, so a turning circle stays a correct ground ellipse (verified 6000.3).
- **Stretched layer (e.g. the meteor head):** Unity puts U=0 on the particle and stretches the quad `size ×
  lengthScale` behind it along the velocity. `make` starts the particle ahead of the painted head centre so the head
  lands exactly on the pivot when the layer's life ends.

## 6. Preview framing (RenderFrames)

| Kind | ortho | camY | length |
|---|---|---|---|
| impact / muzzle | 2.6 | 0 | life + 0.15 |
| slash | 3 | 0 | life + 0.2 |
| explode | 5 | 0 | life + 0.15 |
| cast_sigil (pivot = feet) | 4.4 | 2.0 | life + 0.15 |
| cast_charge | 4 | 0 | life + 0.15 |
| orb_loop | 1.6 | 0 | 3 cycles |
| aura_loop / aura_* (pivot = feet) | 3.4 | 1.2 | 2 cycles |
| shield_bubble / divine_aura (pivot = body) | 4 | 0 | 2 cycles |
| blade_storm (pivot = hero) | 6 | 0 | 1.2 (intro + 2 turns) |
| meteor_strike (pivot = impact) | 7.5 | 3.5 | 1.8 (whole timeline) |
| time_ripple / ground_slam | 6.5 | 0 | life + 0.15 |

Frame 0 of an offline render is empty: `Simulate(t < fixedDeltaTime)` runs no step. It is a preview artefact, not
runtime. Render once per review ground (`reviewGrounds` in the spec: darkest and brightest ground of the game).

## 7. Using the prefabs in gameplay

- One spawn path for every effect (7.8): the game's own path if it has one; otherwise the pool. In this template:
  `PoolingManager.Instance.Show(prefab, pos)` (skill `pooling-manager`); one-shots disable themselves when done and
  `PoolingComponent` returns them on `OnDisable`. With the GameVFX module: `FXEffect.Finished` → pool (ThuVien 2).
  Never `Destroy` them, never a timer to switch them off (6.8).
- **Muzzle / slash:** rotate the root so +X points along the shot or swing (`Quaternion.Euler(0, 0, angleDeg)`); the
  flipbook uses Local alignment and follows it.
- **Cast_sigil / aura:** the pivot is the feet / ground contact. **Explode / impact:** the pivot is the centre.
- **Orb (projectile):** parent the prefab under the projectile transform. Trail motes simulate in world space.
  Disable it (or let the projectile pool do it) when the projectile ends.
- **Zone effects (aura_heal, ground_slam, blade_storm):** scale the root so the painted ring matches the gameplay
  radius (2.4.4); recipes.md §2 gives the ring radius at the default size.
- Hooking a VFX into a gameplay prefab or data is a separate task; do it only when the dev asks.

### Replacing an effect

1. Owner prefab: `PrefabUtility.LoadPrefabContents` → `InstantiatePrefab(<Base>.prefab)` under the right parent →
   remove / disable the old FX children → `SaveAsPrefabAsset` → `UnloadPrefabContents`. Keep the instance name `<Base>`.
2. Files `make` lists as `old`: delete each only when its GUID no longer appears in `Assets/`
   (`grep -rl <guid> Assets --include='*.prefab' --include='*.asset' --include='*.unity' --include='*.mat'
   --include='*.spriteatlasv2' --include='*.anim' --include='*.controller'`). A file `make` rewrote under the same name
   keeps its `.meta`, so its GUID and every reference to it survive.
3. A folder atlas (`fileID: 102900000` + folder GUID in `packables`) picks up new sheets by itself; an atlas that lists
   files needs the new sheets added and the deleted ones removed.

## 8. Gotchas

- **Stretched billboard UV:** Unity maps the sprite's **U along the velocity, with the particle (head) at U=0**.
  Draw spark and comet sprites horizontally with the hot head on the left. The quad extends *behind* the particle.
- **Birth sub-emitter shape is oriented along the parent's velocity** (verified 6000.3): shape +Z = direction of
  travel. A `shape.position` in XY lands in the wrong place; the builder emits trails from a sphere offset to
  `(0, 0, -trailBack)`.
- **Last frame:** frameOverTime ends at 0.9999. A value of 1.0 at an exact cycle boundary would index past the
  last sprite (a blink on loops).
- **No frame blending:** `Mobile/Particles/Alpha Blended` has none, so smoothness comes from the frame count.
- **Slicing API:** `UnityEditor.U2D.Sprites` (`SpriteDataProviderFactories`, `ISpriteNameFileIdDataProvider`), package
  `com.unity.2d.sprite`. `TextureImporter.spritesheet` is obsolete in Unity 6; don't use it.
- **cols × rows must equal frames**: `make` picks the grid, and the builder slices exactly `frames` sprites.
- **Shared Editor:** other sessions may be in Play. Refresh/compile stops their run. Check `unity_editor_state` /
  `unity_agents_list` first. Building prefabs (no `.cs` change) does not recompile.
- **The dev's Prefab Mode:** a rebuild leaves an open stage stale; `Build` refreshes the open stage after rebuilding and
  refuses if it has unsaved edits. `BuildShowcase` refuses while any Prefab Mode is open (the "Prefab Has Been Modified"
  modal would block the Editor and every MCP call).
- **MCP call hanging over 120 s with an empty log tail:** suspect a modal dialog. Screenshot the Editor before
  retrying. Never click Save / Discard for the dev. Cancel is the only safe choice, and only for a dialog your own
  call raised.

## 9. Performance budget (mobile, check against 6.2)

- One flipbook quad + 5–25 small particles per effect; one texture sample, built-in mobile shader; sheet
  ≤ 1600×1280, ASTC 4×4.
- Large explosions overdraw about 40 % of a portrait screen width for 0.7 s (6.3). Fine for occasional blasts;
  effects spawned every frame or by many units at once should use impact-sized recipes and a smaller `--px`.
