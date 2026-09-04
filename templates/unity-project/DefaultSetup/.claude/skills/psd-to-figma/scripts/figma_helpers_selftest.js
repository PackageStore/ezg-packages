// Self-test for figma_helpers.js. Paste figma_helpers.js FIRST, then this file,
// in one use_figma payload. Set PAGE_ID and FONT below before running (the
// caller injects them: PAGE_ID = a page id, FONT = figma.fonts.body from
// psd2figma.json - never hardcode a project font here). It builds
// __helpers_selftest__ on that page, exercises every helper against a synthetic
// solid-colour image, asserts geometry to 0.01px, deletes the frame, and returns
// {pass, failures, created, deleted}.

const PAGE_ID = "__PAGE_ID__";
const FONT = { family: "__FONT_FAMILY__", style: "__FONT_STYLE__" };

function _crc32(bytes) {
  let table = _crc32._t;
  if (!table) {
    table = _crc32._t = [];
    for (let n = 0; n < 256; n++) {
      let c = n;
      for (let k = 0; k < 8; k++) c = (c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1);
      table[n] = c >>> 0;
    }
  }
  let crc = 0xFFFFFFFF;
  for (let i = 0; i < bytes.length; i++) crc = table[(crc ^ bytes[i]) & 0xFF] ^ (crc >>> 8);
  return (crc ^ 0xFFFFFFFF) >>> 0;
}

function _adler32(bytes) {
  let a = 1, b = 0;
  for (let i = 0; i < bytes.length; i++) { a = (a + bytes[i]) % 65521; b = (b + a) % 65521; }
  return ((b << 16) | a) >>> 0;
}

function _be(n) { return [(n >>> 24) & 0xFF, (n >>> 16) & 0xFF, (n >>> 8) & 0xFF, n & 0xFF]; }

function _chunk(type, data) {
  const body = [type.charCodeAt(0), type.charCodeAt(1), type.charCodeAt(2), type.charCodeAt(3)].concat(data);
  return _be(data.length).concat(body).concat(_be(_crc32(body)));
}

function _zlibStore(raw) {
  const out = [0x78, 0x01];
  let i = 0;
  while (i < raw.length) {
    const len = Math.min(65535, raw.length - i);
    out.push((i + len >= raw.length) ? 1 : 0);
    out.push(len & 0xFF, (len >>> 8) & 0xFF);
    const nlen = (~len) & 0xFFFF;
    out.push(nlen & 0xFF, (nlen >>> 8) & 0xFF);
    for (let k = 0; k < len; k++) out.push(raw[i + k]);
    i += len;
  }
  const ad = _adler32(raw);
  return out.concat(_be(ad));
}

function _solidPng(w, h, rgb) {
  const sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
  const ihdr = _be(w).concat(_be(h)).concat([8, 2, 0, 0, 0]);
  const raw = [];
  for (let y = 0; y < h; y++) {
    raw.push(0);
    for (let x = 0; x < w; x++) raw.push(rgb[0], rgb[1], rgb[2]);
  }
  const bytes = sig
    .concat(_chunk('IHDR', ihdr))
    .concat(_chunk('IDAT', _zlibStore(raw)))
    .concat(_chunk('IEND', []));
  return new Uint8Array(bytes);
}

