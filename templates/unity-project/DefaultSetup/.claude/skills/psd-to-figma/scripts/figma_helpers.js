// Plugin API helpers for use_figma builds. Paste this whole file ahead of your
// build script in one use_figma payload, then call the async functions below.
// Plain sandbox JS: no imports, no top-level side effects, every helper async.
// Nothing project-specific lives here - ids, hashes, borders and recipes are
// arguments. See reference/plugin-helpers.md for the trap each helper prevents.

const _loadedFonts = new Set();

async function _imageExists(hash) {
  try {
    return !!figma.getImageByHash(hash);
  } catch (e) {
    return !!(await figma.getImageByHashAsync(hash));
  }
}

async function _loadFont(font) {
  const key = font.family + '\t' + font.style;
  if (_loadedFonts.has(key)) return;
  await figma.loadFontAsync(font);
  _loadedFonts.add(key);
}

function _hexRgb(hex) {
  const s = String(hex).replace('#', '');
  return {
    r: parseInt(s.slice(0, 2), 16) / 255,
    g: parseInt(s.slice(2, 4), 16) / 255,
    b: parseInt(s.slice(4, 6), 16) / 255,
  };
}

function _outsideStroke(weight, align) {
  if (!weight) return 0;
  if (align === 'CENTER') return weight / 2;
  if (align === 'INSIDE') return 0;
  return weight;
}

function _normBorder(border) {
  if (Array.isArray(border)) {
    return { l: border[0], t: border[1], r: border[2], b: border[3] };
  }
  return { l: border.left, t: border.top, r: border.right, b: border.bottom };
}

// Bands that tile an axis: [0..near / near..dim-far / dim-far..dim]. Both sides
// zero collapses to one full-span STRETCH band; a single zero side drops its
// degenerate corner cell rather than emitting a zero-extent rectangle.
function _bands(dim, near, far) {
  if (near === 0 && far === 0) return [{ a: 0, b: dim, c: 'STRETCH' }];
  const out = [];
  if (near > 0) out.push({ a: 0, b: near, c: 'MIN' });
  out.push({ a: near, b: dim - far, c: 'STRETCH' });
  if (far > 0) out.push({ a: dim - far, b: dim, c: 'MAX' });
  return out;
}

function _toEffect(e) {
  const c = _hexRgb(e.color || '#000000');
  const a = e.opacity == null ? 1 : e.opacity;
  return {
    type: e.type,
    color: { r: c.r, g: c.g, b: c.b, a: a },
    offset: e.offset || { x: 0, y: 0 },
    radius: e.radius == null ? 0 : e.radius,
    spread: e.spread == null ? 0 : e.spread,
    visible: e.visible !== false,
    blendMode: e.blendMode || 'NORMAL',
  };
}

async function nineSliceFrame(parent, name, hash, w, h, border, opts) {
  opts = opts || {};
  if (!(await _imageExists(hash))) {
    throw new Error('nineSliceFrame: image not found for hash ' + hash);
  }
  const bd = _normBorder(border);
  if (bd.l + bd.r >= w) {
    throw new Error('nineSliceFrame: left+right (' + bd.l + '+' + bd.r + ') >= w (' + w + ')');
  }
  if (bd.t + bd.b >= h) {
    throw new Error('nineSliceFrame: top+bottom (' + bd.t + '+' + bd.b + ') >= h (' + h + ')');
  }
  const srcW = opts.srcW || w;
  const srcH = opts.srcH || h;

  const frame = figma.createFrame();
  frame.name = name;
  frame.resizeWithoutConstraints(w, h);
  frame.clipsContent = true;
  frame.fills = [];
  frame.strokes = [];
  if (opts.opacity != null) frame.opacity = opts.opacity;
  parent.appendChild(frame);

  const colF = _bands(w, bd.l, bd.r);
  const rowF = _bands(h, bd.t, bd.b);
  const colS = _bands(srcW, bd.l, bd.r);
  const rowS = _bands(srcH, bd.t, bd.b);

  for (let i = 0; i < rowF.length; i++) {
    for (let j = 0; j < colF.length; j++) {
      const cw = colF[j].b - colF[j].a;
      const ch = rowF[i].b - rowF[i].a;
      const sx = colS[j].a / srcW, sw = (colS[j].b - colS[j].a) / srcW;
      const sy = rowS[i].a / srcH, sh = (rowS[i].b - rowS[i].a) / srcH;
      const rect = figma.createRectangle();
      rect.name = 'slice_' + i + '_' + j;
      rect.resizeWithoutConstraints(cw, ch);
      rect.fills = [{
        type: 'IMAGE', imageHash: hash, scaleMode: 'CROP',
        imageTransform: [[sw, 0, sx], [0, sh, sy]],
      }];
      frame.appendChild(rect);
      rect.x = colF[j].a;
      rect.y = rowF[i].a;
      rect.constraints = { horizontal: colF[j].c, vertical: rowF[i].c };
    }
  }
  return frame;
}

