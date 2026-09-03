/**
 * addStateVariants.js
 *
 * Module: 3 — Add a State axis to an existing component set
 * Input:  COMPONENT_SET_ID (string, required)
 *         STATES (string[], optional) — defaults to ['Pressed', 'Disabled'].
 *                Allowed values: Normal, Pressed, Disabled, Active. No Hover,
 *                no Focused — this is a touch game.
 *         TARGET_CHILD (string, optional) — name of the child carrying the rim
 *                effects. Defaults to the variant frame itself.
 *         DISABLED_OPACITY (number, optional) — defaults to 0.45.
 * Output: { set, added, renamed, variantCount, noVisualChange, positions }
 *
 * Mechanics: Figma has no "add a variant axis" API. An axis appears once every
 * variant carries the property. So: clone each existing variant once per new
 * state, name the clone `<existing>, State=<value>`, then rename the originals
 * to `<existing>, State=Normal`.
 *
 * Recipe:
 *   Pressed  — every INNER_SHADOW on the target has its offset.y negated, which
 *              inverts the bevel. Geometry-free, so it works whether or not the
 *              variant uses auto-layout.
 *   Disabled — variant opacity DISABLED_OPACITY, inner shadows removed.
 *   Active   — no automatic recipe. The variant is created and reported in
 *              noVisualChange for hand work.
 *
 * A variant with no inner shadows gets no automatic Pressed change and is
 * reported in noVisualChange. Do not assume the ladder is done because the
 * script returned pass.
 *
 * Usage: Run via use_figma with the figma-use skill loaded first.
 * Pass skillNames: "figma-components". One set per call — verify, then continue.
 */

const set = await figma.getNodeByIdAsync(COMPONENT_SET_ID);
if (!set || set.type !== 'COMPONENT_SET') {
  return { error: `Node ${COMPONENT_SET_ID} is not a component set` };
}
await figma.setCurrentPageAsync(set.parent.type === 'PAGE' ? set.parent : figma.currentPage);

const ALLOWED = ['Normal', 'Pressed', 'Disabled', 'Active'];
const states = (typeof STATES !== 'undefined' && STATES && STATES.length)
  ? STATES : ['Pressed', 'Disabled'];
const bad = states.filter(s => !ALLOWED.includes(s));
if (bad.length) return { error: `disallowed State values: ${bad.join(', ')}. Allowed: ${ALLOWED.join(', ')}` };

const originals = set.children.slice();
if (originals.some(v => /(^|,\s*)State=/.test(v.name))) {
  return { error: 'set already has a State axis', variants: originals.map(v => v.name) };
}

const targetName = typeof TARGET_CHILD !== 'undefined' ? TARGET_CHILD : null;
const disabledOpacity = typeof DISABLED_OPACITY === 'number' ? DISABLED_OPACITY : 0.45;

const pick = variant => {
  if (!targetName) return variant;
  return variant.findOne(n => n.name === targetName) || variant;
};

const noVisualChange = [];
const added = [];

for (const orig of originals) {
  for (const state of states) {
    const clone = orig.clone();
    // Name immediately: a duplicate name inside the set is an error state.
    clone.name = orig.name + ', State=' + state;
    added.push({ id: clone.id, name: clone.name });

    const target = pick(clone);
    const inner = (target.effects || []).filter(e => e.type === 'INNER_SHADOW');

    if (state === 'Pressed') {
      if (inner.length) {
        target.effects = target.effects.map(e =>
          e.type === 'INNER_SHADOW'
            ? Object.assign({}, e, { offset: { x: e.offset.x, y: -e.offset.y } })
            : e);
      } else {
        noVisualChange.push({ id: clone.id, name: clone.name, why: 'no inner shadows to invert' });
      }
    } else if (state === 'Disabled') {
      clone.opacity = disabledOpacity;
      if (inner.length) target.effects = target.effects.filter(e => e.type !== 'INNER_SHADOW');
    } else {
      noVisualChange.push({ id: clone.id, name: clone.name, why: 'Active has no automatic recipe' });
    }
  }
}

const renamed = [];
for (const orig of originals) {
  const before = orig.name;
  orig.name = before + ', State=Normal';
  renamed.push({ id: orig.id, from: before, to: orig.name });
}

// Re-lay out: State on columns, the original axis on rows.
const GAP = 20;   // space/default
const PAD = 30;   // space/margin
const colOrder = ['Normal'].concat(states);
const rowKeys = originals.map(o => o.name.replace(/,\s*State=Normal$/, ''));

const w = Math.max(...set.children.map(c => c.width));
const h = Math.max(...set.children.map(c => c.height));

for (const child of set.children) {
  const m = child.name.match(/^(.*),\s*State=([^,]+)$/);
  if (!m) continue;
  const col = colOrder.indexOf(m[2]);
  const row = rowKeys.indexOf(m[1]);
  if (col < 0 || row < 0) continue;
  child.x = PAD + col * (w + GAP);
  child.y = PAD + row * (h + GAP);
}

let maxX = 0, maxY = 0;
for (const c of set.children) { maxX = Math.max(maxX, c.x + c.width); maxY = Math.max(maxY, c.y + c.height); }
set.resizeWithoutConstraints(maxX + PAD, maxY + PAD);

let axes = null;
try { axes = Object.fromEntries(Object.entries(set.variantGroupProperties).map(([k, v]) => [k, v.values])); }
catch (e) { axes = { _err: String(e) }; }

return {
  set: { id: set.id, name: set.name },
  added, renamed,
  variantCount: set.children.length,
  axes,
  noVisualChange,
  positions: set.children.map(c => ({ name: c.name, x: c.x, y: c.y })),
  createdNodeIds: added.map(a => a.id),
  mutatedNodeIds: renamed.map(r => r.id).concat([set.id]),
};
