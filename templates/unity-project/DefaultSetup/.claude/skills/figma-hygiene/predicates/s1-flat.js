PREDICATES["S-1"] = async function (root, params, ctx) {
  const kids = root.children || []
  if (kids.some(c => c.type === 'FRAME')) return []
  const detail = kids.length
    ? 'no FRAME child; root children are ' + kids.slice(0, 5).map(c => c.type + ':' + c.name).join(', ')
    : 'no children'
  return [ctx.finding('S-1', root, detail)]
}
