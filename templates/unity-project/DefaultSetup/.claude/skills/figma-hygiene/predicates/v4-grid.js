PREDICATES["V-4"] = async function (root, params, ctx) {
  const grid = (ctx.project && ctx.project.grid) || {}
  const frame = grid.frame
  if (frame) {
    const box = root.absoluteBoundingBox
    const w = box ? box.width : root.width
    const h = box ? box.height : root.height
    if (typeof w !== 'number' || typeof h !== 'number') return []
    if (Math.abs(w - frame.width) > 0.5 || Math.abs(h - frame.height) > 0.5) return []
  }
  const id = root.gridStyleId
  if (typeof id !== 'string' || id === '') return [ctx.finding('V-4', root, 'no layout grid style')]
  if (grid.styleId && id !== String(grid.styleId)) {
    return [ctx.finding('V-4', root, 'grid style ' + id + ' is not ' + grid.styleId)]
  }
  return []
}
