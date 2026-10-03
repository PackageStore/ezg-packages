import test from 'node:test'
import assert from 'node:assert/strict'
import { loadPredicates, tree, ctxFor } from './harness.mjs'

const { PREDICATES } = loadPredicates('s1-flat.js')
const run = root => PREDICATES['S-1'](root, {}, ctxFor({}))
const screen = children => tree({ type: 'FRAME', name: 'Screen', id: 's', children })

test('one FRAME child passes', async () => {
  assert.deepEqual(await run(screen([{ type: 'FRAME', name: 'Container-A' }])), [])
})

test('only loose art gives one finding on root', async () => {
  const f = await run(screen([
    { type: 'RECTANGLE', name: 'bg' }, { type: 'TEXT', name: 'title' }, { type: 'INSTANCE', name: 'btn' }]))
  assert.equal(f.length, 1)
  assert.equal(f[0].rule, 'S-1')
  assert.equal(f[0].nodeId, 's')
  assert.equal(f[0].detail, 'no FRAME child; root children are RECTANGLE:bg, TEXT:title, INSTANCE:btn')
})

test('empty screen gives one finding', async () => {
  const f = await run(screen([]))
  assert.equal(f.length, 1)
  assert.equal(f[0].detail, 'no children')
  assert.equal((await run(tree({ type: 'FRAME', name: 'E', id: 'e' }))).length, 1)
})

test('detail lists first 5 children only', async () => {
  const f = await run(screen(Array.from({ length: 8 }, (_, i) => ({ type: 'VECTOR', name: 'v' + i }))))
  assert.equal(f[0].detail, 'no FRAME child; root children are ' + [0, 1, 2, 3, 4].map(i => 'VECTOR:v' + i).join(', '))
})

test('one FRAME among 20 loose rectangles passes', async () => {
  const kids = Array.from({ length: 20 }, (_, i) => ({ type: 'RECTANGLE', name: 'r' + i }))
  assert.deepEqual(await run(screen([...kids, { type: 'FRAME', name: 'F' }])), [])
})

test('COMPONENT and INSTANCE children are not FRAMEs', async () => {
  const f = await run(screen([{ type: 'INSTANCE', name: 'i' }, { type: 'COMPONENT', name: 'c' }]))
  assert.equal(f.length, 1)
})

test('hidden FRAME child counts', async () => {
  assert.deepEqual(await run(screen([{ type: 'FRAME', name: 'h', visible: false }])), [])
})
