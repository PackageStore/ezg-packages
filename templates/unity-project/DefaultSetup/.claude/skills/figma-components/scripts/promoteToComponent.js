/**
 * promoteToComponent.js
 *
 * Module: 4 — Promote repeated screen art into a master, then adopt it
 * Input:  MODE (string, required) — 'promote' or 'adopt'
 *
 *         MODE 'promote' (runs on the Screens page):
 *           SOURCE_NODE_ID (string, required) — the best copy to lift
 *           NAME (string, required) — the new master's name
 *         Output: { masterId, name, w, h, span, offGrid }
 *           The master is created on the Screens page next to the source so the
 *           script keeps its single setCurrentPageAsync. Move it to Components
 *           in a follow-up call.
 *
 *         MODE 'adopt' (runs on the Screens page):
 *           MASTER_ID (string, required)
 *           TARGET_NODE_IDS (string[], required) — the loose copies to replace
 *           DRY_RUN (boolean, optional) — defaults to true
 *         Output: { planned | replaced, mismatches }
 *
 * Adoption writes to the Screens page. It must be followed by the psd-to-figma
 * verify pass (art tolerance 0.00 px, text ink 2 px) and the figma-hygiene
 * post-flight. DRY_RUN defaults to true for that reason — read the plan first.
 *
 * A target whose size differs from the master is reported in mismatches and is
 * NOT replaced. Resizing art is a verify failure, not a cleanup.
 *
 * Usage: Run via use_figma with the figma-use skill loaded first.
 * Pass skillNames: "figma-components".
 */

const GRID = { col: 150, gutter: 24 };
const span = n => n * GRID.col + (n - 1) * GRID.gutter;
const SPANS = [1, 2, 3, 4, 5, 6].map(n => ({ n, w: span(n) }));

if (MODE === 'promote') {
  const src = await figma.getNodeByIdAsync(SOURCE_NODE_ID);
  if (!src) return { error: `Node ${SOURCE_NODE_ID} not found` };

  let page = src.parent;
  while (page && page.type !== 'PAGE') page = page.parent;
  await figma.setCurrentPageAsync(page);

  const clash = page.findOne(n =>
    (n.type === 'COMPONENT' || n.type === 'COMPONENT_SET') && n.name === NAME);
  if (clash) return { existed: true, id: clash.id, name: NAME };

  const copy = src.clone();
  const master = figma.createComponent();
  master.name = NAME;
  master.resize(src.width, src.height);
  master.x = src.absoluteBoundingBox.x + src.width + 200;
  master.y = src.absoluteBoundingBox.y;
  page.appendChild(master);

  if ('children' in copy) {
    for (const child of copy.children.slice()) master.appendChild(child);
    copy.remove();
  } else {
    master.appendChild(copy);
    copy.x = 0; copy.y = 0;
  }

  const match = SPANS.find(s => s.w === Math.round(master.width));
  return {
    masterId: master.id, name: NAME,
    w: Math.round(master.width), h: Math.round(master.height),
    span: match ? match.n : null,
    offGrid: !match,
    note: match ? null
      : `width ${Math.round(master.width)} is off the column grid; nearest spans ${JSON.stringify(SPANS.map(s => s.w))}. Do not resize an imported master — pin it as debt.`,
    createdNodeIds: [master.id],
  };
}

if (MODE === 'adopt') {
  const master = await figma.getNodeByIdAsync(MASTER_ID);
  if (!master || master.type !== 'COMPONENT') return { error: `Node ${MASTER_ID} is not a component` };

  const dry = typeof DRY_RUN === 'undefined' ? true : !!DRY_RUN;

  const first = await figma.getNodeByIdAsync(TARGET_NODE_IDS[0]);
  if (!first) return { error: `Node ${TARGET_NODE_IDS[0]} not found` };
  let page = first.parent;
  while (page && page.type !== 'PAGE') page = page.parent;
  await figma.setCurrentPageAsync(page);

  const inInstance = n => {
    let p = n.parent;
    while (p && p.type !== 'PAGE') { if (p.type === 'INSTANCE') return true; p = p.parent; }
    return false;
  };

  const plan = [];
  const mismatches = [];

  for (const id of TARGET_NODE_IDS) {
    const t = await figma.getNodeByIdAsync(id);
    if (!t) { mismatches.push({ id, why: 'not found' }); continue; }
    if (inInstance(t)) { mismatches.push({ id, name: t.name, why: 'inside an instance — edit the master instead' }); continue; }
    if (Math.round(t.width) !== Math.round(master.width) ||
        Math.round(t.height) !== Math.round(master.height)) {
      mismatches.push({ id, name: t.name,
        why: `size ${Math.round(t.width)}x${Math.round(t.height)} != master ${Math.round(master.width)}x${Math.round(master.height)}`,
        advice: 'add a variant; never resize art' });
      continue;
    }
    plan.push({ id, name: t.name, parent: t.parent.id, index: t.parent.children.indexOf(t),
                x: t.x, y: t.y });
  }

  if (dry) return { dryRun: true, planned: plan, mismatches, masterId: master.id };

  const replaced = [];
  for (const p of plan) {
    const t = await figma.getNodeByIdAsync(p.id);
    if (!t) continue;
    const inst = master.createInstance();
    const parent = t.parent;
    parent.insertChild(p.index, inst);
    inst.name = p.name;
    if (parent.layoutMode === undefined || parent.layoutMode === 'NONE') {
      inst.x = p.x; inst.y = p.y;
    }
    t.remove();
    replaced.push({ instanceId: inst.id, name: inst.name });
  }

  return {
    dryRun: false, replaced, mismatches, masterId: master.id,
    createdNodeIds: replaced.map(r => r.instanceId),
    next: 'run the psd-to-figma verify pass on every screen touched, then figma-hygiene post-flight',
  };
}

return { error: `MODE must be 'promote' or 'adopt', got ${MODE}` };
