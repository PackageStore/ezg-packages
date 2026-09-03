# Patterns

Forked from `natdexterra/work-with-design-systems`
`references/build/patterns-guide.md`. A pattern is a reusable composition of
components, not a component itself.

Upstream puts patterns on their own page in a fixed-width wrapper and numbers the
frames `P{section}.{number} {Name}`. Both are replaced here: this project has one
fixed frame on a multi-column grid, and the file already rejected
`{section}.{number}` component numbering because it would break
`component_ids.json`, `style_ids.json` and the Unity prefab names.

Screen and component names below are illustrative — substitute the file's own.

## Where patterns live

**Default: in this file, as text.** The screens on the `Screens` page are
already the live examples, and they are verified against the PSD. A second copy
on a Patterns page would drift from them and would be verified by nothing.

**Optional: a live example frame on the `Components` page**, named
`Pattern_<Name>`, built only from instances. Use this when a pattern has no
single screen that shows it cleanly.

**Do not add a fourth page.** `figma-to-unity` and `psd-to-figma` both address
the file by the three page names `Screens`, `Components` and `Icons`. A new page
is a change to that contract, not a documentation decision.

## The grid is the pattern language

Every pattern below is stated in columns, not pixels. Read the concrete column
width, gutter, margin, row pitch and safe-zone bands from the project grid style
(`figma.gridStyleId`); their tokens are `space/column`, `space/gutter`,
`space/margin` and `space/row-pitch`. A run of `n` columns is
`span(n) = n × column + (n − 1) × gutter`.

## Patterns in this product

### Top bar

Full width, below the top safe zone. A `Resources_Group` of currency-bar
instances on the left, an icon button on the right.

- Container starts at or below the top safe-zone line.
- Each currency pill is `span(2)`. Two pills plus one gutter is `span(4)` — size
  the group to a whole span rather than to the loose sum.
- Gap between pills → `space/gutter`.
- The settings button sits on the right margin.

### Bottom navigation

Several nav-button instances in a horizontal auto-layout, above the bottom safe
zone.

- The row must end above the bottom safe-zone line.
- When the button count does not divide the column grid evenly, distribute with
  auto-layout `SPACE_BETWEEN` and let the container be the full content span; do
  not force each button onto a column edge.
- A screen that still has loose tab frames instead of instances is the pattern's
  biggest live violation — swap them for instances.

### Popup

A popup plate centred horizontally, an icon button (`Type=Close`) at its
top-right corner, content stacked inside.

- The plate width is pinned debt, off the column grid. Do not resize it. Centre
  it instead: `x = (frame.w − w) / 2`.
- The close button overhangs the plate corner. It is an overlay, so absolute
  position is correct here.
- Content inside the plate uses the plate's own padding, not the screen margin.

### Upgrade list

A vertical stack of row instances at `space/row-pitch` pitch.

- Rows are pinned debt off the column grid — centre them.
- The parent must be an auto-layout frame, per `figma-hygiene` S-4.
- The row's own buy button is the tap target; the row is not.

### Card list

Full-width cards stacked vertically.

- Cards use the full content span (`span` of all columns). New cards use the
  whole span; older cards a few pixels short of it are pinned debt.
- Card height varies with content — `HUG` vertically, `FILL` horizontally.
- Several plain frames of the same card shape are the clearest promotion
  candidate in the file.

### Slot grid

An N×M arrangement of slot instances.

- One container holds the whole grid, per `figma-hygiene` S-5.
- Slot widths are pinned debt, near but not on a whole span.
- Gap → `space/gutter`.

### Offer panel

A titled plate with a reward readout, a timer and one action button. When every
region is a loose `Container_*` frame and genuinely single-use, leave it that
way until a second screen shares the shape.

## What to record per pattern

1. **Composition** — which components, in what order.
2. **Grid** — the column span of the container and of each child.
3. **Spacing** — the spacing token for every gap and pad.
4. **Safe zone** — whether the pattern touches the top or bottom safe zone.
5. **Variations** — what changes between the screens that use it.

## Anti-patterns

- A pattern example built from detached frames or raw art. It must be instances.
- A "pattern" that is two nodes next to each other. Too granular.
- Duplicating a component's own documentation inside a pattern note. Link to the
  component description instead.
- Retro-fitting the column grid onto an imported screen by resizing art. The
  `psd-to-figma` art tolerance is 0.00 px; that is a verify failure, not a
  cleanup.
- Documenting a pattern the product does not use.
