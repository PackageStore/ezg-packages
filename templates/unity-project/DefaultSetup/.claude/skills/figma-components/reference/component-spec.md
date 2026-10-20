# Component specification

Forked from `natdexterra/work-with-design-systems`
`references/build/component-spec.md`. Upstream specifies the ten components of a
web form application (Button, Input, Select, Checkbox, Radio, Badge, Avatar,
Card, Modal, Toast) with a pointer state ladder and WCAG contrast gates. None of
those ten exist in a portrait touch-game UI. This file replaces them with the
archetypes that do.

Read before building or extending any component set.

Component-set names, variant values and token names below are illustrative —
substitute the project's own. Generic archetype names (`Button`, `Row`, `Card`,
`Bar`, `Panel`, `Slot`, `Icon`) stand for whatever the file calls them.

## Core principles

1. **States first.** Design `Pressed` (and `Disabled` when the control can be
   locked) before the happy path. A button set without `Pressed` is incomplete,
   and interactive sets that ship only a `Type` axis are incomplete today.
2. **Auto-layout for anything that stacks or repeats.** Absolute position is for
   overlays — a corner badge, a decorative flourish — and for art whose exact
   offset came from the PSD and is under the 0.00 px verify tolerance.
3. **Tokens only.** Every fill, stroke, radius and gap binds to a `Semantic`
   variable. A raw value in a master is a defect. `figma-tokens` owns the
   variable; this file only says which one.
4. **Name like the file, not like code.** There is no React prop to mirror.
   `Type` / `Color` / `Icon` / `State` are the four axes this file uses, and
   `figma-tokens/reference/naming-conventions.md` is the authority on their
   values.
5. **Base plus composed.** A shared plate becomes its own master, and the public
   component instances it. A bottom-nav button whose background is an instance of
   the shared plate master, rather than a copy of the plate art, is the pattern to
   follow. Each set owns one axis; the next axis comes from a nested, exposed
   instance (see *Composed sets* below).

## Private and base components

Upstream prefixes private components with `.` or `__` so Figma hides them from
the Assets panel. **Do not use a leading dot or underscore in this file.**
The bridge derives prefab filenames from component names — `=` becomes `-`
and `, ` becomes `_` — so the Figma name must be the intended prefab name
(`figma-to-unity/reference/prefab-contract.md` is the single source of truth
for the naming and normalisation rules). `component_ids.json` also keys off
component names.

Use a `Base-` prefix instead: `Base-Plate`, `Base-Row`. It reads the same, sorts
together, and the bridge imports it without mangling. If a base must be hidden from the Assets panel
later, that is a rename decision to make once, with the registry and the Unity
pipeline updated in the same pass.

## Component anatomy

```
ComponentSet "Button"                  State axis only
├── State=Default
│   └── [auto-layout: horizontal, gap → space/tight, padding → space/default]
│       ├── Bg          (exposed instance of Base-Plate, Color=Green; absolute)
│       ├── Icon-Price  (instance of an icon set, INSTANCE_SWAP)
│       └── Price-Value (TEXT, textStyleId → Price-Value)
├── State=Pressed        (content moved down, Overlay on Bg)
└── State=Disabled       (grey Overlay on Bg — only when the control can be locked)

ComponentSet "Base-Plate"              Color axis only
├── Color=Green
└── Color=Yellow
```

Child node names follow `figma-hygiene` S-2/S-3 and the layer table in
`figma-tokens/reference/naming-conventions.md`. No node named `Frame`,
`Frame 3`, `Group` or `Rectangle 5`.

## Variant axes used in this file

| Axis | Property | Values | Where |
|---|---|---|---|
| Kind | `Type` | one value per kind — icon role, currency, destination, rarity, empty/filled | icon buttons, currency bars, nav buttons, slots |
| Colour | `Color` | one value per button colour | button plates |
| Icon | `Icon` | one value per icon drawing | stat containers |
| State | `State` | `Default`, `Pressed` (+ `Disabled` when lockable; `Hover` legacy only) | interactive sets |

Boolean properties for toggles: a `Show Plus` boolean on a currency bar is the
existing example. Instance-swap properties for icon slots. Text properties for
editable labels.

## Composed sets — one axis per set

Each set owns one axis. A quality on another axis comes from a nested instance
of the set that owns it, marked `isExposedInstance = true`, so its variant
property shows on the parent instance's panel.

| Build | Variants designed | Combinations offered |
|---|---|---|
| One `Button` set with `Color` × `State` | N × 3 | N × 3 |
| `Base-Plate` (`Color`) nested in `Button` (`State`) | N + 3 | N × 3 |

The precondition is a recipe that does not read the nested axis. A `State`
recipe that swaps per-colour rim tokens needs one variant per colour and forces
the multiply; an overlay bound to one colour-agnostic token does not. Keep two
axes in one set only when one axis's recipe must read the other axis's value.

`State` is an axis like any other, and it is the outer set: the interactive
root. Every other quality is its own set, nested as an exposed instance.
Variant count is the sum of the axes. A set with fewer variants than the axis
product has holes, and a hole locks that combination in the instance panel:
Figma needs a real variant for every picked combination.

Size flows from the nested set. The outer variant is auto layout, Hug on both
axes. The nested instance is Hug with no width/height override: no fixed frame,
no STRETCH constraint, no manual resize. An override pins the size, so swapping
the nested size axis changes nothing. Fill is fine only when the parent is meant
to decide that axis.

A state look may override the nested instance only for a property the nested
set never varies across its variants, such as effects. If the nested axis varies
the property (a fill per `Color`), the override survives the swap on the placed
instance and pins the old value. The state goes on a sibling `Overlay` node
instead.

