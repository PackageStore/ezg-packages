import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'

const predicatesDir = join(dirname(fileURLToPath(import.meta.url)), '..', 'predicates')

export function loadPredicates(...files) {
  const src = ['prelude.js', ...files].map(f => readFileSync(join(predicatesDir, f), 'utf8')).join('\n')
  return new Function(src + '\nreturn {PREDICATES, helpers: {walk, isSliceFrame, shapeKind, cornerRadii, pathOf, finding}}')()
}

let counter = 0

export function tree(spec, parent = null) {
  const { box, children, ...rest } = spec
  const node = { visible: true, ...rest }
  if (!node.id) node.id = 'n' + ++counter
  node.parent = parent
  if (box) node.absoluteBoundingBox = { x: box[0], y: box[1], width: box[2], height: box[3] }
  if (children) node.children = children.map(c => tree(c, node))
  return node
}

export function ctxFor(project = {}) {
  return { project, finding: (rule, node, detail) => loadPredicates().helpers.finding(rule, node, detail) }
}
