---
name: figma-tokens
description: Build and audit the design-token layer of the project's Figma file — variable collections, scopes, bindings, text/effect styles, and component descriptions. Use when asked to "set up tokens", "create variables", "build the design system", "audit the design system", "find hardcoded colors", "bind values to variables", "fix ALL_SCOPES", "write component descriptions", or when a Figma change needs a token instead of a pasted value. Does NOT import screens (use psd-to-figma), does NOT import to Unity (use figma-to-unity), and does NOT check structure or 9-slice (use figma-hygiene).
---

# Design tokens

Forked from [`natdexterra/work-with-design-systems`](https://github.com/natdexterra/work-with-design-systems)
and cut down to what applies to this file. Upstream targets themeable web design
systems that export CSS; this is a single-theme portrait game UI that exports
Unity UGUI prefabs. What was dropped and why is listed at the bottom — read that
before re-importing anything from upstream.

**Target file and pages:** read the file key (`figma.fileKey`) and the page map
(`figma.pages`) from the project settings file — `psd2figma.json`, the same one
`psd-to-figma` documents. Never hardcode a file key or a page id in a script or
in this prose.

## Prerequisites

1. **Invoke the `figma-use` skill before every `use_figma` call.** It carries the
   Plugin API rules and script templates. Never call `use_figma` without it.
2. `figma-hygiene` runs as the pre/post gate on any write. This skill does not
   replace it — it covers the token layer that hygiene does not check.
3. Read `reference/token-taxonomy.md` before creating a single variable. Its
   first section decides how big this job actually is.

## Critical rules

1. **Generic UI, not PNG.** General UI chrome — button plates, panel and popup
   backgrounds, bars, slot frames, dividers, rows, borders, flat icons — must be
   real Figma geometry: a shape with a SOLID fill, a stroke, a `cornerRadius`,
   9-slice where it stretches. PNG is reserved for **special assets**:
   characters, banners, splash art, decorative icons. A colour variable binds
   only to a SOLID paint, so genericizing the chrome is what creates the token
   surface — the two jobs are one job. See `reference/token-taxonomy.md`.
2. **A PNG that legitimately stays uses `scaleMode: 'FIT'`, not `'FILL'`.**
   Census the image paints by `scaleMode` before any flip: `CROP` paints are
   `slice_*` 9-slice pieces and are correct as they are — do not touch CROP;
   `FILL` paints are the real flip candidates; `FIT` paints are already correct.
   FIT and FILL render identically only when node and image aspect ratios already
   match; where they differ, flipping is a visible change and must go through the
   `psd-to-figma` verify pass (`N-1`, art tolerance 0.00 px). Never flip in bulk
   unverified.
3. **One variable, one explicit scope set.** Never `ALL_SCOPES` (the default),
   never `[]`. Empty scopes make the variable invisible in every picker and
   invisible to `scripts/fixHardcodedToTokens.js`, which indexes by scope.
4. **Do not bind `lineHeight` on a style that is `AUTO`.** Most text styles use
   `lineHeight: AUTO`, and a variable bound to `lineHeight` is always a concrete
   pixel number — there is no "auto" value a variable can hold. AUTO renders at a
   value computed from the font's OS/2 metrics (e.g. a nominal ~42.7 px size can
   render near 51 px), so minting the nominal size would SHRINK the line by
   several px and break the `psd-to-figma` 2 px text ink tolerance. Only styles
   that carry an explicit pixel lineHeight are safe to bind. Leave every AUTO
   style as AUTO.
5. **Do not set `codeSyntax`.** There is no CSS export here. A missing
   `codeSyntax` is not a defect in this file — never report it as one.
6. **One mode per collection, named `Value`.** No Light/Dark, no brand modes.
7. **Bind inside the master, never on an instance.** An instance override is the
   thing tokens exist to remove. A child of an INSTANCE also cannot be resized or
   repositioned — `resize()` is ignored and `x`/`y` assignment throws.
8. **Never touch font identity or `textStyleId`.** Every TEXT node stays on the
   body font (`figma.fonts.body`) with a style id from `style_ids.json`. A font or
   style-binding violation is a bug, not accepted debt — it cannot be pinned.
9. **Work incrementally: one component set per `use_figma` call, verify, then
   continue.** Never build on unverified work.
10. **Match verification depth to the change.** Binding, scope, description and
   rename changes are deterministic — verify them by reading
   `node.boundVariables` / `variable.scopes` / `node.description` back *inside
   the same script* and returning the result. Only structural changes (new
   variant, restructured auto-layout, new property) need `get_metadata` +
   `get_screenshot`. `get_screenshot` is the most rate-limited call there is —
   Figma caps read tools per plan and seat, and the daily cap is the one that
   bites on a 20-set sweep. Run `whoami` if unsure of the tier.
11. **Never detach a component.** Vary content with a variant, a boolean, an
    instance swap, or the master's own slot.

## Workflow

### Phase 1 — Audit (read-only)

Run these through `use_figma`. All are read-only and safe to run on the live
file.

| Script | Input | Returns |
|---|---|---|
| `scripts/inventory.js` | — | every component set, variant count, page |
| `scripts/audit-tokens.js` | `COMPONENT_SET_ID` | unbound fills/strokes/radius, missing text styles, `spacingHistogram` |
| `scripts/audit-naming.js` | `COMPONENT_SET_ID` | `genericNames` (hygiene S-2 violations), `spacedNames` |
| `scripts/audit-states.js` | `COMPONENT_SET_ID` | missing `State` values per game archetype |
| `scripts/audit-detached.js` | — | file-wide detached instances |

Start with `inventory.js`, then loop the per-set scripts over what it returns.
Present the findings and **stop** — do not start writing until the scale is
agreed.

