---
description: Phase D of every /new-ui build — the designer pass. Lays art, FX and motion OVER the frozen Phase C layout (never moves it), proven by ui-layout-lock.py (layout lock + file scope + deterministic revert) and reviewed by ui-visual-reviewer phase D. Never blocks a build. Loaded by create-ui SKILL.md and new-ui-guide.md.
---

# UI Designer Pass — Phase D (overlay-only)

Phases A–C build exactly what the approved mockup specifies. The mockup is a **layout contract**
drawn as a wireframe — flat fills, empty icon slots, placeholder glyphs — so a build that stops at
Phase C ships correct but flat. Phase D finishes the screen: art, FX and motion laid **over** the
layout Phase C shipped. It does not redesign. A layout change is a mockup change (`/ui-mockup`) or a
`/refactor-ui` job, never Phase D's.

This file holds **no style values**. Every colour, kit piece, FX family, easing, duration,
choreography and concept rule comes from `.claude/docs/ArtStyle.md` (rule `art-style`); the task's
own Motion/Art bullets outrank it (dev words in the task win).

Commands below use `python3`; on Windows use `py` (the Store `python3` stub exits silently).
`LOCK` = `python3 .claude/scripts/ui-layout-lock.py`, always run from the project root.

## D0. When Phase D runs

Once per **root screen/popup** built through `/new-ui`, however it was reached — typed by hand,
`/run-backlog` STEP 5.0, `/new-feature` §7, or the `/new-package` UI branch — right **after Phase C
is accepted** (autonomous: the Phase C reviewer `pass`; interactive: the dev's OK on Phase C) and
**before the new-ui-guide §5 evidence report**.

Skip it and record `designer-pass: skipped (<reason>)` in the report / DONE summary when:

| Condition | Why |
|---|---|
| args carry ` \| polish=off` (task `**Workflow args:**` or the `/new-ui` call) | planner/dev opted out |
| dev-only screen: cheat / admin / debug / test (feature `GameCheat`, `Admin`, or a screen name containing `Cheat`, `Debug`, `Admin`) | players never see it |
| reusable child block, not a root screen (an item cell, a widget prefab) | it is polished where it is used |
| running inside `/refactor-ui` (CREATE mode scaffolds via create-ui) | refactor-ui §2–§7 is the fuller designer pass and may change layout |
| no Unity Editor connected | no UI build ran at all |
| `LOCK begin` answers `"action": "skip"` | a piece is not installed in this project (its `reason` names it) |
| the lock-before snapshot cannot be taken (D2.2 fails twice) | nothing could prove the layout survived — `skipped (lock snapshot failed: <error>)` |

`ArtStyle.md` missing or `Status: template` is **not** a skip: run its § Bootstrap first (rule
`art-style`); in an autonomous run do not stop to ask the dev — fill it as `Status: draft` and say in
the report that ArtStyle needs the dev's review.

## D1. The contract — what Phase D may touch

The Phase C prefab is the frozen layout. Phase D may:

1. **Add overlay nodes** — new GameObjects named `fx_<what>` (unique names): glow, rays, shine,
   sparkle, vignette, texture, ribbon/emblem art, particle emitters. Every Graphic in them has
   `raycastTarget = false`; no Button/Selectable, ScrollRect, raycaster, text or localize component.
   **Where an overlay may hang:** under a *leaf visual* node — a frame/panel Image, a title pill, an
   icon or reward cell, a button — or under another overlay node. Under a node with a layout group
   only with `LayoutElement.ignoreLayout = true` (the lock flags it for the reviewer). **Never**
   directly under the prefab root, under one of the root's own children (the template's background /
   popup / full-screen nodes — `FeatureBaseController` and the transition address them by child
   index), or under a ScrollRect's viewport or content (its children are the list). Never inside a
   container the controller fills, clears or indexes at runtime: read the controller first
   (`foreach (Transform`, `GetChild(`, `childCount`, destroy / clear loops).
   Draw order: an overlay that is the first child of a node draws *over* that node's own graphic and
   *under* its children (a glow on a panel's face, behind its content). To draw *behind* a node's
   own graphic (a halo around a panel), the overlay must be an earlier sibling, i.e. the first child
   of the node's parent — legal only when that parent is allowed above (a layout-group parent needs
   `ignoreLayout`).
