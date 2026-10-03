import test from 'node:test'
import assert from 'node:assert/strict'
import { loadPredicates, tree, ctxFor } from './harness.mjs'

const { PREDICATES } = loadPredicates('s5-grid.js')
const params = { minRows: 2, minCols: 2, alignTol: 1 }
const main = { id: 'c1' }
const inst = (x, y, extra = {}) => ({ type: 'INSTANCE', name: 'Card', box: [x, y, 50, 50], getMainComponentAsync: async () => main, ...extra })
const run = root => PREDICATES['S-5'](root, params, ctxFor())

test('2x2 under one parent passes', async () => {
  const root = tree({ type: 'FRAME', name: 'Screen', children: [
    { type: 'FRAME', name: 'Grid', children: [inst(0, 0), inst(60, 0), inst(0, 60), inst(60, 60)] }] })
  assert.deepEqual(await run(root), [])
})

test('2x2 split across two row frames reports once', async () => {
  const root = tree({ type: 'FRAME', name: 'Screen', children: [
    { type: 'FRAME', name: 'Row1', children: [inst(0, 0), inst(60, 0)] },
    { type: 'FRAME', name: 'Row2', children: [inst(0, 60), inst(60, 60)] }] })
  const f = await run(root)
  assert.equal(f.length, 1)
  assert.equal(f[0].rule, 'S-5')
  assert.equal(f[0].nodeId, root.children[0].children[0].id)
  assert.equal(f[0].detail, '4 × Card form a 2×2 grid across 2 parents; put them in one container')
})

test('single row of 4 is not a grid', async () => {
  const root = tree({ type: 'FRAME', name: 'Screen', children: [
    { type: 'FRAME', name: 'A', children: [inst(0, 0), inst(60, 0)] },
    { type: 'FRAME', name: 'B', children: [inst(120, 0), inst(180, 0)] }] })
  assert.deepEqual(await run(root), [])
})

test('different components do not combine', async () => {
  const other = { id: 'c2' }
  const root = tree({ type: 'FRAME', name: 'Screen', children: [
    { type: 'FRAME', name: 'A', children: [inst(0, 0), inst(60, 0, { getMainComponentAsync: async () => other })] },
    { type: 'FRAME', name: 'B', children: [inst(0, 60, { getMainComponentAsync: async () => other }), inst(60, 60)] }] })
  assert.deepEqual(await run(root), [])
})

test('4+4+2 uses the largest full rectangle', async () => {
  const root = tree({ type: 'FRAME', name: 'Screen', children: [
    { type: 'FRAME', name: 'R1', children: [inst(0, 0), inst(60, 0), inst(120, 0), inst(180, 0)] },
    { type: 'FRAME', name: 'R2', children: [inst(0, 60), inst(60, 60), inst(120, 60), inst(180, 60)] },
    { type: 'FRAME', name: 'R3', children: [inst(0, 120), inst(60, 120)] }] })
  const f = await run(root)
  assert.equal(f.length, 1)
  assert.match(f[0].detail, /^8 × Card form a 4×2 grid across 2 parents/)
})

test('alignment within tolerance counts, outside does not', async () => {
  const near = tree({ type: 'FRAME', name: 'Screen', children: [
    { type: 'FRAME', name: 'A', children: [inst(0, 0), inst(60.5, 0)] },
    { type: 'FRAME', name: 'B', children: [inst(0.5, 60), inst(60, 60)] }] })
  assert.equal((await run(near)).length, 1)
  const far = tree({ type: 'FRAME', name: 'Screen', children: [
    { type: 'FRAME', name: 'A', children: [inst(0, 0), inst(60, 0)] },
    { type: 'FRAME', name: 'B', children: [inst(10, 60), inst(70, 60)] }] })
  assert.deepEqual(await run(far), [])
})

test('instances nested inside an instance are ignored', async () => {
  const inner = [inst(0, 0), inst(60, 0), inst(0, 60), inst(60, 60)]
  const root = tree({ type: 'FRAME', name: 'Screen', children: [
    { type: 'INSTANCE', name: 'Wrapper', box: [0, 0, 200, 200], getMainComponentAsync: async () => ({ id: 'w' }), children: inner }] })
  assert.deepEqual(await run(root), [])
})

test('hidden instances are ignored', async () => {
  const root = tree({ type: 'FRAME', name: 'Screen', children: [
    { type: 'FRAME', name: 'A', children: [inst(0, 0), inst(60, 0)] },
    { type: 'FRAME', name: 'B', children: [inst(0, 60), inst(60, 60, { visible: false })] }] })
  assert.deepEqual(await run(root), [])
})