### Phase 2 — Derive the spacing scale

Derive the scale from the live file, not from this prose. Run `audit-tokens.js`
in discovery mode (`SPACING_SCALE = []`), read `spacingHistogram`, keep the values
that recur 3+ times, and fill `SPACING_SCALE`. The screen-grid anchors (column
width, gutter, margin, row pitch — from the layout-grid style) are enforced at
screen level and typically never appear inside a component, so they are
screen-composition tokens, not component ones.

Re-derive after Phase 2b: genericizing PNG chrome adds auto-layout spacing that
does not exist yet. Set `SPACING_SCALE = []` again to re-enter discovery mode and
refill the array.

### Phase 2b — Genericization triage

Do this BEFORE minting colour tokens. A primitive minted for a sprite you are
about to delete is wasted work, and a sprite replaced after its neighbours are
bound leaves an unbound hole in a finished set.

For every `IMAGE` paint, decide one of three:

- **genericize** — UI chrome: plates, panel and popup backgrounds, bars, tracks,
  fills, slot frames, rows, dividers, borders, flat single-colour icons. Rebuild
  as real geometry, 9-slice where it stretches.
- **keep-png** — special asset: characters, creatures, banners, splash art,
  illustrated icons. An illustrated subject/creature icon set is the clearest
  case. Set `scaleMode: 'FIT'` and re-verify any node whose aspect ratio differs
  from its image's.
- **unclear** — do not guess. List it and ask.

Reference pattern: the file's existing generic plates are the most sophisticated
geometry in it — a linear-gradient body plus a paired inner-shadow rim (a lighter
top rim over a darker bottom rim). Find those plates and match that construction
rather than inventing a
flatter one.

### Phase 3 — Create foundations

Per `reference/token-taxonomy.md`: `Primitives` then `Semantic`, one mode each,
explicit scopes at creation time. Create a primitive only when a real value in
the file needs it.

Resolve the `Row_Name` / `Price_Value` duplication first — the two text styles
are byte-identical, so either they merge or their difference becomes a variable.

### Phase 4 — Bind

Two paths:

- **Manual**, per component set, one `use_figma` call each. Verify bindings in
  the same script.
- **Fuzzy**, via `scripts/fixHardcodedToTokens.js` with
  `{componentSetIds: [...], threshold: 0.85}`. It returns `applied` and
  `skipped`. **Review both lists with the user** — a wrong high-confidence
  binding is harder to find later than an unbound value. Requires Phase 3 to be
  complete and scopes to be correct.

### Phase 5 — Descriptions

Write a description for every set using
`reference/component-description-template.md`. No set has one today. Use
UPPERCASE section headers only — `get_design_context` escapes markdown and
collapses newlines.

### Phase 6 — Verify

- Re-run `audit-tokens.js` per set; error count must drop, not move sideways
- Every variable: explicit scopes, aliases a primitive if semantic
- Run the `psd-to-figma` verify pass on any screen whose masters changed
- Hand off to `figma-hygiene` post-flight

There is **no code-export phase**. Tokens stop at the Figma file; Unity gets
values through the UnityFigmaBridge importer, not through a token file.

## What was dropped from upstream, and why

| Dropped | Why it does not apply |
|---|---|
| Phase 6 code export (`tokens.css`, `[data-theme="dark"]`, AI rules file, `token-audit.js`) | No CSS. Unity UGUI via the UnityFigmaBridge. |
| Critical Rule #5 — `codeSyntax.WEB` mandatory on every variable | Only existed to feed the CSS export. The importer never reads it. |
| Mandatory `Light` / `Dark` modes on the Semantic tier | Single-theme game. A second mode doubles every value with nothing to put in it. |
| `color/status/{success,warning,error,info}` | No validation messages, no error toasts. The real axes are rarity, currency and affordability — already variant properties. |
| `audit-accessibility.js` and the WCAG AA contrast checks in `validate-design-system.js` | False errors on stylised game art with stroked, drop-shadowed text. |
| `COMMON_SCALE = [2,4,6,8,…96]` in `audit-tokens.js` | A 4/8 web ladder against a game-art screen grid flags nothing real and misses everything real. Replaced with discovery mode. |
| Web state ladders (`Hover`, `Focused`, `Visited`, `Filled`, …) | Touch game: no pointer, no focus ring. Replaced with `Normal`/`Pressed`/`Disabled`/`Active`. |
| Default component list (Button, Input, Select, Checkbox, Radio, Toggle, Modal, Toast) | A form-app library, not a game HUD. |
| Phase 3 file structure (Cover / Getting Started / Foundations pages, 996 px wrappers) | Conflicts with the existing Screens / Components / Icons pages. |
| `C{section}.{number} {Name}` component numbering | Would rename every set and break the component registry, `style_ids.json`, the screen name table (name-keyed), and Unity prefab names (which derive from component names — see `figma-to-unity/reference/prefab-contract.md`). |
| Framework mapping tables (React / Vue / Tailwind / CSS variables) | Wrong target platform. |
| Multi-brand `Brand` collection, responsive type modes, `clamp()` export | One brand, one fixed portrait frame. |

## Related skills

- `figma-components` — component sets, variant axes, the `State` ladder and
  promotion of repeated screen art. It binds the tokens this skill mints and
  never creates one.
- `psd-to-figma` — screen import, and the numeric verify contract this skill must
  not break. Its `reference/contracts.md` holds the Plugin API traps.
- `figma-hygiene` — structure and visual gate. Owns node naming (S-2, S-3), the
  layout grid (V-4), 9-slice (V-1) and text-style binding (V-3).
- `figma-to-unity` — the downstream import this token layer feeds.