2. **Fill empty art slots** — an existing Image/RawImage that is a *visible* empty slot: sprite /
   texture still empty (null or a Unity built-in placeholder), colour alpha above zero, on a node
   with no Button/Selectable and no Mask, **and** not bound at runtime (spec element not
   `"dynamic"`, no controller / view / template code sets it; Package branch: never pre-fill
   `PurchaseTemplate`). It gets its art plus colour / preserve-aspect / image type on that component.
3. **Create art files** under `<Feature>/Visuals/` (D4).
4. **Add motion** in the screen's controller and a static `<Feature>Motion` class in
   `<Feature>/Scripts/Controller/`; idle-FX components the project lacks go there too (reuse the
   project's shared ones when they exist — never add code elsewhere). All motion runs at runtime
   from the rest state the prefab serializes.
5. **Add helpers to existing nodes** — a `CanvasGroup` at rest (alpha 1, interactable, blocks
   raycasts) for fades, and idle-FX / tween-driver behaviours (the reviewer checks each one).

Phase D must **not**: move, resize, re-anchor, re-pivot, rotate, scale, reparent, reorder, rename,
activate/deactivate or delete any existing node; change any layout group / element / fitter, text,
font, font size, localize key, template instance, or the controller's existing serialized
references; tint, restyle (Shadow/Outline/gradient effects) or replace art an existing node already
shows; add a Graphic, layout, input, text or localize component to an existing node; **edit any file
other than** the screen prefab, the feature's `Scripts/Controller/` and `Visuals/`, `ArtStyle.md`
(+ its pending screenshots) and the lock report — no existing sprite / `.meta` / material / shader /
font / template prefab / other screen; not the screen prefab's own `.meta` (its `assetBundleName`).
No data, economy, save or flow changes. A frozen layout that looks wrong is reported as a mockup
issue in the DONE summary — not fixed here.

`ui-layout-lock.py` enforces the parts a machine can check (`diff` for the prefab, `scope` for the
files). A lock pass is necessary, not sufficient — the reviewer judges the rest.

## D2. Begin + lock the layout (before touching anything)

1. `LOCK begin --prefab <Assets/.../screen_x.prefab> --task <NNN>` — under `/run-backlog` always pass
   the backlog task number (interactive: omit it); `--feature-dir <Assets/.../Feature>` when the
   prefab does not sit under the feature's `Resources/`. Read the JSON:
   - `skip` → D0 skip row, stop here. Exit 2 (not a git repo, prefab missing) → skip with its error.
   - `already-done` / `done-modified` → Phase D was already applied in an earlier session of the
     same task (`done-modified`: later fix rounds edited the prefab since) — go to D7 and only verify
     the record. Phase D never runs twice on one screen for one task; a session left by a
     *different* `--task` is archived and this one starts fresh.
   - `stale` (`ok: false`) → commits since that earlier `begin` changed this screen's files, so
     the session can't be trusted: do not run Phase D on this screen again in this task; record
     `designer-pass: failed (stale session)` and leave the screen as it is.
   - `already-reverted` → Phase D was reverted earlier in this task; do not retry on your own —
     record `designer-pass: reverted`. (`--restart` starts over on purpose, e.g. after Phase C was
     rebuilt; it is refused while a session is in progress.)
   - `resume` → Phase D started in an earlier session: **do not retake lock-before**; jump to D6.1
     to see where the screen stands, then continue (D5 if the polish is unfinished). If it reports
     `lockBeforeExists: false`: take it now when `prefabUnchangedSinceBegin` is true, otherwise
     `revert` and skip (`lock snapshot lost`).
   - `start` → session created in `.claude/tmp/ui-designer/<screen>/` (gitignored; survives a new
     agent session): byte copies of the prefab + its `.meta`, the feature's `Scripts/Controller/` and
     `Visuals/`, `ArtStyle.md`, git blobs of every other dirty file, and the HEAD commit. Use the
     `lockBefore` / `lockAfter` / `captures` paths it prints. A session follows HEAD through commits
     that leave this screen alone; a commit that changes the screen's files makes it stale (`scope` /
     `revert` / `finish` exit 2) — after the task shipped, the next `begin` archives it and starts fresh.
