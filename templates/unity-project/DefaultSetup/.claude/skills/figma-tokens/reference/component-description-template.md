# Component description template

Forked from `natdexterra/work-with-design-systems`
`references/build/component-description-template.md`. The upstream `CODE
GENERATION NOTES` section targeted React props and ARIA attributes; here it
targets the Unity UGUI import instead.

Every component set on the Components page MUST carry a description. None do
today. This is the cheapest high-value pass in the whole token job for two
reasons:

1. **Figma MCP reads the description** and hands it to the next agent verbatim,
   so a set without one forces every future session to guess purpose and
   composition from geometry.
2. **The bridge reads the description at import time.** `ComponentAxisIntent`
   parses the `UNITY:` directive from the COMPONENT_SET description to record
   axis intent (decision D7). Until descriptions are filled in, every set's
   `RuntimeAxes` and `DesignAxes` are empty and all axes land in `Variants`.
   See the `figma-to-unity` skill for the full axis-intent contract.

## The template

```
PURPOSE
One sentence — what this component is and where it appears in the game.

VARIANTS
- Axis and values, e.g. Type = <role-A> | <role-B> | <role-C>
- What actually differs between them (art, colour token, size)
- Which variant is the default

COMPOSITION
- Structure: children, auto-layout direction and gap
- Nested instances, e.g. Nested: <PlateComponent> (Color=Yellow)
- Which text nodes are overridable and their TEXT property names
- Which parts are 9-slice and which are flat image fills
- Bound tokens, e.g. stroke uses color/text/stroke

USAGE
- When to use this set instead of a similar one
- Do not: the specific misuse this set exists to prevent

UNITY NOTES
- Prefab path (see figma-to-unity/reference/prefab-contract.md for the naming rules)
- 9-slice pieces the importer must restore, and their border values
- Fixed vs stretched behaviour on the 6-column grid
- Any known import deviation already pinned in accepted_debt.json

UNITY: runtime-axis=<axes the game switches at runtime>; design-axis=<axes that are baked at import>
```

## MCP delivery format — hard constraints

`get_design_context` escapes markdown and collapses newlines when it delivers a
description:

- `**bold**` arrives as `\*\*bold\*\*`
- `## heading` arrives as `\#\# heading`
- `[brackets]` arrives as `\[brackets\]`
- newlines collapse to single spaces, so sections run together
- backticks are escaped but stay readable

Therefore:

- UPPERCASE words are the **only** reliable section markers. Keep them short and
  single-word so they still parse when the surrounding text runs together.
- `-` bullets are fine.
- Never use `*`, `_`, `#`, `[`, `]` for emphasis.
- Write the source text in sentence case and let UPPERCASE apply only to the
  section headers themselves.

## Private base components

A set prefixed with `.` or `_` may use one line:

```
Internal: shared plate art for the button sets. Not for direct use.
```

## Example — a static 9-slice plate set

A plate that carries no interactive state; the axis is colour only.

```
PURPOSE
9-slice button plate art. Sits behind the label and icon of every action button
in the game.

VARIANTS
- Color = Green | Yellow
- Green is the confirm/buy action, Yellow is the upgrade action
- Green is the default

COMPOSITION
- Fixed height, 9-slice
- Children are slice_tl through slice_br, all image fills
- No text node — the label lives in the consuming set, not here
- No bindable solid fill: the plate is PNG art

USAGE
- Instance this set for any new button plate rather than placing a copy of the
  art, per the figma-hygiene reuse rule S-6
- Do not resize a child of the instance. Figma ignores it. Add a variant instead.
- Do not tint the art with a colour variable. It is an image fill.

UNITY NOTES
- Imports to a plate prefab with Image type Sliced
- 9-slice borders come from the nine-slice registry, not from Figma
- Stretches horizontally on the grid; height is fixed
```

## Example — a variant set with a per-type axis

A resource-bar readout, one variant per currency the game uses.

```
PURPOSE
Top-bar readout for one currency. Shows the currency icon, the current amount
and an optional add button.

VARIANTS
- Type = <currency-A> | <currency-B> | <currency-C>
- Icon art and the amount colour token differ per type
- The primary currency is the default

COMPOSITION
- Auto layout horizontal
- Nested: the background plate component
- Icon_<Type> is a flat image fill, one per variant
- Amount text uses TEXT property Amount, bound to the amount text style
- Amount colour binds to color/currency/<type>
- Text stroke binds to color/text/stroke, shadow uses the text-shadow effect
  style

USAGE
- One instance per currency, arranged left to right in the top-bar container
- Do not hide the icon child to reuse one currency as another. Switch the variant.
- A variant whose width differs on purpose is intentional, not drift — record it.

UNITY NOTES
- Each Type variant imports as a separate plain prefab (no Prefab Variants — D6)
- Plate is 9-slice; the icon is a plain Image
- Anchors to the top-left column edge, below the top safe zone

UNITY: design-axis=Type
```
