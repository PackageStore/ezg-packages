PREDICATES["V-5"] = async function (root, params, ctx) {
  const tol = params.tol, containTol = params.containTol, prefix = params.exemptMaskedPrefix
  const types = ['FRAME', 'RECTANGLE', 'COMPONENT', 'INSTANCE']
  const shapes = []
  const collect = function (nd, masked, hidden) {
    const slice = isSliceFrame(nd)
    hidden = hidden || nd.visible === false
    if (types.indexOf(nd.type) >= 0 && !hidden && !nd.rotation && nd.absoluteBoundingBox) {
      const kind = shapeKind(nd, slice, prefix)
      const b = nd.absoluteBoundingBox
      if (kind) shapes.push({ node: nd, kind, box: [b.x, b.y, b.width, b.height], radii: cornerRadii(nd), masked })
    }
    if (nd.children) {
      const m = masked || (!!prefix && nd.name.indexOf(prefix) === 0)
      for (const c of nd.children) collect(c, m, hidden)
    }
  }
  for (const c of root.children || []) collect(c, false, false)

  const out = []
  shapes.forEach(function (c, i) {
    if (c.kind !== 'shape' || c.masked) return
    const cx = c.box[0], cy = c.box[1], cw = c.box[2], chh = c.box[3]
    let outer = null
    for (const o of shapes.slice(0, i)) {
      if (o.box[0] - containTol <= cx && o.box[1] - containTol <= cy
        && cx + cw <= o.box[0] + o.box[2] + containTol && cy + chh <= o.box[1] + o.box[3] + containTol
        && (!outer || o.box[2] * o.box[3] <= outer.box[2] * outer.box[3]))
        outer = o
    }
    if (!outer || outer.kind !== 'shape') return
    const ox = outer.box[0], oy = outer.box[1], ow = outer.box[2], oh = outer.box[3]
    const left = cx - ox, top = cy - oy, right = ox + ow - cx - cw, bottom = oy + oh - cy - chh
    let worst = null
    ;[[left, top], [right, top], [right, bottom], [left, bottom]].forEach(function (d2, k) {
      const dx = d2[0], dy = d2[1]
      const ro = Math.min(outer.radii[k], Math.min(ow, oh) / 2)
      const d = (dx + dy) / 2
      if (ro <= 0 || Math.abs(dx - dy) > tol || d >= ro) return
      const expected = Math.min(ro - d, Math.min(cw, chh) / 2)
      const actual = Math.min(c.radii[k], Math.min(cw, chh) / 2)
      if (Math.abs(actual - expected) > tol && (!worst || Math.abs(actual - expected) > Math.abs(worst[0] - worst[1])))
        worst = [actual, expected]
    })
    if (worst) {
      const r = function (v) { return Math.round(v * 100) / 100 }
      out.push(ctx.finding('V-5', c.node, 'radius ' + r(worst[0]) + ' != ' + r(worst[1]) + ' (outer ' + outer.node.name + ')'))
    }
  })
  return out
}
