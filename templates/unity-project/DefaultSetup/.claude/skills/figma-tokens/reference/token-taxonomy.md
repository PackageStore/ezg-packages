# Token taxonomy

Forked from `natdexterra/work-with-design-systems` `references/build/token-taxonomy.md`
and rewritten for a single-theme game UI file. Read before creating any variable
collection.

Project values live in the project settings file, not in this prose: the target
file key (`figma.fileKey`), the page map (`figma.pages`) and the body font
(`figma.fonts.body`, `{family, style}`) are read from `psd2figma.json` — the same
settings file `psd-to-figma` documents. The **live Figma file is the source of
truth for the token and style inventory**: take the census with the audit scripts
(`scripts/audit-tokens.js`, `scripts/inventory.js`) and the local-variable /
local-style APIs, and record what you find — never trust a count copied into a
doc, this one included.

## Two classes of art — generic UI vs special asset

Read this section first. It sets the size of the whole job, and it is a policy
decision, not a description of the current file.

This is a hybrid file: some art is uploaded PNG, some is already real Figma
geometry, and text is always real Figma text. The **target** state splits art
into two classes:

| Class | What belongs here | How it is built | Tokenizable |
|---|---|---|---|
| **Generic UI** | Button plates, panel and popup backgrounds, bars, slot frames, dividers, rows, badges, borders, simple flat icons | Real Figma geometry: `RECTANGLE`/`FRAME` with a SOLID fill, a stroke, a `cornerRadius`, and 9-slice where it stretches | **Yes — this is the goal** |
| **Special asset** | Characters, banners, splash art, decorative or illustrative icons, anything hand-painted | PNG `IMAGE` paint | No, and that is correct |

A colour variable binds only to a `SOLID` paint, never to an `IMAGE` paint. So
**genericizing UI chrome is what unlocks the token layer** — the two jobs are one
job. Every plate that becomes a real rounded rectangle with a solid fill turns
into three bindable properties (fill, stroke, radius) that were previously a
single flat sprite.

| Bindable | Not bindable — leave alone |
|---|---|
| SOLID fill on any shape or frame | PNG paint on a **special asset** |
| TEXT fill colour | The layout-grid style (a style, not a variable) |
| Stroke colour and `strokeWeight` | Absolute `x`/`y` of a child of an INSTANCE |
| Effect colour (via effect styles) | Font family and style (gated to `figma.fonts.body`) |
| `cornerRadius` | |
| Auto-layout `padding*` and `itemSpacing` | |
| `fontSize`, `lineHeight`, `letterSpacing`, `fontWeight` on text styles | |

### Image paints use `scaleMode: 'FIT'`, not `'FILL'`

Where a PNG legitimately stays (special assets), its paint must be
`scaleMode: 'FIT'` — the whole image inside the box, aspect ratio preserved.
Figma defaults a placed image to `'FILL'`, which crops whatever does not fit.

Census the image paints before any flip, by `scaleMode`:

| scaleMode | Verdict |
|---|---|
| `CROP` | Correct as-is. These are the `slice_*` 9-slice pieces; CROP is how a 9-slice piece places its region. Do not touch. |
| `FILL` | The actual candidates for the flip to FIT. |
| `FIT` | Already correct. |
| `TILE` | Not used for UI art; investigate any that appear. |

So "switch fill to fit" is a **FILL-only** job — usually a small fraction of the
total image-paint count, once the CROP slice pieces are excluded.

`'FIT'` and `'FILL'` render identically **only** when the node's aspect ratio
already equals the image's. Where a paint is aspect-mismatched (measure the
mismatch per paint; anything over ~1% is visible) that flip changes the picture.
Each such flip must go through the `psd-to-figma` verify pass, because art
tolerance is 0.00 px (`N-1`). Never flip `scaleMode` in bulk without re-verifying
the affected screens.

For anything that stretches — plates, panels, bars — neither mode is the answer.
Build it as 9-slice (Figma `slice_*` nodes; Unity `Image` type `Sliced`).

### Sizing the genericization backlog

Triage every image paint into **genericize / keep-png / unclear**. A name-and-owner
heuristic gives an **upper bound, not a work order**: most flagged nodes are
`slice_*` pieces of an existing 9-slice, and replacing a painted 9-slice with a
solid-fill 9-slice is only right where the art carries no bevel, gradient or
texture that a fill plus stroke cannot reproduce. That call needs eyes on the art,
per component set.

