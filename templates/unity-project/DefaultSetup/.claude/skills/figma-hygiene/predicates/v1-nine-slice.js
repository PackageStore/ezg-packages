PREDICATES["V-1"] = async function (root, params, ctx) {
  const registry = (ctx && ctx.project && ctx.project.nineSlice) || {}
  const results = []
  let byId = null
  for (const stem of Object.keys(registry)) {
    const entry = registry[stem]
    const raw = entry && entry.applied && entry.applied.node
    if (typeof raw !== 'string') continue
    for (const part of raw.split(',')) {
      const m = /(\S+)\s*$/.exec(part)
      if (!m || !/^I?\d+:\d+(;\d+:\d+)*$/.test(m[1])) continue
      if (!byId) {
        byId = {}
        walk(root, n => { if (n.id) byId[n.id] = n })
      }
      const node = byId[m[1]]
      if (node && !isSliceFrame(node)) {
        results.push(ctx.finding('V-1', node, 'registry stem ' + stem + ' is applied here but the node is not 9-slice'))
      }
    }
  }
  return results
}
