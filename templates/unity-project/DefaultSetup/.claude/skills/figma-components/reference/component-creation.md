# Component creation

Forked from the Figma plugin skill `figma-generate-library` (v2.2.95)
`references/component-creation.md`. The API mechanics are upstream's and are
correct; the values, page structure, fonts and tokens are replaced with this
project's.

Read `component-spec.md` first for what to build. This file is how.

Component-set names and token names below are illustrative; substitute the
project's own. `<body-font>` is the project body font (`figma.fonts.body` in
`psd2figma.json`) — load it into a `BODY_FONT` constant at the top of any script
that writes text, rather than hardcoding a family and style.

## 1. Architecture

### Dependency order

Build the thing that gets nested before the thing that nests it. A master
containing an instance of another master cannot exist until that other master
does. A worked tier order for this file:

```
Tier 0  icon sets                              (Icons page)
Tier 1  plate masters (button plates, base plate)
Tier 2  buttons · currency bars · stat containers
Tier 3  nav buttons · list rows · cards · slots
Tier 4  popup container                         (hosts the rest)
```

A nav button that instances a plate master, and a buy button that instances a
button-plate master, are the shape to follow — do not rebuild a plate inside
every button.

### Building Blocks sub-components

When a sub-element has its own state axis that would multiply the parent's
matrix, extract it into its own set named `Building_Blocks_<Parent>_<Part>`.

Upstream uses a `Building Blocks/` slash namespace. This file uses underscores
instead: a slash in a component name becomes a folder in the Assets panel and a
path separator in the Unity prefab name.

Use it when the sub-element has its own variant axes, repeats within the parent,
or has axes the parent does not.

### Base components

Prefix a shared internal master with `Base_`. **Never a leading `.` or `__`** —
upstream's convention for hiding a component from the Assets panel breaks the
Unity prefab name and the `component_ids.json` key.

## 2. Where components live

All masters go on the `Components` page; icon sets go on `Icons`. Read the page
ids from `page_ids.json`. One page per component is an upstream convention and is
out — see `SKILL.md` rule 11.

Position a new set clear of the existing ones. Never leave a top-level node at
(0,0):

```javascript
const page = figma.root.children.find(p => p.name === 'Components');
await figma.setCurrentPageAsync(page);

const right = page.children.reduce((m, n) => Math.max(m, n.x + n.width), 0);
const NEW_X = right + 200;
const NEW_Y = 0;
```

`setCurrentPageAsync` may be called at most once per script, and
`figma.currentPage` resets between calls.

## 3. Base component with bindings

Build the base once, fully bound, then clone it per variant. Binding after
`combineAsVariants` is the most common cause of a set where every variant is
the same colour.

```javascript
const page = figma.root.children.find(p => p.name === 'Components');
await figma.setCurrentPageAsync(page);

const byName = {};
for (const v of await figma.variables.getLocalVariablesAsync()) byName[v.name] = v;

const BODY_FONT = figma.fonts.body;          // {family, style} from psd2figma.json
await figma.loadFontAsync(BODY_FONT);

const base = figma.createComponent();
base.name = 'Base_PriceButton';
base.resize(324, 120);                       // a whole span from the grid style
base.layoutMode = 'HORIZONTAL';
base.primaryAxisAlignItems = 'CENTER';
base.counterAxisAlignItems = 'CENTER';
base.primaryAxisSizingMode = 'FIXED';
base.counterAxisSizingMode = 'FIXED';

base.setBoundVariable('itemSpacing',   byName['space/tight']);
base.setBoundVariable('paddingLeft',   byName['space/default']);
base.setBoundVariable('paddingRight',  byName['space/default']);
base.setBoundVariable('topLeftRadius', byName['radius/button']);
base.setBoundVariable('topRightRadius', byName['radius/button']);
base.setBoundVariable('bottomLeftRadius', byName['radius/button']);
base.setBoundVariable('bottomRightRadius', byName['radius/button']);

const bg = figma.variables.setBoundVariableForPaint(
  { type: 'SOLID', color: { r: 0, g: 0, b: 0 } }, 'color', byName['color/surface/plate']
);
base.fills = [bg];

const label = figma.createText();
label.fontName = BODY_FONT;
label.characters = '1000';
label.name = 'Price_Value';
base.appendChild(label);
label.layoutSizingHorizontal = 'HUG';

const textStyle = (await figma.getLocalTextStylesAsync()).find(s => s.name === 'Price_Value');
await label.setTextStyleIdAsync(textStyle.id);

const fg = figma.variables.setBoundVariableForPaint(
  { type: 'SOLID', color: { r: 1, g: 1, b: 1 } }, 'color', byName['color/text/on-light']
);
label.fills = [fg];

return { createdNodeIds: [base.id, label.id], baseId: base.id };
```