In Unity each `State` variant becomes a prefab that nests the plate variant's
prefab. A placed instance that picks another `Color` has a nested instance whose
component changed, and the bridge replaces that nested prefab with the new
variant's prefab (`figma-to-unity/reference/prefab-contract.md`, *Composed
sets*).

## Archetypes and their required states

There is no keyboard, so there is no `Focused`; there are no forms, so there is
no `Error`, `Success` or `Read-only`; there is no `Active`. The ladder is
`Default` and `Pressed`, plus `Disabled` on a control the game can lock — the
locked look is grey with a reason shown on screen (`.claude/docs/PolishRules.md`
§9.3), and a control that is never locked does not get it. Touch has no pointer,
so new sets do not get `Hover`; a set that already ships `Hover` stays valid and
is not debt.

An existing set that still ships `Normal` or `Active` is debt. Rename it only in
a pass that also updates `component_ids.json` and re-imports its prefabs — the
prefab file names derive from the variant names.

### Button (icon buttons, buy buttons, plates, nav buttons)

| State | Required | Recipe |
|---|---|---|
| `Default` | yes | The base. |
| `Pressed` | yes | Content other than `Bg` translates down by `space/tight`; an `Overlay` rectangle on `Bg`, bound to `color/btn/pressed` (black at low alpha). The rim stays: it belongs to the plate set. |
| `Disabled` | when lockable | An `Overlay` rectangle on `Bg`, bound to `color/btn/disabled` (grey at partial alpha). No offset. |
| `Hover` | no — legacy only | An `Overlay` rectangle on `Bg`, bound to `color/btn/hover` (white at low alpha). No offset. Kept on sets that already ship it; not drawn for new work. |

The `Overlay` copies `Bg`'s box and corner radii, constraints STRETCH/STRETCH.
The per-colour plate tokens (`color/btn/<color>/…`) belong to the plate set's
`Color` variants only. The state tokens, `color/btn/pressed`,
`color/btn/disabled` (and legacy `color/btn/hover`), are colour-agnostic: one
value serves every colour. If one the set needs does not exist, hand the job to
`figma-tokens` first.

### Row (upgrade / list rows)

No `State`. The row is not the tap target; its embedded buy button is, and it
carries the button ladder.

### Card and slot

No `State`. Cards are large; the press affordance lives on the button inside.

Emptiness is a `Type`, not a `State`: a slot uses `Type=Empty` / `Type=<Filled>`.
Keep it that way — an empty slot is a different thing, not a different condition
of the same thing.

### Bar (progress / timer)

No `State`. The fill width is data. Do not add variants for fill percentage;
that is a runtime value in Unity, and a variant per step would be dead weight in
Figma and in the prefab.

### Readout and pill (currency bars, stat containers, text templates)

No `State`. Vary with `Type` and with boolean properties. `Show Plus` is the
pattern.

### Container (popup plate)

No `State`. It is a plate that other things sit on.

### Icon

`Type` only, one variant per drawing. Never fold an icon set into a parent as a
variant axis — a large icon set alone would multiply a parent set by its own
count. Expose it as `INSTANCE_SWAP`.

## Auto-layout rules

- **Direction** follows the content. Buttons horizontal. Rows horizontal. Card
  bodies vertical. Lists vertical.
- **Gap** binds to `space/tight`, `space/default` or `space/gutter`. Never a
  typed number.
- **Padding** binds to a spacing token. Asymmetric padding is fine.
- **Sizing** — `FILL` for the flexible child, `HUG` for the content-sized
  container, `FIXED` for art whose size came from the PSD.
- **Order matters.** `resize()` resets sizing modes to `FIXED`; call it before
  setting `layoutSizing*`. `FILL` and `HUG` are rejected until the node is
  already a child of an auto-layout frame, so `appendChild` first.

## Sizing against the grid

New masters snap to a column span: `span(n) = n × column + (n − 1) × gutter`.
Compute the span table from the project grid style and size to it. Existing
masters whose width is already off the column grid are pinned debt and must not
be resized: the `psd-to-figma` art tolerance is 0.00 px and every screen
instancing them would fail verify.

Minimum touch target ≈44 pt — 128 px on both axes on the 1080-wide frame
(`.claude/docs/PolishRules.md` §9.1); hold every tappable master to it, or give a
smaller visual a transparent hit frame.

## What upstream specified that does not apply

| Upstream | Why it is out |
|---|---|
| The core-10 specs (Button/Input/Select/Checkbox/Radio/Badge/Avatar/Card/Modal/Toast) with web pixel sizes h=32/40/48 | A form-app library. This UI has no text input, no dropdown, no checkbox, no radio, no toast. The archetypes above replace them. |
| `Focused`, `Loading`, `Error`, `Success`, `Read-only` states | Not in this project's ladder: no keyboard, no forms. (`Disabled` stays, for lockable controls only.) |
| The focus-ring standard (2 px outline, `color/border/focus`, 2 px offset) | No focus ring exists or is wanted; there is no `color/border/focus` token. |
| WCAG AA contrast gates (4.5:1 / 3:1) | False failures on stylised game art with stroked, drop-shadowed text. `figma-tokens` dropped the same check. |
| `Size` axis (Small/Medium/Large) | One fixed frame. Size differences here are per-usage art sizes, not a scale ladder. |
| `.` / `__` private prefixes | `MakeValidFileName` turns `.` into `_`, creating ambiguous filenames. `Base-` replaces it. |
| Mapping variant names to React props | Wrong target platform. |
