import { test } from 'node:test'
import assert from 'node:assert/strict'
import { loadPredicates, tree, ctxFor } from './harness.mjs'

const { PREDICATES } = loadPredicates('v5-concentric.js')
const params = { tol: 1, containTol: 0.5, exemptMaskedPrefix: 'Mask-' }
const solid = [{ type: 'SOLID' }]
const run = root => PREDICATES['V-5'](root, params, ctxFor({}))

const plate = (innerR, fills = solid, innerExtra = {}) => tree({
  type: 'FRAME', name: 'S', box: [0, 0, 400, 400], children: [
    { type: 'FRAME', name: 'Outer', box: [10, 10, 200, 100], topLeftRadius: 20, topRightRadius: 20, bottomRightRadius: 20, bottomLeftRadius: 20, fills: solid, children: [
      { type: 'RECTANGLE', name: 'Inner', box: [18, 18, 184, 84], topLeftRadius: innerR, topRightRadius: innerR, bottomRightRadius: innerR, bottomLeftRadius: innerR, fills, ...innerExtra }] }] })

test('r20 inner in r20 outer flags expected 12', async () => {
  const f = await run(plate(20))
  assert.equal(f.length, 1)
  assert.equal(f[0].rule, 'V-5')
  assert.equal(f[0].name, 'Inner')
  assert.equal(f[0].detail, 'radius 20 != 12 (outer Outer)')
})

test('r12 inner passes', async () => {
  assert.deepEqual(await run(plate(12)), [])
})

test('image-filled inner is exempt', async () => {
  assert.deepEqual(await run(plate(20, [{ type: 'IMAGE' }])), [])
})

test('child under a Mask- ancestor is exempt', async () => {
  const root = tree({ type: 'FRAME', name: 'S', box: [0, 0, 400, 400], children: [
    { type: 'FRAME', name: 'Outer', box: [10, 10, 200, 100], topLeftRadius: 20, topRightRadius: 20, bottomRightRadius: 20, bottomLeftRadius: 20, fills: solid, children: [
      { type: 'FRAME', name: 'Mask-Inner', box: [18, 18, 184, 84], fills: solid, children: [
        { type: 'RECTANGLE', name: 'Deep', box: [18, 18, 184, 84], topLeftRadius: 20, fills: solid }] }] }] })
  const f = await run(root)
  assert.ok(!f.some(x => x.name === 'Deep'))
})

test('hidden subtree is skipped', async () => {
  assert.deepEqual(await run(plate(20, solid, { visible: false })), [])
  const root = plate(20)
  root.children[0].visible = false
  assert.deepEqual(await run(root), [])
})

test('rotated node is skipped', async () => {
  assert.deepEqual(await run(plate(20, solid, { rotation: 15 })), [])
})

test('equal-area tie picks the later outer', async () => {
  const r = v => ({ topLeftRadius: v, topRightRadius: v, bottomRightRadius: v, bottomLeftRadius: v })
  const root = tree({ type: 'FRAME', name: 'S', box: [0, 0, 400, 400], children: [
    { type: 'RECTANGLE', name: 'First', box: [10, 10, 200, 100], ...r(40), fills: solid },
    { type: 'RECTANGLE', name: 'Second', box: [10, 10, 200, 100], ...r(20), fills: solid },
    { type: 'RECTANGLE', name: 'Inner', box: [18, 18, 184, 84], ...r(20), fills: solid }] })
  const f = (await run(root)).filter(x => x.name === 'Inner')
  assert.equal(f.length, 1)
  assert.match(f[0].detail, /\(outer Second\)$/)
})

test('art outer yields no finding', async () => {
  const root = plate(20)
  root.children[0].fills = [{ type: 'IMAGE' }]
  assert.deepEqual(await run(root), [])
})

test('corner insets that differ by more than tol skip that corner', async () => {
  const root = plate(20, solid, { box: undefined })
  root.children[0].children[0].absoluteBoundingBox = { x: 18, y: 30, width: 184, height: 70 }
  assert.deepEqual(await run(root), [])
})