keep-png is normally small and clear: the illustrated icon set (characters /
creatures) plus a handful of banners.

Work the Components page first, because a master converts once and every screen
instance follows. Record the backlog per owner:

| Column | Content |
|---|---|
| Owner | component set or node that owns the paints |
| Paints | count of image paints under it |
| Page | Components / Icons / Screens |

Screens-page paint counts very likely include instance children that inherit
their master's paint — census nodes on screens are `I`-prefixed instance copies.
If so, converting the Components-page masters clears most of the screen-side count
for free. **Confirm that overlap before sizing the screen work**; the two figures
may not be additive.

### Order of work

Genericizing before binding is strictly cheaper than the reverse. A colour
primitive minted for a sprite you are about to delete is wasted, and a sprite
replaced after its neighbours are bound leaves an unbound hole in the middle of
a finished set. So: triage first (Phase 2b in `SKILL.md`), genericize the UI
chrome, then bind.

## Taking the census — read the live file, not this doc

There is no fixed variable/style inventory in this skill: it changes every build.
Before Phase 3, and again in Phase 6, read the current state from the file and
record it. Four censuses drive the whole job.

### 1. Variables that exist now

Read `getLocalVariableCollectionsAsync` and, per variable, record:

| Column | Content |
|---|---|
| Collection | `Primitives` / `Semantic` |
| Name | e.g. `color/text/stroke` |
| Value or alias | raw value (primitive) or the primitive it aliases (semantic) |
| Scopes | the explicit scope set — flag any `ALL_SCOPES` or `[]` |

Two rules this census enforces:

- **Every Semantic token must be a verified alias, not a copied value** — read the
  alias back, do not assume it.
- **A binding outside a variable's scopes still applies.** Scopes only filter the
  picker UI, so a too-narrow scope produces a working but *undiscoverable* token
  rather than an error. When you find a hardcoded value that a scoped variable
  should have owned, widen the scope rather than minting a duplicate.

### 2. Styles that exist now

Read the local text / paint / effect / grid styles. Per text style record name,
`fontSize`, `lineHeight` (note `AUTO` vs an explicit pixel value) and
`letterSpacing`. Rules:

- **Read `fontSize` from the live style, never type a rounded literal.** Figma
  stores design pixels at full float precision; a rounded literal (typing `42.67`
  for a `42.66666…` style) nudges every bound node's `fontSize` and breaks the
  ink tolerance. Mint the exact float the style reports.
- **A mature type ramp makes the type tier a binding job, not a design job.** If
  the styles already exist and cover the text, you are binding to them, not
  designing a new ramp.
- **Byte-identical duplicate styles** (same size / lineHeight / letterSpacing)
  must be resolved *before* building the type tier: either merge them, or make the
  one property that is supposed to differ into a variable. Deciding after you have
  bound them is more expensive.
- Note zero-use styles (delete or apply) and any style on the wrong font weight —
  see "Known artifacts".

### 3. The type surface — which sizes earn a token

Histogram every TEXT node's `fontSize`. A size earns a `font-size/*` primitive
when it recurs (the 3-occurrence bar, below); a size used once or twice stays
hardcoded. Record value → occurrence count → where.

- **The text styles ARE the semantic type layer.** `font-size/*` and
  `line-height/*` primitives deliberately get **no** Semantic role aliasing them —
  a `font-size/row-name` token would just duplicate the `Row_Name` style. Every
  *other* primitive gets exactly one role pointing at it; the type primitives are
  the documented exception.
- **Confirm near-identical sizes before minting both.** Two sizes a fraction of a
  pixel apart (e.g. `37` vs `37.333`) are almost certainly one intended size typed
  twice; merging removes a token nobody can tell apart. Confirm against the source
  before you create two.
- `letterSpacing` is typically uniform across every node (often `0 PERCENT`). If
  there is no variation, no token is needed.
- Bindable `line-height` primitives are the **explicit-pixel** lineHeights only —
  see trap 1 for why `AUTO` cannot become one.

### 4. Binding coverage — what is bound, what stays unbound

In Phase 6, count bound vs unbound bindable properties (exclude image paints —
they are not bindable) and record every remaining unbound property with the reason
it stays. Most remainders are correct to leave; the recurring reasons:

