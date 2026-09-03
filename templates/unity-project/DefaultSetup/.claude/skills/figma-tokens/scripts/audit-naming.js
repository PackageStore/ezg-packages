/**
 * audit-naming.js
 *
 * Module: 5 — Naming quality
 * Input:  COMPONENT_SET_ID (string)
 * Output: {
 *           componentName, componentId,
 *           semantic, total, percentage,
 *           genericNames: [{ name, type, nodeId }],
 *           spacedNames:  [{ name, type, nodeId }]
 *         }
 *
 * Flags layers inside a component set that still carry Figma's auto-generated
 * names ("Frame 12", "Rectangle 4", etc.). Names are deduplicated across
 * variants so a shared layer is only counted once.
 *
 * Usage: Run via use_figma with the figma-use skill loaded first.
 * Pass skillNames: "figma-tokens" for logging.
 */

const cs = await figma.getNodeByIdAsync(COMPONENT_SET_ID);
if (!cs || cs.type !== 'COMPONENT_SET') {
  return { error: `Node ${COMPONENT_SET_ID} is not a component set` };
}

// The trailing number is optional: figma-hygiene S-2 bans a bare `Frame` and
// `Group` too, not only `Frame 12`.
const genericPattern = /^(Frame|Group|Rectangle|Ellipse|Vector|Line|Text|Instance|Component|Polygon|Star|Boolean|Union|Subtract|Intersect|Exclude|Slice|Image)(\s+\d+)?$/;

// Layer names in this file are PascalCase or snake_case (e.g. Text_Desc,
// Row_Name, slice_*). A space in a layer name means it was never renamed from a
// Figma default or a paste, so it is reported separately from the hard
// generic-name violations.
const spacedNamePattern = /\s/;

const genericNames = [];
const spacedNames = [];
const allNames = new Set();

for (const variant of cs.children) {
  const allNodes = variant.findAll(() => true);
  for (const node of allNodes) {
    if (allNames.has(node.name)) continue;
    allNames.add(node.name);

    if (genericPattern.test(node.name)) {
      genericNames.push({
        name: node.name,
        type: node.type,
        nodeId: node.id
      });
    } else if (spacedNamePattern.test(node.name)) {
      spacedNames.push({
        name: node.name,
        type: node.type,
        nodeId: node.id
      });
    }
  }
}

const total = allNames.size;
const semantic = total - genericNames.length;
const percentage = total > 0 ? Math.round((semantic / total) * 1000) / 10 : 100;

return {
  componentName: cs.name,
  componentId: cs.id,
  semantic,
  total,
  percentage,
  genericNames,
  spacedNames
};
