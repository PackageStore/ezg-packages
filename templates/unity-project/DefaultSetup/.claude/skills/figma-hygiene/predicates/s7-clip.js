PREDICATES["S-7"] = async function (root, params, ctx) {
  const allow = new RegExp(params.allowPattern)
  const out = []
  walk(root, function (n) {
    if (n === root || n.clipsContent !== true) return
    if (isSliceFrame(n) || allow.test(n.name)) return
    let host = null
    for (let p = n.parent; p && p !== root; p = p.parent) {
      if (p.type === 'INSTANCE') { host = p; break }
    }
    out.push(ctx.finding('S-7', n, host ? 'clips its content (in instance ' + host.name + ')' : 'clips its content'))
  })
  return out
}
