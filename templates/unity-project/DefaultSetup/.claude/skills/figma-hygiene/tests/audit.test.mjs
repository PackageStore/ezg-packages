import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { buildEvalBody, mapTidy, parseArgs } from '../scripts/audit.mjs';
import { loadTable, resolveRows } from '../scripts/rules.mjs';
import { tree } from './harness.mjs';

const H = join(dirname(fileURLToPath(import.meta.url)), '..');
const CLI = join(H, 'scripts', 'audit.mjs');
const table = loadTable(join(H, 'rules.json'));
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
const evalRows = (ids) => resolveRows(table, {}, ids).filter((r) => r.engine === 'eval');
const prelude = readFileSync(join(H, 'predicates', 'prelude.js'), 'utf8');

test('eval body compiles and holds each selected predicate once', () => {
  const rows = evalRows(['S-1', 'S-3']);
  const body = buildEvalBody(rows, ['1:1'], {});
  assert.doesNotThrow(() => new AsyncFunction('figma', body));
  for (const r of rows) {
    const src = readFileSync(join(H, 'predicates', r.eval.predicate), 'utf8').trim();
    assert.equal(body.split(src).length - 1, 1, r.id);
  }
  assert.equal(body.split(prelude.trim()).length - 1, 1);
  assert.ok(!body.includes('PREDICATES["S-7"]'));
});

test('eval body for all rows stays under the MCP eval limit', () => {
  const rows = resolveRows(table, {}).filter((r) => r.engine === 'eval');
  const body = buildEvalBody(rows, ['1:1', '2:2'], { grid: {}, nineSlice: {} });
  assert.doesNotThrow(() => new AsyncFunction('figma', body));
  assert.ok(body.length < 50000, `body is ${body.length} chars`);
});

test('eval body runs against a fixture figma stub', async () => {
  const flat = tree({ id: '9:1', type: 'FRAME', name: 'C', children: [{ type: 'RECTANGLE', name: 'r' }] });
  const ok = tree({ id: '9:2', type: 'FRAME', name: 'D', children: [{ type: 'FRAME', name: 'Inner', children: [] }] });
  const nodes = { '9:1': flat, '9:2': ok };
  const figma = { getNodeByIdAsync: async (id) => nodes[id] ?? null };
  const body = buildEvalBody(evalRows(['S-1']), ['9:1', '9:2', '9:3'], {});
  const out = await new AsyncFunction('figma', body)(figma);
  assert.equal(out.findings.length, 1);
  assert.equal(out.findings[0].rule, 'S-1');
  assert.equal(out.findings[0].nodeId, '9:1');
  assert.deepEqual(out.missing, ['9:3']);
});

test('a throwing predicate becomes one finding', async () => {
  const root = tree({ id: '9:1', type: 'FRAME', name: 'C', children: [] });
  Object.defineProperty(root, 'children', { get() { throw new Error('boom'); } });
  const figma = { getNodeByIdAsync: async () => root };
  const out = await new AsyncFunction('figma', buildEvalBody(evalRows(['S-1']), ['9:1'], {}))(figma);
  assert.equal(out.findings.length, 1);
  assert.equal(out.findings[0].rule, 'S-1');
  assert.match(out.findings[0].detail, /^rule S-1 failed: boom$/);
});

test('predicate path matches the lint path format', async () => {
  const a = tree({
    type: 'FRAME', name: 'A',
    children: [{ type: 'FRAME', name: 'B', children: [{ id: '9:5', type: 'FRAME', name: 'C', children: [] }] }],
  });
  const c = a.children[0].children[0];
  const figma = { getNodeByIdAsync: async () => c };
  const out = await new AsyncFunction('figma', buildEvalBody(evalRows(['S-1']), ['9:5'], {}))(figma);
  assert.equal(out.findings[0].path, 'A / B / C');
});

test('mapTidy maps overlaps and wide gaps', () => {
  const page = { id: '0:1', name: 'P' };
  const a = { id: '1:1', name: 'a' };
  const b = { id: '1:2', name: 'b' };
  const out = mapTidy({
    overlaps: [[a, b]],
    wideGaps: [{ row: 0, before: b, gap: 300 }, { row: 2, above: true, gap: 500 }],
  }, page);
  assert.equal(out.length, 3);
  assert.ok(out.every((f) => f.rule === 'S-10'));
  assert.equal(out[0].nodeId, '1:2');
  assert.equal(out[1].nodeId, '1:2');
  assert.match(out[1].detail, /300/);
  assert.equal(out[2].nodeId, '0:1');
  assert.deepEqual(mapTidy({ pass: true, overlaps: [], wideGaps: [] }, page), []);
  assert.match(mapTidy({ error: 'nope' }, page)[0].detail, /failed: nope/);
});

test('parseArgs validates the target flags and fail-on', () => {
  assert.throws(() => parseArgs(['--project', 'p']));
  assert.throws(() => parseArgs(['--project', 'p', '--screens', 'A', '--page', 'B']));
  assert.throws(() => parseArgs(['--project', 'p', '--screens', 'A', '--fail-on', 'x']));
  assert.throws(() => parseArgs(['--project', 'p', '--bogus']));
  const o = parseArgs(['--project', 'p', '--screens', 'A,B', '--rules', 'S-3']);
  assert.deepEqual(o.screens, ['A', 'B']);
  assert.equal(o.failOn, 'block');
});

function cli(...args) {
  return spawnSync(process.execPath, [CLI, ...args], { encoding: 'utf8', timeout: 30000 });
}

function tmpProject(debt) {
  const dir = mkdtempSync(join(tmpdir(), 'audit-'));
  const p = join(dir, 'project.json');
  writeFileSync(p, JSON.stringify({ fileKey: 'K', screensPage: 'S', debtDir: dir }));
  if (debt) writeFileSync(join(dir, 'K.json'), JSON.stringify({ entries: debt }));
  return p;
}

test('--help prints usage and exits 0', () => {
  const r = cli('--help');
  assert.equal(r.status, 0);
  assert.match(r.stdout, /usage:/);
});

test('bad project exits 2', () => {
  assert.equal(cli('--project', '/no/such/project.json', '--screens', 'A').status, 2);
  assert.equal(cli('--screens', 'A').status, 2);
});

test('unreachable bridge exits 3', () => {
  const r = cli('--project', tmpProject(), '--screens', 'A', '--port', '1');
  assert.equal(r.status, 3, r.stderr);
});

test('unknown debt code exits 2', () => {
  const r = cli('--project', tmpProject([{ rule: 'S-2', nodeId: '1:1', code: 'nope' }]), '--screens', 'A', '--port', '1');
  assert.equal(r.status, 2, r.stderr);
});
