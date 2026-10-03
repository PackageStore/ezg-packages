import test from 'node:test'
import assert from 'node:assert/strict'
import { loadPredicates, tree, ctxFor } from './harness.mjs'

const { PREDICATES, helpers } = loadPredicates()
const { walk, isSliceFrame, shapeKind, cornerRadii, pathOf, finding } = helpers

test('PREDICATES starts empty', () => assert.deepEqual(PREDICATES, {}))

test('walk is pre-order and honors false', () => {
  const t = tree({ type: 'FRAME', name: 'a', children: [
    { type: 'FRAME', name: 'b', children: [{ type: 'TEXT', name: 'c' }] },
    { type: 'TEXT', name: 'd' }] })
  const seen = []
  walk(t, (n, d) => { seen.push(n.name + d) })
  assert.deepEqual(seen, ['a0', 'b1', 'c2', 'd1'])
  const skipped = []
  walk(t, n => { skipped.push(n.name); return n.name !== 'b' })
  assert.deepEqual(skipped, ['a', 'b', 'd'])
})

test('walk visits hidden nodes', () => {
  const t = tree({ type: 'FRAME', name: 'a', children: [{ type: 'FRAME', name: 'h', visible: false }] })
  const seen = []
  walk(t, n => { seen.push(n.name) })
  assert.deepEqual(seen, ['a', 'h'])
})

test('isSliceFrame', () => {
  const slices = [{ type: 'RECTANGLE', name: 'slice_0_0' }, { type: 'RECTANGLE', name: 'slice_0_1' }]
  assert.ok(isSliceFrame(tree({ type: 'FRAME', name: 'p', children: slices })))
  assert.ok(isSliceFrame(tree({ type: 'INSTANCE', name: 'p', children: slices })))
  assert.ok(isSliceFrame(tree({ type: 'COMPONENT', name: 'p', children: slices })))
  assert.ok(!isSliceFrame(tree({ type: 'GROUP', name: 'p', children: slices })))
  assert.ok(!isSliceFrame(tree({ type: 'FRAME', name: 'p', children: [] })))
  assert.ok(!isSliceFrame(tree({ type: 'FRAME', name: 'p' })))
  assert.ok(!isSliceFrame(tree({ type: 'FRAME', name: 'p', children: [...slices, { type: 'TEXT', name: 'x' }] })))
})

test('shapeKind', () => {
  const n = spec => tree({ type: 'RECTANGLE', name: 'r', ...spec })
  assert.equal(shapeKind(n({}), true, 'Mask-'), 'art')
  assert.equal(shapeKind(n({ name: 'Mask-x' }), false, 'Mask-'), 'shape')
  assert.equal(shapeKind(n({ name: 'Mask-x' }), false, undefined), null)
  assert.equal(shapeKind(n({ fills: [{ type: 'IMAGE' }] }), false, 'Mask-'), 'art')
  assert.equal(shapeKind(n({ fills: [{ type: 'SOLID' }] }), false, 'Mask-'), 'shape')
  assert.equal(shapeKind(n({ strokes: [{ type: 'SOLID' }] }), false, 'Mask-'), 'shape')
  assert.equal(shapeKind(n({ fills: [{ type: 'IMAGE', visible: false }] }), false, 'Mask-'), null)
  assert.equal(shapeKind(n({ fills: [] }), false, 'Mask-'), null)
  assert.equal(shapeKind(n({ fills: Symbol.for('mixed') }), false, 'Mask-'), null)
})

test('cornerRadii', () => {
  assert.deepEqual(cornerRadii({}), [0, 0, 0, 0])
  assert.deepEqual(cornerRadii({ topLeftRadius: 1, topRightRadius: 2, bottomRightRadius: 3, bottomLeftRadius: 4 }), [1, 2, 3, 4])
})

test('pathOf stops at PAGE and DOCUMENT', () => {
  const doc = { type: 'DOCUMENT', name: 'doc' }
  const page = { type: 'PAGE', name: 'pg', parent: doc }
  const root = tree({ type: 'FRAME', name: 'a', children: [{ type: 'FRAME', name: 'b', children: [{ type: 'TEXT', name: 'c' }] }] }, page)
  assert.equal(pathOf(root.children[0].children[0]), 'a / b / c')
  assert.equal(pathOf(root), 'a')
})

test('pathOf keeps 6 ancestors and marks the cut', () => {
  let spec = { type: 'TEXT', name: 'leaf' }
  for (const name of ['l6', 'l5', 'l4', 'l3', 'l2', 'l1', 'l0']) spec = { type: 'FRAME', name, children: [spec] }
  let n = tree(spec)
  while (n.children) n = n.children[0]
  assert.equal(pathOf(n), '… / l1 / l2 / l3 / l4 / l5 / l6 / leaf')
  let m = tree({ type: 'FRAME', name: 'x', children: [spec] })
  for (let i = 0; i < 8; i++) m = m.children[0]
  m = tree({ type: 'FRAME', name: 'r', children: [{ type: 'FRAME', name: 's', children: [{ type: 'FRAME', name: 't', children: [{ type: 'FRAME', name: 'u', children: [{ type: 'FRAME', name: 'v', children: [{ type: 'FRAME', name: 'w', children: [{ type: 'TEXT', name: 'leaf' }] }] }] }] }] }] })
  let leaf = m
  while (leaf.children) leaf = leaf.children[0]
  assert.equal(pathOf(leaf), 'r / s / t / u / v / w / leaf')
})

test('finding shape and ctxFor', () => {
  const t = tree({ type: 'FRAME', name: 'a', id: '1:2', children: [{ type: 'TEXT', name: 'b', id: '1:3' }] })
  assert.deepEqual(finding('S-3', t.children[0], 'why'), { rule: 'S-3', nodeId: '1:3', name: 'b', path: 'a / b', detail: 'why' })
  const ctx = ctxFor({ k: 1 })
  assert.deepEqual(ctx.project, { k: 1 })
  assert.deepEqual(ctx.finding('V-1', t, 'd'), finding('V-1', t, 'd'))
})

test('tree fixture defaults', () => {
  const t = tree({ type: 'FRAME', name: 'a', box: [1, 2, 3, 4], children: [{ type: 'TEXT', name: 'b', visible: false }] })
  assert.deepEqual(t.absoluteBoundingBox, { x: 1, y: 2, width: 3, height: 4 })
  assert.equal(t.visible, true)
  assert.equal(t.children[0].visible, false)
  assert.equal(t.children[0].parent, t)
  assert.match(t.id, /^n\d+$/)
})

test('prelude has no module syntax and is small', async () => {
  const { readFileSync } = await import('node:fs')
  const src = readFileSync(new URL('../predicates/prelude.js', import.meta.url), 'utf8')
  assert.ok(!/import |export |figma\./.test(src))
  assert.ok(Buffer.byteLength(src) < 4096)
})