Order traps, all of them real:

- `setBoundVariableForPaint` returns a **new** paint. Capture it and reassign;
  mutating the array in place does nothing.
- `resize()` resets sizing modes to `FIXED`. Call it before setting
  `layoutSizing*`.
- `HUG` and `FILL` are rejected until the node is already a child of an
  auto-layout frame. `appendChild` first.
- `cornerRadius` binds per corner. `setBoundVariable('cornerRadius', v)` is not
  the API; bind the four corner properties.
- Load the font before any text write, and use the node's current font when
  editing existing text: `getStyledTextSegments(['fontName'])`.

## 4. Variant matrix

### Define the axes before writing code

```
Plate set:
  Color → [one value per button colour]
  State → [Normal, Pressed, Disabled]
  Total = colours × states

Nav button:
  Type  → [one value per destination]
  State → [Normal, Pressed, Active]
  Total = destinations × states
```

### The 30-combination cap

Past 30, split. Three ways, in preference order:

1. **Move a visual axis to `INSTANCE_SWAP`.** An icon axis is never a variant
   axis. A large icon set folded in multiplies by its own variant count.
2. **Extract a Building Block** when the sub-element has its own state machine.
3. **Split by the primary axis** into separate sets. Last resort — it doubles
   the registry entries and the Unity prefab count.

### Clone per combination

```javascript
const BASE_ID = 'BASE_ID_FROM_PREVIOUS_CALL';
const page = figma.root.children.find(p => p.name === 'Components');
await figma.setCurrentPageAsync(page);

const base = await figma.getNodeByIdAsync(BASE_ID);
const byName = {};
for (const v of await figma.variables.getLocalVariablesAsync()) byName[v.name] = v;

const axes = { Color: ['Green', 'Yellow'], State: ['Normal', 'Pressed', 'Disabled'] };

const rim = {
  Green:  { high: byName['color/effect/btn-green-high'], low: byName['color/effect/btn-green-low'] },
  Yellow: { high: byName['color/effect/btn-gold-high'],  low: byName['color/effect/btn-gold-low'] },
};

const made = [];
for (const color of axes.Color) {
  for (const state of axes.State) {
    const v = base.clone();
    v.name = 'Color=' + color + ', State=' + state;

    const pair = rim[color];
    // Normal: high on top, low on bottom. Pressed: swapped. Disabled: no rim.
    const eff = state === 'Disabled' ? []
      : state === 'Pressed'
        ? [innerShadow(pair.low, 0, -6), innerShadow(pair.high, 0, 6)]
        : [innerShadow(pair.high, 0, -6), innerShadow(pair.low, 0, 6)];
    v.effects = eff;

    v.opacity = state === 'Disabled' ? 0.45 : 1;

    made.push(v);
  }
}

function innerShadow(variable, ox, oy) {
  const e = { type: 'INNER_SHADOW', color: { r: 0, g: 0, b: 0, a: 1 },
              offset: { x: ox, y: oy }, radius: 0, spread: 0, visible: true, blendMode: 'NORMAL' };
  return figma.variables.setBoundVariableForEffect(e, 'color', variable);
}

return { createdNodeIds: made.map(n => n.id), variantIds: made.map(n => n.id) };
```

Every variant name must be unique inside the set. A duplicate puts the set into
an error state and `componentPropertyDefinitions` then throws
`Component set has existing errors`.