2. Snapshot the Phase C prefab (read-only; measures every node in a throwaway preview scene at
   1080×1920, 1080×2400 and 1536×2048):

   ```bash
   python3 .claude/scripts/ui-layout-lock.py snippet --prefab <prefab> --out <lockBefore>
   ```

   Run the printed C# through `unity_execute_code` (same instance/port as the build) and confirm the
   file exists. Failed twice → `LOCK revert --prefab <prefab>` (nothing to undo yet; it closes the
   session) and skip.
3. Capture the Phase C screen at **all three** sizes into `<captures>/before-<W>x<H>.png`
   (ui-mcp-playbook §5 capture; aspect sweep as in `refactor-ui` §6), then re-pin the Game view to
   1080×1920 (playbook §0).

## D3. Brief (`<captures>/../brief.md`, 10–20 lines — before any art)

Read `ArtStyle.md` and `Read` its boards, every §8 approved image of the same family, every §9
rejected image; the task's Motion/Art bullets; the ui-spec `note` fields (they often name the FX the
drafter intended); the controller (which containers it fills at runtime — D1.1). Then write:

- **Family** (ArtStyle §4b) — which parts are locked; Phase D decorates around them.
- **Focal point** — the one thing the eye goes to (main reward / main CTA / hero), from the spec.
  Idle motion and the strongest FX live only here.
- **Overlay plan** — every `fx_` node: its parent (a legal one, D1.1), art source, why it serves the
  focal point.
- **Art fills** — every empty static slot and the art it gets.
- **Motion plan** — the four layers (D5) with values from the task, else ArtStyle §7.
- **Rejected check** — compare with ArtStyle §9 and §1's "do not" list; change the plan now.

## D4. Art

Reuse first, draw second, generate last:

1. The kit and FX ArtStyle §3 / §6b declare, and FX template prefabs under `uiTemplatesRoot`.
2. Procedural sprites with Pillow (glow, rays, ring, shine sweep, sparkle, vignette, gradient,
   simple ribbon/stamp): white + alpha (tinted in Unity with an ArtStyle §2 token), drawn 4× and
   downsampled LANCZOS, soft edges. Save as `<Feature>/Visuals/<screen>_fx_<name>.png` (`<screen>` =
   the prefab name without `screen_`, snake_case).
3. Hero art / emblems: the image generator (`/gen-icon`, or a connected image MCP) using ArtStyle
   §6 prompts and references — **open every generated image and look at it** before use; no
   generator available → compose from kit + feature art with Pillow. Never block on a tool.

Import: copy the `.meta` of an existing kit FX sprite and give it a new GUID, or set Sprite (2D and
UI), Single, no mipmaps, alpha is transparency, Clamp, Bilinear, mesh FullRect, no physics shape;
9-slice border for stretchable frames. On URP never give a UI Graphic a
`Universal Render Pipeline/Particles/*` material (renders a flat white block) — use the project's UI
additive material or none, and **create** a material in `Visuals/` if one is needed (never edit a
shared one). Put new art next to the kit board: if it reads as a different art family, redo it.
More technique: `refactor-ui` §3. Every new file needs its `.meta`.

## D5. Build the overlay + motion

Edit the prefab the playbook way (`LoadPrefabContents` → edit → `SaveAsPrefabAsset` →
`UnloadPrefabContents` in `try/finally`), one chunk at a time, capture after each chunk. Never put
glow/rays behind text so the text loses contrast; never cover the CTA or a value.

**Motion — four layers.** Values, order and choreography come from the task, else ArtStyle §7; this
table only names the layers:

