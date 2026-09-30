/**
 * addStateVariants.js
 *
 * Module: 3 — Make a State set from a Default master
 * Input:  COMPONENT_ID (string, required) — the Default master: a standalone
 *                COMPONENT, not in a set, that nests the plate set as BG_CHILD.
 *         STATES (string[], optional) — defaults to ['Pressed', 'Hover'].
 *                Allowed values: Pressed, Hover. Default is the master itself.
 *         BG_CHILD (string, optional) — name of the nested plate instance.
 *                Defaults to 'Bg'.
 *         PRESSED_TOKEN (string, optional) — COLOR variable of the Pressed
 *                overlay, e.g. 'color/btn/pressed'.
 *         HOVER_TOKEN (string, optional) — COLOR variable of the Hover
 *                overlay, e.g. 'color/btn/hover'.
 *         PRESSED_OFFSET (number, optional) — px the content moves down on
 *                Pressed; pass the value of `space/tight`. Defaults to 0.
 * Output: { set, exposed, added, variantCount, axes, noVisualChange, positions }
 *
 * The set keeps the master's position but is wider than the master. Tidy the
 * page afterwards with figma-hygiene/scripts/tidyCanvas.js (S-10).
 *
 * One axis per set (figma-components rule 7). The plate set keeps its Color
 * axis; this set carries State only and nests the plate as an exposed
 * instance, so N colours and 3 states are N + 3 variants, not N × 3.
 *
 * Mechanics: BG_CHILD is marked isExposedInstance, the master is cloned once
 * per state, each clone is named `State=<value>`, the master is renamed
 * `State=Default`, and all are combined. The set takes the master's old name,
 * and instances already placed from the master stay linked to State=Default.
 *
 * Recipe — colour-agnostic, it never reads a per-colour token:
 *   Pressed — content other than BG_CHILD moves down PRESSED_OFFSET; an
 *             `Overlay` rectangle bound to PRESSED_TOKEN sits on BG_CHILD.
 *   Hover   — an `Overlay` rectangle bound to HOVER_TOKEN sits on BG_CHILD.
 * The overlay is a sibling, never a fill override on BG_CHILD: a fill override
 * survives a Color swap on the placed instance and would pin the old colour.
 * It copies BG_CHILD's box, corner radii and radius bindings.
 *
 * Reported in noVisualChange for hand work: no token given, no BG_CHILD, a
 * BG_CHILD drawn from image art (a rectangle overlay would paint its
 * transparent corners), a padding too small for PRESSED_OFFSET, and a
 * BG_CHILD in the auto-layout flow (it moves with the content).
 *
 * Usage: Run via use_figma with the figma-use skill loaded first.
 * Pass skillNames: "figma-components". One set per call — verify, then continue.
 */

const master = await figma.getNodeByIdAsync(COMPONENT_ID);
if (!master || master.type !== 'COMPONENT') {
  return { error: `Node ${COMPONENT_ID} is not a component` };
}
if (master.parent.type === 'COMPONENT_SET') {
  return { error: 'component is already in a set; pass a standalone Default master (rule 7: State is its own set)' };
}
let page = master.parent;
while (page.type !== 'PAGE') page = page.parent;
await figma.setCurrentPageAsync(page);

const ALLOWED = ['Pressed', 'Hover'];
const states = (typeof STATES !== 'undefined' && STATES && STATES.length)
  ? STATES : ['Pressed', 'Hover'];
const bad = states.filter(s => !ALLOWED.includes(s));
if (bad.length) return { error: `disallowed State values: ${bad.join(', ')}. Allowed: ${ALLOWED.join(', ')}` };

const bgName = typeof BG_CHILD === 'string' ? BG_CHILD : 'Bg';
const offset = typeof PRESSED_OFFSET === 'number' ? PRESSED_OFFSET : 0;

const colorVars = await figma.variables.getLocalVariablesAsync('COLOR');
const tokenNames = {
  Pressed: typeof PRESSED_TOKEN === 'string' ? PRESSED_TOKEN : null,
  Hover: typeof HOVER_TOKEN === 'string' ? HOVER_TOKEN : null,
};
const overlayVar = {};
for (const [state, name] of Object.entries(tokenNames)) {
  if (!name || !states.includes(state)) continue;
  overlayVar[state] = colorVars.find(v => v.name === name);
  if (!overlayVar[state]) return { error: `${state} token ${name} is not a local COLOR variable` };
}

// A node that belongs to the component itself, not to a nested instance.
const own = (n, root) => {
  for (let p = n.parent; p && p !== root; p = p.parent) if (p.type === 'INSTANCE') return false;
  return true;
};
const findBg = root => root.findOne(n => n.type === 'INSTANCE' && n.name === bgName && own(n, root));

