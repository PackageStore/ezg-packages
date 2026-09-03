// ==== GENERATED CONFIG — REPLACE PER PROJECT ==================================
// This block is project data, not technique. It is pasted into the Figma Plugin
// API sandbox (via the use_figma MCP tool), so this script cannot read the
// filesystem, import a module, or take argparse flags — the values are injected
// here instead of loaded. Regenerate this block from
//   tools/psd2figma/figma_extract_config.json
// whose keys map to CONFIG fields one-to-one:
//   pageName      -> CONFIG.pageName       (the page holding the screen frames)
//   frames        -> CONFIG.frames         (frameName -> output key; drives
//                                           figma_extract_<key>.json)
//   clipLeafNames -> CONFIG.clipLeafNames  (clip-box leaves standing in for a
//                                           Photoshop paragraph box, treated as
//                                           leaves and their text clipped)
//   rowFilter.screen   -> CONFIG.rowFilter.screen   (screen the row skip applies to)
//   rowFilter.keepRow  -> CONFIG.rowFilter.keepRow  (id prefix of the row kept)
//   rowFilter.skipRows -> CONFIG.rowFilter.skipRows (id prefixes of rows skipped)
const CONFIG = {
  pageName: 'Screens',
  frames: {
    'Menu': 'menu',
    'Upgrade_Gold': 'upgrade_gold',
    'Boost': 'boost',
    'Vip_Offer': 'vip_offer',
    'NET_Info': 'NET_info',
    'NET_Popup': 'NET_popup',
    'NET_Collection_Net': 'NET_collection_net',
  },
  clipLeafNames: ['Text_Desc', 'Card_Slot_3'],
  rowFilter: {
    screen: 'Upgrade_Gold',
    keepRow: 'I23:21;',
    skipRows: ['I23:33;', 'I23:46;', 'I23:59;', 'I23:72;', 'I23:85;'],
  },
};
// ==== END GENERATED CONFIG ===================================================

// Re-measure all screens for verify_figma_vs_psd.py.
// Run through the Figma MCP `use_figma` tool, then write each returned object
// to figma_extract_<screen>.json.
//
// Leaf rule: RECTANGLE and TEXT always; a node is a leaf when it is a 9-slice
// container (every child named slice_*) or when it is a clip-box leaf (see
// CONFIG.clipLeafNames — a clipping box standing in for a Photoshop paragraph
// box). Filled frames (Banner) are emitted and still descended into.
//
// The row filter keeps only Row#1 on CONFIG.rowFilter.screen: the PSD manifest
// defines one row, and the row pitch is verified separately. The skip is guarded
// to that one screen.
const page = figma.root.children.find(p => p.name === CONFIG.pageName);
await figma.setCurrentPageAsync(page);

// A plate may be a plain frame, or a Btn_Plate component/instance whose root holds
// the slices directly. All three count as one leaf: the slices are implementation.
const SLICE_HOSTS = ['FRAME', 'INSTANCE', 'COMPONENT'];
function isSliceFrame(n) {
  return SLICE_HOSTS.indexOf(n.type) >= 0 && 'children' in n && n.children.length > 0
    && n.children.every(c => c.name.indexOf('slice_') === 0);
}
function hasVisibleFill(n) {
  return 'fills' in n && Array.isArray(n.fills) && n.fills.some(f => f.visible !== false);
}

function collectClippedText(n, leafName, out) {
  if (n.visible === false) return;
  if (n.type === 'TEXT') {
    const mixed = figma.mixed;
    out.push({
      id: n.id, name: n.name, type: 'TEXT',
      parentLeaf: leafName,
      fontName: n.fontName === mixed ? 'MIXED' : n.fontName,
      fontSize: n.fontSize === mixed ? 'MIXED' : n.fontSize,
      textStyleId: n.textStyleId === mixed ? 'MIXED' : (n.textStyleId || ''),
    });
    return;
  }
  if ('children' in n) for (const c of n.children) collectClippedText(c, leafName, out);
}

function walk(n, frame, out, clippedOut, currentFrame) {
  if (n.visible === false) return;
  const ab = n.absoluteBoundingBox;
  if (!ab) return;
  if (currentFrame === CONFIG.rowFilter.screen &&
      CONFIG.rowFilter.skipRows.some(p => n.id.indexOf(p) === 0)) return;

  const rec = { id: n.id, name: n.name, type: n.type,
                x: ab.x - frame.x, y: ab.y - frame.y, w: ab.width, h: ab.height };
  const leaf = isSliceFrame(n) || CONFIG.clipLeafNames.indexOf(n.name) >= 0;

  if (n.type === 'TEXT') {
    const rb = n.absoluteRenderBounds;
    if (rb) { rec.inkX = rb.x - frame.x; rec.inkY = rb.y - frame.y; rec.inkW = rb.width; rec.inkH = rb.height; }
    rec.strokeWeight = typeof n.strokeWeight === 'number' ? n.strokeWeight : 1;
    rec.strokeAlign = n.strokeAlign;
    rec.hasVisibleStroke = Array.isArray(n.strokes) && n.strokes.some(s => s.visible !== false);
    rec.effects = (n.effects || []).map(e => ({ type: e.type, visible: e.visible, radius: e.radius, offset: e.offset }));
    const mixed = figma.mixed;
    rec.fontName = n.fontName === mixed ? 'MIXED' : n.fontName;
    rec.fontSize = n.fontSize === mixed ? 'MIXED' : n.fontSize;
    rec.textStyleId = n.textStyleId === mixed ? 'MIXED' : (n.textStyleId || '');
    out.push(rec);
    return;
  }
  if (n.type === 'RECTANGLE' || leaf || (n.type === 'FRAME' && hasVisibleFill(n))) out.push(rec);
  if (leaf) {
    if ('children' in n) for (const c of n.children) collectClippedText(c, n.name, clippedOut);
    return;
  }
  if ('children' in n) for (const c of n.children) walk(c, frame, out, clippedOut, currentFrame);
}

const res = {};
for (const [fname, key] of Object.entries(CONFIG.frames)) {
  const frame = page.children.find(c => c.name === fname);
  const ab = frame.absoluteBoundingBox;
  const nodes = [];
  const clippedText = [];
  for (const c of frame.children) walk(c, ab, nodes, clippedText, fname);
  res[key] = { frameId: frame.id, frameName: frame.name, frameX: 0, frameY: 0,
                 frameW: ab.width, frameH: ab.height, nodes, clippedText };
}
return res;