| Reason a property stays unbound | What to do |
|---|---|
| Missing `textStyleId` where every candidate style differs in `lineHeight` | Leave it. Applying one changes leading and breaks the 2 px ink tolerance — a design decision, not a script fix. |
| Stroke/fill hex is a debug or selection artifact | Delete the stroke; do not tokenize it. |
| Hex is a near-duplicate typo of a canonical colour | Canonicalize to the colour that already has the uses, then bind. |
| Value is under the 3-occurrence bar | Leave hardcoded until it recurs. |

**A token whose only real uses came from COMPONENT_SET-root chrome is a phantom**
(see trap 000): the set-root `padding`/`itemSpacing` inflated the histogram, and
once you exclude it the token has zero genuine uses. Either delete it or keep it
as a deliberate scale step — but do not believe a histogram that counted set-root
chrome.

## The 3-occurrence bar and the AUTO lineHeight rule

Two thresholds govern minting, and they recur across every census above:

- **A value earns a token when it recurs three or more times; used once or twice,
  it stays hardcoded.** An unused primitive is worse than a missing one — it shows
  up in every picker and invites a wrong binding.
- **Only styles with an explicit pixel `lineHeight` can be bound.** Most text
  styles use `lineHeight: AUTO`, which cannot become a variable (trap 1). Leave
  `AUTO` alone unless the design actually wants a fixed leading.

## Known artifacts and bugs, not design values

The census will surface these classes. Each is a defect to fix, not a value to
tokenize:

- **Debug / selection strokes** — a thin stray stroke (often a vivid non-palette
  hex at weight 1) left on a root node. Delete it; do not tokenize it.
- **Near-duplicate hex** — the same intended colour typed two ways (a one- or
  two-digit slip in the hex). Canonicalize to the variant that already carries the
  uses.
- **Wrong font weight** — a TEXT node off the gated body style (a heavier or
  lighter weight than the gated one). Per the `psd-to-figma` gate this is a **bug,
  not accepted debt**: it cannot be pinned.
- **Missing `textStyleId`** — bind on the MASTER, never the instance. Many such
  nodes sit inside clipping frames that a normal walk misses, so a naive audit
  under-counts them; walk into clipped children. A node whose size has no matching
  style either reuses an existing same-size style or gets a real new one — do not
  leave it unstyled.
- **Zero-use styles** — delete or apply; do not leave them dangling.

## Architecture — two tiers, one mode

```
Tier 1: Primitives          →  Tier 2: Semantic
(raw values)                   (role layer)
color/ink-900                  color/text/stroke
space/24                       space/gutter
radius/16                      radius/plate
font-size/xl                   font-size/row-name
```

**Tier 3 (component tokens) is not built.** Revisit only when a component needs
a value that contradicts its semantic role. A file with roughly twenty component
sets is below the 15+ threshold where the redirection layer pays for itself —
count the sets before assuming you need it.

### One mode per collection

Every collection has exactly one mode, named `Value`. Do **not** create
`Light`/`Dark`.

Why: this is a single-theme game UI. A second mode doubles the value count and
forces a decision for every token with nothing to put on the other side. The
upstream skill makes Light/Dark mandatory on the Semantic tier because it
targets themeable web apps; that requirement does not transfer.

### No status group

Do not create `color/status/{success,warning,error,info}`. Upstream calls this
mandatory for app UIs. This is a game: it has no validation messages, no error
toasts and no info banners. The game's real functional axes are different, and
they already exist as variant properties:

| Web concept | Game equivalent | Where it lives |
|---|---|---|
| `error` / `success` | Affordable vs unaffordable | the buy / upgrade-row sets |
| Severity ramp | Rarity ramp | the rarity frame set (`Type=<tier>`) |
| Brand accent | Currency identity | the currency resource-bar set (one variant per currency) |
| Primary vs secondary | Button colour | the button plate sets (`Color=<colour>`) |

Model these as semantic colour groups (below), not as status.

## Tier 1 — Primitives

Collection name: `Primitives`. One mode: `Value`.

| Category | Pattern | Notes |
|---|---|---|
| Colour | `color/{name}-{shade}` | Lowercase, hyphen before the shade. Only for colours that actually appear on a SOLID paint, a stroke or an effect. |
| Spacing | `space/{value}` | Named by the pixel value itself (`space/24`), because the grid — not a t-shirt ladder — is the source of truth. |
| Radius | `radius/{value}` | Same rule (`radius/16`). |
| Font size | `font-size/{name}` | `xs`/`sm`/`base`/`lg`/`xl`. Values are the real design pixels, at full float precision, not rounded. |
| Line height | `line-height/{name}` | **Pixels, never percent** — see the trap below. |
| Letter spacing | `letter-spacing/{name}` | Pixels. |

