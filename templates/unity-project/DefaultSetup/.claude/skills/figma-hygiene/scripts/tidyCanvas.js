/**
 * tidyCanvas.js
 *
 * Contract: S-10 — top-level nodes of a page sit in tidy rows, at most GAP apart
 * Input:  PAGE_ID (string, required) — the page to check or tidy
 *         MODE (string, optional) — 'check' (default) or 'tidy'
 * Output: { pass, mode, rows, overlaps, wideGaps, moved }
 *
 * Rows: the page's top-level nodes sorted by top edge; a node joins the current
 * row when its top edge is above that row's bottom edge, else it starts a new
 * row. A SECTION is one block; its children are not tidied.
 *
 * MODE 'check' fails on two top-level nodes that overlap, and on a gap wider
 * than GAP between row neighbours or between consecutive rows. It moves nothing.
 *
 * MODE 'tidy' keeps each node's row and its left-to-right order, top-aligns
 * every row, and puts exactly GAP between neighbours and between rows, from the
 * page's current top-left corner. Run it after every write to the page. It
 * moves top-level nodes only; nothing inside a screen or a set changes.
 *
 * Usage: Run via use_figma with the figma-use skill loaded first.
 * Pass skillNames: "figma-hygiene".
 */

const GAP = 100;
const TOL = 0.5;

const page = await figma.getNodeByIdAsync(PAGE_ID);
if (!page || page.type !== 'PAGE') return { error: `Node ${PAGE_ID} is not a page` };
const mode = typeof MODE === 'undefined' ? 'check' : MODE;
if (mode !== 'check' && mode !== 'tidy') return { error: `MODE must be 'check' or 'tidy', got ${mode}` };
await figma.setCurrentPageAsync(page);

const nodes = page.children.slice();
const label = n => ({ id: n.id, name: n.name });

const rows = [];
for (const n of nodes.slice().sort((a, b) => a.y - b.y || a.x - b.x)) {
  const row = rows[rows.length - 1];
  if (row && n.y < row.bottom) {
    row.nodes.push(n);
    row.bottom = Math.max(row.bottom, n.y + n.height);
  } else {
    rows.push({ nodes: [n], top: n.y, bottom: n.y + n.height });
  }
}
for (const row of rows) row.nodes.sort((a, b) => a.x - b.x || a.y - b.y);

const moved = [];
if (mode === 'tidy' && nodes.length) {
  const x0 = Math.round(Math.min(...nodes.map(n => n.x)));
  let y = Math.round(Math.min(...nodes.map(n => n.y)));
  for (const row of rows) {
    let x = x0, h = 0;
    for (const n of row.nodes) {
      if (Math.abs(n.x - x) > TOL || Math.abs(n.y - y) > TOL) {
        moved.push({ ...label(n), from: [Math.round(n.x), Math.round(n.y)], to: [x, y] });
      }
      n.x = x;
      n.y = y;
      x = Math.round(x + n.width + GAP);
      h = Math.max(h, n.height);
    }
    row.top = y;
    row.bottom = y + h;
    y = Math.round(y + h + GAP);
  }
}

const overlaps = [];
for (let i = 0; i < nodes.length; i++) {
  for (let j = i + 1; j < nodes.length; j++) {
    const a = nodes[i], b = nodes[j];
    if (a.x < b.x + b.width - TOL && b.x < a.x + a.width - TOL &&
        a.y < b.y + b.height - TOL && b.y < a.y + a.height - TOL) {
      overlaps.push([label(a), label(b)]);
    }
  }
}

const wideGaps = [];
rows.forEach((row, r) => {
  let right = row.nodes[0].x + row.nodes[0].width;
  for (let i = 1; i < row.nodes.length; i++) {
    const n = row.nodes[i];
    const gap = n.x - right;
    if (gap > GAP + TOL) wideGaps.push({ row: r, before: label(n), gap: Math.round(gap) });
    right = Math.max(right, n.x + n.width);
  }
  if (r > 0) {
    const gap = row.top - rows[r - 1].bottom;
    if (gap > GAP + TOL) wideGaps.push({ row: r, above: true, gap: Math.round(gap) });
  }
});

return {
  pass: overlaps.length === 0 && wideGaps.length === 0,
  mode,
  rows: rows.map(row => row.nodes.map(n => n.name)),
  overlaps,
  wideGaps,
  moved,
};
