import test from 'node:test'
import assert from 'node:assert/strict'
import { loadPredicates, tree, ctxFor } from './harness.mjs'

const { PREDICATES } = loadPredicates('s4-autolayout.js')
const params = { minSiblings: 2, spacingTol: 1 }
const run = root => PREDICATES['S-4'](root, params, ctxFor())

const comp = (id, name = id) => ({ id, name })
const A = comp('c1', 'Card')
const B = comp('c2', 'Button')
const C = comp('c3', 'Badge')
const inst = (box, main = A, extra = {}) => ({
  type: 'INSTANCE', name: 'i', box, getMainComponentAsync: async () => main, ...extra,
})
const row = (xs, main = A, y = 0) => xs.map(x => inst([x, y, 100, 50], main))
const screen = (children, extra = {}) => tree({ type: 'FRAME', name: 'Screen', layoutMode: 'NONE', box: [0, 0, 1080, 2400], children, ...extra })

test('3 evenly spaced instances under NONE parent give a finding', async () => {
  const s = screen([{ type: 'FRAME', name: 'Row', layoutMode: 'NONE', children: row([0, 120, 240]) }])
  const f = await run(s)
  assert.equal(f.length, 1)
  assert.equal(f[0].rule, 'S-4')
  assert.equal(f[0].name, 'Row')
  assert.equal(f[0].detail, '3 × Card evenly spaced on x; make the parent auto-layout')
})

test('vertical axis is reported as y', async () => {
  const kids = [0, 70, 140].map(y => inst([0, y, 100, 50]))
  const f = await run(screen([{ type: 'FRAME', name: 'Col', children: kids }]))
  assert.equal(f.length, 1)
  assert.match(f[0].detail, /on y;/)
})

test('missing layoutMode counts as NONE', async () => {
  const f = await run(screen([{ type: 'GROUP', name: 'G', children: row([0, 120, 240]) }]))
  assert.equal(f.length, 1)
})

test('auto-layout parent passes', async () => {
  for (const layoutMode of ['VERTICAL', 'HORIZONTAL']) {
    const f = await run(screen([{ type: 'FRAME', name: 'Row', layoutMode, children: row([0, 120, 240]) }]))
    assert.equal(f.length, 0)
  }
})

test('uneven gaps pass', async () => {
  const f = await run(screen([{ type: 'FRAME', name: 'Row', children: row([0, 120, 300]) }]))
  assert.equal(f.length, 0)
})

test('gaps within tolerance pass as uniform, beyond do not', async () => {
  assert.equal((await run(screen([{ type: 'FRAME', name: 'R', children: row([0, 120, 241]) }]))).length, 1)
  assert.equal((await run(screen([{ type: 'FRAME', name: 'R', children: row([0, 120, 243]) }]))).length, 0)
})

test('different main components are not grouped', async () => {
  const kids = [inst([0, 0, 100, 50], A), inst([120, 0, 100, 50], B), inst([240, 0, 100, 50], C)]
  assert.equal((await run(screen([{ type: 'FRAME', name: 'Row', children: kids }]))).length, 0)
})

test('two siblings count with minSiblings 2', async () => {
  const f = await run(screen([{ type: 'FRAME', name: 'Row', children: row([0, 120]) }]))
  assert.equal(f.length, 1)
  assert.match(f[0].detail, /^2 × Card/)
})

test('minSiblings is read from params', async () => {
  const s = screen([{ type: 'FRAME', name: 'Row', children: row([0, 120]) }])
  assert.equal((await PREDICATES['S-4'](s, { minSiblings: 3, spacingTol: 1 }, ctxFor())).length, 0)
})

test('instances off the shared axis position are not uniform', async () => {
  const kids = [inst([0, 0, 100, 50]), inst([120, 30, 100, 50]), inst([240, 0, 100, 50])]
  // x-aligned subset of 2 (first and third, gap 140) still counts as 2; require 3 to isolate the stagger
  const f = await PREDICATES['S-4'](screen([{ type: 'FRAME', name: 'Row', children: kids }]), { minSiblings: 3, spacingTol: 1 }, ctxFor())
  assert.equal(f.length, 0)
})

test('largest aligned subset is tested, not the whole group', async () => {
  const kids = [...row([0, 120, 240]), inst([400, 90, 100, 50])]
  const f = await run(screen([{ type: 'FRAME', name: 'Row', children: kids }]))
  assert.equal(f.length, 1)
  assert.match(f[0].detail, /^3 × Card/)
})

test('null main component is skipped', async () => {
  const kids = [...row([0, 120]), inst([240, 0, 100, 50], null)]
  const f = await run(screen([{ type: 'FRAME', name: 'Row', children: kids }]))
  assert.equal(f.length, 1)
  assert.match(f[0].detail, /^2 ×/)
})

test('does not descend into instance subtrees', async () => {
  const inner = { type: 'INSTANCE', name: 'Master', box: [0, 0, 400, 50], getMainComponentAsync: async () => B, layoutMode: 'NONE', children: row([0, 120, 240]) }
  assert.equal((await run(screen([inner]))).length, 0)
})

test('hidden instances are ignored', async () => {
  const kids = [...row([0, 120]), inst([240, 0, 100, 50], A, { visible: false })]
  const f = await run(screen([{ type: 'FRAME', name: 'Row', children: kids }]))
  assert.match(f[0].detail, /^2 ×/)
})

test('root itself is checked', async () => {
  const f = await run(screen(row([0, 120, 240])))
  assert.equal(f.length, 1)
  assert.equal(f[0].name, 'Screen')
})