Do not invent a 10-shade ramp per hue. Create a primitive only when a real value
in the file needs it. An unused primitive is worse than a missing one: it shows
up in every picker and invites a wrong binding.

### Deriving the spacing scale — do this before writing any space token

The screen layout grid, from `figma-hygiene`, is the anchor: read its column
width, gutter, side margin, row pitch and safe-zone insets from the file's
layout-grid style (do not hardcode them here). Those anchors are enforced at
**screen** level and typically do not appear inside a component — so they are
screen-composition tokens, not component tokens.

`scripts/audit-tokens.js` runs in discovery mode (`SPACING_SCALE = []`) and
returns `spacingHistogram` — every unbound padding and `itemSpacing` value with
its occurrence count. Derive the scale from that histogram, keep the values that
appear three or more times, and only then fill `SPACING_SCALE` in the script so
on-scale violations become errors instead of warnings.

A value that appears once is a one-off dimension. Leave it hardcoded — do not
mint a token for it.

### Radius surface

Histogram every node's `cornerRadius`; a radius earns a `radius/{value}` primitive
when it clears the 3-occurrence bar. Two things to exclude:

- **Figma's default set-frame radius on COMPONENT_SET roots** (a small value like
  `5`, applied to every set) is editor chrome, not a design value. Never mint a
  token for it — see trap 000.
- Radii used once or twice — leave hardcoded.

For a radius applied to top corners only as `MIXED(r,r,0,0)`, bind per corner
(trap 0a).

## Tier 2 — Semantic

Collection name: `Semantic`. One mode: `Value`. Every semantic token aliases a
primitive; it never carries a raw value.

| Group | Pattern | Roles |
|---|---|---|
| Text | `color/text/{role}` | e.g. `title`, `value`, `label`, `stroke` (the text-stroke colour), `on-plate` |
| Currency | `color/currency/{type}` | one per currency the game uses |
| Rarity | `color/rarity/{tier}` | one per rarity-frame variant that exists |
| Button | `color/button/{color}` | one per button-plate colour |
| Surface | `color/surface/{role}` | e.g. `popup`, `plate`, `slot-empty` |
| Border | `color/border/{role}` | e.g. `default`, `slot` |
| Spacing — component | `space/{role}` | one per component-level spacing value that recurs 3+ times inside components |
| Spacing — screen | `space/{role}` | `margin`, `gutter`, `column`, `row-pitch` — screen-composition only; no component uses them internally |
| Radius | `radius/{role}` | e.g. `plate`, `card`, `slot`, `popup` |
| Type | `font-size/{style}` | one per text style, e.g. `row-name`, `price-value` |

Rarity and currency are the two groups worth extending first: both are already
variant axes, so a designer changing one currency's hue in a single place is the
concrete win that justifies the whole exercise.

## Variable scopes — mandatory, and the one thing you cannot get wrong

Set explicit scopes on every variable at creation time.

| Token type | Scopes |
|---|---|
| Surface / background colours | `["FRAME_FILL", "SHAPE_FILL"]` |
| Text colours | `["TEXT_FILL"]` |
| Stroke colours | `["STROKE_COLOR"]` |
| Effect colours | `["EFFECT_COLOR"]` |
| Spacing | `["GAP", "WIDTH_HEIGHT"]` |
| Radius | `["CORNER_RADIUS"]` |
| Font size | `["FONT_SIZE"]` |
| Line height | `["LINE_HEIGHT"]` |
| Letter spacing | `["LETTER_SPACING"]` |
| Font weight | `["FONT_WEIGHT"]` |

Two failure modes, both real:

- `ALL_SCOPES` (the default) puts every variable in every picker. A colour
  variable then offers itself as a corner radius.
- `[]` (empty) is worse: the variable is invisible in **every** picker, so the
  designer pastes a hex value by hand and the token is dead on arrival. It also
  makes the variable invisible to `scripts/fixHardcodedToTokens.js`, which
  indexes candidates by scope.

## codeSyntax — deliberately not set

Do not set `codeSyntax.WEB`. Upstream makes it a critical rule because its
Phase 6 emits `tokens.css` from it. This file has no CSS: screens reach Unity as
UGUI prefabs through the UnityFigmaBridge importer, which never reads
`codeSyntax`.

