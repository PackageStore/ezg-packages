import test from 'node:test'
import assert from 'node:assert/strict'
import { loadPredicates, tree, ctxFor } from './harness.mjs'

const { PREDICATES } = loadPredicates('v4-grid.js')
const run = (node, project) => PREDICATES['V-4'](node, {}, ctxFor(project))
const project = { grid: { styleId: 'S:abc,1:2', frame: { width: 100, height: 200 } } }
const screen = extra => tree({ type: 'FRAME', name: 's', box: [0, 0, 100, 200], ...extra })

test('missing gridStyleId gives a finding', async () => {
  const f = await run(screen(), project)
  assert.equal(f.length, 1)
  assert.equal(f[0].rule, 'V-4')
  assert.equal(f[0].detail, 'no layout grid style')
})

test('empty gridStyleId gives a finding', async () => {
  assert.equal((await run(screen({ gridStyleId: '' }), project)).length, 1)
})

test('matching id passes', async () => {
  assert.deepEqual(await run(screen({ gridStyleId: 'S:abc,1:2' }), project), [])
})

test('wrong id gives a finding when styleId is set', async () => {
  const f = await run(screen({ gridStyleId: 'S:zzz,9:9' }), project)
  assert.equal(f.length, 1)
  assert.equal(f[0].detail, 'grid style S:zzz,9:9 is not S:abc,1:2')
})

test('any id passes when project sets no styleId', async () => {
  const p = { grid: { frame: { width: 100, height: 200 } } }
  assert.deepEqual(await run(screen({ gridStyleId: 'S:x' }), p), [])
  assert.equal((await run(screen(), p)).length, 1)
})

test('non-screen-size frame is skipped', async () => {
  const t = tree({ type: 'COMPONENT', name: 'm', box: [0, 0, 50, 50] })
  assert.deepEqual(await run(t, project), [])
})

test('size tolerance is 0.5 px', async () => {
  assert.equal((await run(screen({ box: [0, 0, 100.4, 200] }), project)).length, 1)
  assert.deepEqual(await run(screen({ box: [0, 0, 100.6, 200] }), project), [])
})

test('no grid.frame means every target is checked', async () => {
  const t = tree({ type: 'FRAME', name: 'any', box: [0, 0, 7, 7] })
  assert.equal((await run(t, { grid: {} })).length, 1)
  assert.equal((await run(t, {})).length, 1)
})
