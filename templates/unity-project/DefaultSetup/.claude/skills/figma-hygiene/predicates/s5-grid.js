PREDICATES["S-5"] = async function (root, params, ctx) {
  const tol = params.alignTol, minCols = params.minCols, minRows = params.minRows
  const byComp = new Map()
  const pending = []
  walk(root, n => {
    if (n.type !== 'INSTANCE') return
    if (n.visible !== false && n.absoluteBoundingBox) pending.push(n)
    return false
  })
  for (const n of pending) {
    let key = n.name
    if (typeof n.getMainComponentAsync === 'function') {
      const main = await n.getMainComponentAsync()
      if (main) key = main.id
    }
    if (!byComp.has(key)) byComp.set(key, { name: n.name, items: [] })
    byComp.get(key).items.push(n)
  }
  function cluster(values) {
    const sorted = values.slice().sort((a, b) => a - b)
    const centers = []
    let last = null
    for (const v of sorted) {
      if (last !== null && v - last <= tol) { last = v; continue }
      centers.push(v)
      last = v
    }
    return centers
  }
  function indexOf(centers, v) {
    let idx = 0
    for (let i = 0; i < centers.length; i++) if (centers[i] <= v + tol && v >= centers[i]) idx = i
    return idx
  }
  const out = []
  for (const [, comp] of byComp) {
    const items = comp.items
    if (items.length < minCols * minRows) continue
    const xs = cluster(items.map(n => n.absoluteBoundingBox.x))
    const ys = cluster(items.map(n => n.absoluteBoundingBox.y))
    if (xs.length < minCols || ys.length < minRows) continue
    const rows = ys.map(() => new Set())
    const cells = new Map()
    for (const n of items) {
      const c = indexOf(xs, n.absoluteBoundingBox.x)
      const r = indexOf(ys, n.absoluteBoundingBox.y)
      rows[r].add(c)
      const k = r + ':' + c
      if (!cells.has(k)) cells.set(k, [])
      cells.get(k).push(n)
    }
    const colSets = new Map()
    const addSet = s => { if (s.size >= minCols) colSets.set([...s].sort((a, b) => a - b).join(','), s) }
    for (const r of rows) {
      addSet(r)
      for (const s of [...colSets.values()]) addSet(new Set([...s].filter(c => r.has(c))))
    }
    let best = null
    for (const s of colSets.values()) {
      const rs = []
      rows.forEach((r, i) => { if ([...s].every(c => r.has(c))) rs.push(i) })
      if (rs.length < minRows) continue
      if (!best || s.size * rs.length > best.cols.size * best.rows.length) best = { cols: s, rows: rs }
    }
    if (!best) continue
    const members = []
    for (const r of best.rows) for (const c of [...best.cols].sort((a, b) => a - b)) members.push(...cells.get(r + ':' + c))
    const parents = new Set(members.map(n => n.parent))
    if (parents.size <= 1) continue
    out.push(ctx.finding('S-5', members[0],
      members.length + ' × ' + comp.name + ' form a ' + best.cols.size + '×' + best.rows.length +
      ' grid across ' + parents.size + ' parents; put them in one container'))
  }
  return out
}
