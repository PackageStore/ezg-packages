PREDICATES["S-8"] = async function (root, params, ctx) {
  const out = []
  const grid = (ctx.project && ctx.project.grid) || {}
  const edges = grid.columnEdges || []
  const stops = edges.concat(edges.map(e => e + (grid.columnWidth || 0)))
  const tol = params.edgeTol || 0
  const pattern = new RegExp(params.containerPattern)
  const rb = root.absoluteBoundingBox
  const onGrid = x => stops.some(s => Math.abs(s - x) <= tol)
  // House safe zone (top 100 px, bottom 60 px); project.json grid.safeTop / grid.safeBottom override it, 0 turns a side off.
  const safeTop = typeof grid.safeTop === "number" ? grid.safeTop : 100
  const safeBottom = typeof grid.safeBottom === "number" ? grid.safeBottom : 60
  const round = v => Math.round(v * 100) / 100
  for (const c of root.children || []) {
    if (!pattern.test(c.name)) continue
    const b = c.absoluteBoundingBox
    if (!b || !rb) continue
    const left = b.x - rb.x
    const right = left + b.width
    const top = b.y - rb.y
    const bottom = top + b.height
    const limit = rb.height - safeBottom
    if (!onGrid(left)) out.push(ctx.finding("S-8", c, "left edge " + round(left) + " is off the column edges"))
    if (!onGrid(right)) out.push(ctx.finding("S-8", c, "right edge " + round(right) + " is off the column edges"))
    if (top < safeTop) out.push(ctx.finding("S-8", c, "top edge " + round(top) + " is above the top safe zone limit " + round(safeTop)))
    if (bottom > limit) out.push(ctx.finding("S-8", c, "bottom edge " + round(bottom) + " is below the safe zone limit " + round(limit)))
    const k = c.constraints
    if (!k || k.horizontal !== "CENTER" || k.vertical !== "CENTER") {
      out.push(ctx.finding("S-8", c, "constraints " + (k ? k.horizontal + "/" + k.vertical : "missing") + ", expected CENTER/CENTER"))
    }
    if (c.clipsContent === true) out.push(ctx.finding("S-8", c, "clipsContent is on"))
  }
  return out
}