## 5. `combineAsVariants` and the grid

A separate `use_figma` call, taking the variant IDs from the previous return.

```javascript
const VARIANT_IDS = ['ID1', 'ID2'];   // from the previous call
const page = figma.root.children.find(p => p.name === 'Components');
await figma.setCurrentPageAsync(page);

const nodes = (await Promise.all(VARIANT_IDS.map(id => figma.getNodeByIdAsync(id))))
  .filter(n => n && n.type === 'COMPONENT');

const cs = figma.combineAsVariants(nodes, page);
cs.name = 'Button';

const axes = { Color: ['Green', 'Yellow'], State: ['Normal', 'Pressed', 'Disabled'] };
const COL_AXIS = 'State';
const ROW_AXIS = 'Color';

const GAP = 20;        // between variants on the canvas — the file's own scale, not upstream's 16
const PAD = 30;        // set-frame inset

const w = Math.max(...cs.children.map(c => c.width));
const h = Math.max(...cs.children.map(c => c.height));

for (const child of cs.children) {
  const props = {};
  child.name.split(', ').forEach(p => { const [k, val] = p.split('='); props[k] = val; });
  const col = axes[COL_AXIS].indexOf(props[COL_AXIS]);
  const row = axes[ROW_AXIS].indexOf(props[ROW_AXIS]);
  child.x = PAD + col * (w + GAP);
  child.y = PAD + row * (h + GAP);
}

let maxX = 0, maxY = 0;
for (const c of cs.children) { maxX = Math.max(maxX, c.x + c.width); maxY = Math.max(maxY, c.y + c.height); }
cs.resizeWithoutConstraints(maxX + PAD, maxY + PAD);

const right = page.children.filter(n => n.id !== cs.id)
  .reduce((m, n) => Math.max(m, n.x + n.width), 0);
cs.x = right + 200;
cs.y = 0;

return { componentSetId: cs.id, variantCount: cs.children.length,
         positions: cs.children.map(c => ({ name: c.name, x: c.x, y: c.y })) };
```

Rules that bite:

- `combineAsVariants` takes a non-empty array of `COMPONENT` nodes only. Filter
  first; a frame or a group in the array throws.
- After combining, every child sits at (0,0). **You must position them.** A set
  where all variants overlap is the single most common failure.
- `resizeWithoutConstraints` after positioning, or the frame clips its children.
- There is no `figma.createComponentSet()`. You cannot make an empty set.

**Columns are `State`, rows are the identity axis.** State is what a reviewer
scans horizontally to check that the ladder is consistent.

## 6. Component properties

Add properties on the **set**, not on a variant. `addComponentProperty` returns
the real key with a `#id:id` suffix appended — capture it and use it
immediately.

```javascript
const cs = await figma.getNodeByIdAsync(CS_ID);

const labelKey = cs.addComponentProperty('Price', 'TEXT', '1000');
for (const child of cs.children) {
  const t = child.findOne(n => n.name === 'Price_Value');
  if (t) t.componentPropertyReferences = { characters: labelKey };
}

const plusKey = cs.addComponentProperty('Show Plus', 'BOOLEAN', true);
for (const child of cs.children) {
  const b = child.findOne(n => n.name === 'Btn_Plus');
  if (b) b.componentPropertyReferences = { visible: plusKey };
}

const iconKey = cs.addComponentProperty('Icon', 'INSTANCE_SWAP', ICON_COMPONENT_ID);
for (const child of cs.children) {
  const slot = child.findOne(n => n.name === 'Icon_Slot');
  if (slot && slot.type === 'INSTANCE') slot.componentPropertyReferences = { mainComponent: iconKey };
}

return { props: Object.keys(cs.componentPropertyDefinitions) };
```

| Node property | Property type | Used for |
|---|---|---|
| `characters` | `TEXT` | Editable label |
| `visible` | `BOOLEAN` | Show or hide a known child |
| `mainComponent` | `INSTANCE_SWAP` | Swap a nested instance |

### When the default must follow the variant

