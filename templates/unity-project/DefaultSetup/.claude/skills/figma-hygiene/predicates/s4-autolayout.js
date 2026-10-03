PREDICATES["S-4"] = async function (root, params, ctx) {
  const minSiblings = params.minSiblings
  const tol = params.spacingTol
  const parents = []
  walk(root, function (n) {
    if (n !== root && n.type === 'INSTANCE') return false
    if (n.children && n.children.length && (!n.layoutMode || n.layoutMode === 'NONE')) parents.push(n)
  })

  function uniformSize(items, axis) {
    const size = axis === 'x' ? 'width' : 'height'
    const sorted = items.slice().sort(function (a, b) { return a.box[axis] - b.box[axis] })
    let first = null
    for (let i = 0; i < sorted.length - 1; i++) {
      const gap = sorted[i + 1].box[axis] - (sorted[i].box[axis] + sorted[i].box[size])
      if (first === null) first = gap
      else if (Math.abs(gap - first) > tol) return 0
    }
    return sorted.length
  }

  function bestAligned(items, axis) {
    const cross = axis === 'x' ? 'y' : 'x'
    const sorted = items.slice().sort(function (a, b) { return a.box[cross] - b.box[cross] })
    let best = 0
    let i = 0
    while (i < sorted.length) {
      const anchor = sorted[i].box[cross]
      const cluster = []
      let j = i
      while (j < sorted.length && sorted[j].box[cross] - anchor <= tol) cluster.push(sorted[j++])
      best = Math.max(best, uniformSize(cluster, axis))
      i = j
    }
    return best
  }

  const out = []
  for (const parent of parents) {
    const groups = {}
    for (const c of parent.children) {
      if (c.type !== 'INSTANCE' || c.visible === false || !c.absoluteBoundingBox) continue
      const main = await c.getMainComponentAsync()
      if (!main) continue
      const g = groups[main.id] || (groups[main.id] = { name: main.name, items: [] })
      g.items.push({ box: c.absoluteBoundingBox })
    }
    for (const id of Object.keys(groups)) {
      const g = groups[id]
      if (g.items.length < minSiblings) continue
      const nx = bestAligned(g.items, 'x')
      const ny = bestAligned(g.items, 'y')
      const count = Math.max(nx, ny)
      if (count < minSiblings) continue
      out.push(ctx.finding('S-4', parent,
        count + ' × ' + g.name + ' evenly spaced on ' + (nx >= ny ? 'x' : 'y') + '; make the parent auto-layout'))
    }
  }
  return out
}
