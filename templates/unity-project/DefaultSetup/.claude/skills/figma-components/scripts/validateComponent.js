/**
 * validateComponent.js
 *
 * Module: 5 — Post-build validation
 * Input:  COMPONENT_SET_ID (string, required)
 *         EXPECTED_VARIANTS (number, optional) — the axis product
 * Output: { name, id, type, variantCount, expected, axes, props, description,
 *           errors: [...], warnings: [...], pass: boolean }
 *
 * Deterministic checks only — no screenshot. Run get_screenshot separately and
 * only when new geometry was created; it is the most rate-limited read there is.
 *
 * Usage: Run via use_figma with the figma-use skill loaded first.
 * Pass skillNames: "figma-components".
 */

const node = await figma.getNodeByIdAsync(COMPONENT_SET_ID);
if (!node) return { error: `Node ${COMPONENT_SET_ID} not found` };

const set = node.type === 'COMPONENT' && node.parent.type === 'COMPONENT_SET'
  ? node.parent : node;

const errors = [];
const warnings = [];

let axes = {};
let props = [];
try {
  axes = set.type === 'COMPONENT_SET' ? (set.variantGroupProperties || {}) : {};
  props = Object.keys(set.componentPropertyDefinitions || {});
} catch (e) {
  errors.push('componentPropertyDefinitions threw: ' + String(e) +
              ' — usually duplicate variant names');
}

const variants = set.type === 'COMPONENT_SET' ? set.children : [set];

// Variant count against the axis product.
const product = Object.values(axes).reduce((a, v) => a * v.values.length, 1);
if (set.type === 'COMPONENT_SET' && product !== variants.length) {
  errors.push(`variant count ${variants.length} != axis product ${product} — the matrix has holes`);
}
if (typeof EXPECTED_VARIANTS !== 'undefined' && EXPECTED_VARIANTS !== null
    && variants.length !== EXPECTED_VARIANTS) {
  errors.push(`variant count ${variants.length} != expected ${EXPECTED_VARIANTS}`);
}

// Duplicate variant names put the set into an error state.
const seen = new Set();
for (const v of variants) {
  if (seen.has(v.name)) errors.push(`duplicate variant name: ${v.name}`);
  seen.add(v.name);
}

// The (0,0) stacking bug.
if (set.type === 'COMPONENT_SET' && variants.length > 1) {
  const pos = new Set(variants.map(v => v.x + ',' + v.y));
  if (pos.size < variants.length) {
    errors.push(`${variants.length - pos.size} variants share a position — grid layout never ran`);
  }
}

// Node names, per figma-hygiene S-2.
const GENERIC = /^(Frame|Group|Rectangle|Ellipse|Vector|Component|Line|Text)( \d+)?$/;
const generic = [];
const unbound = [];
const unstyledText = [];

for (const v of variants) {
  for (const n of v.findAll(() => true)) {
    if (GENERIC.test(n.name)) generic.push({ id: n.id, name: n.name });

    const bv = n.boundVariables || {};

    if ('fills' in n && Array.isArray(n.fills)) {
      n.fills.forEach((f, i) => {
        if (f.type === 'SOLID' && !(bv.fills && bv.fills[i])) {
          unbound.push({ id: n.id, name: n.name, prop: 'fills[' + i + ']' });
        }
      });
    }
    if ('strokes' in n && Array.isArray(n.strokes)) {
      n.strokes.forEach((s, i) => {
        if (s.type === 'SOLID' && !(bv.strokes && bv.strokes[i])) {
          unbound.push({ id: n.id, name: n.name, prop: 'strokes[' + i + ']' });
        }
      });
    }
    if ('cornerRadius' in n && typeof n.cornerRadius === 'number' && n.cornerRadius > 0
        && !bv.topLeftRadius) {
      unbound.push({ id: n.id, name: n.name, prop: 'cornerRadius' });
    }
    if ('itemSpacing' in n && n.layoutMode && n.layoutMode !== 'NONE'
        && n.itemSpacing > 0 && !bv.itemSpacing) {
      unbound.push({ id: n.id, name: n.name, prop: 'itemSpacing' });
    }
    if (n.type === 'TEXT' && !n.textStyleId) {
      unstyledText.push({ id: n.id, name: n.name, chars: n.characters.slice(0, 20) });
    }
  }
}

if (generic.length) errors.push(`${generic.length} generic node names (hygiene S-2)`);
if (unstyledText.length) errors.push(`${unstyledText.length} TEXT nodes without a text style (hygiene V-3)`);
if (unbound.length) warnings.push(`${unbound.length} unbound visual properties`);
if (!set.description || !set.description.length) warnings.push('no description');

return {
  name: set.name, id: set.id, type: set.type,
  variantCount: variants.length,
  expected: typeof EXPECTED_VARIANTS === 'undefined' ? product : EXPECTED_VARIANTS,
  axes: Object.fromEntries(Object.entries(axes).map(([k, v]) => [k, v.values])),
  props,
  description: set.description ? set.description.length : 0,
  generic, unbound: unbound.slice(0, 50), unstyledText,
  errors, warnings,
  pass: errors.length === 0,
};
