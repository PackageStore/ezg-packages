import test from 'node:test'
import assert from 'node:assert/strict'
import { loadPredicates, tree, ctxFor } from './harness.mjs'

const { PREDICATES } = loadPredicates('s8-popup.js')
const params = { containerPattern: '^Container-.*Popup$', edgeTol: 1 }
const project = { grid: { columnEdges: [30, 204, 378, 552, 726, 900], columnWidth: 150, margin: 30, safeBottom: 60 } }
const CENTER = { horizontal: 'CENTER', vertical: 'CENTER' }

const run = child => {
  const root = tree({ type: 'FRAME', name: 'Screen', box: [0, 0, 1080, 2400], children: child ? [child] : [] })
  return PREDICATES['S-8'](root, params, ctxFor(project))
}
const popup = (over = {}) => ({ type: 'FRAME', name: 'Container-InfoPopup', box: [30, 800, 1020, 800], constraints: CENTER, ...over })

test('full-span centred container passes (x=30 to 1050)', async () => {
  assert.deepEqual(await run(popup()), [])
})

test('off-grid left edge gives a finding', async () => {
  const f = await run(popup({ box: [45, 800, 1005, 800] }))
  assert.equal(f.length, 1)
  assert.equal(f[0].rule, 'S-8')
  assert.match(f[0].detail, /left edge/)
})

test('edge within tolerance passes', async () => {
  assert.deepEqual(await run(popup({ box: [30.8, 800, 1019.2, 800] })), [])
})

test('container into the bottom 60 px gives a finding', async () => {
  const f = await run(popup({ box: [30, 1600, 1020, 800] }))
  assert.equal(f.length, 1)
  assert.match(f[0].detail, /bottom edge/)
})

test('container ending exactly at the safe limit passes', async () => {
  assert.deepEqual(await run(popup({ box: [30, 1540, 1020, 800] })), [])
})

test('MIN/MIN constraints give a finding', async () => {
  const f = await run(popup({ constraints: { horizontal: 'MIN', vertical: 'MIN' } }))
  assert.equal(f.length, 1)
  assert.match(f[0].detail, /constraints/)
})

test('missing constraints give a finding', async () => {
  assert.equal((await run(popup({ constraints: undefined }))).length, 1)
})

test('clipsContent on gives a finding', async () => {
  const f = await run(popup({ clipsContent: true }))
  assert.equal(f.length, 1)
  assert.match(f[0].detail, /clipsContent/)
})

test('no matching child gives no finding', async () => {
  assert.deepEqual(await run(null), [])
  assert.deepEqual(await run({ type: 'FRAME', name: 'Plate', box: [5, 5, 10, 10] }), [])
})

test('each failure is its own finding', async () => {
  const f = await run(popup({ box: [45, 1600, 1005, 800], constraints: { horizontal: 'MIN', vertical: 'MIN' }, clipsContent: true }))
  assert.equal(f.length, 4)
})

test('offset root: edges are relative to root', async () => {
  const root = tree({ type: 'FRAME', name: 'Screen', box: [5000, 300, 1080, 2400], children: [popup({ box: [5030, 1100, 1020, 800] })] })
  assert.deepEqual(await PREDICATES['S-8'](root, params, ctxFor(project)), [])
})
