import test from 'node:test'
import assert from 'node:assert/strict'
import { loadPredicates, tree, ctxFor } from './harness.mjs'

const { PREDICATES } = loadPredicates('v1-nine-slice.js')
const params = {}
const slice = id => ({ id, type: 'FRAME', name: 'Btn', children: [{ type: 'RECTANGLE', name: 'slice_0_0' }, { type: 'RECTANGLE', name: 'slice_0_1' }] })
const registry = node => ({ btn: { applied: { node } } })
const run = (root, nineSlice) => PREDICATES['V-1'](root, params, ctxFor({ nineSlice }))

test('slice frame passes', async () => {
  const root = tree({ id: '1:1', type: 'FRAME', name: 'Screen', children: [slice('2:2')] })
  assert.deepEqual(await run(root, registry('Menu/Btn 2:2')), [])
})

test('plain rectangle is a finding', async () => {
  const root = tree({ id: '1:1', type: 'FRAME', name: 'Screen', children: [{ id: '2:2', type: 'RECTANGLE', name: 'Plate' }] })
  const out = await run(root, registry('Menu/Btn 2:2'))
  assert.equal(out.length, 1)
  assert.equal(out[0].rule, 'V-1')
  assert.equal(out[0].nodeId, '2:2')
  assert.equal(out[0].detail, 'registry stem btn is applied here but the node is not 9-slice')
})

test('unparsable entries are skipped', async () => {
  const root = tree({ id: '1:1', type: 'FRAME', name: 'Screen', children: [{ id: '2:2', type: 'RECTANGLE', name: 'Plate' }] })
  const reg = { a: { applied: { node: 'garbage' } }, b: { applied: { node: 42 } }, c: { applied: {} }, d: {}, e: null, f: { applied: { node: ', ,x y' } } }
  assert.deepEqual(await run(root, reg), [])
})

test('ids outside root and missing project data give nothing', async () => {
  const root = tree({ id: '1:1', type: 'FRAME', name: 'Screen', children: [{ id: '2:2', type: 'RECTANGLE', name: 'Plate' }] })
  assert.deepEqual(await run(root, registry('Menu/Btn 9:9')), [])
  assert.deepEqual(await PREDICATES['V-1'](root, params, ctxFor({})), [])
})

test('comma list reports only non-slice ids; root itself counts', async () => {
  const root = tree({ id: '1:1', type: 'FRAME', name: 'Screen', children: [slice('2:2'), { id: '3:3', type: 'RECTANGLE', name: 'Plate' }] })
  const out = await run(root, registry('A/B 2:2, C/D 3:3, E/F 1:1'))
  assert.deepEqual(out.map(f => f.nodeId).sort(), ['1:1', '3:3'])
})
