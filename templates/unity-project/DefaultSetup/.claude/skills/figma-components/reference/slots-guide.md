# Variation and slots

Forked from `natdexterra/work-with-design-systems`
`references/build/slots-guide.md`. The decision tree is kept verbatim in intent
— it is the most useful part of the upstream repo for this file. The slot
implementation section is replaced, because slots do not exist here.

Component and axis names below are illustrative — substitute the file's own.

## Slots are not available in this environment

`figma.createSlot` is not a property of the `figma` global. Reading it throws

```
TypeError: figma.createSlot: no such property 'createSlot' on the figma global object
```

Upstream states slots have been in open beta since March 2026. They are not
reachable from this Plugin API build, so **every compound component here uses
the fallback pattern below.** Re-test the property before designing around
slots; do not assume the beta reached this seat.

## The decision tree

For each customisable region of a component, in this order:

1. **Variant** — a fixed appearance change: kind, colour, state. Use when the
   option set is small and closed, e.g. one `Type` value per currency.
2. **Boolean property** — an element on or off, e.g. a `Show Plus` toggle on a
   currency bar. Booleans only control `visible` in Figma. **If toggling changes
   the layout, it is a variant, not a boolean.**
3. **Instance swap** — one predictable position changes identity, and the type
   is known. Icon slots — a swappable currency icon inside a currency bar.
4. **Nested exposed instance** — the content differs per variant and must stay
   editable on the screen. Set `isExposedInstance = true` on the child. This is
   the replacement for a named slot.

Do not reach past step 4. There is no step 5, and detaching is forbidden.

## Why step 4 replaces the slot

Component properties are **set-level with a single default**. After
`combineAsVariants`, a per-variant `BOOLEAN` or `INSTANCE_SWAP` definition
merges into one definition with one `defaultValue`. So an `INSTANCE_SWAP` cannot
carry a different default per variant.

When the swapped content must follow the variant — a different creature per
rarity, a different icon per currency — give each variant its own nested
instance and expose it:

```javascript
const child = variant.findOne(n => n.name === ICON_SET_NAME);
child.isExposedInstance = true;
```

The exposed instance appears in the instance's properties panel on the screen,
and the designer swaps it there without detaching anything. A currency bar whose
`Icon` is an exposed instance already ships this.

## What the fallback cannot do

A slot accepts arbitrary content. An exposed instance accepts one component at
one position. So a genuinely open region — "put whatever you like in the card
body" — has no clean answer here. Two acceptable resolutions:

- **Make the region a variant.** If the real content set is closed (a few known
  card bodies, not infinite), it was never a slot.
- **Make the region its own master and instance it as a sibling.** The parent
  holds a fixed-size placeholder frame; the screen places the real component
  next to it. Record this in the description as a slot substitute.

Never resolve it by detaching, and never by hiding a master child and stacking a
duplicate on top. Setting `visible = false` on an instance child records a
*removed* override and the node vanishes from the instance tree;
`instance.resetOverrides()` is the only way back.

## Region names

When a component does have a content region, name it for its role, in the file's
own convention — not in upstream's `Leading` / `Trailing` / `Header` / `Footer`
vocabulary, which describes a web list item.

| Region | Name pattern | Example |
|---|---|---|
| Leading visual | `Icon_<Role>` | `Icon_Close` |
| Primary label | `<Thing>_Name` or `<Thing>_Title` | `Row_Name`, `Card_Title` |
| Value readout | `<Thing>_Value` | `Price_Value`, `Stat_Value` |
| Body copy | `Text_Desc` | — |
| Action | `Btn_<Verb>` | `Btn_Buy` |
| Plate behind everything | `Bg` or `Bg_<Role>` | `Bg`, `Bg_Plate` |

`figma-hygiene` S-2/S-3 and
`figma-tokens/reference/naming-conventions.md` are the authority; this table is
the component-side summary.

## Documenting the decision

Every compound component records its variation decision per region in the
`COMPOSITION` section of its description, using
`figma-tokens/reference/component-description-template.md`. Write which
mechanism was chosen and why:

```
COMPOSITION
- Icon: exposed nested instance. Chosen over INSTANCE_SWAP because the default
  must follow the Type variant, and set-level properties carry one default.
- Price_Value: TEXT property. Content is always text.
- Plus button: BOOLEAN property Show Plus. Toggling does not move anything.
```

## Common compositions in this file

| Component | Regions and mechanism |
|---|---|
| Currency bar | `Icon` exposed instance · `Show Plus` boolean · `Type` variant |
| Nav button | `Bg` instance of a plate master · `Type` variant per destination |
| Buy button | `Bg` instance of a button-plate master · price TEXT property |
| List row | `Bg` 9-sliced plate · name and value TEXT properties · nested buy button |
| Card | `State` variant · nested icon instance |
