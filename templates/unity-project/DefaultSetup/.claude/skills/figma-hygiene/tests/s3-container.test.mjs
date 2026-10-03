import test from 'node:test'
import assert from 'node:assert/strict'
import { loadPredicates, tree, ctxFor } from './harness.mjs'

const { PREDICATES } = loadPredicates('s3-container.js')
const params = {
  minChildren: 2,
  allowedPrefixes: ['Container-', 'Btn-', 'Header-', 'Row-', 'Slot-', 'Group-'],
  exemptNames: ['Title', 'Top-Bar', 'Bottom'],
  exemptContains: ['Scroll'],
}
const two = [{ type: 'TEXT', name: 'a' }, { type: 'TEXT', name: 'b' }]
const run = (child, p = params) => {
  const root = tree({ type: 'FRAME', name: 'Screen', children: [child] })
  return PREDICATES['S-3'](root, p, ctxFor())
}
const frame = (name, children = two, type = 'FRAME') => ({ type, name, children })

test('2-child frame named Stuff is a finding', async () => {
  const f = await run(frame('Stuff'))
  assert.equal(f.length, 1)
  assert.equal(f[0].rule, 'S-3')
  assert.equal(f[0].name, 'Stuff')
  assert.equal(f[0].detail, 'grouping frame "Stuff" has 2 children; name it Container-Content')
})

test('Container- passes, Container_ fails', async () => {
  assert.equal((await run(frame('Container-Stuff'))).length, 0)
  assert.equal((await run(frame('Container_Stuff'))).length, 1)
})

test('1-child frame passes', async () => {
  assert.equal((await run(frame('Stuff', [two[0]]))).length, 0)
})

test('exempt names and contains pass', async () => {
  for (const n of ['Title', 'Top-Bar', 'Scroll View']) assert.equal((await run(frame(n))).length, 0, n)
})

test('frame inside an INSTANCE is skipped', async () => {
  const inst = { type: 'INSTANCE', name: 'Inst', children: [frame('Stuff')] }
  assert.equal((await run(inst)).length, 0)
})

test('slice frame is skipped', async () => {
  const slices = [{ type: 'RECTANGLE', name: 'slice_0_0' }, { type: 'RECTANGLE', name: 'slice_0_1' }]
  assert.equal((await run(frame('Stuff', slices))).length, 0)
})

test('params override minChildren', async () => {
  assert.equal((await run(frame('Stuff'), { ...params, minChildren: 3 })).length, 0)
})

test('root is excluded and nested frames are checked', async () => {
  const root = tree({ type: 'FRAME', name: 'Screen', children: [frame('Container-A', [frame('Inner'), two[0]])] })
  const f = await PREDICATES['S-3'](root, params, ctxFor())
  assert.deepEqual(f.map(x => x.name), ['Inner'])
})

test('COMPONENT and GROUP are not grouping frames', async () => {
  assert.equal((await run(frame('Stuff', two, 'COMPONENT'))).length, 0)
  assert.equal((await run(frame('Stuff', two, 'GROUP'))).length, 0)
})