If a variable needs a Unity-side name, put it in the variable **description**.
A missing `codeSyntax` is not a defect in this file — do not let an audit report
it as one.

## Traps

000. **A COMPONENT_SET root's `padding*`, `itemSpacing` AND `cornerRadius` are
   editor chrome, never design values.** They lay out the variant grid you see on
   the canvas. Binding them looks like progress and means nothing. Worse, it
   corrupts the measurement: a spacing histogram that counts set-root chrome as
   real spacing mints phantom tokens that have zero genuine uses. Skip
   `node.type === 'COMPONENT_SET'` for all three property groups when auditing or
   binding.

0000. **An INSTANCE never reports its master's binding.** After you bind a
   master, `instance.boundVariables` stays empty — Figma does not surface the
   inherited binding. Two consequences: audit MASTERS only, or every instance
   reads as a false negative; and never "fix" an instance by binding it, because
   that writes an override, which is the exact thing tokens exist to remove. If
   an audit shows an instance unbound while its master is bound, that is correct
   and there is nothing to do.

00. **`setBoundVariableForPaint` DESTROYS the paint's `opacity`.** It returns a
   new paint whose `opacity` is reset to 1. Binding a colour variable to a
   semi-transparent fill therefore makes it fully opaque — a live visual
   regression, and a silent one.

   Always re-set opacity after binding, in the same script:

   ```js
   const fills = [...node.fills];
   const opacity = fills[0].opacity;                 // capture BEFORE binding
   let p = figma.variables.setBoundVariableForPaint(fills[0], 'color', variable);
   p = { ...p, opacity };                            // restore
   fills[0] = p;
   node.fills = fills;
   ```

   Then verify `node.fills[0].opacity`, not just the binding. Opacity and the
   colour token do coexist — the binding covers `color` only — but you must put
   the opacity back yourself.

0. **Gradient stops DO accept a variable binding.** `setBoundVariableForPaint`
   rejects a `GradientPaint` (its typings accept `SolidPaint` only,
   `plugin-api-standalone.d.ts` line 2157), but constructing the `ColorStop` by
   hand works:

   ```js
   const fills = [...node.fills];
   const g = { ...fills[0] };
   g.gradientStops = g.gradientStops.map((stop, i) =>
     i === 0
       ? { ...stop, boundVariables: { color: { type: 'VARIABLE_ALIAS', id: variable.id } } }
       : stop
   );
   fills[0] = g;
   node.fills = fills;
   ```

   So a recurring gradient can be tokenized per stop; no paint-style workaround is
   needed. Mint a primitive per recurring stop colour when you do it (one per
   distinct top-rim and bottom-rim stop).

0a. **`setBoundVariable('cornerRadius', v)` writes the binding to the four
   individual corner fields, not to a unified one.** Reading
   `boundVariables.cornerRadius` back comes up empty and looks like a failure;
   read `topLeftRadius`, `topRightRadius`, `bottomLeftRadius` and
   `bottomRightRadius` instead. Verify per corner or you will chase a
   non-existent bug.


1. **`lineHeight` is `AUTO` on most text styles, and AUTO cannot become a
   variable.** A variable bound to `lineHeight` is always a concrete pixel
   number; there is no unitless "auto". AUTO renders at a value computed from the
   font's OS/2 metrics table, not from a ratio you can compute reliably (e.g. a
   nominal ~42.7 px size can render near 51 px). So minting the nominal size for
   an AUTO style would SHRINK the rendered line by several px and break the
   `psd-to-figma` 2 px text ink tolerance. Only styles that carry an explicit
   pixel lineHeight are safe to bind. Leave the rest AUTO.
2. **This file does not publish as a library.** Variables and styles created
   here are local. `getLocalVariableCollectionsAsync` is the only source, and no
   other file can reuse them without copying.
3. **A child of an INSTANCE cannot be resized or repositioned.** Binding a
   spacing variable inside a master is fine; expecting the instance to absorb a
   new value by moving its children is not.
4. **Bind inside the master, never on the instance.** An override recorded on an
   instance is the thing tokens exist to remove.
5. **Font identity is gated.** Every TEXT node must stay on the body font
   (`figma.fonts.body`, falling back to `preflight.json` `fonts.body`), with a
   non-empty `textStyleId` in `style_ids.json`. A token pass that swaps a font or
   drops a style binding fails the `psd-to-figma` gate, and that failure is a
   bug — it cannot be pinned as accepted debt.