| Layer | Covers |
|---|---|
| Enter | how the screen and its parts appear |
| Idle | the focal point only |
| Feedback | the press (the button template's own) and the screen's beat on claim / purchase / result |
| Exit / tab change | how it leaves or switches |

Rules (binding):
- The frame's open/close already exists: `FeatureBaseController.AnimOpenUI/AnimCloseUI` +
  `UITransition`. Do not add a second intro on the root. Child stagger → `override AnimOpenUI()`,
  call `base.AnimOpenUI()` first, then run the screen's sequence.
- Tween only `localScale`, alpha (`CanvasGroup`/`Graphic`) and rotation of existing nodes — their
  position and size belong to the layout. Anything goes on overlay nodes. A slide-in of a laid-out
  child = alpha + scale, or an overlay that moves; never `anchoredPosition` / `sizeDelta` of a
  laid-out node.
- Do not scale a button root (its template press effect captures the base scale) and do not insert
  a "visual root" between a node and its children (that reparents); the press stays the template's,
  the beat goes on an overlay.
- DOTween: `SetUpdate(true)`, `SetLink(gameObject)`, kill in `OnDisable` and before every replay;
  re-opening is idempotent (reset to the rest state first, no accumulated scale); one `Sequence`
  per beat; durations/eases as named constants. UniTask, never coroutines; no per-frame allocation.
- Claim/purchase beat: `UIManager.EnableTouch(false)` during the beat, `true` in `finally`, cancel
  with the view's lifetime. Rewards are granted exactly once by the existing code path — animation
  is never a condition for a grant.
- Nothing a tween does at runtime may be saved into the prefab.

**Compile now** (rule `compile-validation`), before any capture — captures and idle components need
compiled code. Still failing after 2 fix rounds → **revert** (end of D6), never a block token.
More technique: `refactor-ui` §5 — except its "visual root" child pattern (reparents → not allowed).

## D6. Verify — all mandatory

1. **Lock.** Snapshot the saved prefab again to `<lockAfter>`, then:

   ```bash
   python3 .claude/scripts/ui-layout-lock.py diff <lockBefore> <lockAfter> \
     --output TechSpec/Mockups/<F>/<S>.ui-layout-lock.json   # no mockup folder → <captures>/../lock-report.json
   python3 .claude/scripts/ui-layout-lock.py scope --prefab <prefab>
   ```

   `diff` exit 1 → undo the change each violation names (never by editing layout), snapshot again,
   re-diff. `scope` exit 1 → put back the files it lists as violations (existing art / materials /
   fonts / templates / other prefabs, code outside `Scripts/Controller/`, the prefab's `.meta`, the
   mockup itself, approved/rejected ArtStyle references, anything else under `.claude/`). The §5 evidence files and regenerated
   ArtStyle boards are allowed.
   `scope` warnings are usually editor auto-dirt — leave them out of the commit. `diff` exit 2 →
   retake **after** (never `before`); `scope` / `revert` / `finish` exit 2 → no usable session
   (stale or unreadable): record `designer-pass: failed (<error>)` and stop Phase D — the screen
   stays as it is for the dev. Run both checks again after every later fix round.
2. **Captures** into `<captures>/after-<W>x<H>[-<state>].png` at 1080×1920, 1080×2400 and 1536×2048,
   every state the task or spec lists (at least default + main CTA state), plus one mid-animation
   frame of the enter and of the feedback beat (drive DOTween manually so callbacks run; trigger the
   beat by calling the `<Feature>Motion` method directly — never run a real claim/purchase, or
   snapshot the `PlayerDataManager` module first and restore it after). Compare each with its
   `before-` twin: same layout, finished art. Re-pin 1080×1920 when done.
3. **Self-critique, two rounds.** "Would a senior designer call anything here amateur?" — focal point
   unmistakable; nothing covers text/CTA; text contrast unchanged or better; no white blocks, hard
   edges or stretched art; FX neither clipped nor misplaced at the other sizes; motion within
   ArtStyle §7, nothing loops outside the focal point. Fix the weakest spot, capture again.
4. **ArtStyle gate** — every ArtStyle §11 row: pass/fail/na with evidence (node, sprite, token,
   image) into `<captures>/../artstyle_gate.md`. A failing `block` row → fix, re-capture, re-grade.
5. **Independent check.**
   - Autonomous: spawn `ui-visual-reviewer` with `phase: "D"` (ui-mcp-playbook §5 template):
     `groundTruth` = approved mockup PNG + ui-spec (geometry) and ArtStyle (art), `before` = the
     `before-*.png` set, `lockReport` = the D6.1 report, `builderClaims` = brief + gate + the `scope`
     output, sizes and states. `block` → fix → step 1 again → re-spawn; max **2 rounds**.
   - Interactive: show the dev each `before-`/`after-` pair and wait for OK (like phases A–C); no
     reviewer spawn. The dev asks for changes → apply them within D1 and repeat D6; the dev prefers
     the flat screen → revert.

**Phase D never blocks the build.** Still failing after the rounds above — lock, scope, compile,
reviewer (or the dev turned it down) — or anything you cannot fix inside the contract → **revert**:

```bash
python3 .claude/scripts/ui-layout-lock.py revert --prefab <prefab>
```

It restores the backed-up prefab / controller / Visuals / ArtStyle, deletes every file Phase D
created, git-restores shared files it touched, and checks the prefab is byte-identical to Phase C
(`prefabRestored: true` — the Phase C verdict still stands); what it discards is copied to the
session's `discarded/` first. It works the same whether or not the backlog already staged the tree.
`unrestorable` non-empty (`ok: false`) → put those files back by hand from git / `discarded/` and
say so in the report. After `finish`, a revert touches only Phase D's own footprint and **refuses**
(`refused: true`, `editedAfterFinish`) when one of those files was edited later — that edit is a
later fix round's work. Then either fix inside the gate, or, only if every later edit was itself an
attempt to fix Phase D, re-run with `--discard-later-edits` (the edits land in `discarded/`).
Then refresh Unity (`AssetDatabase.Refresh`), compile once, (re)generate the new-ui-guide §5
evidence on the Phase C screen (`--visual` = the Phase C verdict), ship Phase C, and record
`designer-pass: reverted (<reason>)`. `prefabRestored: false` → say so in the report; that is the
only case Phase D leaves for the normal gates.

## D7. Record

- `LOCK finish --prefab <prefab>` (calling it again is harmless: it answers `already-done` and keeps
  the footprint it recorded the first time). The session folder stays (gitignored) so a later gate of the same
  task can still `revert`: under `/run-backlog`, a STEP 5b / 6 / 7 / 7.5 (or preflight) block caused
  only by Phase D output that its fix rounds cannot clear → `revert` (plus the evidence regeneration
  above), re-run that gate on Phase C — not the block token. If the revert refuses (see above) and
  `--discard-later-edits` does not apply, the gate ends the way it normally would.
- The new-ui-guide §5 evidence uses the **final** 1080×1920 capture as `<S>.unity.png`; keep
  `<S>.ui-layout-lock.json` (D6.1, `ok: true`) beside it. A reverted Phase D leaves no lock report
  (revert deletes it).
- `ArtStyle.md`: a token / kit piece / FX family / motion value Phase D needed that ArtStyle lacked →
  add it to its section with the reason (`Status: draft`). Put a ~540 px-wide copy of the final
  capture at `.claude/docs/ArtStyle/screens/pending/<Screen>.png` and add (or replace) the screen's
  row in the §8 **Chờ duyệt** table. Never write the approved table — only the dev approves.
- Report / DONE summary line: `designer-pass: done (<n> overlay nodes, <m> art fills, motion:
  <layers>; lock ok, <k> warnings)` · `skipped (<reason>)` · `reverted (<reason>)`.
- Commit list: the prefab, controller + `<Feature>Motion`, art + `.meta`, `ArtStyle.md`, the pending
  PNG, `<S>.ui-layout-lock.json` — never `.claude/tmp/`, never `scope` warnings.
