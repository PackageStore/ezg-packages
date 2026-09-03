/**
 * createComponentWithVariants.js
 *
 * Module: 2 — Build a new component set from a base and a variant matrix
 * Input:  PAGE_ID (string, required) — normally the Components page
 *         NAME (string, required) — set name, PascalCase or Prefix_Name
 *         BASE_ID (string, required) — the base COMPONENT built in a prior call
 *         AXES (object, required) — { AxisName: [values...] }. The LAST axis
 *               becomes the column axis; put State last.
 *         DESCRIPTION (string, optional)
 * Output: { componentSetId, variantCount, axes, positions, createdNodeIds }
 *
 * Build the base fully bound in a previous call. Binding after
 * combineAsVariants is the most common cause of a set where every variant
 * looks the same.
 *
 * This script clones the base per combination and names each clone. It does NOT
 * apply per-variant visual differences — do that in the caller's own loop
 * before combining, or in a follow-up call over set.children. Splitting it this
 * way keeps each use_figma call under the ten-operation guidance.
 *
 * Usage: Run via use_figma with the figma-use skill loaded first.
 * Pass skillNames: "figma-components".
 */

const page = await figma.getNodeByIdAsync(PAGE_ID);
if (!page || page.type !== 'PAGE') return { error: `Node ${PAGE_ID} is not a page` };
await figma.setCurrentPageAsync(page);

const existing = page.findOne(n =>
  (n.type === 'COMPONENT_SET' || n.type === 'COMPONENT') && n.name === NAME);
if (existing) return { existed: true, id: existing.id, type: existing.type, name: NAME };

const base = await figma.getNodeByIdAsync(BASE_ID);
if (!base || base.type !== 'COMPONENT') return { error: `Node ${BASE_ID} is not a component` };

const axisNames = Object.keys(AXES);
if (!axisNames.length) return { error: 'AXES is empty' };

const combos = axisNames
  .map(k => AXES[k])
  .reduce((acc, values) => acc.flatMap(c => values.map(v => c.concat([v]))), [[]]);

if (combos.length > 30) {
  return {
    error: `${combos.length} combinations exceeds the 30 cap`,
    advice: 'move an icon axis to INSTANCE_SWAP, extract a Building_Blocks_* set, or split by the primary axis',
  };
}

const made = [];
for (const combo of combos) {
  const c = base.clone();
  c.name = axisNames.map((ax, i) => ax + '=' + combo[i]).join(', ');
  page.appendChild(c);
  made.push(c);
}

const cs = figma.combineAsVariants(made, page);
cs.name = NAME;
if (typeof DESCRIPTION === 'string' && DESCRIPTION.length) cs.description = DESCRIPTION;

// Grid: last axis on columns, everything else on rows (first axis slowest).
const GAP = 20;   // space/default
const PAD = 30;   // space/margin
const colAxis = axisNames[axisNames.length - 1];
const rowAxes = axisNames.slice(0, -1);

const w = Math.max(...cs.children.map(c => c.width));
const h = Math.max(...cs.children.map(c => c.height));

for (const child of cs.children) {
  const props = {};
  child.name.split(', ').forEach(p => { const [k, v] = p.split('='); props[k] = v; });
  const col = AXES[colAxis].indexOf(props[colAxis]);
  let row = 0;
  for (const ax of rowAxes) row = row * AXES[ax].length + AXES[ax].indexOf(props[ax]);
  child.x = PAD + col * (w + GAP);
  child.y = PAD + row * (h + GAP);
}

let maxX = 0, maxY = 0;
for (const c of cs.children) { maxX = Math.max(maxX, c.x + c.width); maxY = Math.max(maxY, c.y + c.height); }
cs.resizeWithoutConstraints(maxX + PAD, maxY + PAD);

// Never leave a top-level node at (0,0).
const right = page.children
  .filter(n => n.id !== cs.id)
  .reduce((m, n) => Math.max(m, n.x + n.width), 0);
cs.x = right + 200;
cs.y = 0;

return {
  componentSetId: cs.id,
  variantCount: cs.children.length,
  axes: AXES,
  positions: cs.children.map(c => ({ name: c.name, x: c.x, y: c.y })),
  createdNodeIds: [cs.id].concat(cs.children.map(c => c.id)),
};
