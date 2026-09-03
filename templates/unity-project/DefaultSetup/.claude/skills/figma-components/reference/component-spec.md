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

1. **States first.** Design `Pressed` and `Disabled` before the happy path. A
   button set without them is incomplete, and interactive sets that ship only a
   `Type` axis are incomplete today.
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
   follow.

## Private and base components

Upstream prefixes private components with `.` or `__` so Figma hides them from
the Assets panel. **Do not use a leading dot or underscore in this file.**
The bridge derives prefab filenames from component names — `=` becomes `-`
and `, ` becomes `_` — so the Figma name must be the intended prefab name
(`figma-to-unity/reference/prefab-contract.md` is the single source of truth
for the naming and normalisation rules). `component_ids.json` also keys off
component names.

Use a `Base_` prefix instead: `Base_Plate`, `Base_Row`. It reads the same, sorts
together, and the bridge imports it without mangling. If a base must be hidden from the Assets panel
later, that is a rename decision to make once, with the registry and the Unity
pipeline updated in the same pass.

## Component anatomy

```
ComponentSet "Button"
├── State=Normal
│   └── [auto-layout: horizontal, gap → space/tight, padding → space/default]
│       ├── Bg          (instance of the plate master, Color=Green)
│       ├── Icon_Price  (instance of an icon set, INSTANCE_SWAP)
│       └── Price_Value (TEXT, textStyleId → Price_Value)
├── State=Pressed
└── State=Disabled
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
| State | `State` | `Normal`, `Pressed`, `Disabled`, `Active` | interactive sets |

Boolean properties for toggles: a `Show Plus` boolean on a currency bar is the
existing example. Instance-swap properties for icon slots. Text properties for
editable labels.

## Archetypes and their required states

This is a touch game. There is no pointer, so there is no `Hover`; there is no
keyboard, so there is no `Focused`; there are no forms, so there is no `Error`,
`Success` or `Read-only`. The four values below are the entire ladder.

### Button (icon buttons, buy buttons, plates, nav buttons)

| State | Required | Recipe |
|---|---|---|
| `Normal` | yes | The base. |
| `Pressed` | yes | Content translates down by `space/tight`; the rim inner-shadow pair swaps — the `*-high` token moves to the bottom edge and the `*-low` token to the top, inverting the bevel. |
| `Disabled` | yes where the button can be unaffordable or locked | Variant opacity 0.45, rim effects removed, label fill → `color/text/on-dark`. |
| `Active` | only for a selected tab or a latched toggle | A bottom-nav tab needs it; a one-shot buy button does not. |

The rim tokens already exist per colour: `color/effect/btn-<color>-high` /
`color/effect/btn-<color>-low`, plus `color/effect/plate-low`. `Pressed` needs
no new token.

### Row (upgrade / list rows)

| State | Required | Recipe |
|---|---|---|
| `Normal` | yes | The base. |
| `Disabled` | yes | The unaffordable row. Opacity 0.45 on the price cluster only, not the whole row — the player must still read the name and the stat. |
| `Pressed` | no | The row is not the tap target; its embedded buy button is. |

### Card and slot

| State | Required | Recipe |
|---|---|---|
| `Normal` | yes | The base. |
| `Active` | yes where the card can be owned, selected or running | A card that already ships `State=Normal` / `State=Active` is the reference. |
| `Pressed` | no | Cards are large; the press affordance lives on the button inside. |

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

Minimum touch target ≈44 pt — convert to a px floor at the project design width
and hold every tappable master to it.

## What upstream specified that does not apply

| Upstream | Why it is out |
|---|---|
| The core-10 specs (Button/Input/Select/Checkbox/Radio/Badge/Avatar/Card/Modal/Toast) with web pixel sizes h=32/40/48 | A form-app library. This UI has no text input, no dropdown, no checkbox, no radio, no toast. The archetypes above replace them. |
| `Hover`, `Focused`, `Loading`, `Error`, `Success`, `Read-only` states | Touch game, no pointer, no keyboard, no forms. |
| The focus-ring standard (2 px outline, `color/border/focus`, 2 px offset) | No focus ring exists or is wanted; there is no `color/border/focus` token. |
| WCAG AA contrast gates (4.5:1 / 3:1) | False failures on stylised game art with stroked, drop-shadowed text. `figma-tokens` dropped the same check. |
| `Size` axis (Small/Medium/Large) | One fixed frame. Size differences here are per-usage art sizes, not a scale ladder. |
| `.` / `__` private prefixes | `MakeValidFileName` turns `.` into `_`, creating ambiguous filenames. `Base_` replaces it. |
| Mapping variant names to React props | Wrong target platform. |