async function runSelfTest() {
  const failures = [];
  const near = (label, got, want, tol) => {
    if (!(Math.abs(got - want) <= (tol == null ? 0.01 : tol))) {
      failures.push(label + ' got ' + got + ' want ' + want);
    }
  };

  const img = figma.createImage(_solidPng(120, 80, [200, 60, 60]));
  const HASH = img.hash;

  const page = await figma.getNodeByIdAsync(PAGE_ID);
  await figma.setCurrentPageAsync(page);
  let clearX = 0;
  for (const c of page.children) clearX = Math.max(clearX, c.x + c.width);
  clearX += 400;
  const probe = figma.createFrame();
  probe.name = '__helpers_selftest__';
  probe.resizeWithoutConstraints(1400, 1000);
  probe.fills = [];
  page.appendChild(probe);
  probe.x = clearX; probe.y = 0;
  const created = [probe.id];

  // nineSliceFrame 3x3
  const ns = await nineSliceFrame(probe, 'ns3', HASH, 120, 80, [24, 16, 30, 20], {});
  ns.x = 0; ns.y = 0;
  const colX = [0, 24, 90], colW = [24, 66, 30];
  const rowY = [0, 16, 60], rowH = [16, 44, 20];
  if (ns.children.length !== 9) failures.push('ns3 cell count ' + ns.children.length);
  if (ns.clipsContent !== true) failures.push('ns3 clipsContent not true');
  if (ns.fills.length !== 0) failures.push('ns3 frame has fill');
  for (let i = 0; i < 3; i++) for (let j = 0; j < 3; j++) {
    const cell = ns.children.find(c => c.name === 'slice_' + i + '_' + j);
    if (!cell) { failures.push('ns3 missing slice_' + i + '_' + j); continue; }
    near('ns3 ' + i + '_' + j + '.x', cell.x, colX[j]);
    near('ns3 ' + i + '_' + j + '.y', cell.y, rowY[i]);
    near('ns3 ' + i + '_' + j + '.w', cell.width, colW[j]);
    near('ns3 ' + i + '_' + j + '.h', cell.height, rowH[i]);
    const m = cell.fills[0].imageTransform;
    near('ns3 ' + i + '_' + j + ' it.sw', m[0][0], colW[j] / 120);
    near('ns3 ' + i + '_' + j + ' it.sx', m[0][2], colX[j] / 120);
    near('ns3 ' + i + '_' + j + ' it.sh', m[1][1], rowH[i] / 80);
    near('ns3 ' + i + '_' + j + ' it.sy', m[1][2], rowY[i] / 80);
  }
  const cst = (n, hz, vt) => {
    const c = ns.children.find(x => x.name === n);
    if (!c || c.constraints.horizontal !== hz || c.constraints.vertical !== vt) {
      failures.push('ns3 ' + n + ' constraints ' + (c ? JSON.stringify(c.constraints) : 'missing'));
    }
  };
  cst('slice_0_0', 'MIN', 'MIN');
  cst('slice_1_1', 'STRETCH', 'STRETCH');
  cst('slice_2_2', 'MAX', 'MAX');

  // both-zero row axis -> one full-span row, 3 columns
  const ns2 = await nineSliceFrame(probe, 'ns1x3', HASH, 100, 40, [20, 0, 20, 0], { srcW: 100, srcH: 40 });
  ns2.x = 200; ns2.y = 0;
  if (ns2.children.length !== 3) failures.push('ns1x3 cell count ' + ns2.children.length);
  const s01 = ns2.children.find(c => c.name === 'slice_0_1');
  if (s01) {
    near('ns1x3 slice_0_1.h', s01.height, 40);
    if (s01.constraints.vertical !== 'STRETCH') failures.push('ns1x3 slice_0_1 vertical ' + s01.constraints.vertical);
    if (s01.constraints.horizontal !== 'STRETCH') failures.push('ns1x3 slice_0_1 horizontal ' + s01.constraints.horizontal);
  } else failures.push('ns1x3 missing slice_0_1');

  // P-8: borders exceeding the axis throw
  let threw8 = false;
  try { await nineSliceFrame(probe, 'nsbad', HASH, 100, 80, [60, 10, 60, 10], {}); } catch (e) { threw8 = true; }
  if (!threw8) failures.push('nineSliceFrame did not throw when left+right >= w');

  // rectHash CROP identity
  const rh = await rectHash(probe, 'rh', HASH, 300, 200, 30, 20, { scaleMode: 'CROP' });
  near('rh.x', rh.x, 300); near('rh.y', rh.y, 200); near('rh.w', rh.width, 30); near('rh.h', rh.height, 20);
  const rf = rh.fills[0];
  if (rf.scaleMode !== 'CROP') failures.push('rh scaleMode ' + rf.scaleMode);
  const rm = rf.imageTransform;
  near('rh it[0][0]', rm[0][0], 1); near('rh it[0][2]', rm[0][2], 0);
  near('rh it[1][1]', rm[1][1], 1); near('rh it[1][2]', rm[1][2], 0);
  let threwRH = false;
  try { await rectHash(probe, 'rhbad', 'deadbeefdeadbeefdeadbeefdeadbeefdeadbeef', 0, 0, 10, 10, {}); } catch (e) { threwRH = true; }
  if (!threwRH) failures.push('rectHash did not throw on unknown hash');

  // instanceAt
  const comp = figma.createComponent();
  comp.name = 'SelftestComp';
  comp.resizeWithoutConstraints(40, 30);
  comp.fills = [{ type: 'SOLID', color: { r: 0.2, g: 0.6, b: 0.9 } }];
  probe.appendChild(comp);
  comp.x = 400; comp.y = 400;
  created.push(comp.id);
  const inst = await instanceAt(probe, comp.id, 'inst', 500, 400, 80, 60);
  near('inst.x', inst.x, 500); near('inst.y', inst.y, 400);
  near('inst.w', inst.width, 80); near('inst.h', inst.height, 60);
  comp.description = 'natural-size-only';
  let threwNS = false;
  try { await instanceAt(probe, comp.id, 'inst2', 0, 0, 80, 60); } catch (e) { threwNS = true; }
  if (!threwNS) failures.push('instanceAt did not throw for natural-size-only');
  comp.description = '';
  const instN = await instanceAt(probe, comp.id, 'instN', 600, 400, null, null);
  near('instN.w', instN.width, 40); near('instN.h', instN.height, 30);

  // textByInk with stroke + zero-radius shadow (near edges = stroke only)
  const recipe = {
    fontSize: 40, fill: '#FFFFFF', strokeWeight: 3, strokeAlign: 'OUTSIDE',
    strokes: [{ type: 'SOLID', color: '#1A1A1A' }],
    effects: [{ type: 'DROP_SHADOW', color: '#1A1A1A', offset: { x: 0, y: 5 }, radius: 0, spread: 0, visible: true }],
    font: FONT,
  };
  const tr = await textByInk(probe, 'txt', 'Ag', '', recipe, { x: 120, y: 250 }, {});
  near('textByInk dx', tr.dx, 0, 0.01);
  near('textByInk dy', tr.dy, 0, 0.01);

  // reparentKeepWorld
  const fA = figma.createFrame(); fA.name = 'A'; fA.resizeWithoutConstraints(100, 100); fA.fills = []; probe.appendChild(fA); fA.x = 50; fA.y = 500;
  const fB = figma.createFrame(); fB.name = 'B'; fB.resizeWithoutConstraints(100, 100); fB.fills = []; probe.appendChild(fB); fB.x = 800; fB.y = 500;
  const mover = figma.createRectangle(); mover.name = 'mover'; mover.resizeWithoutConstraints(20, 20); mover.fills = [{ type: 'SOLID', color: { r: 1, g: 0, b: 0 } }];
  fA.appendChild(mover); mover.x = 10; mover.y = 10;
  const bw = mover.absoluteTransform;
  await reparentKeepWorld(mover, fB);
  const aw = mover.absoluteTransform;
  near('reparent world x', aw[0][2], bw[0][2]);
  near('reparent world y', aw[1][2], bw[1][2]);
  if (!mover.parent || mover.parent.id !== fB.id) failures.push('reparent parent not fB');

  // readGeometry
  const rg = figma.createRectangle(); rg.name = 'geo'; rg.resizeWithoutConstraints(33, 22); rg.fills = [{ type: 'SOLID', color: { r: 0, g: 1, b: 0 } }];
  probe.appendChild(rg); rg.x = 900; rg.y = 100;
  const g = readGeometry(rg);
  near('geo.x', g.x, 900); near('geo.y', g.y, 100); near('geo.w', g.w, 33); near('geo.h', g.h, 22);
  if (g.id !== rg.id) failures.push('geo id mismatch');
  if (!g.renderBounds) failures.push('geo renderBounds null');
  else { near('geo.rb.x', g.renderBounds.x, 900); near('geo.rb.y', g.renderBounds.y, 100); }

  // deleteByIds
  const tmp = figma.createRectangle(); tmp.name = 'tmp'; tmp.resizeWithoutConstraints(5, 5); probe.appendChild(tmp);
  const tmpId = tmp.id;
  const notFound = await deleteByIds([tmpId, '0:999999']);
  const stillThere = await figma.getNodeByIdAsync(tmpId);
  if (stillThere && !stillThere.removed) failures.push('deleteByIds left the node');
  if (notFound.indexOf('0:999999') < 0) failures.push('deleteByIds did not report missing id');
  if (notFound.indexOf(tmpId) >= 0) failures.push('deleteByIds wrongly reported deleted id as missing');

  const probeId = probe.id;
  probe.remove();
  const deleted = [probeId];
  const gone = await figma.getNodeByIdAsync(probeId);
  if (gone && !gone.removed) failures.push('probe frame not deleted');

  return { pass: failures.length === 0, failures: failures, created: created, deleted: deleted };
}

return await runSelfTest();
