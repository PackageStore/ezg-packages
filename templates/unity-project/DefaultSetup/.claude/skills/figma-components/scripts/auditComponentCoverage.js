/**
 * auditComponentCoverage.js
 *
 * Module: 0 — Component coverage
 * Input:  PAGE_ID (string, required) — one page per call. The runtime allows at
 *         most one setCurrentPageAsync per script, so fan out one call per page
 *         in a single message and merge the results.
 * Output: for a page holding component sets:
 *         { mode: 'components', page, setCount, totalVariants,
 *           sets: [{ id, name, type, w, h, variantCount, variants, axes,
 *                    props, hasDesc, descLen }],
 *           looseTopLevel: [{ id, name, type, w, h }] }
 *
 *         for a page holding screens:
 *         { mode: 'screens', page,
 *           screens: [{ screen, id, totalNodes, topLevelInstances, freeNodes }],
 *           crossScreenCandidates: [...], sameScreenRepeats: [...] }
 *
 * Free nodes are non-instance nodes that are not inside an instance. A high
 * free-node count on a screen means structure that should be a master.
 *
 * Usage: Run via use_figma with the figma-use skill loaded first.
 * Pass skillNames: "figma-components".
 */

const page = await figma.getNodeByIdAsync(PAGE_ID);
if (!page || page.type !== 'PAGE') return { error: `Node ${PAGE_ID} is not a page` };
await figma.setCurrentPageAsync(page);

const sets = page.findAllWithCriteria({ types: ['COMPONENT_SET', 'COMPONENT'] })
  .filter(n => !(n.type === 'COMPONENT' && n.parent.type === 'COMPONENT_SET'));

if (sets.length) {
  const out = sets.map(n => {
    let props = {};
    try { props = n.componentPropertyDefinitions || {}; } catch (e) { props = { _err: String(e) }; }
    const variants = n.type === 'COMPONENT_SET' ? n.children.map(c => c.name) : null;
    const axes = n.type === 'COMPONENT_SET' && n.variantGroupProperties
      ? Object.keys(n.variantGroupProperties) : [];
    return {
      id: n.id, name: n.name, type: n.type,
      w: Math.round(n.width), h: Math.round(n.height),
      variantCount: variants ? variants.length : 1,
      variants, axes,
      props: Object.keys(props),
      hasDesc: !!(n.description && n.description.length),
      descLen: n.description ? n.description.length : 0,
    };
  });
  return {
    mode: 'components',
    page: { id: page.id, name: page.name },
    setCount: out.length,
    totalVariants: out.reduce((a, b) => a + b.variantCount, 0),
    sets: out,
    looseTopLevel: page.children
      .filter(n => n.type !== 'COMPONENT_SET' && n.type !== 'COMPONENT')
      .map(n => ({ id: n.id, name: n.name, type: n.type,
                   w: Math.round(n.width), h: Math.round(n.height) })),
  };
}

// Screens page.
const inInstance = n => {
  let p = n.parent;
  while (p && p.type !== 'PAGE') { if (p.type === 'INSTANCE') return true; p = p.parent; }
  return false;
};

const screens = [];
const groups = {};

for (const screen of page.children) {
  if (!('findAll' in screen)) continue;
  const all = screen.findAll(() => true);
  let instances = 0, free = 0;
  for (const n of all) {
    if (n.type === 'INSTANCE' && !inInstance(n)) instances++;
    if (inInstance(n) || n.type === 'INSTANCE') continue;
    free++;
    // Strip a trailing copy index so Plus_Sign and Plus_Sign_2 group together.
    const stem = n.name.replace(/[ _-]?\d+$/, '');
    const key = stem + '|' + Math.round(n.width) + 'x' + Math.round(n.height) + '|' + n.type;
    (groups[key] = groups[key] || {
      name: stem, type: n.type, w: Math.round(n.width), h: Math.round(n.height), hits: [],
    }).hits.push({ screen: screen.name, id: n.id });
  }
  screens.push({
    screen: screen.name, id: screen.id,
    totalNodes: all.length, topLevelInstances: instances, freeNodes: free,
  });
}

const cands = Object.values(groups)
  .filter(g => g.hits.length >= 2)
  .map(g => {
    const uniq = [...new Set(g.hits.map(h => h.screen))];
    return {
      name: g.name, type: g.type, size: g.w + 'x' + g.h,
      count: g.hits.length, screens: uniq, crossScreen: uniq.length > 1,
      nodeIds: g.hits.map(h => h.id),
    };
  })
  .sort((a, b) => b.count - a.count);

return {
  mode: 'screens',
  page: { id: page.id, name: page.name },
  screens,
  crossScreenCandidates: cands.filter(c => c.crossScreen),
  sameScreenRepeats: cands.filter(c => !c.crossScreen),
};
