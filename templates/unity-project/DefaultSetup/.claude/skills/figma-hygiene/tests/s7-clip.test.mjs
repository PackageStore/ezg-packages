import test from 'node:test'
import assert from 'node:assert/strict'
import { loadPredicates, tree, ctxFor } from './harness.mjs'

const { PREDICATES } = loadPredicates('s7-clip.js')
const params = { allowPattern: 'Scroll|^Mask-' }
const run = root => PREDICATES['S-7'](root, params, ctxFor({}))

test('clipping child frame is a finding', async () => {
  const t = tree({ type: 'FRAME', name: 'Screen', children: [{ type: 'FRAME', name: 'Panel', clipsContent: true }] })
  const f = await run(t)
  assert.equal(f.length, 1)
  assert.equal(f[0].rule, 'S-7')
  assert.equal(f[0].name, 'Panel')
  assert.equal(f[0].detail, 'clips its content')
})

test('root clipping passes', async () => {
  const t = tree({ type: 'FRAME', name: 'Screen', clipsContent: true, children: [{ type: 'FRAME', name: 'Panel' }] })
  assert.deepEqual(await run(t), [])
})

test('Mask-Pattern and Scroll View pass', async () => {
  const t = tree({ type: 'FRAME', name: 'Screen', children: [
    { type: 'FRAME', name: 'Mask-Pattern', clipsContent: true },
    { type: 'FRAME', name: 'Scroll View', clipsContent: true }] })
  assert.deepEqual(await run(t), [])
})

test('allowPattern is case-sensitive and anchored as written', async () => {
  const t = tree({ type: 'FRAME', name: 'Screen', children: [
    { type: 'FRAME', name: 'scroll', clipsContent: true },
    { type: 'FRAME', name: 'My-Mask-X', clipsContent: true }] })
  assert.equal((await run(t)).length, 2)
})

test('slice frame passes', async () => {
  const t = tree({ type: 'FRAME', name: 'Screen', children: [{ type: 'FRAME', name: 'Panel', clipsContent: true, children: [
    { type: 'RECTANGLE', name: 'slice_0_0' }, { type: 'RECTANGLE', name: 'slice_0_1' }] }] })
  assert.deepEqual(await run(t), [])
})

test('clipping node inside an instance names the instance', async () => {
  const t = tree({ type: 'FRAME', name: 'Screen', children: [{ type: 'INSTANCE', name: 'Card', children: [
    { type: 'FRAME', name: 'Inner', clipsContent: true }] }] })
  const f = await run(t)
  assert.equal(f.length, 1)
  assert.match(f[0].detail, /in instance Card/)
})

test('clipping instance itself is not "in instance"; deep descendants are found', async () => {
  const t = tree({ type: 'FRAME', name: 'Screen', children: [{ type: 'INSTANCE', name: 'Card', clipsContent: true, children: [
    { type: 'FRAME', name: 'A', children: [{ type: 'FRAME', name: 'B', clipsContent: true }] }] }] })
  const f = await run(t)
  assert.equal(f.length, 2)
  assert.equal(f[0].detail, 'clips its content')
  assert.match(f[1].detail, /in instance Card/)
})

test('allowed node does not hide clipping descendants', async () => {
  const t = tree({ type: 'FRAME', name: 'Screen', children: [{ type: 'FRAME', name: 'Mask-A', clipsContent: true, children: [
    { type: 'FRAME', name: 'Inner', clipsContent: true }] }] })
  assert.deepEqual((await run(t)).map(f => f.name), ['Inner'])
})
