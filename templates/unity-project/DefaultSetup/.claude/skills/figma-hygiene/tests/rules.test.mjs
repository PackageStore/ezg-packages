import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  loadTable, loadCodes, loadDebt, resolveRows, validateDebt, applyDebt, verdict, ConfigError,
} from '../scripts/rules.mjs';

const H = join(dirname(fileURLToPath(import.meta.url)), '..');
const table = loadTable(join(H, 'rules.json'));
const codes = loadCodes(join(H, 'debt-codes.json'));
const ids = table.rules.map((r) => r.id);

function tmpFile(obj) {
  const p = join(mkdtempSync(join(tmpdir(), 'rules-')), 'rules.json');
  writeFileSync(p, typeof obj === 'string' ? obj : JSON.stringify(obj));
  return p;
}
const row = (o) => ({ id: 'X-1', engine: 'lint', blocks: true, lint: { rule: 'r' }, ...o });

test('real table and codes load', () => {
  assert.equal(ids.length, 14);
  assert.ok(codes.has('other') && codes.has('remote-library'));
});

test('loadTable rejects bad rows', () => {
  const bad = [
    { rules: [row({}), row({})] },
    { rules: [row({ engine: 'magic' })] },
    { rules: [row({ blocks: undefined })] },
    { rules: [row({ blocks: 'yes' })] },
    { rules: [row({ engine: 'eval', eval: {} })] },
    { rules: [row({ lint: {} })] },
    { rules: [row({ id: undefined })] },
    { nope: 1 },
  ];
  for (const t of bad) assert.throws(() => loadTable(tmpFile(t)), ConfigError);
  assert.throws(() => loadTable(tmpFile('{bad')), ConfigError);
  assert.throws(() => loadTable('/no/such/file.json'), ConfigError);
});

test('loadDebt returns empty entries when file is missing', () => {
  const dir = mkdtempSync(join(tmpdir(), 'debt-'));
  assert.deepEqual(loadDebt(dir, 'KEY'), { entries: [] });
  writeFileSync(join(dir, 'KEY.json'), JSON.stringify({ entries: [{ rule: 'S-2', nodeId: '1:2', code: 'other', issue: '#1' }] }));
  assert.equal(loadDebt(dir, 'KEY').entries.length, 1);
});

test('resolveRows merges ruleParams shallowly', () => {
  const base = table.rules.find((r) => r.id === 'S-3').params;
  const [r] = resolveRows(table, { ruleParams: { 'S-3': { minChildren: 3 } } }, ['S-3']);
  assert.equal(r.params.minChildren, 3);
  assert.deepEqual(r.params.allowedPrefixes, base.allowedPrefixes);
  assert.deepEqual(r.params.exemptNames, base.exemptNames);
  const [r2] = resolveRows(table, { ruleParams: { 'S-3': { allowedPrefixes: ['A-'] } } }, ['S-3']);
  assert.deepEqual(r2.params.allowedPrefixes, ['A-']);
});

test('resolveRows ignores blocks override and does not mutate the table', () => {
  const [r] = resolveRows(table, { ruleParams: { 'S-3': { blocks: false } } }, ['S-3']);
  assert.equal(r.blocks, true);
  assert.equal('blocks' in r.params, false);
  assert.equal(table.rules.find((x) => x.id === 'S-3').params.minChildren, 2);
});

test('resolveRows filters, returns all, and rejects unknown ids', () => {
  assert.equal(resolveRows(table, {}).length, 14);
  assert.equal(resolveRows(table, {}, []).length, 14);
  assert.deepEqual(resolveRows(table, {}, ['S-2', 'V-3']).map((r) => r.id), ['S-2', 'V-3']);
  assert.throws(() => resolveRows(table, {}, ['S-99']), ConfigError);
});

test('validateDebt flags unknown code, other without issue, unknown rule, missing nodeId', () => {
  const ok = { rule: 'S-2', nodeId: '1:2', code: 'designer-intent' };
  assert.deepEqual(validateDebt([ok], codes, ids), []);
  assert.equal(validateDebt([{ ...ok, code: 'nope' }], codes).length, 1);
  assert.equal(validateDebt([{ ...ok, code: 'other' }], codes).length, 1);
  assert.equal(validateDebt([{ ...ok, code: 'other', issue: '  ' }], codes).length, 1);
  assert.deepEqual(validateDebt([{ ...ok, code: 'other', issue: '#12' }], codes), []);
  assert.equal(validateDebt([{ ...ok, rule: 'Z-9' }], codes, ids).length, 1);
  assert.equal(validateDebt([{ rule: 'S-2', code: 'other', issue: 'x' }], codes, ids).length, 1);
});

test('applyDebt waives, keeps other rules, lists stale, dedupes', () => {
  const f = (rule, nodeId) => ({ rule, nodeId, name: 'n', path: 'p', detail: 'd' });
  const entries = [
    { rule: 'S-2', nodeId: '1:2', code: 'other', issue: '#1' },
    { rule: 'S-9', nodeId: '9:9', code: 'other', issue: '#1' },
    { rule: 'S-2', nodeId: 'I5:1;7:3', code: 'remote-library' },
  ];
  const findings = [f('S-2', '1:2'), f('S-2', '1:2'), f('V-3', '1:2'), f('S-2', 'I5:1;7:3'), f('S-2', 'I5:1;7:4')];
  const { kept, waived, stale } = applyDebt(findings, entries);
  assert.deepEqual(kept.map((x) => `${x.rule}@${x.nodeId}`), ['V-3@1:2', 'S-2@I5:1;7:4']);
  assert.equal(waived.length, 2);
  assert.deepEqual(stale, [entries[1]]);
});

test('verdict policies', () => {
  const rows = resolveRows(table, {});
  const f = (rule) => ({ rule, nodeId: '1:1', name: '', path: '', detail: '' });
  const warn = [f('S-4')];
  const both = [f('S-4'), f('S-2')];
  assert.equal(verdict(warn, rows, 'block').pass, true);
  assert.equal(verdict(warn, rows, 'any').pass, false);
  assert.equal(verdict(both, rows, 'block').pass, false);
  assert.equal(verdict(both, rows, 'none').pass, true);
  assert.equal(verdict([], rows, 'any').pass, true);
  const v = verdict(both, rows, 'block');
  assert.equal(v.blocking.length, 1);
  assert.equal(v.warnings.length, 1);
  assert.throws(() => verdict([], rows, 'bogus'), ConfigError);
  assert.throws(() => verdict([f('Q-1')], rows, 'block'), ConfigError);
});
