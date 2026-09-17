// ==== INJECTED PLAN ==========================================================
// figma_build_gen.py replaces the placeholder line below with the PLAN object
// read from a build plan JSON (reference/build-plan.md), then prints the
// runnable script. The script is pasted into the Figma Plugin API sandbox via
// the use_figma MCP tool, after figma_helpers.js. Do not paste this template
// as-is; generate it first. Nothing here knows where the plan came from.
/*__PLAN__*/
// ==== END INJECTED PLAN =====================================================
{
  const _allow = new Set(PLAN.allowWarnings || []);
  const _blocked = (PLAN.warnings || []).filter(w =>
    (w.code === 'NO_STYLE' || w.code === 'NO_HASH') && !_allow.has(w.code)
  );
  if (_blocked.length > 0) {
    throw new Error('figma_build: ' + _blocked.length + ' blocking warning(s): ' +
      _blocked.slice(0, 5).map(w => w.code + ' ' + (w.layerKey || '')).join('; '));
  }

  const _page = figma.root.children.find(p => p.name === PLAN.pageName);
  if (!_page) throw new Error('figma_build: page not found: ' + PLAN.pageName);
  await figma.setCurrentPageAsync(_page);

  const _existing = _page.children.find(c => c.name === PLAN.frameName);
  let _replacedId = null;
  if (_existing) {
    if (PLAN.replace === true) {
      _replacedId = _existing.id;
      _existing.remove();
    } else {
      throw new Error('figma_build: frame already exists: ' + PLAN.frameName +
        ' (' + _existing.id + '); set replace=true to overwrite');
    }
  }

  const _frame = figma.createFrame();
  _frame.name = PLAN.frameName;
  _frame.resizeWithoutConstraints(PLAN.frame.w, PLAN.frame.h);
  if (PLAN.frame.x != null) _frame.x = PLAN.frame.x;
  if (PLAN.frame.y != null) _frame.y = PLAN.frame.y;
  _frame.clipsContent = true;
  _frame.fills = [];
  if (PLAN.gridStyleId) await _frame.setGridStyleIdAsync(PLAN.gridStyleId);
  _page.appendChild(_frame);

  const _byId = {};
  const _absX = {};
  const _absY = {};
  const _opsResult = {};
  const _layerIds = {};
  const _textDeltas = {};
  const _warnings = [];
  const _font = PLAN.font || null;

  for (const op of PLAN.ops) {
    const parent = op.parent ? _byId[op.parent] : _frame;
    if (!parent) throw new Error('figma_build: parent not resolved: ' + op.parent + ' for op ' + op.id);
    const pax = op.parent ? (_absX[op.parent] || 0) : 0;
    const pay = op.parent ? (_absY[op.parent] || 0) : 0;
    let node;

    switch (op.op) {
      case 'container': {
        const f = figma.createFrame();
        f.name = op.name;
        f.resizeWithoutConstraints(op.w, op.h);
        f.clipsContent = op.clip === true;
        f.fills = [];
        parent.appendChild(f);
        f.x = op.x;
        f.y = op.y;
        _absX[op.id] = pax + op.x;
        _absY[op.id] = pay + op.y;
        node = f;
        break;
      }
      case 'rect': {
        node = await rectHash(parent, op.name, op.hash, op.x, op.y, op.w, op.h);
        if (op.opacity != null && op.opacity !== 1) node.opacity = op.opacity;
        break;
      }
      case 'nineSlice': {
        const nsOpts = {};
        if (op.srcW) nsOpts.srcW = op.srcW;
        if (op.srcH) nsOpts.srcH = op.srcH;
        if (op.opacity != null && op.opacity !== 1) nsOpts.opacity = op.opacity;
        node = await nineSliceFrame(parent, op.name, op.hash, op.w, op.h, op.border, nsOpts);
        node.x = op.x;
        node.y = op.y;
        break;
      }
      case 'instance': {
        const _comp = await figma.getNodeByIdAsync(op.componentId);
        const _nat = _comp && (_comp.description || '').indexOf('natural-size-only') >= 0;
        node = await instanceAt(parent, op.componentId, op.name, op.x, op.y,
          _nat ? null : op.w, _nat ? null : op.h);
        if (op.opacity != null && op.opacity !== 1) node.opacity = op.opacity;
        break;
      }
      case 'text': {
        if (!op.chars) {
          _warnings.push('EMPTY_TEXT ' + (op.name || op.id));
          continue;
        }
        const _ink = { x: op.ink.x - pax, y: op.ink.y - pay, w: op.ink.w, h: op.ink.h };
        const _res = await textByInk(parent, op.name, op.chars, op.styleId, op.recipe, _ink, { font: _font });
        node = _res.node;
        _textDeltas[op.id] = { dx: _res.dx, dy: _res.dy };
        if (_res.renderBoundsFallback) _warnings.push('RENDER_BOUNDS_FALLBACK ' + op.name);
        if (op.opacity != null && op.opacity !== 1) node.opacity = op.opacity;
        break;
      }
      default:
        throw new Error('figma_build: unknown op type: ' + op.op);
    }

    _byId[op.id] = node;
    _opsResult[op.id] = node.id;
    if (op.layerKey) _layerIds[op.layerKey] = node.id;
  }

  return {
    key: PLAN.key,
    frameId: _frame.id,
    replacedId: _replacedId,
    ops: _opsResult,
    layerIds: _layerIds,
    textDeltas: _textDeltas,
    warnings: _warnings,
  };
}