async function rectHash(parent, name, hash, x, y, w, h, opts) {
  opts = opts || {};
  if (!(await _imageExists(hash))) {
    throw new Error('rectHash: image not found for hash ' + hash);
  }
  const mode = opts.scaleMode || 'FILL';
  const fill = { type: 'IMAGE', imageHash: hash, scaleMode: mode };
  if (mode === 'CROP') fill.imageTransform = [[1, 0, 0], [0, 1, 0]];
  const rect = figma.createRectangle();
  rect.name = name;
  rect.resizeWithoutConstraints(w, h);
  rect.fills = [fill];
  parent.appendChild(rect);
  rect.x = x;
  rect.y = y;
  return rect;
}

async function instanceAt(parent, componentId, name, x, y, w, h) {
  const comp = await figma.getNodeByIdAsync(componentId);
  if (!comp) throw new Error('instanceAt: component not found: ' + componentId);
  if (comp.type === 'COMPONENT_SET') {
    throw new Error('instanceAt: id is a COMPONENT_SET; pass a variant COMPONENT id: ' + componentId);
  }
  if (comp.type !== 'COMPONENT') {
    throw new Error('instanceAt: node is not a COMPONENT (' + comp.type + '): ' + componentId);
  }
  const inst = comp.createInstance();
  inst.name = name;
  parent.appendChild(inst);
  if (w != null && h != null) {
    const desc = comp.description || '';
    if (desc.indexOf('natural-size-only') >= 0) {
      inst.remove();
      throw new Error('instanceAt: ' + name + ' is natural-size-only; do not pass w,h (component ' + componentId + ')');
    }
    if (Math.round(inst.width) !== Math.round(w) || Math.round(inst.height) !== Math.round(h)) {
      inst.resize(w, h);
    }
  }
  inst.x = x;
  inst.y = y;
  return inst;
}

async function textByInk(parent, name, chars, styleId, recipe, inkBox, opts) {
  opts = opts || {};
  const font = opts.font || recipe.font;
  if (!font) throw new Error('textByInk: no font; pass opts.font or recipe.font as {family, style}');
  await _loadFont(font);

  const t = figma.createText();
  t.name = name;
  t.fontName = font;
  t.characters = String(chars);
  if (styleId) await t.setTextStyleIdAsync(styleId);
  if (recipe.fontSize != null) t.fontSize = recipe.fontSize;
  t.textAutoResize = 'WIDTH_AND_HEIGHT';
  if (opts.align) t.textAlignHorizontal = opts.align;
  if (recipe.fill) t.fills = [{ type: 'SOLID', color: _hexRgb(recipe.fill) }];
  if (recipe.strokeWeight) {
    t.strokeWeight = recipe.strokeWeight;
    t.strokeAlign = recipe.strokeAlign || 'OUTSIDE';
    if (recipe.strokes) {
      t.strokes = recipe.strokes.map(s => ({ type: 'SOLID', color: _hexRgb(s.color) }));
    }
  }
  if (recipe.effects) {
    t.effects = recipe.effects.filter(e => e.visible !== false).map(_toEffect);
  }
  parent.appendChild(t);

  const outside = _outsideStroke(recipe.strokeWeight, recipe.strokeAlign || 'OUTSIDE');
  const p = parent.absoluteTransform;
  const targetX = p[0][2] + inkBox.x;
  const targetY = p[1][2] + inkBox.y;

  let dx = 0, dy = 0;
  for (let iter = 0; iter < 4; iter++) {
    const rb = t.absoluteRenderBounds;
    if (!rb) throw new Error('textByInk: absoluteRenderBounds null (empty text or invisible fill): ' + name);
    dx = (rb.x + outside) - targetX;
    dy = (rb.y + outside) - targetY;
    if (Math.abs(dx) <= 0.005 && Math.abs(dy) <= 0.005) break;
    t.x = t.x - dx;
    t.y = t.y - dy;
  }
  return { node: t, dx: dx, dy: dy };
}

async function reparentKeepWorld(node, parent) {
  const t = node.absoluteTransform;
  const wx = t[0][2], wy = t[1][2];
  parent.appendChild(node);
  const p = parent.absoluteTransform;
  node.x = wx - p[0][2];
  node.y = wy - p[1][2];
  return node;
}

async function deleteByIds(ids) {
  const notFound = [];
  for (const id of ids) {
    const n = await figma.getNodeByIdAsync(id);
    if (!n || n.removed) { notFound.push(id); continue; }
    n.remove();
  }
  return notFound;
}

function readGeometry(node) {
  const parent = node.parent;
  let ox = 0, oy = 0;
  if (parent && 'absoluteTransform' in parent) {
    ox = parent.absoluteTransform[0][2];
    oy = parent.absoluteTransform[1][2];
  }
  const rb = node.absoluteRenderBounds;
  return {
    id: node.id,
    name: node.name,
    x: node.x,
    y: node.y,
    w: node.width,
    h: node.height,
    renderBounds: rb ? { x: rb.x - ox, y: rb.y - oy, w: rb.width, h: rb.height } : null,
  };
}
