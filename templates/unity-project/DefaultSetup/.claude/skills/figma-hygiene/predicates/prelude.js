const PREDICATES = {}
function walk(node, visit, depth = 0) {
  if (visit(node, depth) === false) return
  const kids = node.children
  if (kids) for (const c of kids) walk(c, visit, depth + 1)
}
function isSliceFrame(n) {
  return ['FRAME', 'INSTANCE', 'COMPONENT'].indexOf(n.type) >= 0 && !!n.children && n.children.length > 0
    && n.children.every(c => c.name.indexOf('slice_') === 0)
}
function shapeKind(n, slice, maskedPrefix) {
  if (slice) return 'art'
  if (maskedPrefix && n.name.indexOf(maskedPrefix) === 0) return 'shape'
  const fills = Array.isArray(n.fills) ? n.fills : []
  const strokes = Array.isArray(n.strokes) ? n.strokes : []
  const paints = fills.concat(strokes).filter(p => p.visible !== false)
  if (paints.some(p => p.type === 'IMAGE')) return 'art'
  return paints.length ? 'shape' : null
}
function cornerRadii(n) {
  return [n.topLeftRadius || 0, n.topRightRadius || 0, n.bottomRightRadius || 0, n.bottomLeftRadius || 0]
}
function pathOf(n) {
  const names = []
  for (let p = n.parent; p && p.type !== 'PAGE' && p.type !== 'DOCUMENT'; p = p.parent) names.unshift(p.name)
  const cut = names.length > 6
  return (cut ? '… / ' : '') + names.slice(-6).concat(n.name).join(' / ')
}
function finding(rule, n, detail) {
  return { rule, nodeId: n.id, name: n.name, path: pathOf(n), detail }
}
