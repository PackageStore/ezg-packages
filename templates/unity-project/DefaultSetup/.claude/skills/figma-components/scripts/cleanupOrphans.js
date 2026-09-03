/**
 * cleanupOrphans.js
 *
 * Module: recovery — remove exactly the nodes named by the state ledger
 * Input:  NODE_IDS (string[], required) — ids returned by a previous call
 * Output: { removed: [{id, name, type}], missing: [id], refused: [{id, why}] }
 *
 * Never delete by name prefix. A prefix match takes nodes a person made by
 * hand. This script only removes ids the caller supplies, and refuses a node
 * that has live instances or that sits on the Screens page.
 *
 * node.remove() invalidates the handle immediately, so every field is read
 * before the call.
 *
 * Usage: Run via use_figma with the figma-use skill loaded first.
 * Pass skillNames: "figma-components".
 */

const removed = [];
const missing = [];
const refused = [];

for (const id of NODE_IDS) {
  const n = await figma.getNodeByIdAsync(id);
  if (!n || n.removed) { missing.push(id); continue; }

  const info = { id, name: n.name, type: n.type };

  let page = n.parent;
  while (page && page.type !== 'PAGE') page = page.parent;
  if (page && page.name === 'Screens') {
    refused.push(Object.assign({ why: 'node is on the Screens page — deleting screen art is not cleanup' }, info));
    continue;
  }

  // getInstancesAsync lives on ComponentNode, not ComponentSetNode — ask each
  // variant for a set.
  const masters = n.type === 'COMPONENT' ? [n]
    : n.type === 'COMPONENT_SET' ? n.children.filter(c => c.type === 'COMPONENT')
    : [];
  let live = 0;
  for (const m of masters) live += (await m.getInstancesAsync()).length;
  if (live) {
    refused.push(Object.assign({ why: `${live} live instances` }, info));
    continue;
  }

  n.remove();
  removed.push(info);
}

return { removed, missing, refused };