const noVisualChange = [];
const exposed = [];
const bg = findBg(master);
if (!bg) {
  noVisualChange.push({ id: master.id, name: master.name, why: `no ${bgName} instance to expose or overlay` });
} else if (!bg.isExposedInstance) {
  bg.isExposedInstance = true;
  exposed.push({ id: bg.id, name: bg.name });
}
const bgIsArt = bg && ((Array.isArray(bg.fills) && bg.fills.some(f => f.type === 'IMAGE'))
  || ('children' in bg && bg.children.length > 0 && bg.children.every(c => c.name.indexOf('slice_') === 0)));

async function addOverlay(cbg, variable) {
  const rect = figma.createRectangle();
  rect.name = 'Overlay';
  const parent = cbg.parent;
  parent.insertChild(parent.children.indexOf(cbg) + 1, rect);
  if ('layoutMode' in parent && parent.layoutMode !== 'NONE') rect.layoutPositioning = 'ABSOLUTE';
  rect.resize(cbg.width, cbg.height);
  rect.x = cbg.x;
  rect.y = cbg.y;
  rect.constraints = { horizontal: 'STRETCH', vertical: 'STRETCH' };
  rect.fills = [figma.variables.setBoundVariableForPaint(
    { type: 'SOLID', color: { r: 0, g: 0, b: 0 } }, 'color', variable)];
  const bv = cbg.boundVariables || {};
  for (const corner of ['topLeftRadius', 'topRightRadius', 'bottomRightRadius', 'bottomLeftRadius']) {
    const alias = bv[corner];
    const v = alias && await figma.variables.getVariableByIdAsync(alias.id);
    if (v) rect.setBoundVariable(corner, v);
    else rect[corner] = cbg[corner] || 0;
  }
  return rect;
}

const baseName = master.name;
const home = { x: master.x, y: master.y };
const variants = [master];
const added = [];

for (const state of states) {
  const clone = master.clone();
  // Name immediately: a duplicate name inside the set is an error state.
  clone.name = 'State=' + state;
  variants.push(clone);
  added.push({ id: clone.id, name: clone.name });
  const cbg = findBg(clone);

  if (state === 'Pressed' && offset) {
    if (clone.layoutMode && clone.layoutMode !== 'NONE') {
      if (clone.paddingBottom < offset) {
        noVisualChange.push({ id: clone.id, name: clone.name, why: `paddingBottom ${clone.paddingBottom} < PRESSED_OFFSET ${offset}` });
      } else {
        clone.paddingTop += offset;
        clone.paddingBottom -= offset;
        if (cbg && cbg.parent === clone && cbg.layoutPositioning !== 'ABSOLUTE')
          noVisualChange.push({ id: clone.id, name: clone.name, why: `${bgName} is in the auto-layout flow and moves with the content` });
      }
    } else {
      for (const c of clone.children) if (c !== cbg) c.y += offset;
    }
  }

  if (!overlayVar[state]) {
    noVisualChange.push({ id: clone.id, name: clone.name, why: `no ${state.toUpperCase()}_TOKEN given` });
  } else if (cbg && bgIsArt) {
    noVisualChange.push({ id: clone.id, name: clone.name, why: `${bgName} is image art; overlay by hand` });
  } else if (cbg) {
    const rect = await addOverlay(cbg, overlayVar[state]);
    added.push({ id: rect.id, name: clone.name + ' / Overlay' });
  }
}

master.name = 'State=Default';
const set = figma.combineAsVariants(variants, master.parent);
set.name = baseName;

const GAP = 20;   // space/default
const PAD = 30;   // space/margin
const order = ['Default'].concat(states);
const w = Math.max(...set.children.map(c => c.width));
for (const child of set.children) {
  child.x = PAD + order.indexOf(child.name.replace(/^State=/, '')) * (w + GAP);
  child.y = PAD;
}
let maxX = 0, maxY = 0;
for (const c of set.children) { maxX = Math.max(maxX, c.x + c.width); maxY = Math.max(maxY, c.y + c.height); }
set.resizeWithoutConstraints(maxX + PAD, maxY + PAD);
set.x = home.x;
set.y = home.y;

let axes = null;
try { axes = Object.fromEntries(Object.entries(set.variantGroupProperties).map(([k, v]) => [k, v.values])); }
catch (e) { axes = { _err: String(e) }; }

return {
  set: { id: set.id, name: set.name },
  exposed, added,
  variantCount: set.children.length,
  axes,
  noVisualChange,
  positions: set.children.map(c => ({ name: c.name, x: c.x, y: c.y })),
  createdNodeIds: added.map(a => a.id).concat([set.id]),
  mutatedNodeIds: [master.id].concat(exposed.map(e => e.id)),
};
