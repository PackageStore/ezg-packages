/**
 * audit-states.js
 *
 * Module: 2 — Interactive states
 * Input:  COMPONENT_SET_ID (string)
 * Output: {
 *           componentName, componentId, componentType,
 *           stateProperty, allVariantProperties,
 *           expectedStates, foundStates, missingStates,
 *           percentage, note?
 *         }
 *
 * Infers the component archetype from the component set name (longest match
 * wins, so "Radio Button" maps to `radio button` rather than `button`),
 * then checks whether the variants cover the states expected for that
 * archetype. Static archetypes (plate, popup, progress bar, rarity, …) score N/A
 * since interactive states do not apply.
 *
 * Usage: Run via use_figma with the figma-use skill loaded first.
 * Pass skillNames: "figma-tokens" for logging.
 */

const cs = await figma.getNodeByIdAsync(COMPONENT_SET_ID);
if (!cs || cs.type !== 'COMPONENT_SET') {
  return { error: `Node ${COMPONENT_SET_ID} is not a component set` };
}

// CONFIG — expected states per component archetype. The keys are archetype
// fragments matched against a component-set name; tune them to the project's own
// component naming. The state VALUES are the touch-game ladder
// (Normal/Pressed/Disabled/Active, Empty/Filled, Off/On) and should not gain web
// states (no Hover, no Focused).
//
// Multi-word keys exist alongside single-word ones because a "Radio Button"
// is *not* a button (no Pressed) and an "Inline Link" needs Visited like a
// link rather than the field/picker fallback. Longest-match wins so the
// multi-word key is preferred when present.
//
// Toggle/Checkbox/Radio include both interaction states AND binary-axis
// state names (Off/On, Unchecked/Checked, Unselected/Selected). Industry
// DS (Material, IBM Carbon, Polaris, Apple HIG) treat these as two state
// dimensions; permissive matching keeps a binary-only DS from scoring 0%.
// Proper multi-axis modeling deferred to v2.1.
const stateMap = {
  // Multi-word archetypes — must come before single-word fallbacks.
  // Longest match wins, so 'btn plate' beats 'btn'.
  'btn plate':     ['Normal'],
  'bg btn':        ['Normal'],
  'icon button':   ['Normal', 'Pressed', 'Disabled'],
  'menu button':   ['Normal', 'Pressed', 'Disabled'],
  'resource bar':  ['Normal'],
  'stat container':['Normal'],
  'text template': ['Normal'],
  'merge slot':    ['Empty', 'Filled'],

  // Single-word archetypes.
  btn:      ['Normal', 'Pressed', 'Disabled'],
  button:   ['Normal', 'Pressed', 'Disabled'],
  row:      ['Normal', 'Disabled'],
  card:     ['Normal', 'Active'],
  boost:    ['Normal', 'Active'],
  slot:     ['Empty', 'Filled'],
  toggle:   ['Off', 'On'],
  tab:      ['Normal', 'Active', 'Disabled'],

  // Static — variant axis is Type/Color/Icon, not State.
  plate:    ['Normal'],
  popup:    ['Normal'],
  progress: ['Normal'],
  bar:      ['Normal'],
  timer:    ['Normal'],
  rarity:   ['Normal'],
  currency: ['Normal'],
  holder:   ['Normal'],
  icon:     ['Normal'],
  frame:    ['Normal'],
  container:['Normal'],
  text:     ['Normal']
};

// Archetypes with no interactive state axis at all. A missing State property
// on these is correct, not a gap. This is a touch game: there is no Hover and
// no Focused anywhere in this file — never add them.
const NO_STATE_REQUIRED = new Set([
  'plate', 'bg btn', 'btn plate',
  'popup', 'progress', 'bar', 'timer',
  'rarity', 'currency', 'resource bar', 'holder',
  'text', 'text template', 'stat container',
  'icon', 'frame', 'container',
  'divider', 'image', 'logo'
]);

// Strip non-letters so "Radio Button" → "radiobutton" and "icon-button"
// → "iconbutton". Apply same transform to keys when comparing so multi-word
// keys with spaces still match.
const normalize = (s) => s.toLowerCase().replace(/[^a-z]/g, '');
const nameNorm = normalize(cs.name);
const archetypeKeys = Object.keys(stateMap);
const matches = archetypeKeys.filter(t => nameNorm.includes(normalize(t)));
const componentType = matches.sort((a, b) => b.length - a.length)[0] || 'unknown';
const expectedStates = componentType === 'unknown' ? ['Default'] : stateMap[componentType];

// Parse variant names ("Type=Primary, Size=Medium, State=Hover")
const variantProps = {};
for (const variant of cs.children) {
  if (!variant.name) continue;
  const pairs = variant.name.split(',').map(p => p.trim());
  for (const pair of pairs) {
    const [key, val] = pair.split('=').map(s => s.trim());
    if (!key || !val) continue;
    if (!variantProps[key]) variantProps[key] = new Set();
    variantProps[key].add(val);
  }
}

// Short-circuit: archetype legitimately has no interactive-state axis.
if (NO_STATE_REQUIRED.has(componentType)) {
  return {
    componentName: cs.name,
    componentId: cs.id,
    componentType,
    stateProperty: null,
    allVariantProperties: Object.fromEntries(
      Object.entries(variantProps).map(([k, v]) => [k, [...v]])
    ),
    expectedStates: [],
    foundStates: [],
    missingStates: [],
    percentage: 100,
    note: 'Archetype does not require interactive states — score N/A.'
  };
}

// Singular `State` is canonical (Figma's recommendation, all major DS).
// Plural `States` and `Status`/`Condition` accepted as real-world tolerance.
const stateKey = Object.keys(variantProps).find(k =>
  ['state', 'states', 'status', 'condition'].includes(k.toLowerCase())
);

const foundStates = stateKey ? [...variantProps[stateKey]] : ['Default'];
const foundNorm = new Set(foundStates.map(s => s.toLowerCase()));
const missingStates = expectedStates.filter(s => !foundNorm.has(s.toLowerCase()));
const percentage = expectedStates.length > 0
  ? Math.round(((expectedStates.length - missingStates.length) / expectedStates.length) * 1000) / 10
  : 100;

return {
  componentName: cs.name,
  componentId: cs.id,
  componentType,
  stateProperty: stateKey || null,
  allVariantProperties: Object.fromEntries(
    Object.entries(variantProps).map(([k, v]) => [k, [...v]])
  ),
  expectedStates,
  foundStates,
  missingStates,
  percentage
};
