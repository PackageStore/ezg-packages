PREDICATES["S-3"] = async function (root, params, ctx) {
  const prefixes = params.allowedPrefixes || []
  const exact = params.exemptNames || []
  const contains = params.exemptContains || []
  const out = []
  walk(root, function (n) {
    if (n !== root && n.type === 'INSTANCE') return false
    if (n === root || n.type !== 'FRAME' || isSliceFrame(n)) return
    if (!n.children || n.children.length < params.minChildren) return
    const ok = prefixes.some(p => n.name.indexOf(p) === 0)
      || exact.indexOf(n.name) >= 0
      || contains.some(s => n.name.indexOf(s) >= 0)
    if (!ok) {
      out.push(ctx.finding('S-3', n, 'grouping frame "' + n.name + '" has ' + n.children.length
        + ' children; name it ' + (prefixes[0] || '') + 'Content'))
    }
  })
  return out
}
