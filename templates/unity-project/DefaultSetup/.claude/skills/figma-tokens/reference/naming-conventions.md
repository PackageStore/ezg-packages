# Naming conventions

Forked from `natdexterra/work-with-design-systems`
`references/build/naming-conventions.md`. The upstream framework mapping tables
(React / Vue / Tailwind / CSS variables) are removed: this file targets Unity UGUI
through the UnityFigmaBridge importer (`figma-to-unity/reference/prefab-contract.md`
has the naming rules), so a Figma name must read well to a Unity developer, not
to a CSS author.

This file governs **variable and token names**. Node, screen and container names
are governed by `.claude/skills/figma-hygiene/SKILL.md` (S-2, S-3) and
`.claude/skills/psd-to-figma/reference/component-registry.md`. Where they
overlap, those two win.

## Variable naming

| Rule | Example | Why |
|---|---|---|
| `/` as group separator | `color/text/stroke` | Figma standard; builds the folder tree in the Variables panel |
| Lowercase, hyphen inside a segment | `color/ink-900`, `space/row-pitch` | One casing rule for all tokens, so no token name needs remembering |
| No dots | `color/text/stroke`, never `color.text.stroke` | Dots collide with Figma's internal notation |
| Maximum three segments | `color/text/stroke` | `color/bg/surface/card/inner` is unreadable and unfindable |
| Numeric spacing and radius names | `space/24`, `radius/16` | The 6-column grid is the source of truth, so the value *is* the name. A t-shirt ladder (`space/md`) hides which grid measure a token means. |
| Semantic names are roles, not values | `space/gutter`, not `space/24-alt` | The role survives a value change; that is the entire point of the tier |
| Collections named by tier | `Primitives`, `Semantic` | Immediate identification |

**Casing split, stated once:** tokens are `lowercase-with-hyphens`; nodes and
components are `PascalCase` or `snake_case` per the existing file. These two
systems do not mix, and neither should be converted to the other.

## Collection and mode naming

| Collection | Modes |
|---|---|
| `Primitives` | `Value` |
| `Semantic` | `Value` |

One mode each. No `Light`, no `Dark`, no brand modes — see
`token-taxonomy.md`.

## Component and component-set naming

The file's existing names are the convention. Do not renumber or re-case them.
Read the real names out of `component_ids.json` (or a `componentPropertyDefinitions`
census) before you add one — the patterns below say what shape a new name takes,
not what any particular component is called.

| Observed pattern | Shape a new name takes |
|---|---|
| PascalCase for a public component | `<Role>`, `<Role><Noun>` |
| `Btn_` prefix for buttons | `Btn_<Purpose>` |
| `Bg_` prefix for backgrounds | `Bg_<Owner>` |
| `Frame_` prefix for containers and holders | `Frame_<Content>` |
| `Container_` prefix for layout groups | per `figma-hygiene` S-3 |
| `snake_case` for legacy nodes already in the file | leave as found; `slice_*` for 9-slice cells |

**Do not adopt upstream's `C{section}.{number} {Name}` numbering.** It would
rename every component set in the file and break
`psd-to-figma/reference/component-registry.md`, `style_ids.json` and the Unity
prefab import names, for no gain.

## Variant property naming

The axis properties are named `Type`, `Color`, `Icon` and `State`; read the
actual values a set uses from its `componentPropertyDefinitions`. Value naming per
axis:

| Axis | Property | Value naming |
|---|---|---|
| Kind | `Type` | PascalCase role or subject — the currency, rarity tier, button function, or slot state the variant represents |
| Colour | `Color` | the plate colour, PascalCase (e.g. `Green`, `Yellow`, `White`) |
| Icon | `Icon` | the icon identity |
| State | `State` | see the rule below |

Rules:

- **`State` values are `Normal` / `Pressed` / `Disabled` / `Active` only.** There
  is no `Hover` and no `Focused` anywhere in this file, and none may be added:
  this is a touch game with no pointer and no keyboard focus ring. Upstream's
  state ladders assume a web pointer — ignore them.
- **Every variant name must be unique within its set.** A duplicate puts the set
  into an error state, after which reading `componentPropertyDefinitions` throws
  `Component set has existing errors`.
- Placeholder values such as `Type=Rectangle_4` are debt. Rename to the real
  role when you touch that set; do not add more.

## Layer naming inside components

`figma-hygiene` S-2 is the gate: zero nodes named `Frame`, `Frame N`, `Group` or
`Group N`. `scripts/audit-naming.js` reports these as `genericNames`, and reports
any name containing a space as `spacedNames`.

| Layer role | Convention | Example |
|---|---|---|
| Grouping frame | `Container_<Content>` | `Container_Rows` |
| Text | Content role | `Row_Name`, `Price_Value`, `Text_Desc` |
| Icon | Role-based | `Icon_Close`, `Icon_Star` |
| 9-slice piece | `slice_*` | `slice_tl`, `slice_c` |
| Art | Purpose | `Bg_Plate`, `Art_Hero` |

Clear any live violations `scripts/audit-naming.js` reports as `genericNames`
(bare `Frame`/`Group`/`Rectangle` names) before handing off.

## Anti-patterns

- Abbreviating a token role: `color/txt/pri` instead of `color/text/primary`
- `color1`, `space2` — meaningless names
- Versioning in a name: `Btn_Buy_v2`. Use the component description instead.
- A semantic token that carries a raw value instead of aliasing a primitive
- A primitive minted for a value that appears once in the file
- Setting `codeSyntax.WEB` — this file has no CSS export
- Renaming existing components to match an external convention