It cannot. Properties are set-level with one `defaultValue`; per-variant
`BOOLEAN` and `INSTANCE_SWAP` definitions merge on `combineAsVariants`. Give
each variant its own nested instance and expose it instead:

```javascript
for (const child of cs.children) {
  const inst = child.findOne(n => n.type === 'INSTANCE' && n.name === 'Icon_Currency');
  if (inst) inst.isExposedInstance = true;
}
```

A currency bar whose `Icon` is an exposed nested instance already ships this
pattern. See `slots-guide.md`.

### Never read `componentPropertyDefinitions` from a variant

Narrow to the owner first, or it throws:

```javascript
const owner = node.type === 'COMPONENT_SET' ? node
  : (node.type === 'COMPONENT' && node.parent.type === 'COMPONENT_SET') ? node.parent
  : node;
const props = owner.componentPropertyDefinitions;
```

Optional chaining does not make the getter safe.

## 7. Documentation

Set the description on the set, using
`figma-tokens/reference/component-description-template.md`. UPPERCASE section
headers only — `get_design_context` escapes markdown and collapses newlines.

```javascript
cs.description = [
  'PURPOSE',
  'Coloured button plates. Nested as Bg inside buy buttons and nav buttons.',
  '',
  'COMPOSITION',
  '- Body: linear gradient, bound to color/surface/plate.',
  '- Rim: paired inner shadows, color/effect/btn-<color>-high and -low.',
  '',
  'STATES',
  '- Pressed swaps the rim pair, inverting the bevel.',
  '- Disabled drops the rim and sets opacity 0.45.',
].join('\n');
```

Do not set `documentationLinks` to an external URL — there is no Storybook and
no published spec. Do not set `codeSyntax`; there is no CSS export.

## 8. Validation

Run `scripts/validateComponent.js`. It checks, inside one script:

- variant count matches the axis product
- `variantGroupProperties` lists the expected axes and values
- no child named `Component 1`, `Frame`, `Frame N`, `Group` or `Rectangle N`
- every fill, stroke, radius and gap has a `boundVariables` entry
- every TEXT node has a non-empty `textStyleId`
- variant positions are distinct — the (0,0) stacking bug

Only call `get_screenshot` when new geometry was created. It is the most
rate-limited call available, and the daily cap is what bites on a multi-set
sweep.

### Symptom table

| Symptom | Cause | Fix |
|---|---|---|
| All variants overlap at (0,0) | Grid layout never ran after `combineAsVariants` | Re-run §5 |
| Every variant the same colour | Bindings applied after combining | Rebind on `cs.children` |
| Text invisible or `undefined` | Font not loaded, or fill equals the background | Load `<body-font>`; bind fill to a `color/text/*` token |
| Set frame clips its variants | `resizeWithoutConstraints` not called | Recompute bounds, resize |
| `BOOLEAN` has no effect | `componentPropertyReferences` set on the variant frame, not the child | Set it on the child node |
| `INSTANCE_SWAP` offers nothing | Default was not a real component id | Pass an existing component id |
| `combineAsVariants` throws | A non-`COMPONENT` node in the array | Filter by `type === 'COMPONENT'` |
| `componentPropertyDefinitions` throws | Read from a variant, or duplicate variant names | Narrow to the set; make names unique |
| `resize()` silently does nothing | The node is a child of an `INSTANCE` | Edit the master |

## 9. Worked example — adding `State` to an existing set

The Phase 3 job. `scripts/addStateVariants.js` does this; the shape is:

1. Read the set, capture its existing axis name and values, and clone the
   `State=Normal` row from the current variants.
2. For each existing variant, clone once per new state value, rename to
   `<ExistingAxis>=<Value>, State=<NewValue>`, and apply the state recipe.
3. Rename the original variants to append `, State=Normal`.
4. Figma promotes the set to two axes automatically once every variant carries
   both properties. There is no separate "add axis" API.
5. Re-lay out the grid with `State` on columns.
6. Validate, then update the description's `STATES` section.

Do one set per `use_figma` call. Verify before the next.
